using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Game.Combat;

/// <summary>
/// Pure combat math from <c>docs/04-GAME-MECHANICS.md</c>. Knows nothing about EF or
/// <see cref="TickContext"/>; takes simple inputs and returns casualty deltas. The
/// <c>CombatStep</c> and <c>AirStrikeStep</c> wrap it.
/// <para/>
/// Effective strength formula:
/// <code>effective = stack.Strength * UnitTypeStrength * moraleMult * xpMult * terrainMult * randomRoll</code>
/// where:
/// <list type="bullet">
///   <item>moraleMult = 0.5 + 0.5 * (morale / 100)            (0.5 at morale 0, 1.0 at morale 100)</item>
///   <item>xpMult     = 1.0 + 0.005 * experience               (capped at +50% at xp=100)</item>
///   <item>terrainMult = 1.0 (MVP — terrain affects movement, not ground combat directly)</item>
///   <item>randomRoll = 0.85..1.15 from per-tick deterministic RNG</item>
/// </list>
/// Combined-arms bonus (+20%) applies to a side that fields at least one ground + one air + one anti-air.
/// </summary>
public static class CombatResolver
{
    /// <summary>Casualty result for a single stack: integer Strength to subtract.</summary>
    public sealed record StackCasualty(Guid UnitId, int StrengthLoss);

    /// <summary>One side's contribution to a battle (post-air-phase for ground combat).</summary>
    public sealed record Side(Guid PlayerId, IReadOnlyList<Unit> Stacks);

    /// <summary>Outcome of a resolved battle.</summary>
    public sealed record BattleOutcome(
        Guid? WinnerPlayerId,                   // null = stalemate (both sides have survivors)
        IReadOnlyList<StackCasualty> Casualties // casualties for every stack on every side
    );

    /// <summary>
    /// Resolve a ground engagement at one province between exactly two sides. Phase 2 will
    /// extend to N-way (free-for-all) by reducing pairwise; MVP only ever sees 2 because
    /// movement queues land one attacker per tick.
    /// </summary>
    public static BattleOutcome ResolveGround(Side attacker, Side defender, IRandomSource rng) =>
        ResolveGround(attacker, defender, rng,
            defenderBonusMultiplier: 1.0,
            attackerCombinedArmsMultiplier: HasCombinedArms(attacker.Stacks) ? 1.20 : 1.0,
            defenderCombinedArmsMultiplier: HasCombinedArms(defender.Stacks) ? 1.20 : 1.0);

    /// <summary>
    /// Phase 3f overload: identical to <see cref="ResolveGround(Side, Side, IRandomSource)"/>
    /// but multiplies the defender's effective strength AND outgoing damage by
    /// <paramref name="defenderBonusMultiplier"/> (e.g. <c>1.15</c> when the defender
    /// has a general assigned at the province). The bonus stacks multiplicatively with
    /// the combined-arms multiplier. Pass <c>1.0</c> for "no bonus" (default behavior).
    /// </summary>
    public static BattleOutcome ResolveGround(
        Side attacker,
        Side defender,
        IRandomSource rng,
        double defenderBonusMultiplier) =>
        ResolveGround(attacker, defender, rng,
            defenderBonusMultiplier,
            attackerCombinedArmsMultiplier: HasCombinedArms(attacker.Stacks) ? 1.20 : 1.0,
            defenderCombinedArmsMultiplier: HasCombinedArms(defender.Stacks) ? 1.20 : 1.0);

    /// <summary>
    /// Phase 3g overload: also accepts per-side combined-arms multipliers. Callers are
    /// responsible for deciding whether each side's combined-arms boost applies (composition
    /// check + the <c>combined_arms</c> doctrine tech raising 1.20 → 1.25). Pass
    /// <c>1.0</c> to disable the boost for that side. Unlike the lower overloads, this
    /// one does NOT re-check composition internally — the caller's value is used as-is.
    /// </summary>
    public static BattleOutcome ResolveGround(
        Side attacker,
        Side defender,
        IRandomSource rng,
        double defenderBonusMultiplier,
        double attackerCombinedArmsMultiplier,
        double defenderCombinedArmsMultiplier) =>
        ResolveGround(attacker, defender, rng,
            defenderBonusMultiplier,
            attackerCombinedArmsMultiplier,
            defenderCombinedArmsMultiplier,
            attackerBonusMultiplier: 1.0);

    /// <summary>
    /// Phase 4f overload: adds <paramref name="attackerBonusMultiplier"/>, the symmetric
    /// counterpart to <paramref name="defenderBonusMultiplier"/>. Used by
    /// <c>CombatStep</c> to apply the Maneuver Warfare doctrine bonus when the attacker
    /// has the tech AND at least one of their stacks moved into the contested province
    /// this same tick. Stacks multiplicatively with the combined-arms multiplier on
    /// effective strength + outgoing damage. Pass <c>1.0</c> for no bonus.
    /// </summary>
    public static BattleOutcome ResolveGround(
        Side attacker,
        Side defender,
        IRandomSource rng,
        double defenderBonusMultiplier,
        double attackerCombinedArmsMultiplier,
        double defenderCombinedArmsMultiplier,
        double attackerBonusMultiplier)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(rng);
        if (defenderBonusMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(defenderBonusMultiplier),
                "Defender bonus multiplier must be positive.");
        if (attackerCombinedArmsMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(attackerCombinedArmsMultiplier),
                "Attacker combined-arms multiplier must be positive.");
        if (defenderCombinedArmsMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(defenderCombinedArmsMultiplier),
                "Defender combined-arms multiplier must be positive.");
        if (attackerBonusMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(attackerBonusMultiplier),
                "Attacker bonus multiplier must be positive.");

        var attackerEff = TotalEffectiveStrength(attacker.Stacks, rng) * attackerCombinedArmsMultiplier * attackerBonusMultiplier;
        var defenderEff = TotalEffectiveStrength(defender.Stacks, rng) * defenderCombinedArmsMultiplier * defenderBonusMultiplier;

        // Damage = sum over (attackerStack -> targetStack) of attackerStack.eff * matrixFraction.
        // We pre-distribute damage by computing each stack's "share" of own-side total, then
        // for each opposing stack, the damage taken is proportional to share-of-opposing-strength.
        // This avoids special casing and stays linear.
        var attackerDamageOnDefender = ComputePairwiseDamage(attacker.Stacks, defender.Stacks, rng);
        var defenderDamageOnAttacker = ComputePairwiseDamage(defender.Stacks, attacker.Stacks, rng);

        // Apply the combined-arms multiplier to outgoing damage too (effective strength
        // boost for damage purposes). Tracked separately so we don't double-apply it on
        // effective-strength comparisons used purely for outcome decisions.
        if (attackerCombinedArmsMultiplier != 1.0)
            for (var i = 0; i < attackerDamageOnDefender.Count; i++)
                attackerDamageOnDefender[i] = (attackerDamageOnDefender[i].Item1, attackerDamageOnDefender[i].Item2 * attackerCombinedArmsMultiplier);
        if (defenderCombinedArmsMultiplier != 1.0)
            for (var i = 0; i < defenderDamageOnAttacker.Count; i++)
                defenderDamageOnAttacker[i] = (defenderDamageOnAttacker[i].Item1, defenderDamageOnAttacker[i].Item2 * defenderCombinedArmsMultiplier);

        // Phase 3f: defender bonus also boosts defender outgoing damage so a general
        // (or defense-in-depth doctrine) makes the garrison both tougher and meaner.
        if (defenderBonusMultiplier != 1.0)
            for (var i = 0; i < defenderDamageOnAttacker.Count; i++)
                defenderDamageOnAttacker[i] = (defenderDamageOnAttacker[i].Item1, defenderDamageOnAttacker[i].Item2 * defenderBonusMultiplier);

        // Phase 4f: attacker bonus (Maneuver Warfare) symmetric to defender bonus —
        // boosts attacker outgoing damage on top of combined-arms scaling.
        if (attackerBonusMultiplier != 1.0)
            for (var i = 0; i < attackerDamageOnDefender.Count; i++)
                attackerDamageOnDefender[i] = (attackerDamageOnDefender[i].Item1, attackerDamageOnDefender[i].Item2 * attackerBonusMultiplier);

        var casualties = new List<StackCasualty>(attacker.Stacks.Count + defender.Stacks.Count);
        foreach (var (id, dmg) in attackerDamageOnDefender)
            casualties.Add(new StackCasualty(id, ClampLoss(dmg, FindStrength(defender.Stacks, id))));
        foreach (var (id, dmg) in defenderDamageOnAttacker)
            casualties.Add(new StackCasualty(id, ClampLoss(dmg, FindStrength(attacker.Stacks, id))));

        // Winner: side with surviving total strength. Tie / both-zero = no winner (no capture).
        var defenderRemaining = defender.Stacks.Sum(s => s.Strength) - casualties.Where(c => defender.Stacks.Any(s => s.Id == c.UnitId)).Sum(c => c.StrengthLoss);
        var attackerRemaining = attacker.Stacks.Sum(s => s.Strength) - casualties.Where(c => attacker.Stacks.Any(s => s.Id == c.UnitId)).Sum(c => c.StrengthLoss);

        Guid? winner = (attackerRemaining, defenderRemaining) switch
        {
            ( > 0, <= 0) => attacker.PlayerId,
            ( <= 0, > 0) => defender.PlayerId,
            _ => null,
        };

        return new BattleOutcome(winner, casualties);
    }

    /// <summary>
    /// Resolve an air-strike: one attacking air stack vs the defenders at <paramref name="targetStacks"/>.
    /// AA at the target shoots first; surviving attacker damages all defending ground stacks per matrix.
    /// Returns casualties for both the attacker and the defending stacks.
    /// </summary>
    public static BattleOutcome ResolveAirStrike(Unit attacker, IReadOnlyList<Unit> targetStacks, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(targetStacks);
        ArgumentNullException.ThrowIfNull(rng);

        var casualties = new List<StackCasualty>();

        // 1) Defending fighters intercept (they get a free shot before AA).
        var defendingFighters = targetStacks.Where(u => u.Type == UnitType.MultiroleFighter && u.Strength > 0).ToList();
        var attackerStrength = (double)attacker.Strength;
        foreach (var f in defendingFighters)
        {
            var fEff = EffectiveStrength(f, rng);
            var frac = CombatStats.DamageFraction(f.Type, attacker.Type);
            attackerStrength -= fEff * frac;
            if (attackerStrength <= 0) break;
        }

        // 2) AA fires (unless stealth bomber rolls bypass).
        if (attackerStrength > 0)
        {
            // Phase 3b: stealth drones share the bomber's bypass roll — small RCS, hard to track.
            var stealthBypass = (attacker.Type == UnitType.StealthBomber || attacker.Type == UnitType.StealthDrone)
                && rng.NextDouble() < CombatStats.StealthBypassChance;
            if (!stealthBypass)
            {
                foreach (var aa in targetStacks.Where(u => u.Type == UnitType.AABattery && u.Strength > 0))
                {
                    var aaEff = EffectiveStrength(aa, rng);
                    var frac = CombatStats.DamageFraction(aa.Type, attacker.Type);
                    attackerStrength -= aaEff * frac;
                    if (attackerStrength <= 0) break;
                }
            }
        }

        // Casualty for the attacker = original strength minus survivors.
        var attackerLoss = attacker.Strength - (int)Math.Max(0, Math.Round(attackerStrength));
        if (attackerLoss > 0) casualties.Add(new StackCasualty(attacker.Id, attackerLoss));

        // 3) Surviving attacker damages every target the matrix allows (mostly ground stacks).
        if (attackerStrength > 0)
        {
            // Recompute attacker effective strength using the surviving Strength.
            var survivingStrength = attackerStrength;
            var morale = attacker.Morale;
            var xp = attacker.Experience;
            var attackerEff = survivingStrength
                * CombatStats.UnitTypeStrength(attacker.Type)
                * MoraleMultiplier(morale)
                * ExperienceMultiplier(xp)
                * RandomRoll(rng);

            foreach (var target in targetStacks)
            {
                if (target.Strength <= 0) continue;
                if (target.Id == attacker.Id) continue;

                var frac = CombatStats.DamageFraction(attacker.Type, target.Type);
                if (frac <= 0) continue;

                var dmg = attackerEff * frac;
                casualties.Add(new StackCasualty(target.Id, ClampLoss(dmg, target.Strength)));
            }
        }

        // No "winner" semantics on an air strike — it's a raid.
        return new BattleOutcome(WinnerPlayerId: null, Casualties: casualties);
    }

    // -------------- helpers --------------

    private static double TotalEffectiveStrength(IEnumerable<Unit> stacks, IRandomSource rng) =>
        stacks.Sum(s => EffectiveStrength(s, rng));

    private static double EffectiveStrength(Unit u, IRandomSource rng) =>
        u.Strength
        * CombatStats.UnitTypeStrength(u.Type)
        * MoraleMultiplier(u.Morale)
        * ExperienceMultiplier(u.Experience)
        * RandomRoll(rng); // terrainMult deferred (always 1.0 in MVP)

    private static double MoraleMultiplier(int morale) =>
        0.5 + 0.5 * (Math.Clamp(morale, 0, 100) / 100.0);

    private static double ExperienceMultiplier(int xp) =>
        1.0 + 0.005 * Math.Clamp(xp, 0, 100);

    private static double RandomRoll(IRandomSource rng) =>
        0.85 + rng.NextDouble() * 0.30; // 0.85..1.15

    private static bool HasCombinedArms(IEnumerable<Unit> stacks)
    {
        bool ground = false, air = false, antiair = false;
        foreach (var s in stacks)
        {
            if (s.Strength <= 0) continue;
            if (CombatStats.IsGround(s.Type)) ground = true;
            if (CombatStats.IsAir(s.Type)) air = true;
            if (CombatStats.IsAntiAir(s.Type)) antiair = true;
        }
        return ground && air && antiair;
    }

    private static int ClampLoss(double damage, int targetStrength) =>
        (int)Math.Max(0, Math.Min(targetStrength, Math.Round(damage)));

    private static int FindStrength(IEnumerable<Unit> stacks, Guid id) =>
        stacks.First(s => s.Id == id).Strength;

    /// <summary>
    /// For each (attackerStack, targetStack) pair the matrix allows, distribute attacker
    /// effective strength across targets weighted by target's effective-strength share.
    /// Produces a list of (targetId, damage) entries (one per target, summed across attackers).
    /// </summary>
    private static List<(Guid TargetId, double Damage)> ComputePairwiseDamage(
        IReadOnlyList<Unit> attackers,
        IReadOnlyList<Unit> targets,
        IRandomSource rng)
    {
        var byTarget = new Dictionary<Guid, double>(targets.Count);
        foreach (var t in targets) byTarget[t.Id] = 0.0;

        if (attackers.Count == 0 || targets.Count == 0) return byTarget.Select(kv => (kv.Key, kv.Value)).ToList();

        // For each attacker stack: distribute its (eff * frac) damage proportionally over targets
        // it can engage, weighted by target.Strength so big stacks soak more.
        foreach (var a in attackers)
        {
            if (a.Strength <= 0) continue;
            var aEff = EffectiveStrength(a, rng);

            // engageable targets: (target, frac) where frac > 0 and target alive.
            var eligible = new List<(Unit Target, double Frac)>();
            foreach (var t in targets)
            {
                if (t.Strength <= 0) continue;
                var frac = CombatStats.DamageFraction(a.Type, t.Type);
                if (frac > 0) eligible.Add((t, frac));
            }
            if (eligible.Count == 0) continue;

            var totalTargetWeight = eligible.Sum(e => (double)e.Target.Strength);
            if (totalTargetWeight <= 0) continue;

            foreach (var (target, frac) in eligible)
            {
                var share = target.Strength / totalTargetWeight;
                var dmg = aEff * frac * share;
                byTarget[target.Id] += dmg;
            }
        }

        return byTarget.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}

using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Combat;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Step 7 of the tick pipeline (docs/07 §"CombatStep"). Runs after MovementStep so that
/// attackers who Moved or Attacked into a defended province this tick are co-located with
/// the defender, and after AirStrikeStep so air casualties are already applied.
/// <para/>
/// MVP: only ground units participate in CombatStep (air units never sit in a province
/// owner-share the way ground stacks do — they raid via AirStrikeStep). For each province
/// where ground units of more than one player are present, we resolve a single attacker-vs-defender
/// engagement: the defender is the province owner (or the largest non-owner side if neutral),
/// the attacker is the side that arrived this tick (or the largest other side if multiple).
/// Multi-side melee is Phase 2.
/// <para/>
/// Province capture: if after combat the defender's ground stacks at the province total 0
/// strength AND the attacker has surviving ground strength, ownership flips to the attacker.
/// </summary>
public sealed class CombatStep : ITickStep
{
    public string Name => "Combat";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Group living ground units by province.
        var byProvince = context.Units
            .Where(u => u.Strength > 0
                && u.LocationProvinceId.HasValue
                && u.Domain == UnitDomain.Ground)
            .GroupBy(u => u.LocationProvinceId!.Value);

        foreach (var grp in byProvince)
        {
            var provinceId = grp.Key;
            var stacks = grp.ToList();

            // Multi-owner check.
            var owners = stacks.Select(s => s.OwnerPlayerId).Distinct().ToList();
            if (owners.Count < 2) continue;

            var province = context.World.Provinces.FirstOrDefault(p => p.Id == provinceId);
            if (province is null) continue;

            // Defender: province owner if present in the stack list; otherwise the
            // owner with the largest total strength.
            var defenderPlayerId =
                province.OwnerPlayerId.HasValue && owners.Contains(province.OwnerPlayerId.Value)
                    ? province.OwnerPlayerId.Value
                    : owners
                        .Select(o => (Owner: o, Strength: stacks.Where(s => s.OwnerPlayerId == o).Sum(s => s.Strength)))
                        .OrderByDescending(x => x.Strength)
                        .First().Owner;

            // Attacker: the largest other-owner side. (Multi-attacker free-for-all is Phase 2.)
            var attackerPlayerId = owners
                .Where(o => o != defenderPlayerId)
                .Select(o => (Owner: o, Strength: stacks.Where(s => s.OwnerPlayerId == o).Sum(s => s.Strength)))
                .OrderByDescending(x => x.Strength)
                .First().Owner;

            var attackerStacks = stacks.Where(s => s.OwnerPlayerId == attackerPlayerId).ToList();
            var defenderStacks = stacks.Where(s => s.OwnerPlayerId == defenderPlayerId).ToList();

            var preAttackerStrength = attackerStacks.Sum(s => s.Strength);
            var preDefenderStrength = defenderStacks.Sum(s => s.Strength);

            var outcome = CombatResolver.ResolveGround(
                attacker: new CombatResolver.Side(attackerPlayerId, attackerStacks),
                defender: new CombatResolver.Side(defenderPlayerId, defenderStacks),
                rng: context.Rng);

            AirStrikeStep.ApplyCasualties(context, outcome.Casualties, "Combat");

            var postAttacker = attackerStacks.Sum(s => s.Strength);
            var postDefender = defenderStacks.Sum(s => s.Strength);

            context.Events.Add(new CombatResolvedEvent(
                Tick: context.ProcessingTick,
                ProvinceId: provinceId,
                AttackerPlayerId: attackerPlayerId,
                DefenderPlayerId: defenderPlayerId,
                AttackerStrengthLoss: preAttackerStrength - postAttacker,
                DefenderStrengthLoss: preDefenderStrength - postDefender,
                WinnerPlayerId: outcome.WinnerPlayerId));

            // Capture: defender wiped, attacker survives.
            if (postDefender <= 0 && postAttacker > 0)
            {
                var fromOwner = province.OwnerPlayerId;
                province.OwnerPlayerId = attackerPlayerId;
                // Capture morale shock per docs/04: -50 morale if it's the defender's capital
                // is handled in EventStep; here we just apply a -20 to the captured province.
                province.MoraleLevel = Math.Max(0, province.MoraleLevel - 20);

                // Re-link the new owner's nav (if loaded) — keeps in-memory graph consistent.
                var newOwnerPlayer = context.World.Players.FirstOrDefault(p => p.Id == attackerPlayerId);
                if (newOwnerPlayer is not null && !newOwnerPlayer.OwnedProvinces.Any(p => p.Id == province.Id))
                {
                    newOwnerPlayer.OwnedProvinces.Add(province);
                }
                if (fromOwner.HasValue)
                {
                    var oldOwnerPlayer = context.World.Players.FirstOrDefault(p => p.Id == fromOwner.Value);
                    var existing = oldOwnerPlayer?.OwnedProvinces.FirstOrDefault(p => p.Id == province.Id);
                    if (oldOwnerPlayer is not null && existing is not null)
                    {
                        oldOwnerPlayer.OwnedProvinces.Remove(existing);
                    }
                }

                context.Events.Add(new ProvinceCapturedEvent(
                    Tick: context.ProcessingTick,
                    ProvinceId: province.Id,
                    FromPlayerId: fromOwner,
                    ToPlayerId: attackerPlayerId));
            }
        }
    }
}

using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Combat;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Tests.Game.Combat;

public class CombatResolverTests
{
    [Fact]
    public void Resolve_ground_is_deterministic_for_same_seed()
    {
        var (atkA, defA) = TwoSides();
        var (atkB, defB) = TwoSides();

        var rA = CombatResolver.ResolveGround(atkA, defA, new DeterministicRandom(42));
        var rB = CombatResolver.ResolveGround(atkB, defB, new DeterministicRandom(42));

        rA.WinnerPlayerId.Should().Be(rB.WinnerPlayerId);
        rA.Casualties.Select(c => c.StrengthLoss).Should().BeEquivalentTo(rB.Casualties.Select(c => c.StrengthLoss));
    }

    [Fact]
    public void Resolve_ground_emits_casualties_for_both_sides()
    {
        var (attacker, defender) = TwoSides();
        var result = CombatResolver.ResolveGround(attacker, defender, new DeterministicRandom(7));

        result.Casualties.Should().NotBeEmpty();
        result.Casualties.Sum(c => c.StrengthLoss).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Stealth_bomber_stat_multiplier_is_higher_than_combat_drone()
    {
        CombatStats.UnitTypeStrength(UnitType.StealthBomber)
            .Should().BeGreaterThan(CombatStats.UnitTypeStrength(UnitType.CombatDrone));
    }

    [Fact]
    public void Aa_does_zero_damage_to_ground_units_per_matrix()
    {
        CombatStats.DamageFraction(UnitType.AABattery, UnitType.MechInfantry).Should().Be(0.0);
        CombatStats.DamageFraction(UnitType.AABattery, UnitType.MainBattleTank).Should().Be(0.0);
    }

    [Fact]
    public void Mbt_dominates_mech_infantry_in_matrix()
    {
        CombatStats.DamageFraction(UnitType.MainBattleTank, UnitType.MechInfantry)
            .Should().BeGreaterThan(CombatStats.DamageFraction(UnitType.MechInfantry, UnitType.MainBattleTank));
    }

    // ---- Phase 3c: Submarine + ASW asymmetry ----

    [Fact]
    public void Submarine_devastates_carrier_per_matrix()
    {
        CombatStats.DamageFraction(UnitType.Submarine, UnitType.AircraftCarrier).Should().Be(0.20);
    }

    [Fact]
    public void Non_asw_naval_cannot_damage_submarines()
    {
        // Carrier has no ASW capability of its own (only its wings + escorts do).
        CombatStats.DamageFraction(UnitType.AircraftCarrier, UnitType.Submarine).Should().Be(0.0);
        // Ground / AA / fighters have no business hurting subs either.
        CombatStats.DamageFraction(UnitType.MainBattleTank,   UnitType.Submarine).Should().Be(0.0);
        CombatStats.DamageFraction(UnitType.AABattery,        UnitType.Submarine).Should().Be(0.0);
        CombatStats.DamageFraction(UnitType.MultiroleFighter, UnitType.Submarine).Should().Be(0.0);
    }

    [Fact]
    public void Asw_destroyers_outdamage_asw_frigates_vs_submarines()
    {
        CombatStats.DamageFraction(UnitType.Destroyer, UnitType.Submarine)
            .Should().BeGreaterThan(CombatStats.DamageFraction(UnitType.Frigate, UnitType.Submarine));
    }

    [Fact]
    public void IsAsw_includes_only_frigate_and_destroyer()
    {
        CombatStats.IsAsw(UnitType.Frigate).Should().BeTrue();
        CombatStats.IsAsw(UnitType.Destroyer).Should().BeTrue();
        CombatStats.IsAsw(UnitType.AircraftCarrier).Should().BeFalse();
        CombatStats.IsAsw(UnitType.Submarine).Should().BeFalse();
    }

    [Fact]
    public void IsNaval_now_includes_submarine()
    {
        CombatStats.IsNaval(UnitType.Submarine).Should().BeTrue();
    }

    // ---- Phase 3f: defender bonus multiplier (theater commander) ----

    [Fact]
    public void Defender_bonus_increases_defender_outgoing_damage_vs_attacker()
    {
        var (atkBaseline, defBaseline) = TwoSides();
        var (atkBonus,    defBonus)    = TwoSides();

        // Same RNG seed both runs: only difference is the bonus multiplier.
        var baseline = CombatResolver.ResolveGround(atkBaseline, defBaseline,
            new DeterministicRandom(42), defenderBonusMultiplier: 1.0);
        var withBonus = CombatResolver.ResolveGround(atkBonus, defBonus,
            new DeterministicRandom(42), defenderBonusMultiplier: 1.50); // exaggerated for signal

        // Attacker (Alice) casualties should be higher when defender has the bonus.
        var aliceAttackerIds = atkBaseline.Stacks.Select(s => s.Id).ToHashSet();
        var attackerLossBaseline = baseline.Casualties
            .Where(c => aliceAttackerIds.Contains(c.UnitId)).Sum(c => c.StrengthLoss);

        var aliceAttackerIdsBonus = atkBonus.Stacks.Select(s => s.Id).ToHashSet();
        var attackerLossBonus = withBonus.Casualties
            .Where(c => aliceAttackerIdsBonus.Contains(c.UnitId)).Sum(c => c.StrengthLoss);

        attackerLossBonus.Should().BeGreaterThan(attackerLossBaseline,
            "the +50% defender bonus should magnify defender outgoing damage");
    }

    [Fact]
    public void Defender_bonus_default_overload_matches_explicit_one()
    {
        var (atk1, def1) = TwoSides();
        var (atk2, def2) = TwoSides();

        var noOverload = CombatResolver.ResolveGround(atk1, def1, new DeterministicRandom(99));
        var explicit10 = CombatResolver.ResolveGround(atk2, def2, new DeterministicRandom(99),
            defenderBonusMultiplier: 1.0);

        noOverload.WinnerPlayerId.Should().Be(explicit10.WinnerPlayerId);
        noOverload.Casualties.Sum(c => c.StrengthLoss)
            .Should().Be(explicit10.Casualties.Sum(c => c.StrengthLoss));
    }

    [Fact]
    public void Defender_bonus_zero_or_negative_throws()
    {
        var (atk, def) = TwoSides();

        Action zero = () => CombatResolver.ResolveGround(atk, def,
            new DeterministicRandom(1), defenderBonusMultiplier: 0);
        Action negative = () => CombatResolver.ResolveGround(atk, def,
            new DeterministicRandom(1), defenderBonusMultiplier: -1);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- Phase 3g: per-side combined-arms multipliers (combined_arms doctrine) ----

    [Fact]
    public void Combined_arms_multiplier_increase_amplifies_that_sides_damage()
    {
        // Both sides have ground+air+AA so combined-arms triggers for both. Boosting
        // ONLY the defender's multiplier should raise attacker losses; baseline keeps
        // both at 1.20.
        var (atk1, def1) = TwoCombinedArmsSides();
        var (atk2, def2) = TwoCombinedArmsSides();

        var baseline = CombatResolver.ResolveGround(atk1, def1, new DeterministicRandom(11),
            defenderBonusMultiplier: 1.0,
            attackerCombinedArmsMultiplier: 1.20,
            defenderCombinedArmsMultiplier: 1.20);
        var boosted = CombatResolver.ResolveGround(atk2, def2, new DeterministicRandom(11),
            defenderBonusMultiplier: 1.0,
            attackerCombinedArmsMultiplier: 1.20,
            defenderCombinedArmsMultiplier: 1.25);

        var atk1Ids = atk1.Stacks.Select(s => s.Id).ToHashSet();
        var atk2Ids = atk2.Stacks.Select(s => s.Id).ToHashSet();
        var baselineAttackerLoss = baseline.Casualties.Where(c => atk1Ids.Contains(c.UnitId)).Sum(c => c.StrengthLoss);
        var boostedAttackerLoss  = boosted.Casualties.Where(c => atk2Ids.Contains(c.UnitId)).Sum(c => c.StrengthLoss);

        boostedAttackerLoss.Should().BeGreaterThan(baselineAttackerLoss,
            "the higher defender combined-arms multiplier should raise defender outgoing damage");
    }

    [Fact]
    public void Combined_arms_multiplier_zero_or_negative_throws()
    {
        var (atk, def) = TwoSides();

        Action zeroAttacker = () => CombatResolver.ResolveGround(atk, def,
            new DeterministicRandom(1), defenderBonusMultiplier: 1.0,
            attackerCombinedArmsMultiplier: 0, defenderCombinedArmsMultiplier: 1.20);
        Action zeroDefender = () => CombatResolver.ResolveGround(atk, def,
            new DeterministicRandom(1), defenderBonusMultiplier: 1.0,
            attackerCombinedArmsMultiplier: 1.20, defenderCombinedArmsMultiplier: -0.1);

        zeroAttacker.Should().Throw<ArgumentOutOfRangeException>();
        zeroDefender.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static (CombatResolver.Side Attacker, CombatResolver.Side Defender) TwoCombinedArmsSides()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        var attackerStacks = new List<Unit>
        {
            MakeUnit(aliceId, UnitType.MainBattleTank,    1000),
            MakeUnit(aliceId, UnitType.MultiroleFighter,  300),
            MakeUnit(aliceId, UnitType.AABattery,         200),
        };
        var defenderStacks = new List<Unit>
        {
            MakeUnit(bobId, UnitType.MechInfantry,      1500),
            MakeUnit(bobId, UnitType.MultiroleFighter,  300),
            MakeUnit(bobId, UnitType.AABattery,         200),
        };

        return (new CombatResolver.Side(aliceId, attackerStacks), new CombatResolver.Side(bobId, defenderStacks));
    }

    private static (CombatResolver.Side Attacker, CombatResolver.Side Defender) TwoSides()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();

        var attackerStacks = new List<Unit>
        {
            MakeUnit(aliceId, UnitType.MainBattleTank, 1000),
            MakeUnit(aliceId, UnitType.MechInfantry,   1000),
        };
        var defenderStacks = new List<Unit>
        {
            MakeUnit(bobId, UnitType.MechInfantry,   1500),
            MakeUnit(bobId, UnitType.MobileArtillery, 500),
        };

        return (new CombatResolver.Side(aliceId, attackerStacks), new CombatResolver.Side(bobId, defenderStacks));
    }

    private static Unit MakeUnit(Guid ownerId, UnitType type, int strength) => new()
    {
        Id = Guid.NewGuid(),
        OwnerPlayerId = ownerId,
        Type = type,
        Domain = type >= UnitType.ReconDrone ? UnitDomain.Air : UnitDomain.Ground,
        Strength = strength,
        Morale = 100,
        Experience = 0,
    };
}

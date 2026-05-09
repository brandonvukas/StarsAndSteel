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

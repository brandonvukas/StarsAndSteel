using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Orders;

namespace StarsAndSteel.Tests.Game.Orders;

/// <summary>
/// Spot-checks of the static <see cref="BuildCatalog"/>. We don't unit-test every row;
/// just enough to catch typos and to prove the lookup contract holds.
/// </summary>
public sealed class BuildCatalogTests
{
    [Fact]
    public void GetUnit_returns_canonical_MechInfantry_costs()
    {
        var spec = BuildCatalog.GetUnit(UnitType.MechInfantry);

        spec.Domain.Should().Be(UnitDomain.Ground);
        spec.Money.Should().Be(200);
        spec.Steel.Should().Be(100);
        spec.Manpower.Should().Be(100);
        spec.TicksToBuild.Should().Be(5);
        spec.RequiredBuilding.Should().Be(BuildingType.RecruitmentCenter);
    }

    [Fact]
    public void GetUnit_returns_air_unit_with_AirBase_requirement()
    {
        var spec = BuildCatalog.GetUnit(UnitType.CombatDrone);

        spec.Domain.Should().Be(UnitDomain.Air);
        spec.RequiredBuilding.Should().Be(BuildingType.AirBase);
    }

    [Fact]
    public void GetUnit_throws_for_unmapped_type()
    {
        // Phase 2+ MVP: every UnitType currently maps. We assert the contract by passing
        // an obviously bogus enum cast — the lookup table is intentionally exhaustive of
        // the catalogue and any future addition must add a row here too.
        var bogus = (UnitType)9999;

        Action act = () => BuildCatalog.GetUnit(bogus);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetBuilding_returns_RecruitmentCenter_with_known_cost()
    {
        var spec = BuildCatalog.GetBuilding(BuildingType.RecruitmentCenter);

        spec.Money.Should().Be(1000);
        spec.Steel.Should().Be(200);
        spec.Manpower.Should().Be(100);
        spec.TicksToBuild.Should().Be(10);
    }

    [Fact]
    public void GetBuilding_throws_for_non_MVP_type()
    {
        // HardenedBunker is explicitly Phase 2 per docs/04 §"Buildings & defense".
        Action act = () => BuildCatalog.GetBuilding(BuildingType.HardenedBunker);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(UnitType.MechInfantry, true)]
    [InlineData(UnitType.StealthBomber, true)]
    public void IsUnitBuildable_matches_GetUnit(UnitType type, bool expected)
    {
        BuildCatalog.IsUnitBuildable(type).Should().Be(expected);
    }
}

using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Orders;

namespace StarsAndSteel.Tests.Game.Orders;

/// <summary>
/// Pure tests for <see cref="OrderService"/>. No DbContext: we hand-build entities and
/// pass already-loaded graphs.
/// </summary>
public sealed class OrderServiceTests
{
    private readonly OrderService _service = new();
    private const int CurrentTick = 10;

    // ---- Move ----------------------------------------------------------

    [Fact]
    public void ValidateMove_accepts_owned_ground_unit_to_adjacent_province()
    {
        var f = new Fixture();
        var result = _service.ValidateMove(
            f.Alice_MechInf, f.Alice, f.ProvinceB,
            new HashSet<Guid> { f.ProvinceB.Id }, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.UnitOrder!.OrderType.Should().Be(OrderType.Move);
        result.UnitOrder.IssuedAtTick.Should().Be(CurrentTick + 1);
        result.UnitOrder.TargetProvinceId.Should().Be(f.ProvinceB.Id);
        result.UnitOrder.UnitId.Should().Be(f.Alice_MechInf.Id);
    }

    [Fact]
    public void ValidateMove_rejects_unit_owned_by_other_player()
    {
        var f = new Fixture();
        var result = _service.ValidateMove(
            f.Bob_MechInf, f.Alice, f.ProvinceA,
            new HashSet<Guid> { f.ProvinceA.Id }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.UnitNotOwnedByCaller);
    }

    [Fact]
    public void ValidateMove_rejects_air_unit()
    {
        var f = new Fixture();
        var result = _service.ValidateMove(
            f.Alice_CombatDrone, f.Alice, f.ProvinceB,
            new HashSet<Guid> { f.ProvinceB.Id }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.UnitDomainMismatch);
    }

    [Fact]
    public void ValidateMove_rejects_in_transit_unit()
    {
        var f = new Fixture();
        f.Alice_MechInf.IsInTransit = true;
        f.Alice_MechInf.LocationProvinceId = null;

        var result = _service.ValidateMove(
            f.Alice_MechInf, f.Alice, f.ProvinceB,
            new HashSet<Guid> { f.ProvinceB.Id }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.UnitInTransit);
    }

    [Fact]
    public void ValidateMove_rejects_non_adjacent_target()
    {
        var f = new Fixture();
        var result = _service.ValidateMove(
            f.Alice_MechInf, f.Alice, f.ProvinceC,
            new HashSet<Guid> { f.ProvinceB.Id }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.TargetProvinceNotAdjacent);
    }

    [Fact]
    public void ValidateMove_rejects_when_world_ended()
    {
        var f = new Fixture();
        var result = _service.ValidateMove(
            f.Alice_MechInf, f.Alice, f.ProvinceB,
            new HashSet<Guid> { f.ProvinceB.Id }, CurrentTick, GameWorldStatus.Ended);

        result.Rejection.Should().Be(OrderRejectionReason.GameEnded);
    }

    // ---- Attack --------------------------------------------------------

    [Fact]
    public void ValidateAttack_accepts_ground_unit_to_adjacent_target()
    {
        var f = new Fixture();
        var result = _service.ValidateAttack(
            f.Alice_MechInf, f.Alice, f.ProvinceB,
            new HashSet<Guid> { f.ProvinceB.Id }, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.UnitOrder!.OrderType.Should().Be(OrderType.Attack);
    }

    // ---- Air strike ----------------------------------------------------

    [Fact]
    public void ValidateAirStrike_accepts_air_unit_at_AirBase()
    {
        var f = new Fixture();
        var hosting = new[] { new Building { Type = BuildingType.AirBase, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateAirStrike(
            f.Alice_CombatDrone, f.Alice, f.ProvinceC, hosting, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.UnitOrder!.OrderType.Should().Be(OrderType.AirStrike);
    }

    [Fact]
    public void ValidateAirStrike_rejects_when_no_AirBase_at_hosting_province()
    {
        var f = new Fixture();
        var hosting = new[] { new Building { Type = BuildingType.MilitaryBase, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateAirStrike(
            f.Alice_CombatDrone, f.Alice, f.ProvinceC, hosting, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.AirUnitNotAtAirBase);
    }

    [Fact]
    public void ValidateAirStrike_rejects_ground_unit()
    {
        var f = new Fixture();
        var hosting = new[] { new Building { Type = BuildingType.AirBase, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateAirStrike(
            f.Alice_MechInf, f.Alice, f.ProvinceC, hosting, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.UnitDomainMismatch);
    }

    // ---- Build unit ----------------------------------------------------

    [Fact]
    public void ValidateBuildUnit_accepts_when_required_building_present_and_resources_sufficient()
    {
        var f = new Fixture();
        var rc = new Building { Type = BuildingType.RecruitmentCenter, ProvinceId = f.ProvinceA.Id };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, quantity: 1000,
            new[] { rc }, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.ConstructionOrder!.OrderType.Should().Be(OrderType.BuildUnit);
        result.ConstructionOrder.UnitType.Should().Be(UnitType.MechInfantry);
        result.ConstructionOrder.Quantity.Should().Be(1000);
        result.ConstructionOrder.TicksRemaining.Should().Be(5);
        result.ConstructionOrder.IssuedAtTick.Should().Be(CurrentTick + 1);
    }

    [Fact]
    public void ValidateBuildUnit_rejects_when_province_not_owned()
    {
        var f = new Fixture();
        var rc = new Building { Type = BuildingType.RecruitmentCenter, ProvinceId = f.ProvinceB.Id };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceB, UnitType.MechInfantry, 1000,
            new[] { rc }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.ProvinceNotOwnedByCaller);
    }

    [Fact]
    public void ValidateBuildUnit_rejects_when_required_building_missing()
    {
        var f = new Fixture();
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, 1000,
            Array.Empty<Building>(), CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.RequiredBuildingMissing);
    }

    [Fact]
    public void ValidateBuildUnit_rejects_when_resources_insufficient()
    {
        var f = new Fixture();
        f.Alice.Money = 0; // Mech inf needs 200 money / 1000 strength.
        var rc = new Building { Type = BuildingType.RecruitmentCenter, ProvinceId = f.ProvinceA.Id };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, 1000,
            new[] { rc }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.InsufficientResources);
    }

    [Fact]
    public void ValidateBuildUnit_rejects_quantity_out_of_range()
    {
        var f = new Fixture();
        var rc = new Building { Type = BuildingType.RecruitmentCenter, ProvinceId = f.ProvinceA.Id };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, 0,
            new[] { rc }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.QuantityOutOfRange);
    }

    [Fact]
    public void ValidateBuildUnit_scales_cost_linearly_with_quantity()
    {
        var f = new Fixture();
        // Mech inf costs 200 money / 1000 strength. 2000 strength = 400 money.
        // Give Alice exactly 399 money — should fail.
        f.Alice.Money = 399;
        // Inflate other resources so they're not the bottleneck.
        f.Alice.Steel = 1_000_000;
        f.Alice.Manpower = 1_000_000;

        var rc = new Building { Type = BuildingType.RecruitmentCenter, ProvinceId = f.ProvinceA.Id };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, 2000,
            new[] { rc }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.InsufficientResources);
    }

    // ---- Build building -----------------------------------------------

    [Fact]
    public void ValidateBuildBuilding_accepts_owned_province_with_resources()
    {
        var f = new Fixture();
        var result = _service.ValidateBuildBuilding(
            f.Alice, f.ProvinceA, BuildingType.SteelMill,
            CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.ConstructionOrder!.BuildingType.Should().Be(BuildingType.SteelMill);
        result.ConstructionOrder.TicksRemaining.Should().Be(12);
    }

    [Fact]
    public void ValidateBuildBuilding_rejects_non_MVP_building_type()
    {
        var f = new Fixture();
        var result = _service.ValidateBuildBuilding(
            f.Alice, f.ProvinceA, BuildingType.HardenedBunker,
            CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.BuildingNotInCatalogue);
    }

    [Fact]
    public void ValidateBuildBuilding_rejects_when_resources_insufficient()
    {
        var f = new Fixture();
        f.Alice.Money = 0; // SteelMill needs 1500.

        var result = _service.ValidateBuildBuilding(
            f.Alice, f.ProvinceA, BuildingType.SteelMill,
            CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.InsufficientResources);
    }

    [Fact]
    public void DebitForBuild_subtracts_unit_costs_scaled_by_quantity()
    {
        var f = new Fixture();
        f.Alice.Money = 1000;
        f.Alice.Steel = 1000;
        f.Alice.Manpower = 1000;

        var order = new ConstructionOrder
        {
            OrderType = OrderType.BuildUnit,
            UnitType = UnitType.MechInfantry,
            Quantity = 2000,
        };

        OrderService.DebitForBuild(f.Alice, order);

        f.Alice.Money.Should().Be(1000 - 400);
        f.Alice.Steel.Should().Be(1000 - 200);
        f.Alice.Manpower.Should().Be(1000 - 200);
    }

    [Fact]
    public void DebitForBuild_subtracts_building_costs()
    {
        var f = new Fixture();
        f.Alice.Money = 5000;
        f.Alice.Steel = 1000;
        f.Alice.Manpower = 1000;

        var order = new ConstructionOrder
        {
            OrderType = OrderType.BuildBuilding,
            BuildingType = BuildingType.SteelMill,
        };

        OrderService.DebitForBuild(f.Alice, order);

        f.Alice.Money.Should().Be(5000 - 1500);
        f.Alice.Steel.Should().Be(1000 - 100);
        f.Alice.Manpower.Should().Be(1000 - 100);
    }

    // ---- Test fixture --------------------------------------------------

    private sealed class Fixture
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public Player Alice { get; }
        public Player Bob { get; }
        public Province ProvinceA { get; } // owned by Alice
        public Province ProvinceB { get; } // owned by Bob, adjacent to A
        public Province ProvinceC { get; } // neutral, distant
        public Unit Alice_MechInf { get; }
        public Unit Alice_CombatDrone { get; }
        public Unit Bob_MechInf { get; }

        public Fixture()
        {
            Alice = new Player
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, NationName = "Alice",
                FlagPrimaryHex = "#000000", FlagSecondaryHex = "#ffffff",
                Money = 100_000, Oil = 100_000, Steel = 100_000,
                Electronics = 100_000, Food = 100_000, Manpower = 100_000,
            };
            Bob = new Player
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, NationName = "Bob",
                FlagPrimaryHex = "#111111", FlagSecondaryHex = "#222222",
            };

            ProvinceA = new Province
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, Name = "A",
                Type = ProvinceType.Capital, OwnerPlayerId = Alice.Id,
            };
            ProvinceB = new Province
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, Name = "B",
                Type = ProvinceType.Industrial, OwnerPlayerId = Bob.Id,
            };
            ProvinceC = new Province
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, Name = "C",
                Type = ProvinceType.Resource, OwnerPlayerId = null,
            };

            Alice_MechInf = new Unit
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, OwnerPlayerId = Alice.Id,
                Type = UnitType.MechInfantry, Domain = UnitDomain.Ground,
                Strength = 1000, Morale = 100, LocationProvinceId = ProvinceA.Id,
            };
            Alice_CombatDrone = new Unit
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, OwnerPlayerId = Alice.Id,
                Type = UnitType.CombatDrone, Domain = UnitDomain.Air,
                Strength = 500, Morale = 100, LocationProvinceId = ProvinceA.Id,
            };
            Bob_MechInf = new Unit
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, OwnerPlayerId = Bob.Id,
                Type = UnitType.MechInfantry, Domain = UnitDomain.Ground,
                Strength = 1000, Morale = 100, LocationProvinceId = ProvinceB.Id,
            };
        }
    }
}

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
            f.Alice_CombatDrone, f.Alice, f.ProvinceC, hosting, Array.Empty<Unit>(), CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.UnitOrder!.OrderType.Should().Be(OrderType.AirStrike);
    }

    [Fact]
    public void ValidateAirStrike_rejects_when_no_AirBase_at_hosting_province()
    {
        var f = new Fixture();
        var hosting = new[] { new Building { Type = BuildingType.MilitaryBase, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateAirStrike(
            f.Alice_CombatDrone, f.Alice, f.ProvinceC, hosting, Array.Empty<Unit>(), CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.AirUnitNotAtAirBase);
    }

    [Fact]
    public void ValidateAirStrike_rejects_ground_unit()
    {
        var f = new Fixture();
        var hosting = new[] { new Building { Type = BuildingType.AirBase, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateAirStrike(
            f.Alice_MechInf, f.Alice, f.ProvinceC, hosting, Array.Empty<Unit>(), CurrentTick, GameWorldStatus.Active);

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
            new[] { rc }, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

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
            new[] { rc }, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.ProvinceNotOwnedByCaller);
    }

    [Fact]
    public void ValidateBuildUnit_rejects_when_required_building_missing()
    {
        var f = new Fixture();
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, 1000,
            Array.Empty<Building>(), Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

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
            new[] { rc }, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.InsufficientResources);
    }

    [Fact]
    public void ValidateBuildUnit_rejects_quantity_out_of_range()
    {
        var f = new Fixture();
        var rc = new Building { Type = BuildingType.RecruitmentCenter, ProvinceId = f.ProvinceA.Id };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, 0,
            new[] { rc }, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

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
            new[] { rc }, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.InsufficientResources);
    }

    // ---- Phase 2b: Carrier wing capacity & embarked-wing air strike ---

    [Fact]
    public void ValidateBuildUnit_CarrierAirWing_rejects_when_no_friendly_carrier_at_province()
    {
        var f = new Fixture();
        f.ProvinceA.IsCoastal = true;
        var nyard = new Building { Type = BuildingType.NavalYard, ProvinceId = f.ProvinceA.Id };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.CarrierAirWing, 1,
            new[] { nyard }, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.NoCarrierWithSpareCapacity);
    }

    [Fact]
    public void ValidateBuildUnit_CarrierAirWing_accepts_when_friendly_carrier_has_spare_slot()
    {
        var f = new Fixture();
        f.ProvinceA.IsCoastal = true;
        var nyard = new Building { Type = BuildingType.NavalYard, ProvinceId = f.ProvinceA.Id };
        var carrier = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.AircraftCarrier, Domain = UnitDomain.Naval,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.CarrierAirWing, 1,
            new[] { nyard }, new[] { carrier }, Array.Empty<ConstructionOrder>(), CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void ValidateBuildUnit_CarrierAirWing_rejects_when_capacity_full_including_pending_orders()
    {
        var f = new Fixture();
        f.ProvinceA.IsCoastal = true;
        var nyard = new Building { Type = BuildingType.NavalYard, ProvinceId = f.ProvinceA.Id };
        var carrier = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.AircraftCarrier, Domain = UnitDomain.Naval,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };
        // 2 wings already embarked + 2 in-flight build orders = 4 = full capacity.
        var wings = Enumerable.Range(0, 2).Select(_ => new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.CarrierAirWing, Domain = UnitDomain.Air, ParentUnitId = carrier.Id,
            Strength = 500, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        }).ToArray();
        var pending = Enumerable.Range(0, 2).Select(_ => new ConstructionOrder
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            ProvinceId = f.ProvinceA.Id, OrderType = OrderType.BuildUnit,
            UnitType = UnitType.CarrierAirWing, Quantity = 1,
            IssuedAtTick = CurrentTick, TicksRemaining = 5, Status = OrderStatus.Pending,
        }).ToArray();

        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.CarrierAirWing, 1,
            new[] { nyard }, new Unit[] { carrier }.Concat(wings).ToArray(), pending, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.NoCarrierWithSpareCapacity);
    }

    [Fact]
    public void ValidateAirStrike_CarrierAirWing_accepts_without_AirBase_when_parent_carrier_present()
    {
        var f = new Fixture();
        var carrier = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.AircraftCarrier, Domain = UnitDomain.Naval,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };
        var wing = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.CarrierAirWing, Domain = UnitDomain.Air, ParentUnitId = carrier.Id,
            Strength = 500, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };

        // No AirBase building at province — wings sortie from the carrier instead.
        var result = _service.ValidateAirStrike(
            wing, f.Alice, f.ProvinceC, Array.Empty<Building>(),
            new[] { carrier, wing }, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.UnitOrder!.OrderType.Should().Be(OrderType.AirStrike);
    }

    [Fact]
    public void ValidateAirStrike_CarrierAirWing_rejects_when_parent_carrier_missing_at_location()
    {
        var f = new Fixture();
        // Wing's parent id points at a carrier that's NOT in hostingUnits (sunk or moved).
        var wing = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.CarrierAirWing, Domain = UnitDomain.Air, ParentUnitId = Guid.NewGuid(),
            Strength = 500, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };

        var result = _service.ValidateAirStrike(
            wing, f.Alice, f.ProvinceC, Array.Empty<Building>(),
            new[] { wing }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.AirUnitNotAtAirBase);
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

    // ---- MissileLaunch (Phase 3a) -------------------------------------

    [Fact]
    public void ValidateMissileLaunch_accepts_owned_missile_with_silo_at_launch_province()
    {
        var f = new Fixture();
        var silos = new[] { new Building
        {
            Id = Guid.NewGuid(), ProvinceId = f.ProvinceA.Id, Province = f.ProvinceA,
            Type = BuildingType.MissileSilo, Level = 1, ConstructedAtTick = 0,
        } };

        var result = _service.ValidateMissileLaunch(
            f.Alice_Cruise, f.Alice, f.ProvinceA, f.ProvinceB,
            silos, nukesEnabledForWorld: true, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.UnitOrder!.OrderType.Should().Be(OrderType.MissileLaunch);
        result.UnitOrder.TargetProvinceId.Should().Be(f.ProvinceB.Id);
        result.UnitOrder.UnitId.Should().Be(f.Alice_Cruise.Id);
    }

    [Fact]
    public void ValidateMissileLaunch_rejects_when_launch_province_has_no_silo()
    {
        var f = new Fixture();
        // No buildings on ProvinceA — silo missing.
        var result = _service.ValidateMissileLaunch(
            f.Alice_Cruise, f.Alice, f.ProvinceA, f.ProvinceB,
            Array.Empty<Building>(), nukesEnabledForWorld: true, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.MissileSiloMissing);
    }

    [Fact]
    public void ValidateMissileLaunch_rejects_nuclear_when_world_disables_nukes()
    {
        var f = new Fixture();
        var silos = new[] { new Building
        {
            Id = Guid.NewGuid(), ProvinceId = f.ProvinceA.Id, Province = f.ProvinceA,
            Type = BuildingType.MissileSilo, Level = 1, ConstructedAtTick = 0,
        } };

        var result = _service.ValidateMissileLaunch(
            f.Alice_Nuke, f.Alice, f.ProvinceA, f.ProvinceB,
            silos, nukesEnabledForWorld: false, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.NukesDisabledForWorld);
    }

    [Fact]
    public void ValidateMissileLaunch_allows_cruise_even_when_nukes_disabled()
    {
        var f = new Fixture();
        var silos = new[] { new Building
        {
            Id = Guid.NewGuid(), ProvinceId = f.ProvinceA.Id, Province = f.ProvinceA,
            Type = BuildingType.MissileSilo, Level = 1, ConstructedAtTick = 0,
        } };

        var result = _service.ValidateMissileLaunch(
            f.Alice_Cruise, f.Alice, f.ProvinceA, f.ProvinceB,
            silos, nukesEnabledForWorld: false, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void ValidateMissileLaunch_rejects_non_missile_unit()
    {
        var f = new Fixture();
        var silos = new[] { new Building
        {
            Id = Guid.NewGuid(), ProvinceId = f.ProvinceA.Id, Province = f.ProvinceA,
            Type = BuildingType.MissileSilo, Level = 1, ConstructedAtTick = 0,
        } };

        var result = _service.ValidateMissileLaunch(
            f.Alice_MechInf, f.Alice, f.ProvinceA, f.ProvinceB,
            silos, nukesEnabledForWorld: true, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.UnitDomainMismatch);
    }

    [Fact]
    public void ValidateMissileLaunch_rejects_unit_owned_by_other_player()
    {
        var f = new Fixture();
        var silos = new[] { new Building
        {
            Id = Guid.NewGuid(), ProvinceId = f.ProvinceA.Id, Province = f.ProvinceA,
            Type = BuildingType.MissileSilo, Level = 1, ConstructedAtTick = 0,
        } };
        // Bob tries to launch Alice's missile.
        var result = _service.ValidateMissileLaunch(
            f.Alice_Cruise, f.Bob, f.ProvinceA, f.ProvinceB,
            silos, nukesEnabledForWorld: true, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.UnitNotOwnedByCaller);
    }

    // ---- BuildUnit tech gating (Phase 3b) -----------------------------

    [Fact]
    public void ValidateBuildUnit_rejects_stealth_bomber_when_tech_not_unlocked()
    {
        var f = new Fixture();
        var airBase = new[] { new Building { Type = BuildingType.AirBase, ProvinceId = f.ProvinceA.Id } };
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.StealthBomber, quantity: 1000,
            airBase, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(),
            CurrentTick, GameWorldStatus.Active,
            unlockedTechIds: Array.Empty<string>());

        result.Rejection.Should().Be(OrderRejectionReason.RequiredTechMissing);
    }

    [Fact]
    public void ValidateBuildUnit_accepts_stealth_bomber_when_tech_unlocked()
    {
        var f = new Fixture();
        var airBase = new[] { new Building { Type = BuildingType.AirBase, ProvinceId = f.ProvinceA.Id } };
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.StealthBomber, quantity: 1000,
            airBase, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(),
            CurrentTick, GameWorldStatus.Active,
            unlockedTechIds: new[] { "stealth_systems" });

        result.IsAccepted.Should().BeTrue();
        result.ConstructionOrder!.UnitType.Should().Be(UnitType.StealthBomber);
    }

    [Fact]
    public void ValidateBuildUnit_accepts_stealth_drone_when_drone_tech_unlocked()
    {
        var f = new Fixture();
        var airBase = new[] { new Building { Type = BuildingType.AirBase, ProvinceId = f.ProvinceA.Id } };
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.StealthDrone, quantity: 1000,
            airBase, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(),
            CurrentTick, GameWorldStatus.Active,
            unlockedTechIds: new[] { "stealth_drones" });

        result.IsAccepted.Should().BeTrue();
        result.ConstructionOrder!.UnitType.Should().Be(UnitType.StealthDrone);
    }

    [Fact]
    public void ValidateBuildUnit_unrelated_tech_does_not_unlock_stealth_bomber()
    {
        var f = new Fixture();
        var airBase = new[] { new Building { Type = BuildingType.AirBase, ProvinceId = f.ProvinceA.Id } };
        // Caller has unlocked OTHER techs but not stealth_systems.
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.StealthBomber, quantity: 1000,
            airBase, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(),
            CurrentTick, GameWorldStatus.Active,
            unlockedTechIds: new[] { "advanced_armor", "smart_munitions", "stealth_drones" });

        result.Rejection.Should().Be(OrderRejectionReason.RequiredTechMissing);
    }

    [Fact]
    public void ValidateBuildUnit_non_gated_unit_ignores_tech_list()
    {
        var f = new Fixture();
        var rc = new[] { new Building { Type = BuildingType.RecruitmentCenter, ProvinceId = f.ProvinceA.Id } };
        // Mech infantry has no RequiredTechId — should accept with empty tech list.
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.MechInfantry, quantity: 1000,
            rc, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(),
            CurrentTick, GameWorldStatus.Active,
            unlockedTechIds: Array.Empty<string>());

        result.IsAccepted.Should().BeTrue();
    }

    // ---- BuildUnit tech gating: Submarine (Phase 3c) -----------------

    [Fact]
    public void ValidateBuildUnit_rejects_submarine_when_tech_not_unlocked()
    {
        var f = new Fixture();
        f.ProvinceA.IsCoastal = true;
        var nyard = new[] { new Building { Type = BuildingType.NavalYard, ProvinceId = f.ProvinceA.Id } };
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.Submarine, quantity: 100,
            nyard, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(),
            CurrentTick, GameWorldStatus.Active,
            unlockedTechIds: Array.Empty<string>());

        result.Rejection.Should().Be(OrderRejectionReason.RequiredTechMissing);
    }

    [Fact]
    public void ValidateBuildUnit_accepts_submarine_when_submarine_warfare_unlocked()
    {
        var f = new Fixture();
        f.ProvinceA.IsCoastal = true;
        var nyard = new[] { new Building { Type = BuildingType.NavalYard, ProvinceId = f.ProvinceA.Id } };
        var result = _service.ValidateBuildUnit(
            f.Alice, f.ProvinceA, UnitType.Submarine, quantity: 100,
            nyard, Array.Empty<Unit>(), Array.Empty<ConstructionOrder>(),
            CurrentTick, GameWorldStatus.Active,
            unlockedTechIds: new[] { "submarine_warfare" });

        result.IsAccepted.Should().BeTrue();
        result.ConstructionOrder!.UnitType.Should().Be(UnitType.Submarine);
    }

    // ---- CyberAttack (Phase 3d) ---------------------------------------

    [Fact]
    public void ValidateCyberAttack_accepts_when_all_prereqs_met()
    {
        var f = new Fixture();
        var ops = new[] { new Building { Type = BuildingType.CyberOperationsCenter, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateCyberAttack(
            f.Alice, f.ProvinceA, f.ProvinceB, ops,
            unlockedTechIds: new[] { "cyber_warfare" },
            CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.CyberAttackOrder!.AttackerPlayerId.Should().Be(f.Alice.Id);
        result.CyberAttackOrder.LaunchProvinceId.Should().Be(f.ProvinceA.Id);
        result.CyberAttackOrder.TargetProvinceId.Should().Be(f.ProvinceB.Id);
        result.CyberAttackOrder.IssuedAtTick.Should().Be(CurrentTick + 1);
        result.CyberAttackOrder.EffectKind.Should().BeNull(); // rolled at resolve time
        result.CyberAttackOrder.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void ValidateCyberAttack_rejects_when_world_ended()
    {
        var f = new Fixture();
        var ops = new[] { new Building { Type = BuildingType.CyberOperationsCenter, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateCyberAttack(
            f.Alice, f.ProvinceA, f.ProvinceB, ops,
            new[] { "cyber_warfare" }, CurrentTick, GameWorldStatus.Ended);

        result.Rejection.Should().Be(OrderRejectionReason.GameEnded);
    }

    [Fact]
    public void ValidateCyberAttack_rejects_when_launch_province_not_owned()
    {
        var f = new Fixture();
        // Bob tries to launch from Alice's province.
        var ops = new[] { new Building { Type = BuildingType.CyberOperationsCenter, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateCyberAttack(
            f.Bob, f.ProvinceA, f.ProvinceB, ops,
            new[] { "cyber_warfare" }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.ProvinceNotOwnedByCaller);
    }

    [Fact]
    public void ValidateCyberAttack_rejects_when_cyber_ops_center_missing()
    {
        var f = new Fixture();
        var result = _service.ValidateCyberAttack(
            f.Alice, f.ProvinceA, f.ProvinceB, Array.Empty<Building>(),
            new[] { "cyber_warfare" }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.CyberOpsCenterMissing);
    }

    [Fact]
    public void ValidateCyberAttack_rejects_when_cyber_warfare_tech_missing()
    {
        var f = new Fixture();
        var ops = new[] { new Building { Type = BuildingType.CyberOperationsCenter, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateCyberAttack(
            f.Alice, f.ProvinceA, f.ProvinceB, ops,
            unlockedTechIds: Array.Empty<string>(),
            CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.RequiredTechMissing);
    }

    [Fact]
    public void ValidateCyberAttack_rejects_when_target_unowned()
    {
        var f = new Fixture();
        var ops = new[] { new Building { Type = BuildingType.CyberOperationsCenter, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateCyberAttack(
            f.Alice, f.ProvinceA, f.ProvinceC, ops,
            new[] { "cyber_warfare" }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.CyberTargetUnowned);
    }

    [Fact]
    public void ValidateCyberAttack_rejects_when_target_is_own_province()
    {
        var f = new Fixture();
        // Make ProvinceC owned by Alice; she can't cyber herself.
        f.ProvinceC.OwnerPlayerId = f.Alice.Id;
        var ops = new[] { new Building { Type = BuildingType.CyberOperationsCenter, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateCyberAttack(
            f.Alice, f.ProvinceA, f.ProvinceC, ops,
            new[] { "cyber_warfare" }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.CyberCannotTargetSelf);
    }

    [Fact]
    public void ValidateCyberAttack_rejects_when_resources_insufficient()
    {
        var f = new Fixture();
        f.Alice.Money = 0; // < 500 cost
        var ops = new[] { new Building { Type = BuildingType.CyberOperationsCenter, ProvinceId = f.ProvinceA.Id } };

        var result = _service.ValidateCyberAttack(
            f.Alice, f.ProvinceA, f.ProvinceB, ops,
            new[] { "cyber_warfare" }, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.InsufficientResources);
    }

    [Fact]
    public void DebitForCyberAttack_subtracts_money_and_electronics()
    {
        var f = new Fixture();
        f.Alice.Money = 1000;
        f.Alice.Electronics = 1000;

        OrderService.DebitForCyberAttack(f.Alice);

        f.Alice.Money.Should().Be(1000 - OrderService.CyberAttackMoneyCost);
        f.Alice.Electronics.Should().Be(1000 - OrderService.CyberAttackElectronicsCost);
    }

    // ---- Sabotage (Phase 3e) ------------------------------------------

    [Fact]
    public void ValidateSabotage_accepts_SF_targeting_adjacent_enemy_with_buildings()
    {
        var f = new Fixture();
        var sf = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.SpecialForces, Domain = UnitDomain.Ground,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };
        var targetBuildings = new[] { new Building { Id = Guid.NewGuid(), Type = BuildingType.SteelMill, ProvinceId = f.ProvinceB.Id } };

        var result = _service.ValidateSabotage(
            sf, f.Alice, f.ProvinceB, new HashSet<Guid> { f.ProvinceB.Id },
            targetBuildings, CurrentTick, GameWorldStatus.Active);

        result.IsAccepted.Should().BeTrue();
        result.UnitOrder!.OrderType.Should().Be(OrderType.Sabotage);
        result.UnitOrder.UnitId.Should().Be(sf.Id);
        result.UnitOrder.TargetProvinceId.Should().Be(f.ProvinceB.Id);
        result.UnitOrder.IssuedAtTick.Should().Be(CurrentTick + 1);
    }

    [Fact]
    public void ValidateSabotage_rejects_non_special_forces_unit()
    {
        var f = new Fixture();
        var targetBuildings = new[] { new Building { Type = BuildingType.SteelMill, ProvinceId = f.ProvinceB.Id } };

        var result = _service.ValidateSabotage(
            f.Alice_MechInf, f.Alice, f.ProvinceB, new HashSet<Guid> { f.ProvinceB.Id },
            targetBuildings, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.SabotageRequiresSpecialForces);
    }

    [Fact]
    public void ValidateSabotage_rejects_non_adjacent_target()
    {
        var f = new Fixture();
        var sf = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.SpecialForces, Domain = UnitDomain.Ground,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };
        var targetBuildings = new[] { new Building { Type = BuildingType.SteelMill, ProvinceId = f.ProvinceC.Id } };

        var result = _service.ValidateSabotage(
            sf, f.Alice, f.ProvinceC, new HashSet<Guid> { f.ProvinceB.Id },
            targetBuildings, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.TargetProvinceNotAdjacent);
    }

    [Fact]
    public void ValidateSabotage_rejects_target_owned_by_caller()
    {
        var f = new Fixture();
        var sf = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.SpecialForces, Domain = UnitDomain.Ground,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };
        // Make ProvinceB owned by Alice — same player, should reject.
        f.ProvinceB.OwnerPlayerId = f.Alice.Id;
        var targetBuildings = new[] { new Building { Type = BuildingType.SteelMill, ProvinceId = f.ProvinceB.Id } };

        var result = _service.ValidateSabotage(
            sf, f.Alice, f.ProvinceB, new HashSet<Guid> { f.ProvinceB.Id },
            targetBuildings, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.SabotageTargetNotEnemy);
    }

    [Fact]
    public void ValidateSabotage_rejects_target_with_no_buildings()
    {
        var f = new Fixture();
        var sf = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Alice.Id,
            Type = UnitType.SpecialForces, Domain = UnitDomain.Ground,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceA.Id,
        };

        var result = _service.ValidateSabotage(
            sf, f.Alice, f.ProvinceB, new HashSet<Guid> { f.ProvinceB.Id },
            Array.Empty<Building>(), CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.SabotageTargetHasNoBuildings);
    }

    [Fact]
    public void ValidateSabotage_rejects_unit_owned_by_other_player()
    {
        var f = new Fixture();
        var bobSf = new Unit
        {
            Id = Guid.NewGuid(), GameWorldId = f.WorldId, OwnerPlayerId = f.Bob.Id,
            Type = UnitType.SpecialForces, Domain = UnitDomain.Ground,
            Strength = 1000, Morale = 100, LocationProvinceId = f.ProvinceB.Id,
        };
        var targetBuildings = new[] { new Building { Type = BuildingType.SteelMill, ProvinceId = f.ProvinceA.Id } };

        // Alice tries to sabotage with Bob's SF unit.
        var result = _service.ValidateSabotage(
            bobSf, f.Alice, f.ProvinceA, new HashSet<Guid> { f.ProvinceA.Id },
            targetBuildings, CurrentTick, GameWorldStatus.Active);

        result.Rejection.Should().Be(OrderRejectionReason.UnitNotOwnedByCaller);
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
        // Phase 3a: missile stockpiles for MissileLaunch tests.
        public Unit Alice_Cruise { get; }
        public Unit Alice_Nuke { get; }

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
            Alice_Cruise = new Unit
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, OwnerPlayerId = Alice.Id,
                Type = UnitType.CruiseMissile, Domain = UnitDomain.Missile,
                Strength = 1, Morale = 100, LocationProvinceId = ProvinceA.Id,
            };
            Alice_Nuke = new Unit
            {
                Id = Guid.NewGuid(), GameWorldId = WorldId, OwnerPlayerId = Alice.Id,
                Type = UnitType.NuclearMissile, Domain = UnitDomain.Missile,
                Strength = 1, Morale = 100, LocationProvinceId = ProvinceA.Id,
            };
        }
    }
}

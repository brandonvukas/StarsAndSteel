using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class ConstructionStepTests
{
    [Fact]
    public void Decrements_ticks_remaining_each_run_and_marks_in_progress()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var capital = AddProvince(world, alice, "Cap");
        AddBuilding(capital, BuildingType.RecruitmentCenter);
        var order = BuildUnitOrder(world, alice, capital, UnitType.MechInfantry, qty: 1000, ticksRemaining: 3);
        var ctx = Context(world, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        order.TicksRemaining.Should().Be(2);
        order.Status.Should().Be(OrderStatus.InProgress);
        ctx.UnitsToInsert.Should().BeEmpty();
    }

    [Fact]
    public void Completes_unit_build_when_ticks_hit_zero()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var capital = AddProvince(world, alice, "Cap");
        AddBuilding(capital, BuildingType.RecruitmentCenter);
        var order = BuildUnitOrder(world, alice, capital, UnitType.MechInfantry, qty: 2000, ticksRemaining: 1);
        var ctx = Context(world, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Complete);
        ctx.UnitsToInsert.Should().ContainSingle()
            .Which.Should().Match<Core.Entities.Unit>(u =>
                u.Type == UnitType.MechInfantry &&
                u.Strength == 2000 &&
                u.LocationProvinceId == capital.Id &&
                u.OwnerPlayerId == alice.Id);
        ctx.Events.OfType<UnitBuiltEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Completes_building_and_attaches_to_province()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var capital = AddProvince(world, alice, "Cap");
        var order = BuildBuildingOrder(world, alice, capital, BuildingType.SteelMill, ticksRemaining: 1);
        var ctx = Context(world, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Complete);
        ctx.BuildingsToInsert.Should().ContainSingle();
        capital.Buildings.Should().Contain(b => b.Type == BuildingType.SteelMill);
        ctx.Events.OfType<BuildingCompletedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Cancels_order_if_province_changed_owners()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var capital = AddProvince(world, bob, "Cap"); // Bob owns it now
        var order = BuildBuildingOrder(world, alice, capital, BuildingType.SteelMill, ticksRemaining: 1);
        var ctx = Context(world, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled);
        ctx.BuildingsToInsert.Should().BeEmpty();
    }

    [Fact]
    public void Air_unit_build_sets_home_base_to_construction_province()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var capital = AddProvince(world, alice, "Cap");
        AddBuilding(capital, BuildingType.AirBase);
        var order = BuildUnitOrder(world, alice, capital, UnitType.CombatDrone, qty: 500, ticksRemaining: 1);
        var ctx = Context(world, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        ctx.UnitsToInsert.Should().ContainSingle()
            .Which.HomeBaseProvinceId.Should().Be(capital.Id);
    }

    [Fact]
    public void CarrierAirWing_completes_with_ParentUnitId_pointing_at_friendly_carrier_at_province()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var port = AddProvince(world, alice, "Port");
        port.IsCoastal = true;
        AddBuilding(port, BuildingType.NavalYard);
        var carrier = AddUnit(world, alice, port, UnitType.AircraftCarrier, 1000);
        var order = BuildUnitOrder(world, alice, port, UnitType.CarrierAirWing, qty: 500, ticksRemaining: 1);
        var ctx = Context(world, units: new List<Core.Entities.Unit> { carrier }, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Complete);
        var built = ctx.UnitsToInsert.Should().ContainSingle().Subject;
        built.Type.Should().Be(UnitType.CarrierAirWing);
        built.ParentUnitId.Should().Be(carrier.Id);
        built.LocationProvinceId.Should().Be(port.Id);
        built.HomeBaseProvinceId.Should().Be(port.Id);
    }

    [Fact]
    public void CarrierAirWing_cancels_when_carrier_left_or_was_sunk_before_completion()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var port = AddProvince(world, alice, "Port");
        port.IsCoastal = true;
        AddBuilding(port, BuildingType.NavalYard);
        // No carrier in context.Units — simulates carrier moving away or sinking.
        var order = BuildUnitOrder(world, alice, port, UnitType.CarrierAirWing, qty: 500, ticksRemaining: 1);
        var ctx = Context(world, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled);
        ctx.UnitsToInsert.Should().BeEmpty();
    }

    // ---- Phase 4b2: Carrier Strike Group wonder ----------------------

    [Fact]
    public void CarrierStrikeGroup_completion_spawns_veteran_carrier_and_two_wings()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var port = AddProvince(world, alice, "Port");
        port.IsCoastal = true;
        var order = BuildBuildingOrder(world, alice, port, BuildingType.CarrierStrikeGroup, ticksRemaining: 1);
        var ctx = Context(world, constructionOrders: new[] { order });

        new ConstructionStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Complete);
        // Building inserted.
        ctx.BuildingsToInsert.Should().ContainSingle()
            .Which.Type.Should().Be(BuildingType.CarrierStrikeGroup);

        // Three units inserted: one carrier + two wings.
        ctx.UnitsToInsert.Should().HaveCount(3);

        var carrier = ctx.UnitsToInsert.Single(u => u.Type == UnitType.AircraftCarrier);
        carrier.OwnerPlayerId.Should().Be(alice.Id);
        carrier.LocationProvinceId.Should().Be(port.Id);
        carrier.Strength.Should().Be(1000);
        carrier.Experience.Should().Be(1, "veteran spawn");
        carrier.ParentUnitId.Should().BeNull();

        var wings = ctx.UnitsToInsert.Where(u => u.Type == UnitType.CarrierAirWing).ToList();
        wings.Should().HaveCount(2);
        wings.Should().AllSatisfy(w =>
        {
            w.OwnerPlayerId.Should().Be(alice.Id);
            w.LocationProvinceId.Should().Be(port.Id);
            w.Strength.Should().Be(500);
            w.Experience.Should().Be(1, "veteran spawn");
            w.ParentUnitId.Should().Be(carrier.Id);
            w.HomeBaseProvinceId.Should().Be(port.Id);
        });

        // One BuildingCompletedEvent + three UnitBuiltEvents.
        ctx.Events.OfType<BuildingCompletedEvent>().Should().ContainSingle();
        ctx.Events.OfType<UnitBuiltEvent>().Should().HaveCount(3);
    }
}

using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class AirStrikeStepTests
{
    [Fact]
    public void Air_strike_against_empty_target_emits_event_with_no_losses()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var attacker = AddUnit(world, alice, alicePr, UnitType.CombatDrone, 1000);
        var order = AirStrikeOrder(attacker, bobPr);
        var ctx = Context(world,
            units: new[] { attacker },
            unitOrders: new[] { order });

        new AirStrikeStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Complete);
        var ev = ctx.Events.OfType<AirStrikeResolvedEvent>().Should().ContainSingle().Subject;
        ev.AttackerStrengthLoss.Should().Be(0);
        ev.DefenderStrengthLoss.Should().Be(0);
    }

    [Fact]
    public void Aa_at_target_damages_attacker()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        var attacker = AddUnit(world, alice, alicePr, UnitType.CombatDrone, 1000);
        var aa = AddUnit(world, bob, bobPr, UnitType.AABattery, 1000);
        var order = AirStrikeOrder(attacker, bobPr);
        var ctx = Context(world,
            units: new[] { attacker, aa },
            unitOrders: new[] { order });

        new AirStrikeStep().Execute(ctx);

        attacker.Strength.Should().BeLessThan(1000);
    }

    [Fact]
    public void Surviving_attacker_damages_ground_targets()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        // Strong attacker, no AA at target → damage carries through to ground.
        var attacker = AddUnit(world, alice, alicePr, UnitType.StrategicBomber, 2000);
        var defender = AddUnit(world, bob, bobPr, UnitType.MechInfantry, 5000);
        var order = AirStrikeOrder(attacker, bobPr);
        var ctx = Context(world,
            units: new[] { attacker, defender },
            unitOrders: new[] { order });

        new AirStrikeStep().Execute(ctx);

        defender.Strength.Should().BeLessThan(5000);
        attacker.Strength.Should().Be(2000); // no AA hit it
    }

    [Fact]
    public void Destroyed_unit_is_queued_for_deletion_and_emits_destroyed_event()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        // Tiny weak attacker vs massive AA wall — attacker dies.
        var attacker = AddUnit(world, alice, alicePr, UnitType.CombatDrone, 50);
        var aa1 = AddUnit(world, bob, bobPr, UnitType.AABattery, 5000);
        var aa2 = AddUnit(world, bob, bobPr, UnitType.AABattery, 5000);
        var order = AirStrikeOrder(attacker, bobPr);
        var ctx = Context(world,
            units: new[] { attacker, aa1, aa2 },
            unitOrders: new[] { order });

        new AirStrikeStep().Execute(ctx);

        attacker.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().Contain(attacker);
        ctx.Events.OfType<UnitDestroyedEvent>().Should().Contain(e => e.UnitId == attacker.Id);
    }

    [Fact]
    public void Ground_unit_air_strike_order_is_cancelled()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var src = AddProvince(world, alice, "Src");
        var bob = AddPlayer(world, "Bob");
        var bobPr = AddProvince(world, bob, "BobPr");
        var groundUnit = AddUnit(world, alice, src, UnitType.MechInfantry, 1000);
        var order = AirStrikeOrder(groundUnit, bobPr);
        var ctx = Context(world,
            units: new[] { groundUnit },
            unitOrders: new[] { order });

        new AirStrikeStep().Execute(ctx);

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Sinking_a_carrier_destroys_its_embarked_wings_with_CarrierLost_cause()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var bob = AddPlayer(world, "Bob");
        var alicePr = AddProvince(world, alice, "AlicePr");
        var bobPr = AddProvince(world, bob, "BobPr");
        bobPr.IsCoastal = true;
        // Heavy bomber, no AA at the carrier's province → strike carries through.
        var attacker = AddUnit(world, alice, alicePr, UnitType.StrategicBomber, 5000);
        // Tiny carrier so the strike is guaranteed to sink it.
        var carrier = AddUnit(world, bob, bobPr, UnitType.AircraftCarrier, 1);
        var wing1 = AddUnit(world, bob, bobPr, UnitType.CarrierAirWing, 500, parentUnitId: carrier.Id);
        var wing2 = AddUnit(world, bob, bobPr, UnitType.CarrierAirWing, 500, parentUnitId: carrier.Id);
        var order = AirStrikeOrder(attacker, bobPr);
        var ctx = Context(world,
            units: new[] { attacker, carrier, wing1, wing2 },
            unitOrders: new[] { order });

        new AirStrikeStep().Execute(ctx);

        carrier.Strength.Should().Be(0);
        wing1.Strength.Should().Be(0);
        wing2.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().Contain(new[] { carrier, wing1, wing2 });
        var destroyed = ctx.Events.OfType<UnitDestroyedEvent>().ToList();
        destroyed.Should().Contain(e => e.UnitId == carrier.Id);
        destroyed.Should().Contain(e => e.UnitId == wing1.Id && e.Cause == "CarrierLost");
        destroyed.Should().Contain(e => e.UnitId == wing2.Id && e.Cause == "CarrierLost");
    }
}

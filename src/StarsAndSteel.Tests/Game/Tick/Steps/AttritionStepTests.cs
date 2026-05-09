using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class AttritionStepTests
{
    [Fact]
    public void Unit_on_hostile_territory_loses_strength_and_morale()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var enemyProv = AddProvince(world, bob, "Quebec");
        var unit = AddUnit(world, alice, enemyProv, UnitType.MechInfantry, strength: 1000, morale: 100);
        var ctx = Context(world, units: new[] { unit });

        new AttritionStep().Execute(ctx);

        unit.Strength.Should().Be(980); // -2%
        unit.Morale.Should().Be(95);
    }

    [Fact]
    public void Unit_on_friendly_territory_takes_no_attrition()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, 1000);
        var ctx = Context(world, units: new[] { unit });

        new AttritionStep().Execute(ctx);

        unit.Strength.Should().Be(1000);
        unit.Morale.Should().Be(100);
    }

    [Fact]
    public void Unit_on_neutral_territory_takes_attrition()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, owner: null, "Wilds");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, 1000);
        var ctx = Context(world, units: new[] { unit });

        new AttritionStep().Execute(ctx);

        unit.Strength.Should().Be(980);
    }

    [Fact]
    public void Unit_in_transit_is_skipped()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, bob, "Quebec");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, 1000);
        unit.IsInTransit = true;
        var ctx = Context(world, units: new[] { unit });

        new AttritionStep().Execute(ctx);

        unit.Strength.Should().Be(1000);
    }

    [Fact]
    public void Air_units_are_skipped()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, bob, "Quebec");
        var unit = AddUnit(world, alice, prov, UnitType.MultiroleFighter, 1000);
        var ctx = Context(world, units: new[] { unit });

        new AttritionStep().Execute(ctx);

        unit.Strength.Should().Be(1000);
    }

    [Fact]
    public void Stack_dropped_to_zero_emits_destroyed_event_and_queues_delete()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, bob, "Quebec");
        // Tiny stack so 2% attrition (rounded up to at least 1) wipes it out.
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, strength: 1, morale: 100);
        var ctx = Context(world, units: new[] { unit });

        new AttritionStep().Execute(ctx);

        unit.Strength.Should().Be(0);
        ctx.UnitsToDelete.Should().ContainSingle().Which.Id.Should().Be(unit.Id);
        var ev = ctx.Events.OfType<UnitDestroyedEvent>().Should().ContainSingle().Subject;
        ev.UnitId.Should().Be(unit.Id);
        ev.Cause.Should().Be("Attrition");
    }

    [Fact]
    public void Already_destroyed_unit_is_skipped()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, bob, "Quebec");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, 0);
        var ctx = Context(world, units: new[] { unit });

        new AttritionStep().Execute(ctx);

        ctx.UnitsToDelete.Should().BeEmpty();
        ctx.Events.Should().BeEmpty();
    }
}

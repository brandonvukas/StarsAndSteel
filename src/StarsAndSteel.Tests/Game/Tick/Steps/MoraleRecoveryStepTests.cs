using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class MoraleRecoveryStepTests
{
    [Fact]
    public void Owned_non_besieged_province_recovers_one_morale_per_tick()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        prov.MoraleLevel = 50;
        var ctx = Context(world);

        new MoraleRecoveryStep().Execute(ctx);

        prov.MoraleLevel.Should().Be(51);
    }

    [Fact]
    public void Province_at_full_morale_does_not_overshoot()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        prov.MoraleLevel = 100;
        var ctx = Context(world);

        new MoraleRecoveryStep().Execute(ctx);

        prov.MoraleLevel.Should().Be(100);
    }

    [Fact]
    public void Neutral_province_does_not_recover()
    {
        var world = NewWorld();
        var prov = AddProvince(world, owner: null, "Wilds");
        prov.MoraleLevel = 30;
        var ctx = Context(world);

        new MoraleRecoveryStep().Execute(ctx);

        prov.MoraleLevel.Should().Be(30);
    }

    [Fact]
    public void Besieged_province_does_not_recover()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, alice, "Texas");
        prov.MoraleLevel = 40;
        var enemy = AddUnit(world, bob, prov, UnitType.MechInfantry, 100);
        var ctx = Context(world, units: new[] { enemy });

        new MoraleRecoveryStep().Execute(ctx);

        prov.MoraleLevel.Should().Be(40);
    }

    [Fact]
    public void Friendly_garrison_unit_recovers_morale()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, 100, morale: 60);
        var ctx = Context(world, units: new[] { unit });

        new MoraleRecoveryStep().Execute(ctx);

        unit.Morale.Should().Be(61);
    }

    [Fact]
    public void Unit_on_hostile_territory_does_not_recover_via_this_step()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, bob, "Quebec");
        var unit = AddUnit(world, alice, prov, UnitType.MechInfantry, 100, morale: 60);
        var ctx = Context(world, units: new[] { unit });

        new MoraleRecoveryStep().Execute(ctx);

        unit.Morale.Should().Be(60);
    }

    [Fact]
    public void Step_emits_no_events()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        prov.MoraleLevel = 50;
        var ctx = Context(world);

        new MoraleRecoveryStep().Execute(ctx);

        ctx.Events.Should().BeEmpty();
    }
}

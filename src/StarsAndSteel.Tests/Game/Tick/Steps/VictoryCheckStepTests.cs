using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class VictoryCheckStepTests
{
    [Fact]
    public void Player_owning_eighty_percent_triggers_victory()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        // 10 provinces total: alice owns 8, bob owns 2 → alice at 80% exactly.
        for (var i = 0; i < 8; i++) AddProvince(world, alice, $"A{i}");
        for (var i = 0; i < 2; i++) AddProvince(world, bob, $"B{i}");
        var ctx = Context(world);

        new VictoryCheckStep().Execute(ctx);

        world.Status.Should().Be(GameWorldStatus.Ended);
        world.EndedAt.Should().NotBeNull();
        alice.IsAlive.Should().BeTrue();
        bob.IsAlive.Should().BeFalse();
        var victory = ctx.Events.OfType<VictoryAchievedEvent>().Should().ContainSingle().Subject;
        victory.WinnerPlayerId.Should().Be(alice.Id);
        victory.WinnerNationName.Should().Be("USA");
        victory.OwnedProvinceCount.Should().Be(8);
        victory.TotalProvinceCount.Should().Be(10);
    }

    [Fact]
    public void Player_below_threshold_does_not_win()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        for (var i = 0; i < 7; i++) AddProvince(world, alice, $"A{i}");
        for (var i = 0; i < 3; i++) AddProvince(world, bob, $"B{i}");
        var ctx = Context(world);

        new VictoryCheckStep().Execute(ctx);

        world.Status.Should().Be(GameWorldStatus.Active);
        world.EndedAt.Should().BeNull();
        ctx.Events.OfType<VictoryAchievedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Player_with_zero_provinces_is_eliminated()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        // Only alice owns; bob has been wiped out but world hasn't been won yet (alice < 80%).
        AddProvince(world, alice, "A1");
        AddProvince(world, owner: null, "Wilds1");
        AddProvince(world, owner: null, "Wilds2");
        AddProvince(world, owner: null, "Wilds3");
        AddProvince(world, owner: null, "Wilds4");
        var ctx = Context(world);

        new VictoryCheckStep().Execute(ctx);

        bob.IsAlive.Should().BeFalse();
        alice.IsAlive.Should().BeTrue();
        var elim = ctx.Events.OfType<PlayerEliminatedEvent>().Should().ContainSingle().Subject;
        elim.PlayerId.Should().Be(bob.Id);
        world.Status.Should().Be(GameWorldStatus.Active);
    }

    [Fact]
    public void Already_ended_world_is_no_op()
    {
        var world = NewWorld();
        world.Status = GameWorldStatus.Ended;
        var alice = AddPlayer(world, "USA");
        AddProvince(world, alice, "A1");
        var ctx = Context(world);

        new VictoryCheckStep().Execute(ctx);

        ctx.Events.Should().BeEmpty();
        // Status stays Ended; EndedAt was never set in the fixture.
        world.EndedAt.Should().BeNull();
    }

    [Fact]
    public void Already_dead_player_does_not_re_emit_eliminated()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        bob.IsAlive = false;
        AddProvince(world, alice, "A1");
        var ctx = Context(world);

        new VictoryCheckStep().Execute(ctx);

        ctx.Events.OfType<PlayerEliminatedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Victory_marks_all_other_living_players_dead()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var carol = AddPlayer(world, "Mexico");
        // 10 provinces — alice 8, bob 1, carol 1 → alice wins.
        for (var i = 0; i < 8; i++) AddProvince(world, alice, $"A{i}");
        AddProvince(world, bob, "B1");
        AddProvince(world, carol, "C1");
        var ctx = Context(world);

        new VictoryCheckStep().Execute(ctx);

        alice.IsAlive.Should().BeTrue();
        bob.IsAlive.Should().BeFalse();
        carol.IsAlive.Should().BeFalse();
        ctx.Events.OfType<PlayerEliminatedEvent>().Should().HaveCount(2);
    }
}

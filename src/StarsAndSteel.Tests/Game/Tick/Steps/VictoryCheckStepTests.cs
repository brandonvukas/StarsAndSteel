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

    [Fact]
    public void Coalition_of_allies_wins_when_combined_share_meets_threshold()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var carol = AddPlayer(world, "Mexico");
        // 10 provinces: alice 5, bob 3, carol 2. alice+bob = 8 = 80% threshold; allied.
        for (var i = 0; i < 5; i++) AddProvince(world, alice, $"A{i}");
        for (var i = 0; i < 3; i++) AddProvince(world, bob, $"B{i}");
        for (var i = 0; i < 2; i++) AddProvince(world, carol, $"C{i}");
        var relations = RelationsBetween(world, (alice, bob, DiplomaticStatus.Allied));
        var ctx = Context(world, relations: relations);

        new VictoryCheckStep().Execute(ctx);

        world.Status.Should().Be(GameWorldStatus.Ended);
        alice.IsAlive.Should().BeTrue();
        bob.IsAlive.Should().BeTrue();
        carol.IsAlive.Should().BeFalse();
        var coalition = ctx.Events.OfType<CoalitionVictoryAchievedEvent>().Should().ContainSingle().Subject;
        coalition.WinnerPlayerIds.Should().BeEquivalentTo(new[] { alice.Id, bob.Id });
        coalition.OwnedProvinceCount.Should().Be(8);
        coalition.TotalProvinceCount.Should().Be(10);
        ctx.Events.OfType<VictoryAchievedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Solo_victory_takes_precedence_over_coalition()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        // alice 8, bob 2. alice qualifies solo.
        for (var i = 0; i < 8; i++) AddProvince(world, alice, $"A{i}");
        for (var i = 0; i < 2; i++) AddProvince(world, bob, $"B{i}");
        var relations = RelationsBetween(world, (alice, bob, DiplomaticStatus.Allied));
        var ctx = Context(world, relations: relations);

        new VictoryCheckStep().Execute(ctx);

        ctx.Events.OfType<VictoryAchievedEvent>().Should().ContainSingle().Which.WinnerPlayerId.Should().Be(alice.Id);
        ctx.Events.OfType<CoalitionVictoryAchievedEvent>().Should().BeEmpty();
        bob.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void Non_allied_players_cannot_form_coalition()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        for (var i = 0; i < 5; i++) AddProvince(world, alice, $"A{i}");
        for (var i = 0; i < 5; i++) AddProvince(world, bob, $"B{i}");
        // No relations → implicit hostility; no coalition possible.
        var ctx = Context(world);

        new VictoryCheckStep().Execute(ctx);

        world.Status.Should().Be(GameWorldStatus.Active);
        ctx.Events.OfType<CoalitionVictoryAchievedEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Coalition_requires_full_mutual_alliance_clique()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var carol = AddPlayer(world, "Mexico");
        // 10 provinces: alice 4, bob 3, carol 3. Need a 3-clique to win (sum 10 >= 8).
        // alice<->bob and alice<->carol allied, but bob-carol NOT allied → no clique of 3.
        for (var i = 0; i < 4; i++) AddProvince(world, alice, $"A{i}");
        for (var i = 0; i < 3; i++) AddProvince(world, bob, $"B{i}");
        for (var i = 0; i < 3; i++) AddProvince(world, carol, $"C{i}");
        var relations = RelationsBetween(world,
            (alice, bob, DiplomaticStatus.Allied),
            (alice, carol, DiplomaticStatus.Allied));
        var ctx = Context(world, relations: relations);

        new VictoryCheckStep().Execute(ctx);

        // alice+bob = 7 < 8; alice+carol = 7 < 8 — no winning clique.
        world.Status.Should().Be(GameWorldStatus.Active);
        ctx.Events.OfType<CoalitionVictoryAchievedEvent>().Should().BeEmpty();
    }
}

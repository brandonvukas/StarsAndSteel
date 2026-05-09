using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

public class NewsStepTests
{
    [Fact]
    public void Province_capture_emits_breaking_combat_headline_with_related_player()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var quebec = AddProvince(world, alice, "Quebec");
        var ctx = Context(world);
        ctx.Events.Add(new ProvinceCapturedEvent(
            Tick: ctx.ProcessingTick,
            ProvinceId: quebec.Id,
            FromPlayerId: bob.Id,
            ToPlayerId: alice.Id));

        new NewsStep().Execute(ctx);

        ctx.NewsItemsToInsert.Should().ContainSingle();
        var item = ctx.NewsItemsToInsert[0];
        item.Tick.Should().Be(ctx.ProcessingTick);
        item.GameWorldId.Should().Be(world.Id);
        item.Severity.Should().Be(NewsSeverity.Breaking);
        item.Category.Should().Be(NewsCategory.Combat);
        item.RelatedPlayerId.Should().Be(alice.Id);
        item.Headline.Should().Contain("Quebec");
        item.Headline.Should().NotContain("{");

        var ev = ctx.Events.OfType<NewsPublishedEvent>().Should().ContainSingle().Subject;
        ev.NewsItemId.Should().Be(item.Id);
        ev.Headline.Should().Be(item.Headline);
        ev.Severity.Should().Be(NewsSeverity.Breaking);
    }

    [Fact]
    public void Capture_from_neutral_uses_neutral_forces_label()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Greenland");
        var ctx = Context(world);
        ctx.Events.Add(new ProvinceCapturedEvent(
            Tick: ctx.ProcessingTick,
            ProvinceId: prov.Id,
            FromPlayerId: null,
            ToPlayerId: alice.Id));

        new NewsStep().Execute(ctx);

        var item = ctx.NewsItemsToInsert.Should().ContainSingle().Subject;
        // "neutral forces" is the documented fallback when there's no prior owner.
        (item.Headline + " " + item.Body).Should().Contain("neutral forces");
    }

    [Fact]
    public void Combat_headline_is_suppressed_when_same_province_was_captured_same_tick()
    {
        // One engagement, one card. ProvinceCapture wins the headline; the CombatResolved
        // event still goes on the wire (clients may want it for VFX) but no NewsItem row.
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, alice, "Quebec");
        var ctx = Context(world);
        ctx.Events.Add(new CombatResolvedEvent(
            Tick: ctx.ProcessingTick,
            ProvinceId: prov.Id,
            AttackerPlayerId: alice.Id,
            DefenderPlayerId: bob.Id,
            AttackerStrengthLoss: 100,
            DefenderStrengthLoss: 500,
            WinnerPlayerId: alice.Id));
        ctx.Events.Add(new ProvinceCapturedEvent(
            Tick: ctx.ProcessingTick,
            ProvinceId: prov.Id,
            FromPlayerId: bob.Id,
            ToPlayerId: alice.Id));

        new NewsStep().Execute(ctx);

        // Only the capture headline — combat is suppressed.
        ctx.NewsItemsToInsert.Should().ContainSingle()
            .Which.Severity.Should().Be(NewsSeverity.Breaking);
    }

    [Fact]
    public void Inconclusive_combat_emits_notable_headline()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, bob, "Quebec");
        var ctx = Context(world);
        ctx.Events.Add(new CombatResolvedEvent(
            Tick: ctx.ProcessingTick,
            ProvinceId: prov.Id,
            AttackerPlayerId: alice.Id,
            DefenderPlayerId: bob.Id,
            AttackerStrengthLoss: 200,
            DefenderStrengthLoss: 200,
            WinnerPlayerId: null));

        new NewsStep().Execute(ctx);

        var item = ctx.NewsItemsToInsert.Should().ContainSingle().Subject;
        item.Severity.Should().Be(NewsSeverity.Notable);
        item.Category.Should().Be(NewsCategory.Combat);
        // Winner null → falls back to attacker for color-coding.
        item.RelatedPlayerId.Should().Be(alice.Id);
    }

    [Fact]
    public void Air_strike_emits_notable_headline()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var bob = AddPlayer(world, "Canada");
        var prov = AddProvince(world, bob, "Quebec");
        var attacker = AddUnit(world, alice, prov, UnitType.MultiroleFighter, 100);
        var ctx = Context(world);
        ctx.Events.Add(new AirStrikeResolvedEvent(
            Tick: ctx.ProcessingTick,
            AttackerUnitId: attacker.Id,
            AttackerPlayerId: alice.Id,
            TargetProvinceId: prov.Id,
            AttackerStrengthLoss: 0,
            DefenderStrengthLoss: 50));

        new NewsStep().Execute(ctx);

        var item = ctx.NewsItemsToInsert.Should().ContainSingle().Subject;
        item.Severity.Should().Be(NewsSeverity.Notable);
        item.Headline.Should().Contain("USA");
    }

    [Fact]
    public void Unit_built_emits_info_politics_headline()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        var unit = AddUnit(world, alice, prov, UnitType.MainBattleTank, 100);
        var ctx = Context(world);
        ctx.Events.Add(new UnitBuiltEvent(
            Tick: ctx.ProcessingTick,
            UnitId: unit.Id,
            OwnerPlayerId: alice.Id,
            ProvinceId: prov.Id,
            Type: UnitType.MainBattleTank,
            Strength: 100));

        new NewsStep().Execute(ctx);

        var item = ctx.NewsItemsToInsert.Should().ContainSingle().Subject;
        item.Severity.Should().Be(NewsSeverity.Info);
        item.Category.Should().Be(NewsCategory.Politics);
        item.RelatedPlayerId.Should().Be(alice.Id);
        item.Headline.Should().Contain("Texas");
        item.Headline.Should().Contain("MainBattleTank");
    }

    [Fact]
    public void Building_completed_emits_info_economy_headline()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        var building = AddBuilding(prov, BuildingType.Refinery);
        var ctx = Context(world);
        ctx.Events.Add(new BuildingCompletedEvent(
            Tick: ctx.ProcessingTick,
            BuildingId: building.Id,
            OwnerPlayerId: alice.Id,
            ProvinceId: prov.Id,
            Type: BuildingType.Refinery,
            Level: 1));

        new NewsStep().Execute(ctx);

        var item = ctx.NewsItemsToInsert.Should().ContainSingle().Subject;
        item.Severity.Should().Be(NewsSeverity.Info);
        item.Category.Should().Be(NewsCategory.Economy);
        item.Headline.Should().Contain("Refinery");
    }

    [Fact]
    public void NewsPublishedEvent_in_context_does_not_recursively_news_itself()
    {
        // The step snapshots Events before iterating; otherwise it would loop forever
        // emitting NewsPublishedEvents about NewsPublishedEvents. Guard the contract.
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var prov = AddProvince(world, alice, "Texas");
        var ctx = Context(world);
        ctx.Events.Add(new ProvinceCapturedEvent(
            Tick: ctx.ProcessingTick,
            ProvinceId: prov.Id,
            FromPlayerId: null,
            ToPlayerId: alice.Id));

        new NewsStep().Execute(ctx);

        ctx.NewsItemsToInsert.Should().HaveCount(1);
        ctx.Events.OfType<NewsPublishedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public void Resource_and_movement_events_are_not_headline_worthy()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "USA");
        var p1 = AddProvince(world, alice, "Texas");
        var p2 = AddProvince(world, alice, "Oklahoma");
        var unit = AddUnit(world, alice, p1, UnitType.MechInfantry, 100);
        var ctx = Context(world);
        ctx.Events.Add(new ResourcesProducedEvent(
            Tick: ctx.ProcessingTick,
            PlayerId: alice.Id,
            MoneyDelta: 100, OilDelta: 0, SteelDelta: 0,
            ElectronicsDelta: 0, FoodDelta: 0, ManpowerDelta: 0));
        ctx.Events.Add(new UnitMovedEvent(
            Tick: ctx.ProcessingTick,
            UnitId: unit.Id,
            OwnerPlayerId: alice.Id,
            FromProvinceId: p1.Id,
            ToProvinceId: p2.Id));

        new NewsStep().Execute(ctx);

        ctx.NewsItemsToInsert.Should().BeEmpty();
        ctx.Events.OfType<NewsPublishedEvent>().Should().BeEmpty();
    }
}

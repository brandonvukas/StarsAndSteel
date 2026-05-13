using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Tick.Steps;

/// <summary>
/// Phase 4c: pure tests for <see cref="RandomEventStep"/>. We use a scripted
/// <see cref="IRandomSource"/> so we control which event fires and which subject
/// is picked — no seed-hunting, every test is deterministic-by-construction.
/// </summary>
public class RandomEventStepTests
{
    [Fact]
    public void Skips_when_trigger_roll_misses()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice", 10_000, 0, 0, 0, 0, 0);
        var prov = AddProvince(world, alice, "Cap");
        AddBuilding(prov, BuildingType.SteelMill);

        var rng = new ScriptedRng(doubles: new[] { 0.99 }); // > 0.15 trigger threshold
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        ctx.Events.Should().BeEmpty();
        ctx.BuildingsToDelete.Should().BeEmpty();
        prov.Buildings.Should().HaveCount(1);
    }

    // ---- NaturalDisaster --------------------------------------------------

    [Fact]
    public void NaturalDisaster_destroys_a_random_non_wonder_building()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var prov = AddProvince(world, alice, "Cap");
        var doomed = AddBuilding(prov, BuildingType.SteelMill);

        // 0.0 → trigger fires; ints: kindIdx=0 (NaturalDisaster), pickIdx=0
        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 0, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        prov.Buildings.Should().NotContain(doomed);
        ctx.BuildingsToDelete.Should().ContainSingle().Which.Should().Be(doomed);
        var ev = ctx.Events.OfType<RandomEventOccurredEvent>().Should().ContainSingle().Subject;
        ev.Kind.Should().Be(RandomEventKind.NaturalDisaster);
        ev.ProvinceId.Should().Be(prov.Id);
        ev.AffectedPlayerId.Should().Be(alice.Id);
    }

    [Fact]
    public void NaturalDisaster_skips_wonders()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var prov = AddProvince(world, alice, "Cap");
        // Only a wonder on the map — disaster has no eligible target.
        AddBuilding(prov, BuildingType.HooverDamReborn);

        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 0, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        prov.Buildings.Should().HaveCount(1, "wonders are immune to disasters");
        ctx.BuildingsToDelete.Should().BeEmpty();
        ctx.Events.Should().BeEmpty();
    }

    // ---- ResourceBoom -----------------------------------------------------

    [Fact]
    public void ResourceBoom_credits_owner_with_bonus_from_picked_province()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice", 0, 0, 0, 0, 0, 0);
        var prov = AddProvince(world, alice, "Cap");
        prov.MoneyPerTick = 100;
        prov.SteelPerTick = 50;

        // kindIdx=1 (ResourceBoom), pickIdx=0
        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 1, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        alice.Money.Should().Be(100, "100 base × 1.0 boom factor");
        alice.Steel.Should().Be(50);
        ctx.Events.OfType<RandomEventOccurredEvent>().Should().ContainSingle()
            .Which.Kind.Should().Be(RandomEventKind.ResourceBoom);
    }

    [Fact]
    public void ResourceBoom_skips_when_no_province_produces_resources()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var prov = AddProvince(world, alice, "Cap");
        // All per-tick = 0 (default).

        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 1, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        ctx.Events.Should().BeEmpty();
        alice.Money.Should().Be(0);
    }

    // ---- ScientificBreakthrough ------------------------------------------

    [Fact]
    public void ScientificBreakthrough_adds_progress_to_an_active_research_row()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var research = new ResearchProgress
        {
            Id = Guid.NewGuid(),
            PlayerId = alice.Id,
            TechId = "advanced_armor",
            ProgressPoints = 10,
            IsUnlocked = false,
        };

        // kindIdx=2 (ScientificBreakthrough), pickIdx=0
        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 2, 0 });
        var ctx = Context(world, rng: rng, activeResearch: new List<ResearchProgress> { research });

        new RandomEventStep().Execute(ctx);

        research.ProgressPoints.Should().Be(10 + RandomEventStep.BreakthroughProgress);
        ctx.Events.OfType<RandomEventOccurredEvent>().Should().ContainSingle()
            .Which.AffectedPlayerId.Should().Be(alice.Id);
    }

    [Fact]
    public void ScientificBreakthrough_skips_when_no_active_research()
    {
        var world = NewWorld();
        AddPlayer(world, "Alice");

        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 2, 0 });
        var ctx = Context(world, rng: rng, activeResearch: new List<ResearchProgress>());

        new RandomEventStep().Execute(ctx);

        ctx.Events.Should().BeEmpty();
    }

    // ---- CivilUnrest ------------------------------------------------------

    [Fact]
    public void CivilUnrest_drops_morale_by_20_clamped_to_zero()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var prov = AddProvince(world, alice, "Cap");
        prov.MoraleLevel = 50;

        // kindIdx=3 (CivilUnrest), pickIdx=0
        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 3, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        prov.MoraleLevel.Should().Be(30);
        var ev = ctx.Events.OfType<RandomEventOccurredEvent>().Should().ContainSingle().Subject;
        ev.Magnitude.Should().Be(20);
    }

    [Fact]
    public void CivilUnrest_clamps_when_morale_below_loss_amount()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice");
        var prov = AddProvince(world, alice, "Cap");
        prov.MoraleLevel = 5;

        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 3, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        prov.MoraleLevel.Should().Be(0);
        ctx.Events.OfType<RandomEventOccurredEvent>().Should().ContainSingle()
            .Which.Magnitude.Should().Be(5, "actual loss reported, not the configured 20");
    }

    // ---- MarketCrash ------------------------------------------------------

    [Fact]
    public void MarketCrash_drains_10_percent_of_victim_money()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice", money: 5000, 0, 0, 0, 0, 0);

        // kindIdx=4 (MarketCrash), pickIdx=0
        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 4, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        alice.Money.Should().Be(5000 - 500);
        ctx.Events.OfType<RandomEventOccurredEvent>().Should().ContainSingle()
            .Which.Magnitude.Should().Be(500);
    }

    [Fact]
    public void MarketCrash_skips_players_below_minimum_money()
    {
        var world = NewWorld();
        AddPlayer(world, "Alice", money: 999, 0, 0, 0, 0, 0); // below MarketCrashMinMoney=1000

        var rng = new ScriptedRng(doubles: new[] { 0.0 }, ints: new[] { 4, 0 });
        var ctx = Context(world, rng: rng);

        new RandomEventStep().Execute(ctx);

        ctx.Events.Should().BeEmpty();
    }

    // ---- Helpers ----------------------------------------------------------

    /// <summary>
    /// IRandomSource stub that hands back pre-scripted values from two arrays
    /// (one for NextDouble, one for NextInt). Keeps tests assertion-driven
    /// instead of seed-driven. Calling beyond array length throws so a test
    /// that triggers more rolls than expected fails loudly.
    /// </summary>
    private sealed class ScriptedRng : IRandomSource
    {
        private readonly Queue<double> _doubles;
        private readonly Queue<int> _ints;
        public ScriptedRng(IEnumerable<double>? doubles = null, IEnumerable<int>? ints = null)
        {
            _doubles = new Queue<double>(doubles ?? Array.Empty<double>());
            _ints = new Queue<int>(ints ?? Array.Empty<int>());
        }
        public long State => 0;
        public int NextInt(int exclusiveMax)
        {
            if (_ints.Count == 0) throw new InvalidOperationException("ScriptedRng ran out of int values.");
            return _ints.Dequeue();
        }
        public double NextDouble()
        {
            if (_doubles.Count == 0) throw new InvalidOperationException("ScriptedRng ran out of double values.");
            return _doubles.Dequeue();
        }
    }
}

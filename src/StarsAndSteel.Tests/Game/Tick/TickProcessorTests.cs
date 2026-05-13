using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Tests.Game.Tick;

public class TickProcessorTests
{
    [Fact]
    public void Advances_tick_counter_and_persists_rng_state()
    {
        var world = MinimalWorld(seed: 42);
        var processor = new TickProcessor();

        var result = processor.ProcessOneTick(world, utcNow: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        result.Tick.Should().Be(1);
        world.CurrentTick.Should().Be(1);
        // RNG state is always written back. Phase 4c: RandomEventStep rolls
        // one NextDouble() per tick to decide whether an event fires; even
        // when the trigger misses (no event), the RNG state advances. So
        // the post-tick state equals the state after one Advance() — not
        // the initial state. The persistence contract still holds:
        // world.RngState is always synced from the generator at end of tick.
        var expected = new DeterministicRandom(42L);
        expected.NextDouble();
        world.RngState.Should().Be(expected.State);
        world.NextTickDueUtc.Should().Be(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Two_runs_from_same_starting_state_produce_identical_outcomes()
    {
        // Replay invariant (docs/07): state(T+1) = f(state(T), orders, rng(T)).
        // With no orders, two worlds starting from the same state must end
        // identical after one tick.
        var worldA = MinimalWorld(seed: 99);
        var worldB = MinimalWorld(seed: 99);

        AddOwnedProvinceWithBuilding(worldA);
        AddOwnedProvinceWithBuilding(worldB);

        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rA = new TickProcessor().ProcessOneTick(worldA, now);
        var rB = new TickProcessor().ProcessOneTick(worldB, now);

        worldA.CurrentTick.Should().Be(worldB.CurrentTick);
        worldA.RngState.Should().Be(worldB.RngState);
        worldA.Players.First().Money.Should().Be(worldB.Players.First().Money);
        worldA.Players.First().Steel.Should().Be(worldB.Players.First().Steel);
        rA.Events.Should().HaveCount(rB.Events.Count);
    }

    [Fact]
    public void Resource_production_step_emits_event_for_owner()
    {
        var world = MinimalWorld(seed: 1);
        AddOwnedProvinceWithBuilding(world);

        var result = new TickProcessor().ProcessOneTick(world, DateTime.UtcNow);

        result.Events.OfType<ResourcesProducedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Custom_step_list_replaces_defaults()
    {
        var world = MinimalWorld(seed: 1);
        var spy = new SpyStep();

        var processor = new TickProcessor(new[] { (ITickStep)spy });
        processor.ProcessOneTick(world, DateTime.UtcNow);

        spy.Executions.Should().Be(1);
    }

    [Fact]
    public void Empty_step_list_throws()
    {
        Action act = () => _ = new TickProcessor(Array.Empty<ITickStep>());
        act.Should().Throw<ArgumentException>();
    }

    // ---------- helpers ----------

    private static GameWorld MinimalWorld(int seed)
    {
        var world = new GameWorld
        {
            Id = Guid.NewGuid(),
            Name = "T",
            Status = GameWorldStatus.Active,
            CurrentTick = 0,
            TickIntervalSeconds = 60,
            NextTickDueUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            MapSeed = seed,
            RngState = seed,
            RowVersion = new byte[8],
        };

        var player = new Player
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            IsAi = false,
            NationName = "P1",
            FlagPrimaryHex = "#fff",
            FlagSecondaryHex = "#000",
            IsAlive = true,
        };
        world.Players.Add(player);
        return world;
    }

    private static void AddOwnedProvinceWithBuilding(GameWorld world)
    {
        var owner = world.Players.First();
        var province = new Province
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            Name = "Capital",
            Type = ProvinceType.Capital,
            OwnerPlayerId = owner.Id,
            OwnerPlayer = owner,
            MoraleLevel = 100,
            MoneyPerTick = 50,
            SteelPerTick = 20,
        };
        province.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(),
            ProvinceId = province.Id,
            Province = province,
            Type = BuildingType.SteelMill,
            Level = 1,
        });
        world.Provinces.Add(province);
        owner.OwnedProvinces.Add(province);
    }

    private sealed class SpyStep : ITickStep
    {
        public string Name => "Spy";
        public int Executions { get; private set; }
        public void Execute(TickContext context) => Executions++;
    }
}

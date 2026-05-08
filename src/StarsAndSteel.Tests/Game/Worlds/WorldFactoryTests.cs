using FluentAssertions;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Seeding;
using StarsAndSteel.Game.Worlds;

namespace StarsAndSteel.Tests.Game.Worlds;

/// <summary>
/// Pure-C# tests against <see cref="WorldFactory"/>. No DbContext, no Testcontainers.
/// </summary>
public sealed class WorldFactoryTests
{
    [Fact]
    public void Build_creates_world_in_lobby_state_with_now_seeded_clocks()
    {
        var fixedNow = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var factory = new WorldFactory(new FixedTimeProvider(fixedNow));

        var result = factory.Build("Demo", seed: 42, map: TwoProvinceMap());

        result.World.Status.Should().Be(GameWorldStatus.Lobby);
        result.World.CurrentTick.Should().Be(0);
        result.World.MapSeed.Should().Be(42);
        result.World.RngState.Should().Be(42);
        result.World.CreatedAt.Should().Be(fixedNow.UtcDateTime);
        result.World.NextTickDueUtc.Should().Be(fixedNow.UtcDateTime);
        result.World.StartedAt.Should().BeNull();
    }

    [Fact]
    public void Build_copies_every_province_with_a_fresh_id_but_same_data()
    {
        var factory = new WorldFactory(TimeProvider.System);
        var map = TwoProvinceMap();

        var result = factory.Build("Demo", seed: 1, map: map);

        result.World.Provinces.Should().HaveCount(2);

        var usa = result.World.Provinces.Single(p => p.Name == "United States");
        usa.Type.Should().Be(ProvinceType.Capital);
        usa.MoneyPerTick.Should().Be(100);
        usa.OwnerPlayerId.Should().BeNull("provinces start neutral");
        usa.MoraleLevel.Should().Be(100);
        usa.GameWorldId.Should().Be(result.World.Id);

        // Fresh Guid — must NOT match the seeder's deterministic Guid (otherwise
        // creating two worlds in one DB would PK-collide).
        var seedUsa = map.Provinces.Single(p => p.Name == "United States");
        usa.Id.Should().NotBe(seedUsa.Id);
    }

    [Fact]
    public void Build_translates_adjacencies_and_preserves_invariant()
    {
        var factory = new WorldFactory(TimeProvider.System);
        var map = TwoProvinceMap();

        var result = factory.Build("Demo", seed: 1, map: map);

        result.Adjacencies.Should().HaveCount(1);

        var edge = result.Adjacencies.Single();
        edge.ProvinceAId.CompareTo(edge.ProvinceBId)
            .Should().BeLessThan(0,
                "the docs/03 ProvinceAId < ProvinceBId invariant must hold after re-stamping");

        // Both ends point at provinces in this world.
        var ids = result.World.Provinces.Select(p => p.Id).ToHashSet();
        ids.Should().Contain(edge.ProvinceAId);
        ids.Should().Contain(edge.ProvinceBId);
    }

    [Fact]
    public void Build_produces_distinct_guids_across_two_worlds_with_same_map()
    {
        var factory = new WorldFactory(TimeProvider.System);
        var map = TwoProvinceMap();

        var first = factory.Build("First", seed: 1, map: map);
        var second = factory.Build("Second", seed: 2, map: map);

        first.World.Id.Should().NotBe(second.World.Id);

        // No province ID overlaps — that's the whole point of re-stamping.
        var firstIds = first.World.Provinces.Select(p => p.Id).ToHashSet();
        var secondIds = second.World.Provinces.Select(p => p.Id).ToHashSet();
        firstIds.Overlaps(secondIds).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_rejects_blank_name(string name)
    {
        var factory = new WorldFactory(TimeProvider.System);
        var act = () => factory.Build(name, seed: 1, map: TwoProvinceMap());
        act.Should().Throw<ArgumentException>();
    }

    private static MapSeedData TwoProvinceMap()
    {
        // Mirrors shared/map-data.json so the assertions match the production wiring.
        var usaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var canadaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var provinces = new[]
        {
            new ProvinceRow(
                Id: usaId, Name: "United States", Type: ProvinceType.Capital,
                IsCoastal: true, CenterX: 250f, CenterY: 300f,
                BasePopulation: 330_000_000,
                MoneyPerTick: 100, OilPerTick: 20, SteelPerTick: 30,
                ElectronicsPerTick: 25, FoodPerTick: 40, ManpowerPerTick: 50),
            new ProvinceRow(
                Id: canadaId, Name: "Canada", Type: ProvinceType.Resource,
                IsCoastal: true, CenterX: 250f, CenterY: 150f,
                BasePopulation: 39_000_000,
                MoneyPerTick: 40, OilPerTick: 50, SteelPerTick: 25,
                ElectronicsPerTick: 10, FoodPerTick: 20, ManpowerPerTick: 15),
        };

        // Guid order: usaId < canadaId in this hard-coded map.
        var adjacencies = new[]
        {
            new AdjacencyRow(usaId, canadaId, TerrainCost: 1.0f, IsSeaCrossing: false),
        };

        return new MapSeedData(provinces, adjacencies);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}

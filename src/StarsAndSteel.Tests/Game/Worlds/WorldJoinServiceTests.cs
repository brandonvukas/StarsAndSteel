using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Seeding;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Steps;
using StarsAndSteel.Game.Worlds;

namespace StarsAndSteel.Tests.Game.Worlds;

/// <summary>
/// Pure-C# tests against <see cref="WorldJoinService"/>. Exercises the full
/// happy-path mutation against a freshly-built world graph and then the failure
/// modes (no capitals, duplicate user, ended world).
/// </summary>
public sealed class WorldJoinServiceTests
{
    [Fact]
    public void Joining_assigns_capital_starter_resources_and_starter_buildings()
    {
        var world = NewWorldWithOneCapital();
        var join = new WorldJoinService();

        var player = join.AddHumanPlayer(
            world,
            userId: Guid.NewGuid(),
            nationName: "Alice",
            flagPrimaryHex: "#ff0000",
            flagSecondaryHex: "#ffffff",
            nowUtc: DateTime.UtcNow);

        player.Should().NotBeNull();

        // Resources match docs/03 §"Starting resources".
        player!.Money.Should().Be(StarterPackage.StartingMoney);
        player.Oil.Should().Be(StarterPackage.StartingOil);
        player.Steel.Should().Be(StarterPackage.StartingSteel);
        player.Electronics.Should().Be(StarterPackage.StartingElectronics);
        player.Food.Should().Be(StarterPackage.StartingFood);
        player.Manpower.Should().Be(StarterPackage.StartingManpower);

        // Capital is owned by the player and has all 4 starter buildings.
        var capital = world.Provinces.Single(p => p.OwnerPlayerId == player.Id);
        capital.Type.Should().Be(ProvinceType.Capital);
        capital.Buildings.Select(b => b.Type).Should().BeEquivalentTo(new[]
        {
            BuildingType.RecruitmentCenter,
            BuildingType.MilitaryBase,
            BuildingType.AirBase,
            BuildingType.FinancialDistrict,
        });
        capital.Buildings.Should().OnlyContain(b => b.Level == 1);

        // Starter units stationed at the capital.
        var units = world.Players.Single().OwnedUnits.ToList();
        units.Should().HaveCount(StarterPackage.MechInfantryStackCount + StarterPackage.AaBatteryStackCount);
        units.Count(u => u.Type == UnitType.MechInfantry && u.Strength == StarterPackage.MechInfantryStrength)
            .Should().Be(StarterPackage.MechInfantryStackCount);
        units.Count(u => u.Type == UnitType.AABattery && u.Strength == StarterPackage.AaBatteryStrength)
            .Should().Be(StarterPackage.AaBatteryStackCount);
        units.Should().OnlyContain(u => u.LocationProvinceId == capital.Id && !u.IsInTransit);
    }

    [Fact]
    public void First_join_flips_world_to_active_and_schedules_first_tick()
    {
        var world = NewWorldWithOneCapital();
        var nowUtc = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc);

        var join = new WorldJoinService();
        join.AddHumanPlayer(world, Guid.NewGuid(), "Alice", "#ff0000", "#ffffff", nowUtc);

        world.Status.Should().Be(GameWorldStatus.Active);
        world.StartedAt.Should().Be(nowUtc);
        world.NextTickDueUtc.Should().Be(nowUtc.AddSeconds(world.TickIntervalSeconds));
    }

    [Fact]
    public void Join_returns_null_when_no_capital_is_free()
    {
        var world = NewWorldWithOneCapital();
        var join = new WorldJoinService();

        // First join takes the only capital.
        join.AddHumanPlayer(world, Guid.NewGuid(), "Alice", "#ff0000", "#ffffff", DateTime.UtcNow)
            .Should().NotBeNull();

        // Second join has nowhere to go.
        var second = join.AddHumanPlayer(world, Guid.NewGuid(), "Bob", "#0000ff", "#ffffff", DateTime.UtcNow);
        second.Should().BeNull();
    }

    [Fact]
    public void Join_returns_null_when_user_already_joined_this_world()
    {
        // Two capitals so capital scarcity isn't the failure reason.
        var world = NewWorldWithCapitals(2);
        var join = new WorldJoinService();
        var userId = Guid.NewGuid();

        join.AddHumanPlayer(world, userId, "Alice", "#ff0000", "#ffffff", DateTime.UtcNow)
            .Should().NotBeNull();

        join.AddHumanPlayer(world, userId, "Alice2", "#ff0000", "#ffffff", DateTime.UtcNow)
            .Should().BeNull();
    }

    [Fact]
    public void Join_returns_null_for_ended_world()
    {
        var world = NewWorldWithOneCapital();
        world.Status = GameWorldStatus.Ended;

        var join = new WorldJoinService();
        join.AddHumanPlayer(world, Guid.NewGuid(), "Alice", "#ff0000", "#ffffff", DateTime.UtcNow)
            .Should().BeNull();
    }

    [Fact]
    public void Joined_capital_produces_resources_when_resource_step_runs()
    {
        // Sanity: ResourceProductionStep operates on capital + buildings exactly
        // the way WorldJoinService leaves it. After one tick, money should equal
        // base + FinancialDistrict bonus (100 base * (1 + 0.2*1) = 120).
        var world = NewWorldWithOneCapital(moneyPerTick: 100);
        var join = new WorldJoinService();
        var player = join.AddHumanPlayer(
            world, Guid.NewGuid(), "Alice", "#ff0000", "#ffffff", DateTime.UtcNow)!;

        var ctx = new TickContext(world, processingTick: 1, rng: new DeterministicRandom(world.RngState));
        new ResourceProductionStep().Execute(ctx);

        player.Money.Should().Be(StarterPackage.StartingMoney + 120);
    }

    private static GameWorld NewWorldWithOneCapital(int moneyPerTick = 100) =>
        NewWorldWithCapitals(1, moneyPerTick);

    private static GameWorld NewWorldWithCapitals(int capitalCount, int moneyPerTick = 100)
    {
        var world = new GameWorld
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Status = GameWorldStatus.Lobby,
            CurrentTick = 0,
            TickIntervalSeconds = 60,
            CreatedAt = DateTime.UtcNow,
            NextTickDueUtc = DateTime.UtcNow,
            MapSeed = 1,
            RngState = 1,
            RowVersion = Array.Empty<byte>(),
        };

        for (var i = 0; i < capitalCount; i++)
        {
            var p = new Province
            {
                Id = Guid.NewGuid(),
                GameWorldId = world.Id,
                GameWorld = world,
                Name = $"Capital {i + 1}",
                Type = ProvinceType.Capital,
                MoraleLevel = 100,
                MoneyPerTick = moneyPerTick,
            };
            world.Provinces.Add(p);
        }

        return world;
    }
}

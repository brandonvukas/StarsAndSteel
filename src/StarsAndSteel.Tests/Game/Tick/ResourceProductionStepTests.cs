using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick;
using StarsAndSteel.Game.Tick.Events;
using StarsAndSteel.Game.Tick.Steps;

namespace StarsAndSteel.Tests.Game.Tick;

/// <summary>
/// Pure-C# tests against POCO graphs. No DbContext, no Testcontainers.
/// </summary>
public class ResourceProductionStepTests
{
    [Fact]
    public void Adds_base_output_for_each_owned_province_with_no_buildings()
    {
        var (world, alice) = WorldWithOnePlayer();

        AddProvince(world, alice, money: 100, oil: 50, steel: 25, electronics: 10, food: 5, manpower: 2);
        AddProvince(world, alice, money: 50, oil: 0, steel: 100, electronics: 0, food: 0, manpower: 1);

        var ctx = NewContext(world);
        new ResourceProductionStep().Execute(ctx);

        alice.Money.Should().Be(150);
        alice.Oil.Should().Be(50);
        alice.Steel.Should().Be(125);
        alice.Electronics.Should().Be(10);
        alice.Food.Should().Be(5);
        alice.Manpower.Should().Be(3);
    }

    [Fact]
    public void Building_bonus_applies_only_to_matching_resource()
    {
        var (world, alice) = WorldWithOnePlayer();

        var province = AddProvince(world, alice, money: 100, oil: 100, steel: 100, electronics: 0, food: 0, manpower: 0);
        province.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(),
            ProvinceId = province.Id,
            Province = province,
            Type = BuildingType.SteelMill,
            Level = 2, // 1 + 0.25*2 = 1.5x steel
            ConstructedAtTick = 0,
        });

        new ResourceProductionStep().Execute(NewContext(world));

        alice.Money.Should().Be(100);
        alice.Oil.Should().Be(100);
        alice.Steel.Should().Be(150);
    }

    [Theory]
    [InlineData(100, 100)] // full
    [InlineData(30, 100)]  // boundary: still full at 30
    [InlineData(29, 50)]   // <30 -> 50%
    [InlineData(10, 50)]   // boundary: still 50% at 10
    [InlineData(9, 0)]     // <10 -> nothing
    [InlineData(0, 0)]     // floor
    public void Morale_modifier_matches_docs_04(int morale, int expectedMoney)
    {
        var (world, alice) = WorldWithOnePlayer();

        var province = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        province.MoraleLevel = morale;

        new ResourceProductionStep().Execute(NewContext(world));

        alice.Money.Should().Be(expectedMoney);
    }

    // ---- Phase 3a: nuclear fallout multiplier --------------------------

    [Theory]
    [InlineData(0, 100)]   // unaffected
    [InlineData(50, 50)]   // half output
    [InlineData(100, 0)]   // wasteland
    [InlineData(25, 75)]   // 25% radiation -> 75% output
    public void Radiation_modifier_scales_output_linearly(int radiation, int expectedMoney)
    {
        var (world, alice) = WorldWithOnePlayer();

        var province = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        province.RadiationLevel = radiation;

        new ResourceProductionStep().Execute(NewContext(world));

        alice.Money.Should().Be(expectedMoney);
    }

    [Fact]
    public void Radiation_stacks_multiplicatively_with_morale()
    {
        // 50% morale floor doesn't apply (50 morale = full output per docs/04 since
        // moraleFactor returns 1.0 above the 30 cliff). So we need a morale below
        // 30 to get the 0.5 morale factor: pick 20 morale and 50 radiation.
        // Expected: 100 * 0.5 (morale) * 0.5 (radiation) = 25.
        var (world, alice) = WorldWithOnePlayer();

        var province = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        province.MoraleLevel = 20;
        province.RadiationLevel = 50;

        new ResourceProductionStep().Execute(NewContext(world));

        alice.Money.Should().Be(25);
    }

    [Fact]
    public void Emits_one_ResourcesProducedEvent_per_player_with_deltas()
    {
        var (world, alice) = WorldWithOnePlayer();
        var bob = AddPlayer(world, "Bob", money: 0);
        AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        AddProvince(world, bob, money: 0, oil: 0, steel: 50, electronics: 0, food: 0, manpower: 0);

        var ctx = NewContext(world);
        new ResourceProductionStep().Execute(ctx);

        ctx.Events.Should().HaveCount(2);
        ctx.Events.OfType<ResourcesProducedEvent>().Should().Contain(e =>
            e.PlayerId == alice.Id && e.MoneyDelta == 100 && e.SteelDelta == 0);
        ctx.Events.OfType<ResourcesProducedEvent>().Should().Contain(e =>
            e.PlayerId == bob.Id && e.MoneyDelta == 0 && e.SteelDelta == 50);
    }

    [Fact]
    public void Neutral_province_produces_nothing()
    {
        var world = NewWorld();
        var p = new Province
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            Name = "No Man's Land",
            Type = ProvinceType.Resource,
            OwnerPlayerId = null, // explicitly neutral
            MoneyPerTick = 999,
            MoraleLevel = 100,
        };
        world.Provinces.Add(p);

        var ctx = NewContext(world);
        new ResourceProductionStep().Execute(ctx);

        ctx.Events.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_buildings_of_same_type_stack_additively()
    {
        var (world, alice) = WorldWithOnePlayer();
        var province = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);

        // Two FinancialDistricts at level 1 each: 1 + 0.20 + 0.20 = 1.4x money.
        province.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(), ProvinceId = province.Id, Province = province,
            Type = BuildingType.FinancialDistrict, Level = 1,
        });
        province.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(), ProvinceId = province.Id, Province = province,
            Type = BuildingType.FinancialDistrict, Level = 1,
        });

        new ResourceProductionStep().Execute(NewContext(world));

        alice.Money.Should().Be(140);
    }

    // ---------- helpers ----------

    private static GameWorld NewWorld() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        Status = GameWorldStatus.Active,
        CurrentTick = 0,
        TickIntervalSeconds = 60,
        NextTickDueUtc = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        MapSeed = 1,
        RngState = 1,
        RowVersion = new byte[8],
    };

    private static (GameWorld World, Player Alice) WorldWithOnePlayer()
    {
        var world = NewWorld();
        var alice = AddPlayer(world, "Alice", money: 0);
        return (world, alice);
    }

    private static Player AddPlayer(GameWorld world, string name, long money)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            IsAi = false,
            NationName = name,
            FlagPrimaryHex = "#ffffff",
            FlagSecondaryHex = "#000000",
            IsAlive = true,
            Money = money,
        };
        world.Players.Add(player);
        return player;
    }

    private static Province AddProvince(
        GameWorld world,
        Player owner,
        int money,
        int oil,
        int steel,
        int electronics,
        int food,
        int manpower)
    {
        var p = new Province
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            Name = $"P-{world.Provinces.Count + 1}",
            Type = ProvinceType.Industrial,
            OwnerPlayerId = owner.Id,
            OwnerPlayer = owner,
            MoraleLevel = 100,
            MoneyPerTick = money,
            OilPerTick = oil,
            SteelPerTick = steel,
            ElectronicsPerTick = electronics,
            FoodPerTick = food,
            ManpowerPerTick = manpower,
        };
        world.Provinces.Add(p);
        owner.OwnedProvinces.Add(p);
        return p;
    }

    private static TickContext NewContext(GameWorld world) =>
        new(world, processingTick: world.CurrentTick + 1, rng: new DeterministicRandom(world.RngState));

    // ---------- Phase 2F: logistics network bonus ----------

    [Fact]
    public void Logistics_network_bonus_applies_when_two_owned_provinces_with_base_are_adjacent()
    {
        var (world, alice) = WorldWithOnePlayer();
        var p1 = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        var p2 = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        p1.Buildings.Add(new Building { Id = Guid.NewGuid(), ProvinceId = p1.Id, Province = p1, Type = BuildingType.MilitaryBase, Level = 1 });

        var adj = MakeAdj(p1, p2);
        var ctx = new TickContext(world, world.CurrentTick + 1, new DeterministicRandom(world.RngState),
            units: new List<Unit>(),
            pendingUnitOrders: new List<UnitOrder>(),
            pendingConstructionOrders: new List<ConstructionOrder>(),
            adjacencies: new List<ProvinceAdjacency> { adj });

        new ResourceProductionStep().Execute(ctx);

        // Both p1 and p2 in network → 100 * 1.10 each = 110 + 110 = 220.
        alice.Money.Should().Be(220);
    }

    [Fact]
    public void Logistics_bonus_does_not_apply_to_isolated_owned_province()
    {
        var (world, alice) = WorldWithOnePlayer();
        var p1 = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        p1.Buildings.Add(new Building { Id = Guid.NewGuid(), ProvinceId = p1.Id, Province = p1, Type = BuildingType.MilitaryBase, Level = 1 });

        new ResourceProductionStep().Execute(NewContext(world));

        // Single province, no adjacency → no bonus.
        alice.Money.Should().Be(100);
    }

    [Fact]
    public void Logistics_bonus_skipped_when_no_military_base_in_component()
    {
        var (world, alice) = WorldWithOnePlayer();
        var p1 = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        var p2 = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        // Adjacent, both owned, but no MilitaryBase anywhere.
        var adj = MakeAdj(p1, p2);
        var ctx = new TickContext(world, world.CurrentTick + 1, new DeterministicRandom(world.RngState),
            units: new List<Unit>(),
            pendingUnitOrders: new List<UnitOrder>(),
            pendingConstructionOrders: new List<ConstructionOrder>(),
            adjacencies: new List<ProvinceAdjacency> { adj });

        new ResourceProductionStep().Execute(ctx);

        alice.Money.Should().Be(200);
    }

    [Fact]
    public void Logistics_bonus_does_not_cross_sea_adjacency()
    {
        var (world, alice) = WorldWithOnePlayer();
        var p1 = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        var p2 = AddProvince(world, alice, money: 100, oil: 0, steel: 0, electronics: 0, food: 0, manpower: 0);
        p1.Buildings.Add(new Building { Id = Guid.NewGuid(), ProvinceId = p1.Id, Province = p1, Type = BuildingType.MilitaryBase, Level = 1 });

        var adj = MakeAdj(p1, p2);
        adj.IsSeaCrossing = true;
        var ctx = new TickContext(world, world.CurrentTick + 1, new DeterministicRandom(world.RngState),
            units: new List<Unit>(),
            pendingUnitOrders: new List<UnitOrder>(),
            pendingConstructionOrders: new List<ConstructionOrder>(),
            adjacencies: new List<ProvinceAdjacency> { adj });

        new ResourceProductionStep().Execute(ctx);

        // p1 isolated (sea edge ignored) — no bonus.
        alice.Money.Should().Be(200);
    }

    private static ProvinceAdjacency MakeAdj(Province a, Province b)
    {
        var (lo, hi) = a.Id.CompareTo(b.Id) < 0 ? (a, b) : (b, a);
        return new ProvinceAdjacency
        {
            ProvinceAId = lo.Id, ProvinceA = lo,
            ProvinceBId = hi.Id, ProvinceB = hi,
            TerrainCost = 1.0f,
        };
    }
}

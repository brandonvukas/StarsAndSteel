using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Ai;
using StarsAndSteel.Game.Tick;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Ai;

public class HawkPlannerTests
{
    private static IRandomSource Rng() => new DeterministicRandom(42);

    private static Player MakeHawk(GameWorld world,
        long money = 0, long oil = 0, long steel = 0,
        long electronics = 0, long food = 0, long manpower = 0)
    {
        var p = AddPlayer(world, "Hawk", money, oil, steel, electronics, food, manpower);
        p.IsAi = true;
        p.AiPersonality = AiPersonality.Hawk;
        return p;
    }

    [Fact]
    public void Attacks_weak_adjacent_enemy_when_strength_advantage_exceeds_threshold()
    {
        var world = NewWorld();
        var hawk = MakeHawk(world);
        var enemy = AddPlayer(world, "Enemy");

        var hawkProv = AddProvince(world, hawk, "HawkLand");
        var enemyProv = AddProvince(world, enemy, "EnemyLand");

        // Hawk: 2000-strength MechInfantry. Enemy: 100-strength NationalGuard. Clear margin.
        var attacker = AddUnit(world, hawk, hawkProv, UnitType.MechInfantry, 2000);
        AddUnit(world, enemy, enemyProv, UnitType.NationalGuard, 100);

        var plan = HawkPlanner.Plan(
            me: hawk,
            world: world,
            allUnits: world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            adjacencies: new[] { Adj(hawkProv, enemyProv) },
            processingTick: 1,
            rng: Rng());

        plan.UnitOrders.Should().ContainSingle();
        var order = plan.UnitOrders.Single();
        order.OrderType.Should().Be(OrderType.Attack);
        order.UnitId.Should().Be(attacker.Id);
        order.TargetProvinceId.Should().Be(enemyProv.Id);
        order.IssuedAtTick.Should().Be(1, "AI orders are eligible the same tick they're emitted");
        order.Status.Should().Be(OrderStatus.Pending);
        plan.ConstructionOrders.Should().BeEmpty();
    }

    [Fact]
    public void Recruits_MechInfantry_when_no_attack_is_viable_and_resources_allow()
    {
        var world = NewWorld();
        // MechInfantry per BuildCatalog: Money 200, Steel 100, Manpower 100 per 1000.
        var hawk = MakeHawk(world, money: 1000, steel: 1000, manpower: 1000);

        var hawkProv = AddProvince(world, hawk, "HawkLand");
        AddBuilding(hawkProv, BuildingType.RecruitmentCenter); // required for MechInfantry

        // No enemies anywhere — attack branch must fail.
        var plan = HawkPlanner.Plan(
            me: hawk,
            world: world,
            allUnits: Array.Empty<Unit>(),
            adjacencies: Array.Empty<ProvinceAdjacency>(),
            processingTick: 1,
            rng: Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().ContainSingle();
        var order = plan.ConstructionOrders.Single();
        order.OrderType.Should().Be(OrderType.BuildUnit);
        order.UnitType.Should().Be(UnitType.MechInfantry);
        order.Quantity.Should().Be(1000);
        order.ProvinceId.Should().Be(hawkProv.Id);
        order.IssuedAtTick.Should().Be(1);

        // Resources debited.
        hawk.Money.Should().Be(1000 - 200);
        hawk.Steel.Should().Be(1000 - 100);
        hawk.Manpower.Should().Be(1000 - 100);
    }

    [Fact]
    public void Does_nothing_when_no_attack_viable_and_resources_insufficient()
    {
        var world = NewWorld();
        var hawk = MakeHawk(world); // zero resources
        var hawkProv = AddProvince(world, hawk, "HawkLand");
        AddBuilding(hawkProv, BuildingType.RecruitmentCenter);

        var plan = HawkPlanner.Plan(
            me: hawk,
            world: world,
            allUnits: Array.Empty<Unit>(),
            adjacencies: Array.Empty<ProvinceAdjacency>(),
            processingTick: 1,
            rng: Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
        hawk.Money.Should().Be(0);
    }

    [Fact]
    public void Does_not_attack_when_defender_strength_neutralizes_advantage()
    {
        var world = NewWorld();
        var hawk = MakeHawk(world, money: 5000, steel: 5000, manpower: 5000); // affords recruit
        var enemy = AddPlayer(world, "Enemy");

        var hawkProv = AddProvince(world, hawk, "HawkLand");
        AddBuilding(hawkProv, BuildingType.RecruitmentCenter);
        var enemyProv = AddProvince(world, enemy, "EnemyLand");

        // Equal strength on both sides — Hawk requires a 20% margin so attack must fail.
        AddUnit(world, hawk, hawkProv, UnitType.MechInfantry, 1000);
        AddUnit(world, enemy, enemyProv, UnitType.MechInfantry, 1000);

        var plan = HawkPlanner.Plan(
            me: hawk,
            world: world,
            allUnits: world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            adjacencies: new[] { Adj(hawkProv, enemyProv) },
            processingTick: 1,
            rng: Rng());

        plan.UnitOrders.Should().BeEmpty("attacker doesn't have a comfortable margin");
        // Falls through to recruit since resources allow.
        plan.ConstructionOrders.Should().ContainSingle()
            .Which.OrderType.Should().Be(OrderType.BuildUnit);
    }

    [Fact]
    public void Skips_planner_for_non_Hawk_AI_personalities()
    {
        var world = NewWorld();
        var industrialist = AddPlayer(world, "Suit");
        industrialist.IsAi = true;
        industrialist.AiPersonality = AiPersonality.Industrialist;

        var prov = AddProvince(world, industrialist, "Suburb");
        AddBuilding(prov, BuildingType.RecruitmentCenter);
        industrialist.Money = 100_000;

        var plan = HawkPlanner.Plan(
            me: industrialist,
            world: world,
            allUnits: Array.Empty<Unit>(),
            adjacencies: Array.Empty<ProvinceAdjacency>(),
            processingTick: 1,
            rng: Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
    }
}

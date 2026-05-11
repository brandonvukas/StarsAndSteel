using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Ai;
using StarsAndSteel.Game.Tick;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Ai;

public class IsolationistPlannerTests
{
    private static IRandomSource Rng() => new DeterministicRandom(42);

    private static Player MakeIso(GameWorld world,
        long money = 0, long oil = 0, long steel = 0,
        long electronics = 0, long food = 0, long manpower = 0)
    {
        var p = AddPlayer(world, "Iso", money, oil, steel, electronics, food, manpower);
        p.IsAi = true;
        p.AiPersonality = AiPersonality.Isolationist;
        return p;
    }

    [Fact]
    public void Builds_MilitaryBase_on_border_province_when_resources_allow()
    {
        var world = NewWorld();
        // MilitaryBase costs Money 2000, Oil 100, Steel 500, Electronics 100, Manpower 100.
        var iso = MakeIso(world, money: 5000, oil: 500, steel: 1000, electronics: 500, manpower: 500);
        var enemy = AddPlayer(world, "Enemy");

        var border = AddProvince(world, iso, "Border");
        var hostileNeighbour = AddProvince(world, enemy, "Foreign");

        var plan = IsolationistPlanner.Plan(iso, world,
            allUnits: Array.Empty<Unit>(),
            adjacencies: new[] { Adj(border, hostileNeighbour) },
            processingTick: 1, Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().ContainSingle();
        var order = plan.ConstructionOrders.Single();
        order.OrderType.Should().Be(OrderType.BuildBuilding);
        order.BuildingType.Should().Be(BuildingType.MilitaryBase);
        order.ProvinceId.Should().Be(border.Id);
    }

    [Fact]
    public void Recruits_AABattery_when_no_border_MilitaryBase_is_needed()
    {
        var world = NewWorld();
        // AABattery: Money 400, Steel 200, Electronics 100. Plenty here.
        var iso = MakeIso(world, money: 1000, steel: 500, electronics: 500);
        var prov = AddProvince(world, iso, "Hub");
        AddBuilding(prov, BuildingType.MilitaryBase);
        // No border province (no adjacencies, no other players' provinces).

        var plan = IsolationistPlanner.Plan(iso, world,
            Array.Empty<Unit>(), Array.Empty<ProvinceAdjacency>(),
            processingTick: 1, Rng());

        plan.ConstructionOrders.Should().ContainSingle();
        var order = plan.ConstructionOrders.Single();
        order.OrderType.Should().Be(OrderType.BuildUnit);
        order.UnitType.Should().Be(UnitType.AABattery);
    }

    [Fact]
    public void Falls_back_to_MechInfantry_when_only_a_RecruitmentCenter_exists()
    {
        var world = NewWorld();
        var iso = MakeIso(world, money: 500, steel: 200, manpower: 200);
        var prov = AddProvince(world, iso, "Hub");
        AddBuilding(prov, BuildingType.RecruitmentCenter);

        var plan = IsolationistPlanner.Plan(iso, world,
            Array.Empty<Unit>(), Array.Empty<ProvinceAdjacency>(),
            processingTick: 1, Rng());

        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().UnitType.Should().Be(UnitType.MechInfantry);
    }

    [Fact]
    public void Never_emits_an_attack_order_even_with_obvious_target()
    {
        var world = NewWorld();
        var iso = MakeIso(world, money: 100_000);
        var enemy = AddPlayer(world, "Enemy");
        var mine = AddProvince(world, iso, "Mine");
        var theirs = AddProvince(world, enemy, "Theirs");
        AddBuilding(mine, BuildingType.MilitaryBase); // satisfies border-MB priority
        AddUnit(world, iso, mine, UnitType.MainBattleTank, 5000);
        AddUnit(world, enemy, theirs, UnitType.NationalGuard, 50);

        var plan = IsolationistPlanner.Plan(iso, world,
            allUnits: world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            adjacencies: new[] { Adj(mine, theirs) },
            processingTick: 1, Rng());

        plan.UnitOrders.Should().BeEmpty("isolationists never initiate combat");
    }
}

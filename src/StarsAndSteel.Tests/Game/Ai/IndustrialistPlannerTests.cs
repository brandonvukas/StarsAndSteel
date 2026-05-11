using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Ai;
using StarsAndSteel.Game.Tick;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Ai;

public class IndustrialistPlannerTests
{
    private static IRandomSource Rng() => new DeterministicRandom(42);

    private static Player MakeIndustrialist(GameWorld world,
        long money = 0, long oil = 0, long steel = 0,
        long electronics = 0, long food = 0, long manpower = 0)
    {
        var p = AddPlayer(world, "Indy", money, oil, steel, electronics, food, manpower);
        p.IsAi = true;
        p.AiPersonality = AiPersonality.Industrialist;
        return p;
    }

    [Fact]
    public void Queues_a_FinancialDistrict_at_provinces_lacking_one_when_resources_allow()
    {
        var world = NewWorld();
        // FD costs Money 2000, Steel 100, Electronics 100, Manpower 50 (per BuildCatalog).
        var indy = MakeIndustrialist(world, money: 5000, steel: 500, electronics: 500, manpower: 200);
        AddProvince(world, indy, "Hub");

        // Tick 0 → rotation starts with FD.
        var plan = IndustrialistPlanner.Plan(indy, world,
            allUnits: Array.Empty<Unit>(),
            adjacencies: Array.Empty<ProvinceAdjacency>(),
            processingTick: 0,
            rng: Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().ContainSingle();
        var order = plan.ConstructionOrders.Single();
        order.OrderType.Should().Be(OrderType.BuildBuilding);
        order.BuildingType.Should().Be(BuildingType.FinancialDistrict);
        order.IssuedAtTick.Should().Be(0, "AI orders are eligible the same tick they're emitted");
        // Resources debited.
        indy.Money.Should().Be(5000 - 2000);
    }

    [Fact]
    public void Falls_back_to_MechInfantry_recruit_when_no_economy_buildings_can_be_placed()
    {
        var world = NewWorld();
        // Enough for MechInfantry (200/100/100) but not for any economy building (≥1500 money).
        var indy = MakeIndustrialist(world, money: 1000, steel: 1000, manpower: 1000);
        var prov = AddProvince(world, indy, "Hub");
        AddBuilding(prov, BuildingType.RecruitmentCenter);
        // Already has all three economy buildings so the rotation finds nowhere to place.
        AddBuilding(prov, BuildingType.FinancialDistrict);
        AddBuilding(prov, BuildingType.Refinery);
        AddBuilding(prov, BuildingType.SteelMill);

        var plan = IndustrialistPlanner.Plan(indy, world,
            Array.Empty<Unit>(), Array.Empty<ProvinceAdjacency>(),
            processingTick: 0, Rng());

        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().OrderType.Should().Be(OrderType.BuildUnit);
        plan.ConstructionOrders.Single().UnitType.Should().Be(UnitType.MechInfantry);
    }

    [Fact]
    public void Returns_empty_when_resources_are_insufficient_for_anything()
    {
        var world = NewWorld();
        var indy = MakeIndustrialist(world); // zero resources
        var prov = AddProvince(world, indy, "Hub");
        AddBuilding(prov, BuildingType.RecruitmentCenter);

        var plan = IndustrialistPlanner.Plan(indy, world,
            Array.Empty<Unit>(), Array.Empty<ProvinceAdjacency>(),
            processingTick: 0, Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
    }

    [Fact]
    public void Does_nothing_for_non_Industrialist_personalities()
    {
        var world = NewWorld();
        var hawk = AddPlayer(world, "Hawk", money: 999_999, oil: 0, steel: 999_999, electronics: 0, food: 0, manpower: 0);
        hawk.IsAi = true;
        hawk.AiPersonality = AiPersonality.Hawk;
        AddProvince(world, hawk, "Hub");

        var plan = IndustrialistPlanner.Plan(hawk, world,
            Array.Empty<Unit>(), Array.Empty<ProvinceAdjacency>(),
            processingTick: 0, Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
    }
}

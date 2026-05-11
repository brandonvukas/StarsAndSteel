using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Ai;
using StarsAndSteel.Game.Tick;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Ai;

public class SchemerPlannerTests
{
    private static IRandomSource Rng() => new DeterministicRandom(42);

    private static Player MakeSchemer(GameWorld world,
        long money = 0, long oil = 0, long steel = 0,
        long electronics = 0, long food = 0, long manpower = 0)
    {
        var p = AddPlayer(world, "Schemer", money, oil, steel, electronics, food, manpower);
        p.IsAi = true;
        p.AiPersonality = AiPersonality.Schemer;
        return p;
    }

    [Fact]
    public void Attacks_isolated_weak_target_with_3x_strength_margin()
    {
        var world = NewWorld();
        var schemer = MakeSchemer(world);
        var enemy = AddPlayer(world, "Enemy");

        var mine = AddProvince(world, schemer, "Mine");
        var orphan = AddProvince(world, enemy, "Orphan");
        // Schemer: 5000-strength MBT (effective 7500). Defender: 100 NG (effective 70).
        // Margin 7500 / (70 * 3) ≈ 35 — easily over the 3× threshold.
        var attacker = AddUnit(world, schemer, mine, UnitType.MainBattleTank, 5000);
        AddUnit(world, enemy, orphan, UnitType.NationalGuard, 100);
        // Orphan has no adjacent friendly stack → isolated.

        var plan = SchemerPlanner.Plan(schemer, world,
            allUnits: world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            adjacencies: new[] { Adj(mine, orphan) },
            processingTick: 1, Rng());

        plan.UnitOrders.Should().ContainSingle();
        var order = plan.UnitOrders.Single();
        order.OrderType.Should().Be(OrderType.Attack);
        order.UnitId.Should().Be(attacker.Id);
        order.TargetProvinceId.Should().Be(orphan.Id);
    }

    [Fact]
    public void Skips_attack_when_target_has_a_reinforcement_in_neighbour()
    {
        var world = NewWorld();
        var schemer = MakeSchemer(world, money: 500, steel: 500, manpower: 200);
        var enemy = AddPlayer(world, "Enemy");

        var mine = AddProvince(world, schemer, "Mine");
        var target = AddProvince(world, enemy, "Target");
        var reinforcement = AddProvince(world, enemy, "Reinforce");
        AddBuilding(mine, BuildingType.RecruitmentCenter); // for fallback recruit

        AddUnit(world, schemer, mine, UnitType.MainBattleTank, 5000);
        AddUnit(world, enemy, target, UnitType.NationalGuard, 100);
        AddUnit(world, enemy, reinforcement, UnitType.MechInfantry, 1000); // would reinforce target

        var plan = SchemerPlanner.Plan(schemer, world,
            allUnits: world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            adjacencies: new[] { Adj(mine, target), Adj(target, reinforcement) },
            processingTick: 1, Rng());

        plan.UnitOrders.Should().BeEmpty("schemers don't attack reinforced targets");
        // Fallback recruit kicked in (MechInfantry).
        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().UnitType.Should().Be(UnitType.MechInfantry);
    }

    [Fact]
    public void Builds_CombatDrone_when_AirBase_present_and_no_attack_target()
    {
        var world = NewWorld();
        // CombatDrone: Money 400, Oil 100, Electronics 200.
        var schemer = MakeSchemer(world, money: 1000, oil: 500, electronics: 500);
        var prov = AddProvince(world, schemer, "Hub");
        AddBuilding(prov, BuildingType.AirBase);

        var plan = SchemerPlanner.Plan(schemer, world,
            Array.Empty<Unit>(), Array.Empty<ProvinceAdjacency>(),
            processingTick: 1, Rng());

        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().UnitType.Should().Be(UnitType.CombatDrone);
    }

    [Fact]
    public void Skips_attack_when_strength_margin_is_below_3x_threshold()
    {
        var world = NewWorld();
        var schemer = MakeSchemer(world); // no resources for fallbacks
        var enemy = AddPlayer(world, "Enemy");
        var mine = AddProvince(world, schemer, "Mine");
        var theirs = AddProvince(world, enemy, "Theirs");
        // Both 1000 MechInfantry → 1× margin, well below 3×.
        AddUnit(world, schemer, mine, UnitType.MechInfantry, 1000);
        AddUnit(world, enemy, theirs, UnitType.MechInfantry, 1000);

        var plan = SchemerPlanner.Plan(schemer, world,
            allUnits: world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            adjacencies: new[] { Adj(mine, theirs) },
            processingTick: 1, Rng());

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
    }
}

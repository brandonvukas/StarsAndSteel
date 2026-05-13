using FluentAssertions;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Ai;
using StarsAndSteel.Game.Tick;
using static StarsAndSteel.Tests.Game.Tick.Steps.TickTestGraph;

namespace StarsAndSteel.Tests.Game.Ai;

/// <summary>
/// Phase 4d: pure tests for <see cref="InsurgentPlanner"/>. ScriptedRng controls the
/// d100 branch roll plus any sub-selections so each test pins exactly one code path.
/// </summary>
public class InsurgentPlannerTests
{
    private static Player MakeIns(GameWorld world,
        long money = 0, long oil = 0, long steel = 0,
        long electronics = 0, long food = 0, long manpower = 0)
    {
        var p = AddPlayer(world, "Ins", money, oil, steel, electronics, food, manpower);
        p.IsAi = true;
        p.AiPersonality = AiPersonality.Insurgent;
        return p;
    }

    [Fact]
    public void Idle_branch_emits_nothing()
    {
        var world = NewWorld();
        var ins = MakeIns(world, money: 1_000_000, steel: 100_000, manpower: 100_000, electronics: 100_000, oil: 100_000);
        var prov = AddProvince(world, ins, "Cap");
        AddBuilding(prov, BuildingType.RecruitmentCenter);

        // Roll = 95 -> idle branch (>= BuildThreshold 90).
        var rng = new ScriptedRng(ints: new[] { 95 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(), Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
        // Resources untouched.
        ins.Money.Should().Be(1_000_000);
    }

    [Fact]
    public void Attack_branch_picks_a_random_adjacent_enemy_with_no_margin_check()
    {
        var world = NewWorld();
        var ins = MakeIns(world);
        var enemy = AddPlayer(world, "Enemy");
        var mine = AddProvince(world, ins, "Mine");
        var theirs = AddProvince(world, enemy, "Theirs");

        // Wildly outmatched attacker — Insurgent attacks anyway.
        AddUnit(world, ins, mine, UnitType.MechInfantry, 100);
        AddUnit(world, enemy, theirs, UnitType.MainBattleTank, 5000);

        // Roll = 5 -> attack branch. Sub-pick = 0 (only one candidate).
        var rng = new ScriptedRng(ints: new[] { 5, 0 });
        var plan = InsurgentPlanner.Plan(ins, world,
            world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            new[] { Adj(mine, theirs) }, 1, rng);

        plan.UnitOrders.Should().ContainSingle();
        var order = plan.UnitOrders.Single();
        order.OrderType.Should().Be(OrderType.Attack);
        order.TargetProvinceId.Should().Be(theirs.Id);
        order.IssuedAtTick.Should().Be(1);
    }

    [Fact]
    public void Attack_branch_falls_through_to_recruit_when_no_targets_exist()
    {
        var world = NewWorld();
        // No enemies, no adjacencies. Attack branch finds nothing -> recruit fallback fires.
        var ins = MakeIns(world, money: 10_000, steel: 5_000, manpower: 5_000, electronics: 5_000, oil: 5_000);
        var prov = AddProvince(world, ins, "Cap");
        AddBuilding(prov, BuildingType.RecruitmentCenter);

        // Roll = 10 -> attack branch (no candidates -> fall through).
        // Recruit start offset = 0 -> MechInfantry (needs RecruitmentCenter — present).
        // No randomness needed for province pick (recruit uses lex-Guid first).
        var rng = new ScriptedRng(ints: new[] { 10, 0 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().UnitType.Should().Be(UnitType.MechInfantry);
    }

    [Fact]
    public void Recruit_branch_picks_random_unit_from_chaos_pool()
    {
        var world = NewWorld();
        var ins = MakeIns(world, money: 100_000, steel: 50_000, manpower: 50_000, electronics: 50_000, oil: 50_000);
        var prov = AddProvince(world, ins, "Cap");
        // Provide all required buildings so any chaos-pool unit is viable.
        AddBuilding(prov, BuildingType.RecruitmentCenter);
        AddBuilding(prov, BuildingType.MilitaryBase);
        AddBuilding(prov, BuildingType.AirBase);

        // Roll = 35 -> recruit branch. Sub-pick = 1 -> ChaosUnits[1] = MainBattleTank.
        var rng = new ScriptedRng(ints: new[] { 35, 1 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.ConstructionOrders.Should().ContainSingle();
        var order = plan.ConstructionOrders.Single();
        order.OrderType.Should().Be(OrderType.BuildUnit);
        order.UnitType.Should().Be(UnitType.MainBattleTank);
    }

    [Fact]
    public void Recruit_branch_walks_pool_when_first_pick_is_unaffordable()
    {
        var world = NewWorld();
        // Enough for MechInfantry only — MainBattleTank, drones, fighters all unaffordable.
        var ins = MakeIns(world, money: 500, steel: 300, manpower: 200);
        var prov = AddProvince(world, ins, "Cap");
        AddBuilding(prov, BuildingType.RecruitmentCenter);

        // Roll = 35 -> recruit. Sub-pick = 1 (MBT) -> unaffordable -> walk to AABattery (no MB) ->
        // CombatDrone (no AirBase) -> MultiroleFighter (no AirBase) -> MechInfantry (works).
        var rng = new ScriptedRng(ints: new[] { 35, 1 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().UnitType.Should().Be(UnitType.MechInfantry);
    }

    [Fact]
    public void Build_branch_constructs_random_building_at_random_owned_province()
    {
        var world = NewWorld();
        var ins = MakeIns(world, money: 10_000, steel: 2_000, manpower: 2_000, electronics: 2_000, oil: 2_000);
        var p1 = AddProvince(world, ins, "P1");
        var p2 = AddProvince(world, ins, "P2");

        // Roll = 70 -> build branch. Sub-pick building = 0 -> ChaosBuildings[0] = RecruitmentCenter.
        // Province pick = 0 -> first by lex Guid (could be either).
        var rng = new ScriptedRng(ints: new[] { 70, 0, 0 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.ConstructionOrders.Should().ContainSingle();
        var order = plan.ConstructionOrders.Single();
        order.OrderType.Should().Be(OrderType.BuildBuilding);
        order.BuildingType.Should().Be(BuildingType.RecruitmentCenter);
        (order.ProvinceId == p1.Id || order.ProvinceId == p2.Id).Should().BeTrue();
    }

    [Fact]
    public void Build_branch_walks_pool_when_first_pick_is_unaffordable()
    {
        var world = NewWorld();
        // Only enough for the cheapest building (RecruitmentCenter).
        // Set huge money + just enough resources so RC fits but most others don't.
        // RC cost: roughly Money 1000, Steel 100, Manpower 100 (per BuildCatalog defaults).
        var ins = MakeIns(world, money: 1_500, steel: 200, manpower: 200);
        AddProvince(world, ins, "Cap");

        // Roll = 70 -> build. Start at index 1 (MilitaryBase) — unaffordable; walk through
        // AirBase, FinancialDistrict, Refinery, SteelMill, TechPark — all unaffordable;
        // wraps to RecruitmentCenter (index 0) — fits.
        var rng = new ScriptedRng(ints: new[] { 70, 1, 0 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().BuildingType.Should().Be(BuildingType.RecruitmentCenter);
    }

    [Fact]
    public void Build_branch_skips_provinces_that_already_have_target_building()
    {
        var world = NewWorld();
        var ins = MakeIns(world, money: 10_000, steel: 2_000, manpower: 2_000);
        var p1 = AddProvince(world, ins, "P1");
        var p2 = AddProvince(world, ins, "P2");
        AddBuilding(p1, BuildingType.RecruitmentCenter);

        // Force RecruitmentCenter pick — only p2 is eligible.
        var rng = new ScriptedRng(ints: new[] { 70, 0, 0 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().ProvinceId.Should().Be(p2.Id);
    }

    [Fact]
    public void Skips_non_insurgent_players()
    {
        var world = NewWorld();
        var hawk = AddPlayer(world, "Hawk");
        hawk.IsAi = true;
        hawk.AiPersonality = AiPersonality.Hawk;

        var rng = new ScriptedRng(ints: new[] { 5 });
        var plan = InsurgentPlanner.Plan(hawk, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
        // No int should have been consumed — assert by trying to read another (would throw if consumed).
        // (Actually the planner short-circuits before any roll, so the queue is full.)
    }

    [Fact]
    public void Skips_dead_insurgent()
    {
        var world = NewWorld();
        var ins = MakeIns(world);
        ins.IsAlive = false;

        var rng = new ScriptedRng(ints: new[] { 5 });
        var plan = InsurgentPlanner.Plan(ins, world, Array.Empty<Unit>(),
            Array.Empty<ProvinceAdjacency>(), 1, rng);

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().BeEmpty();
    }

    [Fact]
    public void Attack_branch_ignores_unowned_provinces()
    {
        var world = NewWorld();
        var ins = MakeIns(world, money: 5_000, steel: 1_000, manpower: 1_000);
        var mine = AddProvince(world, ins, "Mine");
        var unowned = AddProvince(world, owner: null, "Wild");
        AddUnit(world, ins, mine, UnitType.MechInfantry, 100);
        AddBuilding(mine, BuildingType.RecruitmentCenter);

        // Roll = 5 (attack) -> no valid target (only unowned neighbour) -> falls to recruit.
        // Recruit start = 0 -> MechInfantry succeeds.
        var rng = new ScriptedRng(ints: new[] { 5, 0 });
        var plan = InsurgentPlanner.Plan(ins, world,
            world.Provinces.SelectMany(p => p.UnitsStationed).ToList(),
            new[] { Adj(mine, unowned) }, 1, rng);

        plan.UnitOrders.Should().BeEmpty();
        plan.ConstructionOrders.Should().ContainSingle();
        plan.ConstructionOrders.Single().UnitType.Should().Be(UnitType.MechInfantry);
    }

    /// <summary>
    /// Local-only helper mirroring RandomEventStepTests. We don't promote it to TickTestGraph
    /// until a third consumer appears.
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
            if (_ints.Count == 0)
                throw new InvalidOperationException("ScriptedRng ran out of int values.");
            return _ints.Dequeue() % Math.Max(1, exclusiveMax);
        }
        public double NextDouble()
        {
            if (_doubles.Count == 0)
                throw new InvalidOperationException("ScriptedRng ran out of double values.");
            return _doubles.Dequeue();
        }
    }
}

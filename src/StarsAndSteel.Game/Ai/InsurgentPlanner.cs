using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Combat;
using StarsAndSteel.Game.Orders;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Game.Ai;

/// <summary>
/// Insurgent (Wildcard) planner per <c>docs/09-AI-OPPONENTS.md</c>: "Random per game from a
/// wide range; rerolls weekly. Goal: chaos. Declares wars without warning, signs peace just
/// as randomly. Tells: makes no sense, that's the point. <i>Watch this.</i>"
/// <para/>
/// Phase 4d behaviour — pure-RNG action picker. Each tick rolls a single d100 to choose ONE
/// of four branches:
/// <list type="number">
///   <item><b>30%</b> — Random adjacent attack with NO margin check (chaotic poke; might
///         lose hard, that's the point).</item>
///   <item><b>30%</b> — Recruit a random unit from the chaos pool at any owned province
///         that hosts the required building.</item>
///   <item><b>30%</b> — Construct a random building from the chaos pool at a random owned
///         province that lacks it.</item>
///   <item><b>10%</b> — Idle. "Watch this."</item>
/// </list>
/// All sub-selections (which unit, which building, which province, which target) pull from
/// <see cref="IRandomSource"/> so behaviour is deterministic per world seed and replays
/// reproduce. Multiplier rerolls + diplomacy chaos land in later phases when those systems
/// exist; this planner captures the "no coherent strategy" personality MVP.
/// <para/>
/// Pure: takes the in-memory graph, returns orders, debits <paramref name="me"/>'s
/// resources when a build/recruit is queued (matching the controller path).
/// </summary>
public static class InsurgentPlanner
{
    /// <summary>Branch thresholds (cumulative). Roll = 0..99.</summary>
    private const int AttackThreshold = 30;
    private const int RecruitThreshold = 60;
    private const int BuildThreshold = 90;
    // 90..99 = idle.

    /// <summary>Chaos unit pool — one from each domain plus AA. Cheap-to-mid cost.</summary>
    private static readonly UnitType[] ChaosUnits =
    {
        UnitType.MechInfantry,
        UnitType.MainBattleTank,
        UnitType.AABattery,
        UnitType.CombatDrone,
        UnitType.MultiroleFighter,
    };

    /// <summary>Chaos building pool — economy + military mix.</summary>
    private static readonly BuildingType[] ChaosBuildings =
    {
        BuildingType.RecruitmentCenter,
        BuildingType.MilitaryBase,
        BuildingType.AirBase,
        BuildingType.FinancialDistrict,
        BuildingType.Refinery,
        BuildingType.SteelMill,
        BuildingType.TechPark,
    };

    public static AiPlan Plan(
        Player me,
        GameWorld world,
        IEnumerable<Unit> allUnits,
        IEnumerable<ProvinceAdjacency> adjacencies,
        int processingTick,
        IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(me);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(allUnits);
        ArgumentNullException.ThrowIfNull(adjacencies);
        ArgumentNullException.ThrowIfNull(rng);

        if (!me.IsAi || me.AiPersonality != AiPersonality.Insurgent || !me.IsAlive)
            return Empty;

        var roll = rng.NextInt(100);

        // Branch 1: Chaotic attack.
        if (roll < AttackThreshold)
        {
            var unitList = allUnits as IList<Unit> ?? allUnits.ToList();
            var adjList = adjacencies as IList<ProvinceAdjacency> ?? adjacencies.ToList();
            var attack = TryPickRandomAttack(me, world, unitList, adjList, rng);
            if (attack is not null)
                return new AiPlan(new[] { BuildAttackOrder(attack.Value.Unit, attack.Value.Target, processingTick) },
                                  Array.Empty<ConstructionOrder>());
            // Fall through to recruit if no valid attack found — Insurgent still wants to do *something*.
        }

        // Branch 2: Random recruit (also fallback from failed attack — note AttackThreshold
        // < RecruitThreshold so a failed-attack roll still satisfies this guard).
        if (roll < RecruitThreshold)
        {
            var recruit = TryQueueRandomRecruit(me, world, processingTick, rng);
            if (recruit is not null)
                return new AiPlan(Array.Empty<UnitOrder>(), new[] { recruit });
            // Fall through to build.
        }

        // Branch 3: Random building.
        if (roll < BuildThreshold)
        {
            var build = TryQueueRandomBuilding(me, world, processingTick, rng);
            if (build is not null)
                return new AiPlan(Array.Empty<UnitOrder>(), new[] { build });
        }

        // Branch 4: Idle (or all branches failed). Watch this.
        return Empty;
    }

    private static readonly AiPlan Empty = new(Array.Empty<UnitOrder>(), Array.Empty<ConstructionOrder>());

    private readonly record struct AttackChoice(Unit Unit, Province Target);

    /// <summary>
    /// Chaotic attack picker: enumerate all (my-ground-unit, adjacent-enemy-province) pairs,
    /// pick one uniformly at random. NO strength margin check — Insurgent will gladly throw
    /// 100 MechInf at 5000 MBT. That's the personality.
    /// </summary>
    private static AttackChoice? TryPickRandomAttack(
        Player me, GameWorld world,
        IList<Unit> allUnits, IList<ProvinceAdjacency> adjacencies, IRandomSource rng)
    {
        // Adjacency lookup.
        var adjMap = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var e in adjacencies)
        {
            if (!adjMap.TryGetValue(e.ProvinceAId, out var aSet)) { aSet = new(); adjMap[e.ProvinceAId] = aSet; }
            aSet.Add(e.ProvinceBId);
            if (!adjMap.TryGetValue(e.ProvinceBId, out var bSet)) { bSet = new(); adjMap[e.ProvinceBId] = bSet; }
            bSet.Add(e.ProvinceAId);
        }

        var provinceById = world.Provinces.ToDictionary(p => p.Id);

        // Index my ground units by location (skip in-transit, AA, dead).
        var myUnitsByLocation = new Dictionary<Guid, List<Unit>>();
        foreach (var u in allUnits)
        {
            if (u.OwnerPlayerId != me.Id) continue;
            if (u.IsInTransit || u.Strength <= 0) continue;
            if (u.Domain != UnitDomain.Ground || u.Type == UnitType.AABattery) continue;
            if (u.LocationProvinceId is not Guid loc) continue;
            if (!myUnitsByLocation.TryGetValue(loc, out var list)) { list = new(); myUnitsByLocation[loc] = list; }
            list.Add(u);
        }

        // Build candidate (unit, target) list — deterministic ordering for stable RNG selection.
        var candidates = new List<AttackChoice>();
        foreach (var province in me.OwnedProvinces.OrderBy(p => p.Id))
        {
            if (!myUnitsByLocation.TryGetValue(province.Id, out var here)) continue;
            if (!adjMap.TryGetValue(province.Id, out var neighbours)) continue;

            foreach (var nid in neighbours.OrderBy(g => g))
            {
                if (!provinceById.TryGetValue(nid, out var target)) continue;
                if (target.OwnerPlayerId == me.Id) continue;
                if (target.OwnerPlayerId is null) continue; // unowned — Insurgent prefers actual targets

                foreach (var attacker in here.OrderBy(u => u.Id))
                    candidates.Add(new AttackChoice(attacker, target));
            }
        }

        if (candidates.Count == 0) return null;
        return candidates[rng.NextInt(candidates.Count)];
    }

    private static UnitOrder BuildAttackOrder(Unit attacker, Province target, int processingTick) => new()
    {
        Id = Guid.NewGuid(),
        UnitId = attacker.Id,
        Unit = attacker,
        OrderType = OrderType.Attack,
        TargetProvinceId = target.Id,
        TargetProvince = target,
        IssuedAtTick = processingTick,
        Status = OrderStatus.Pending,
    };

    /// <summary>
    /// Random recruit: pick a chaos-pool unit at random, then delegate to
    /// IndustrialistPlanner.TryQueueRecruitment (which handles affordability + building check).
    /// If the rolled unit isn't viable, walk the rest of the pool from the rolled offset
    /// (deterministic) and try each once before giving up.
    /// </summary>
    private static ConstructionOrder? TryQueueRandomRecruit(
        Player me, GameWorld world, int processingTick, IRandomSource rng)
    {
        var start = rng.NextInt(ChaosUnits.Length);
        for (int i = 0; i < ChaosUnits.Length; i++)
        {
            var type = ChaosUnits[(start + i) % ChaosUnits.Length];
            var order = IndustrialistPlanner.TryQueueRecruitment(me, world, type, 1000, processingTick);
            if (order is not null) return order;
        }
        return null;
    }

    /// <summary>
    /// Random building: pick a chaos-pool building at random and place it on a random owned
    /// province that lacks it. Walk pool from rolled offset on failure (same scheme as recruit).
    /// </summary>
    private static ConstructionOrder? TryQueueRandomBuilding(
        Player me, GameWorld world, int processingTick, IRandomSource rng)
    {
        var start = rng.NextInt(ChaosBuildings.Length);
        for (int i = 0; i < ChaosBuildings.Length; i++)
        {
            var type = ChaosBuildings[(start + i) % ChaosBuildings.Length];
            var order = TryQueueBuilding(me, world, type, processingTick, rng);
            if (order is not null) return order;
        }
        return null;
    }

    private static ConstructionOrder? TryQueueBuilding(
        Player me, GameWorld world, BuildingType type, int processingTick, IRandomSource rng)
    {
        if (!BuildCatalog.IsBuildingBuildable(type)) return null;
        var spec = BuildCatalog.GetBuilding(type);

        if (me.Money < spec.Money || me.Oil < spec.Oil || me.Steel < spec.Steel
            || me.Electronics < spec.Electronics || me.Food < spec.Food || me.Manpower < spec.Manpower)
            return null;

        // Eligible provinces: owned, lacking this building. Deterministic order, then random pick.
        var eligible = me.OwnedProvinces
            .Where(p => p.Buildings.All(b => b.Type != type))
            .OrderBy(p => p.Id)
            .ToList();
        if (eligible.Count == 0) return null;

        var province = eligible[rng.NextInt(eligible.Count)];

        me.Money       -= spec.Money;
        me.Oil         -= spec.Oil;
        me.Steel       -= spec.Steel;
        me.Electronics -= spec.Electronics;
        me.Food        -= spec.Food;
        me.Manpower    -= spec.Manpower;

        return new ConstructionOrder
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            OwnerPlayerId = me.Id,
            OwnerPlayer = me,
            ProvinceId = province.Id,
            Province = province,
            OrderType = OrderType.BuildBuilding,
            UnitType = null,
            Quantity = 1,
            BuildingType = type,
            IssuedAtTick = processingTick,
            TicksRemaining = spec.TicksToBuild,
            Status = OrderStatus.Pending,
        };
    }
}

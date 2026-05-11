using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Orders;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Game.Ai;

/// <summary>
/// Isolationist planner per <c>docs/09-AI-OPPONENTS.md</c>: "Build Defense ×1.7,
/// AA Investment ×1.5, Attack ×0.4". MVP behaviour:
/// <list type="number">
///   <item>For any owned province that borders an enemy AND lacks a MilitaryBase,
///         queue a MilitaryBase (the cheapest path to fielding AABattery).</item>
///   <item>Otherwise, recruit AABattery (anti-air) at a province with a MilitaryBase.</item>
///   <item>Otherwise, recruit MechInfantry as garrison (RecruitmentCenter required).</item>
/// </list>
/// Isolationists never initiate attacks. Border = adjacent to a province whose owner
/// is not me (including unowned). Pure: takes the in-memory graph, returns orders.
/// </summary>
public static class IsolationistPlanner
{
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
        ArgumentNullException.ThrowIfNull(adjacencies);
        ArgumentNullException.ThrowIfNull(rng);
        _ = allUnits; // unused: isolationist plans purely off ownership topology

        if (!me.IsAi || me.AiPersonality != AiPersonality.Isolationist || !me.IsAlive)
            return Empty;

        // 1) MilitaryBase on a border province — gates AABattery production.
        var mb = TryQueueBorderMilitaryBase(me, world, adjacencies, processingTick);
        if (mb is not null)
            return new AiPlan(Array.Empty<UnitOrder>(), new[] { mb });

        // 2) AABattery (anti-air investment).
        var aa = IndustrialistPlanner.TryQueueRecruitment(me, world, UnitType.AABattery, 1000, processingTick);
        if (aa is not null)
            return new AiPlan(Array.Empty<UnitOrder>(), new[] { aa });

        // 3) MechInfantry garrison.
        var inf = IndustrialistPlanner.TryQueueRecruitment(me, world, UnitType.MechInfantry, 1000, processingTick);
        if (inf is not null)
            return new AiPlan(Array.Empty<UnitOrder>(), new[] { inf });

        return Empty;
    }

    private static readonly AiPlan Empty = new(Array.Empty<UnitOrder>(), Array.Empty<ConstructionOrder>());

    private static ConstructionOrder? TryQueueBorderMilitaryBase(
        Player me, GameWorld world, IEnumerable<ProvinceAdjacency> adjacencies, int processingTick)
    {
        const BuildingType type = BuildingType.MilitaryBase;
        var spec = BuildCatalog.GetBuilding(type);

        if (me.Money < spec.Money || me.Oil < spec.Oil || me.Steel < spec.Steel
            || me.Electronics < spec.Electronics || me.Food < spec.Food || me.Manpower < spec.Manpower)
            return null;

        // Build adjacency lookup.
        var adjMap = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var e in adjacencies)
        {
            if (!adjMap.TryGetValue(e.ProvinceAId, out var aSet)) { aSet = new(); adjMap[e.ProvinceAId] = aSet; }
            aSet.Add(e.ProvinceBId);
            if (!adjMap.TryGetValue(e.ProvinceBId, out var bSet)) { bSet = new(); adjMap[e.ProvinceBId] = bSet; }
            bSet.Add(e.ProvinceAId);
        }

        var provinceById = world.Provinces.ToDictionary(p => p.Id);

        // Pick the first owned border province lacking a MilitaryBase (lex-Guid order).
        var province = me.OwnedProvinces
            .Where(p => p.Buildings.All(b => b.Type != type)
                && IsBorderProvince(p, me.Id, adjMap, provinceById))
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (province is null) return null;

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

    private static bool IsBorderProvince(
        Province province, Guid myId,
        IReadOnlyDictionary<Guid, HashSet<Guid>> adjMap,
        IReadOnlyDictionary<Guid, Province> provinceById)
    {
        if (!adjMap.TryGetValue(province.Id, out var neighbours)) return false;
        foreach (var nid in neighbours)
        {
            if (!provinceById.TryGetValue(nid, out var n)) continue;
            if (n.OwnerPlayerId != myId) return true; // unowned or hostile
        }
        return false;
    }
}

using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Combat;
using StarsAndSteel.Game.Orders;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Game.Ai;

/// <summary>
/// Result of a single planner pass: orders to inject into the current tick's pending
/// queues, and an indication that <c>me</c>'s resource columns were debited (the caller
/// already mutated the player; this is informational).
/// </summary>
public sealed record AiPlan(
    IReadOnlyList<UnitOrder> UnitOrders,
    IReadOnlyList<ConstructionOrder> ConstructionOrders);

/// <summary>
/// MVP Hawk planner: greedy "attack-or-recruit" heuristic per <c>docs/09-AI-OPPONENTS.md</c>.
/// Hawk multipliers (Attack ×1.5, Build Military ×1.3, Diplomacy ×0.6) aren't applied as
/// scorer weights in MVP — there's only one action class per branch — but the priority
/// order (attack first, recruit fallback) reflects the personality.
/// <para/>
/// Pure: takes the in-memory graph, returns orders. Mutates only the resource columns on
/// <paramref name="me"/> when a recruitment order is queued (matching the controller path
/// in <see cref="OrderService.DebitForBuild"/>). Designed to be called from
/// <see cref="Tick.Steps.AiTurnStep"/> against the same tracked entities the runner
/// will SaveChanges later.
/// </summary>
public static class HawkPlanner
{
    /// <summary>
    /// Decision budget per tick. Hawk MVP issues at most one action per tick — attack
    /// preferred over recruit. Future phases will lift this per docs/09.
    /// </summary>
    private const int MaxActionsPerTick = 1;

    /// <summary>
    /// Plan one tick of Hawk activity. <paramref name="processingTick"/> must equal
    /// <c>TickContext.ProcessingTick</c> so emitted orders are consumable by the
    /// downstream Movement / Combat / Construction steps in the same tick.
    /// </summary>
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

        if (!me.IsAi || me.AiPersonality != AiPersonality.Hawk || !me.IsAlive)
        {
            return Empty;
        }

        var adjList = adjacencies as IList<ProvinceAdjacency> ?? adjacencies.ToList();
        var unitList = allUnits as IList<Unit> ?? allUnits.ToList();

        // Build adjacency lookup: provinceId -> set of adjacent provinceIds.
        var adjMap = new Dictionary<Guid, HashSet<Guid>>(adjList.Count * 2);
        foreach (var edge in adjList)
        {
            if (!adjMap.TryGetValue(edge.ProvinceAId, out var aSet))
            {
                aSet = new HashSet<Guid>();
                adjMap[edge.ProvinceAId] = aSet;
            }
            aSet.Add(edge.ProvinceBId);
            if (!adjMap.TryGetValue(edge.ProvinceBId, out var bSet))
            {
                bSet = new HashSet<Guid>();
                adjMap[edge.ProvinceBId] = bSet;
            }
            bSet.Add(edge.ProvinceAId);
        }

        var provinceById = world.Provinces.ToDictionary(p => p.Id);

        // 1) Try to find a profitable adjacent attack from any owned province.
        var attack = TryPickAttack(me, unitList, provinceById, adjMap);
        if (attack is not null)
        {
            return new AiPlan(
                UnitOrders: new[] { BuildAttackOrder(attack.Value.Unit, attack.Value.Target, processingTick) },
                ConstructionOrders: Array.Empty<ConstructionOrder>());
        }

        // 2) Otherwise, queue a recruit at the cheapest viable owned province with a
        //    Recruitment Center (MechInfantry needs RC per BuildCatalog).
        if (MaxActionsPerTick >= 1)
        {
            var recruit = TryQueueRecruitment(me, world, processingTick);
            if (recruit is not null)
            {
                return new AiPlan(
                    UnitOrders: Array.Empty<UnitOrder>(),
                    ConstructionOrders: new[] { recruit });
            }
        }

        return Empty;
    }

    private static readonly AiPlan Empty = new(
        Array.Empty<UnitOrder>(),
        Array.Empty<ConstructionOrder>());

    private readonly record struct AttackChoice(Unit Unit, Province Target, double Score);

    private static AttackChoice? TryPickAttack(
        Player me,
        IList<Unit> allUnits,
        IReadOnlyDictionary<Guid, Province> provinceById,
        IReadOnlyDictionary<Guid, HashSet<Guid>> adjMap)
    {
        // Index units by location for cheap defender lookup. Excludes in-transit units
        // (they aren't actually somewhere defendable).
        var unitsByLocation = new Dictionary<Guid, List<Unit>>(allUnits.Count);
        foreach (var u in allUnits)
        {
            if (u.IsInTransit) continue;
            if (u.LocationProvinceId is not Guid loc) continue;
            if (!unitsByLocation.TryGetValue(loc, out var list))
            {
                list = new List<Unit>();
                unitsByLocation[loc] = list;
            }
            list.Add(u);
        }

        AttackChoice? best = null;

        foreach (var province in me.OwnedProvinces)
        {
            if (!unitsByLocation.TryGetValue(province.Id, out var here)) continue;

            // Pick the strongest ground attacker stationed here (skip AA — defensive only,
            // and skip in-transit which we already filtered).
            var attacker = here
                .Where(u => u.OwnerPlayerId == me.Id
                            && u.Domain == UnitDomain.Ground
                            && u.Type != UnitType.AABattery
                            && u.Strength > 0)
                .OrderByDescending(u => u.Strength * CombatStats.UnitTypeStrength(u.Type))
                .FirstOrDefault();
            if (attacker is null) continue;

            if (!adjMap.TryGetValue(province.Id, out var neighbours)) continue;

            foreach (var neighbourId in neighbours)
            {
                if (!provinceById.TryGetValue(neighbourId, out var neighbour)) continue;
                if (neighbour.OwnerPlayerId == me.Id) continue; // not a target if I own it

                var defenderStrength = 0.0;
                if (unitsByLocation.TryGetValue(neighbour.Id, out var defenders))
                {
                    foreach (var d in defenders)
                    {
                        if (d.OwnerPlayerId == me.Id) continue;
                        defenderStrength += d.Strength * CombatStats.UnitTypeStrength(d.Type);
                    }
                }

                var attackStrength = attacker.Strength * CombatStats.UnitTypeStrength(attacker.Type);
                // Hawk wants a comfortable margin — require attacker > defender by 20%.
                // Score is the absolute margin so the best target wins.
                var margin = attackStrength - defenderStrength * 1.2;
                if (margin <= 0) continue;

                if (best is null || margin > best.Value.Score)
                {
                    best = new AttackChoice(attacker, neighbour, margin);
                }
            }
        }

        return best;
    }

    private static UnitOrder BuildAttackOrder(Unit attacker, Province target, int processingTick) => new()
    {
        Id = Guid.NewGuid(),
        UnitId = attacker.Id,
        Unit = attacker,
        OrderType = OrderType.Attack,
        TargetProvinceId = target.Id,
        TargetProvince = target,
        // AI runs as the FIRST step of this tick, so orders must be eligible THIS tick:
        // IssuedAtTick == ProcessingTick (not +1 like the controller path).
        IssuedAtTick = processingTick,
        Status = OrderStatus.Pending,
    };

    private static ConstructionOrder? TryQueueRecruitment(Player me, GameWorld world, int processingTick)
    {
        // MechInfantry per StarterPackage; cheapest unit + RC is the starter building.
        const UnitType type = UnitType.MechInfantry;
        const int quantity = 1000;
        var spec = BuildCatalog.GetUnit(type);

        // Linear scaling like OrderService.
        var f = quantity / 1000.0;
        long money       = (long)Math.Ceiling(spec.Money       * f);
        long oil         = (long)Math.Ceiling(spec.Oil         * f);
        long steel       = (long)Math.Ceiling(spec.Steel       * f);
        long electronics = (long)Math.Ceiling(spec.Electronics * f);
        long food        = (long)Math.Ceiling(spec.Food        * f);
        long manpower    = (long)Math.Ceiling(spec.Manpower    * f);

        if (me.Money < money || me.Oil < oil || me.Steel < steel
            || me.Electronics < electronics || me.Food < food || me.Manpower < manpower)
        {
            return null;
        }

        // Find a province I own with the required building. Lex-Guid order for determinism.
        var province = me.OwnedProvinces
            .Where(p => p.Buildings.Any(b => b.Type == spec.RequiredBuilding))
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (province is null) return null;

        // Debit. Mirrors OrderService.DebitForBuild.
        me.Money       -= money;
        me.Oil         -= oil;
        me.Steel       -= steel;
        me.Electronics -= electronics;
        me.Food        -= food;
        me.Manpower    -= manpower;

        return new ConstructionOrder
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            OwnerPlayerId = me.Id,
            OwnerPlayer = me,
            ProvinceId = province.Id,
            Province = province,
            OrderType = OrderType.BuildUnit,
            UnitType = type,
            Quantity = quantity,
            BuildingType = null,
            // Eligible THIS tick — same rationale as attack order above.
            IssuedAtTick = processingTick,
            TicksRemaining = spec.TicksToBuild,
            Status = OrderStatus.Pending,
        };
    }
}

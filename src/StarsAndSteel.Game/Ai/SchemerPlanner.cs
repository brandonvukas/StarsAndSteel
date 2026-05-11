using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Combat;
using StarsAndSteel.Game.Orders;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Game.Ai;

/// <summary>
/// Schemer planner per <c>docs/09-AI-OPPONENTS.md</c>: "Cyber ×1.6, Espionage ×1.5,
/// conventional Attack ×0.7, Diplomacy dynamic". Cyber + espionage land in Phase 3,
/// so MVP-2J behaviour is the conventional shadow:
/// <list type="number">
///   <item>Build CombatDrones (electronics-heavy, low-manpower) — surprisingly small
///         standing army, high tech tells.</item>
///   <item>Only attack when the strength margin is large (≥3× defender) AND the target
///         is isolated (defender has no friendly stack in any of its neighbours).</item>
///   <item>Otherwise, recruit MechInfantry as a token presence.</item>
/// </list>
/// "Pretends friendliness" is enforced by the high attack threshold; backstabs land in
/// Phase 3 with treaty-aware diplomacy. Pure: takes the in-memory graph, returns orders.
/// </summary>
public static class SchemerPlanner
{
    /// <summary>Strength margin attacker must exceed defender to commit. Higher than Hawk's 1.2.</summary>
    private const double MinAttackMargin = 3.0;

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

        if (!me.IsAi || me.AiPersonality != AiPersonality.Schemer || !me.IsAlive)
            return Empty;

        var unitList = allUnits as IList<Unit> ?? allUnits.ToList();
        var adjList = adjacencies as IList<ProvinceAdjacency> ?? adjacencies.ToList();

        // 1) Opportunistic strike: only against an isolated, vastly-outnumbered target.
        var attack = TryPickIsolatedAttack(me, world, unitList, adjList);
        if (attack is not null)
        {
            return new AiPlan(
                new[] { BuildAttackOrder(attack.Value.Unit, attack.Value.Target, processingTick) },
                Array.Empty<ConstructionOrder>());
        }

        // 2) Build a CombatDrone (high-tech tell). Falls through if no AirBase or insufficient
        //    electronics — Schemer doesn't insist.
        var drone = IndustrialistPlanner.TryQueueRecruitment(me, world, UnitType.CombatDrone, 1000, processingTick);
        if (drone is not null)
            return new AiPlan(Array.Empty<UnitOrder>(), new[] { drone });

        // 3) Token MechInfantry recruit.
        var inf = IndustrialistPlanner.TryQueueRecruitment(me, world, UnitType.MechInfantry, 1000, processingTick);
        if (inf is not null)
            return new AiPlan(Array.Empty<UnitOrder>(), new[] { inf });

        return Empty;
    }

    private static readonly AiPlan Empty = new(Array.Empty<UnitOrder>(), Array.Empty<ConstructionOrder>());

    private readonly record struct AttackChoice(Unit Unit, Province Target, double Score);

    private static AttackChoice? TryPickIsolatedAttack(
        Player me, GameWorld world, IList<Unit> allUnits, IList<ProvinceAdjacency> adjacencies)
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

        // Index living non-transit units by location for defender/reinforcement lookup.
        var unitsByLocation = new Dictionary<Guid, List<Unit>>();
        foreach (var u in allUnits)
        {
            if (u.IsInTransit || u.Strength <= 0) continue;
            if (u.LocationProvinceId is not Guid loc) continue;
            if (!unitsByLocation.TryGetValue(loc, out var list)) { list = new(); unitsByLocation[loc] = list; }
            list.Add(u);
        }

        var provinceById = world.Provinces.ToDictionary(p => p.Id);

        AttackChoice? best = null;

        foreach (var province in me.OwnedProvinces)
        {
            if (!unitsByLocation.TryGetValue(province.Id, out var here)) continue;
            var attacker = here
                .Where(u => u.OwnerPlayerId == me.Id && u.Domain == UnitDomain.Ground
                    && u.Type != UnitType.AABattery && u.Strength > 0)
                .OrderByDescending(u => u.Strength * CombatStats.UnitTypeStrength(u.Type))
                .FirstOrDefault();
            if (attacker is null) continue;
            if (!adjMap.TryGetValue(province.Id, out var neighbours)) continue;

            foreach (var nid in neighbours)
            {
                if (!provinceById.TryGetValue(nid, out var target)) continue;
                if (target.OwnerPlayerId == me.Id) continue;
                if (target.OwnerPlayerId is not Guid targetOwner) continue; // skip unowned (no scheme value)

                // Defender strength at the target province.
                double defenderStrength = 0.0;
                if (unitsByLocation.TryGetValue(target.Id, out var defenders))
                {
                    foreach (var d in defenders)
                    {
                        if (d.OwnerPlayerId == me.Id) continue;
                        defenderStrength += d.Strength * CombatStats.UnitTypeStrength(d.Type);
                    }
                }

                // Isolation: target's neighbours must contain NO friendly-to-defender stack
                // (i.e. no reinforcements one move away). Schemer goes after orphans only.
                if (!adjMap.TryGetValue(target.Id, out var targetNeighbours)) continue;
                bool hasReinforcement = false;
                foreach (var tnid in targetNeighbours)
                {
                    if (tnid == province.Id) continue; // exclude my province
                    if (!unitsByLocation.TryGetValue(tnid, out var others)) continue;
                    if (others.Any(u => u.OwnerPlayerId == targetOwner && u.Strength > 0))
                    { hasReinforcement = true; break; }
                }
                if (hasReinforcement) continue;

                var attackStrength = attacker.Strength * CombatStats.UnitTypeStrength(attacker.Type);
                // Schemer threshold: attacker must be ≥3× defender (or defender = 0).
                if (defenderStrength > 0 && attackStrength < defenderStrength * MinAttackMargin) continue;

                var score = attackStrength - defenderStrength * MinAttackMargin;
                if (best is null || score > best.Value.Score)
                    best = new AttackChoice(attacker, target, score);
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
        IssuedAtTick = processingTick,
        Status = OrderStatus.Pending,
    };
}

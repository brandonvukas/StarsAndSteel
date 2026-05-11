using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Step 5 of the tick pipeline (docs/07 §"MovementStep"). MVP simplification: a Move or
/// Attack order completes in one tick — the unit jumps from its current province to the
/// adjacent target. Multi-tick path traversal (with <see cref="Unit.TransitArrivalTick"/>
/// and per-terrain costs) lands in Phase 2 once the map has more than one adjacency.
/// <para/>
/// We only consume <see cref="OrderType.Move"/> here. <see cref="OrderType.Attack"/> orders
/// are also relocations — they put the attacker into the target province so
/// <see cref="CombatStep"/> finds it co-located with the defender. The order is then marked
/// <see cref="OrderStatus.Complete"/> regardless of whether combat happens (combat is the
/// consequence, not part of "movement").
/// </summary>
public sealed class MovementStep : ITickStep
{
    public string Name => "Movement";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Index adjacency once for O(1) "is X adjacent to Y?" checks.
        var adj = new HashSet<(Guid, Guid)>(context.Adjacencies.Count * 2);
        foreach (var e in context.Adjacencies)
        {
            adj.Add((e.ProvinceAId, e.ProvinceBId));
            adj.Add((e.ProvinceBId, e.ProvinceAId));
        }

        // Index units by id for fast lookup.
        var unitsById = context.Units.ToDictionary(u => u.Id);
        var provinceById = context.World.Provinces.ToDictionary(p => p.Id);

        foreach (var order in context.PendingUnitOrders)
        {
            if (order.Status != OrderStatus.Pending) continue;
            if (order.OrderType != OrderType.Move && order.OrderType != OrderType.Attack) continue;
            if (!unitsById.TryGetValue(order.UnitId, out var unit)) { order.Status = OrderStatus.Cancelled; continue; }
            if (unit.Strength <= 0) { order.Status = OrderStatus.Cancelled; continue; }
            // Phase 2I: ground or naval may execute a Move/Attack order. Air units use AirStrike.
            if (unit.Domain != UnitDomain.Ground && unit.Domain != UnitDomain.Naval)
            { order.Status = OrderStatus.Cancelled; continue; }
            if (order.TargetProvinceId is null) { order.Status = OrderStatus.Cancelled; continue; }
            if (unit.LocationProvinceId is null) { order.Status = OrderStatus.Cancelled; continue; }

            var from = unit.LocationProvinceId.Value;
            var to = order.TargetProvinceId.Value;

            // Adjacency check (defensive — controller already checked, but state may have changed).
            if (!adj.Contains((from, to))) { order.Status = OrderStatus.Cancelled; continue; }

            // Phase 2I: naval units may only move coastal-to-coastal (no inland traversal).
            // True ocean tiles are deferred; sea-crossing edges in shared/map-data.json
            // already connect coastal land provinces (HI<->CA, AK<->WA, etc.).
            if (unit.Domain == UnitDomain.Naval)
            {
                var fromProv = provinceById.TryGetValue(from, out var fp) ? fp : null;
                var toProv = provinceById.TryGetValue(to, out var tp) ? tp : null;
                if (fromProv is null || toProv is null || !fromProv.IsCoastal || !toProv.IsCoastal)
                {
                    order.Status = OrderStatus.Cancelled;
                    continue;
                }
            }

            // Phase 2E: diplomacy gating. Both Move and Attack relocate the stack into the
            // target province; if the destination is owned by a third party we have to know
            // whether crossing is permitted. Same-owner / unowned / allied destinations are
            // always permitted (allied = friendly passage). Otherwise the order is only
            // valid if the relation is hostile (explicit War or no row), and even then a
            // Move into a hostile owner's province is rejected — the player must use Attack
            // to express intent. This protects against accidental friendly-fire from stale
            // orders queued before a peace treaty was ratified.
            if (provinceById.TryGetValue(to, out var destProvince) &&
                destProvince.OwnerPlayerId is Guid destOwner &&
                destOwner != unit.OwnerPlayerId)
            {
                var hostile = context.Relations.IsHostile(unit.OwnerPlayerId, destOwner);
                if (!hostile)
                {
                    // Explicit Peace / NAP / TradeAgreement / Allied (allied is allowed below).
                    if (!context.Relations.AreAllied(unit.OwnerPlayerId, destOwner))
                    {
                        order.Status = OrderStatus.Cancelled;
                        continue;
                    }
                    // Allied: fall through, friendly passage permitted.
                }
                else if (order.OrderType == OrderType.Move)
                {
                    // Hostile owner but the player only issued Move (no combat intent).
                    // Cancel rather than silently turning it into an attack.
                    order.Status = OrderStatus.Cancelled;
                    continue;
                }
            }

            unit.LocationProvinceId = to;
            unit.IsInTransit = false;
            unit.TransitFromProvinceId = null;
            unit.TransitToProvinceId = null;
            unit.TransitArrivalTick = null;
            order.Status = OrderStatus.Complete;

            context.Events.Add(new UnitMovedEvent(
                Tick: context.ProcessingTick,
                UnitId: unit.Id,
                OwnerPlayerId: unit.OwnerPlayerId,
                FromProvinceId: from,
                ToProvinceId: to));
        }
    }
}

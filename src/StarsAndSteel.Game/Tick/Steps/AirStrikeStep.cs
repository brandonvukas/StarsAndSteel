using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Combat;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Step 6 of the tick pipeline (docs/07 §"AirStrikeStep"). Drains pending
/// <see cref="OrderType.AirStrike"/> orders and applies the air-strike portion of the
/// docs/04 combat formula via <see cref="CombatResolver.ResolveAirStrike"/>:
/// defending fighters intercept → AA fires (unless stealth bypass) → surviving attackers
/// damage ground stacks per the unit-interaction matrix.
/// <para/>
/// MVP scoping:
/// <list type="bullet">
///   <item>No range check (every air unit can hit any province; deferred to Phase 2).</item>
///   <item>Air units do not relocate to the target — they "raid and return".</item>
///   <item>Friendly air units at the target are not collateral targets.</item>
/// </list>
/// </summary>
public sealed class AirStrikeStep : ITickStep
{
    public string Name => "AirStrike";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var unitsById = context.Units.ToDictionary(u => u.Id);
        var unitsByProvince = context.Units
            .Where(u => u.LocationProvinceId.HasValue && u.Strength > 0)
            .GroupBy(u => u.LocationProvinceId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var order in context.PendingUnitOrders)
        {
            if (order.Status != OrderStatus.Pending) continue;
            if (order.OrderType != OrderType.AirStrike) continue;
            if (!unitsById.TryGetValue(order.UnitId, out var attacker)) { order.Status = OrderStatus.Cancelled; continue; }
            if (attacker.Strength <= 0) { order.Status = OrderStatus.Cancelled; continue; }
            if (attacker.Domain != UnitDomain.Air) { order.Status = OrderStatus.Cancelled; continue; }
            if (order.TargetProvinceId is null) { order.Status = OrderStatus.Cancelled; continue; }

            var targetId = order.TargetProvinceId.Value;
            var enemiesAtTarget = unitsByProvince.TryGetValue(targetId, out var stacks)
                ? stacks.Where(u => u.OwnerPlayerId != attacker.OwnerPlayerId).ToList()
                : new List<Unit>();

            // Even with no defenders we still mark complete and emit an event.
            var preAttackerStrength = attacker.Strength;
            var preDefenderTotal = enemiesAtTarget.Sum(u => u.Strength);

            var outcome = CombatResolver.ResolveAirStrike(attacker, enemiesAtTarget, context.Rng);

            ApplyCasualties(context, outcome.Casualties, "AirStrike");

            var attackerLoss = preAttackerStrength - attacker.Strength;
            var defenderLoss = preDefenderTotal - enemiesAtTarget.Sum(u => u.Strength);

            order.Status = OrderStatus.Complete;
            context.Events.Add(new AirStrikeResolvedEvent(
                Tick: context.ProcessingTick,
                AttackerUnitId: attacker.Id,
                AttackerPlayerId: attacker.OwnerPlayerId,
                TargetProvinceId: targetId,
                AttackerStrengthLoss: attackerLoss,
                DefenderStrengthLoss: defenderLoss));
        }
    }

    /// <summary>
    /// Apply <see cref="CombatResolver.StackCasualty"/> deltas to units in the context,
    /// emit <see cref="UnitDestroyedEvent"/> for any stack that hits 0, and queue the
    /// destroyed units for deletion by the runner.
    /// </summary>
    internal static void ApplyCasualties(TickContext context, IReadOnlyList<CombatResolver.StackCasualty> casualties, string cause)
    {
        var unitsById = context.Units.ToDictionary(u => u.Id);
        foreach (var c in casualties)
        {
            if (!unitsById.TryGetValue(c.UnitId, out var unit)) continue;
            unit.Strength = Math.Max(0, unit.Strength - c.StrengthLoss);
            if (unit.Strength == 0)
            {
                context.UnitsToDelete.Add(unit);
                context.Events.Add(new UnitDestroyedEvent(
                    Tick: context.ProcessingTick,
                    UnitId: unit.Id,
                    OwnerPlayerId: unit.OwnerPlayerId,
                    LocationProvinceId: unit.LocationProvinceId,
                    Cause: cause));
            }
        }
    }
}

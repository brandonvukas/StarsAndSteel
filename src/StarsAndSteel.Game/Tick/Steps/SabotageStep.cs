using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Phase 3e: drains pending <see cref="OrderType.Sabotage"/> orders. Each order:
/// <list type="number">
///   <item>Verifies the SF unit still exists and is alive at its launch province.</item>
///   <item>Verifies the target province still has at least one building (the
///   province might have been reconquered or razed between submission and now —
///   in that case the order completes as a fizzle).</item>
///   <item>Picks one random building to destroy via the per-world RNG (deterministic
///   index pick, ordered by <see cref="Building.Id"/> for cross-platform stability).</item>
///   <item>Inflicts <see cref="SfStrengthLoss"/> casualties on the SF unit (clamped at zero;
///   queues for deletion if wiped).</item>
///   <item>Reduces the target's <see cref="Province.MoraleLevel"/> by <see cref="TargetMoraleLoss"/>.</item>
/// </list>
/// Slots after MissileImpactStep and before CombatStep so combat resolves with
/// the post-sabotage building set (a defender's <see cref="BuildingType.HardenedBunker"/>
/// destroyed by SF will not protect the garrison this tick).
/// </summary>
public sealed class SabotageStep : ITickStep
{
    public string Name => "Sabotage";

    /// <summary>Strength casualties the SF stack takes on extraction per attempt.</summary>
    internal const int SfStrengthLoss = 200;

    /// <summary>MoraleLevel subtracted from the target province per successful sabotage.</summary>
    internal const int TargetMoraleLoss = 10;

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var unitsById = context.Units.ToDictionary(u => u.Id);
        var provincesById = context.World.Provinces.ToDictionary(p => p.Id);

        foreach (var order in context.PendingUnitOrders)
        {
            if (order.Status != OrderStatus.Pending) continue;
            if (order.OrderType != OrderType.Sabotage) continue;

            // SF unit must still exist + be alive. If it died this tick (e.g. attrition),
            // cancel the order — no zombie sabotage.
            if (!unitsById.TryGetValue(order.UnitId, out var sf) || sf.Strength <= 0)
            {
                order.Status = OrderStatus.Cancelled;
                continue;
            }

            if (!order.TargetProvinceId.HasValue
                || !provincesById.TryGetValue(order.TargetProvinceId.Value, out var target))
            {
                order.Status = OrderStatus.Cancelled;
                continue;
            }

            // Target province may have been recaptured by the attacker between submission
            // and resolution. Fizzle the order in that case (no friendly-fire sabotage)
            // but still mark Complete so we don't re-process it.
            if (target.OwnerPlayerId is null || target.OwnerPlayerId == sf.OwnerPlayerId)
            {
                order.Status = OrderStatus.Complete;
                continue;
            }

            // Pick a building. Deterministic order by Id ordinal to keep replays stable.
            var buildings = target.Buildings
                .OrderBy(b => b.Id)
                .ToList();
            if (buildings.Count == 0)
            {
                // No buildings left to destroy; fizzle but complete.
                order.Status = OrderStatus.Complete;
                context.Events.Add(new SabotageResolvedEvent(
                    Tick: context.ProcessingTick,
                    OrderId: order.Id,
                    AttackerPlayerId: sf.OwnerPlayerId,
                    SfUnitId: sf.Id,
                    TargetProvinceId: target.Id,
                    TargetPlayerId: target.OwnerPlayerId.Value,
                    DestroyedBuildingId: null,
                    DestroyedBuildingType: null,
                    SfStrengthLoss: 0,
                    TargetMoraleLoss: 0));
                continue;
            }

            var index = context.Rng.NextInt(buildings.Count);
            var victim = buildings[index];
            target.Buildings.Remove(victim);
            context.BuildingsToDelete.Add(victim);

            // Apply SF casualties.
            int sfBefore = sf.Strength;
            sf.Strength = Math.Max(0, sfBefore - SfStrengthLoss);
            int actualLoss = sfBefore - sf.Strength;
            if (sf.Strength == 0)
            {
                context.UnitsToDelete.Add(sf);
            }

            // Province morale hit (clamp 0..100).
            int beforeMorale = target.MoraleLevel;
            target.MoraleLevel = Math.Max(0, beforeMorale - TargetMoraleLoss);

            order.Status = OrderStatus.Complete;
            context.Events.Add(new SabotageResolvedEvent(
                Tick: context.ProcessingTick,
                OrderId: order.Id,
                AttackerPlayerId: sf.OwnerPlayerId,
                SfUnitId: sf.Id,
                TargetProvinceId: target.Id,
                TargetPlayerId: target.OwnerPlayerId.Value,
                DestroyedBuildingId: victim.Id,
                DestroyedBuildingType: victim.Type.ToString(),
                SfStrengthLoss: actualLoss,
                TargetMoraleLoss: beforeMorale - target.MoraleLevel));
        }
    }
}

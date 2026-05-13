using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Phase 3d: drains pending <see cref="CyberAttackOrder"/> rows. For each order:
/// <list type="number">
///   <item>Verify the target province still has an owner (provinces can flip ownership
///   between submission and resolution; if the target is now unowned the attack fizzles
///   without effect but the order still completes).</item>
///   <item>Roll <see cref="CyberEffectKind"/> via the per-world RNG (uniform 0..1).</item>
///   <item>Apply the effect to the target's owner (drain Money or slow research).</item>
///   <item>Stamp the rolled effect on the order, mark Complete, emit
///   <see cref="CyberAttackResolvedEvent"/>.</item>
/// </list>
/// Pipeline placement: AFTER ConstructionStep and ResearchStep so cyber doesn't wipe
/// out research progress that was about to unlock this tick. Sits before MoraleRecovery
/// so a future "cyber tanks morale" effect would land before the bounce-back step.
/// </summary>
public sealed class CyberAttackStep : ITickStep
{
    public string Name => "CyberAttack";

    /// <summary>Money subtracted from the target owner per <see cref="CyberEffectKind.DrainMoney"/> attack.</summary>
    internal const int MoneyDrainPerAttack = 1500;

    /// <summary>ProgressPoints subtracted from one in-progress research row per <see cref="CyberEffectKind.SlowResearch"/> attack.</summary>
    internal const int ResearchPointsPerAttack = 200;

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var playersById = context.World.Players.ToDictionary(p => p.Id);
        var provincesById = context.World.Provinces.ToDictionary(p => p.Id);
        var researchByPlayer = context.ActiveResearch
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var order in context.PendingCyberAttackOrders)
        {
            if (order.Status != OrderStatus.Pending) continue;
            if (!provincesById.TryGetValue(order.TargetProvinceId, out var target))
            {
                order.Status = OrderStatus.Cancelled;
                continue;
            }

            // Target may have been captured / abandoned since submission. Fizzle but
            // still complete the order so the row doesn't get re-processed forever.
            if (target.OwnerPlayerId is null
                || !playersById.TryGetValue(target.OwnerPlayerId.Value, out var targetOwner))
            {
                order.Status = OrderStatus.Complete;
                continue;
            }

            // Roll the effect. Uniform 50/50 between the two MVP variants.
            var roll = context.Rng.NextDouble();
            var effect = roll < 0.5 ? CyberEffectKind.SlowResearch : CyberEffectKind.DrainMoney;
            order.EffectKind = effect;

            int moneyDrained = 0;
            int researchPointsLost = 0;
            string? affectedTechId = null;

            switch (effect)
            {
                case CyberEffectKind.DrainMoney:
                {
                    long before = targetOwner.Money;
                    targetOwner.Money = Math.Max(0L, before - MoneyDrainPerAttack);
                    moneyDrained = (int)(before - targetOwner.Money);
                    break;
                }
                case CyberEffectKind.SlowResearch:
                {
                    if (researchByPlayer.TryGetValue(targetOwner.Id, out var rows) && rows.Count > 0)
                    {
                        // Deterministic pick: the row with the largest current ProgressPoints
                        // (the one closest to unlocking is the juiciest sabotage target). Ties
                        // broken by techId ordinal compare for cross-platform determinism.
                        var victim = rows
                            .OrderByDescending(r => r.ProgressPoints)
                            .ThenBy(r => r.TechId, StringComparer.Ordinal)
                            .First();
                        int beforePts = victim.ProgressPoints;
                        victim.ProgressPoints = Math.Max(0, beforePts - ResearchPointsPerAttack);
                        researchPointsLost = beforePts - victim.ProgressPoints;
                        affectedTechId = victim.TechId;
                    }
                    // If target has no active research, the effect simply does nothing — we
                    // don't reroll to DrainMoney because that would let the attacker game it.
                    break;
                }
            }

            order.Status = OrderStatus.Complete;
            context.Events.Add(new CyberAttackResolvedEvent(
                Tick: context.ProcessingTick,
                CyberAttackOrderId: order.Id,
                AttackerPlayerId: order.AttackerPlayerId,
                TargetProvinceId: order.TargetProvinceId,
                TargetPlayerId: targetOwner.Id,
                EffectKind: effect,
                MoneyDrained: moneyDrained,
                ResearchPointsLost: researchPointsLost,
                AffectedTechId: affectedTechId));
        }
    }
}

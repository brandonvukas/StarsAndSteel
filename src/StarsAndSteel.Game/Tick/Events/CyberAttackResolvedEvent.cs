using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Phase 3d: a CyberAttackOrder was resolved this tick. <see cref="EffectKind"/>
/// is the effect rolled by the per-world RNG; <see cref="MoneyDrained"/> and
/// <see cref="ResearchPointsLost"/> are the actual amounts subtracted (after
/// clamping at zero), so a "no-op" cyber attack still emits this event with
/// both magnitudes equal to zero — useful for news headlines and audits.
/// </summary>
public sealed record CyberAttackResolvedEvent(
    int Tick,
    Guid CyberAttackOrderId,
    Guid AttackerPlayerId,
    Guid TargetProvinceId,
    Guid TargetPlayerId,
    CyberEffectKind EffectKind,
    int MoneyDrained,
    int ResearchPointsLost,
    string? AffectedTechId
) : TickEvent(Tick);

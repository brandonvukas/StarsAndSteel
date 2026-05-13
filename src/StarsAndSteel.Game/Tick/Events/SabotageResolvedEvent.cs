namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Phase 3e: a Special Forces sabotage order was resolved this tick.
/// <see cref="DestroyedBuildingId"/> is null when the order fizzled (target had
/// no buildings at resolution time — they may have been destroyed or the
/// province may have been captured between submission and resolution).
/// <see cref="SfStrengthLoss"/> is the casualty count taken on extraction
/// (always &gt;= 0).
/// </summary>
public sealed record SabotageResolvedEvent(
    int Tick,
    Guid OrderId,
    Guid AttackerPlayerId,
    Guid SfUnitId,
    Guid TargetProvinceId,
    Guid TargetPlayerId,
    Guid? DestroyedBuildingId,
    string? DestroyedBuildingType,
    int SfStrengthLoss,
    int TargetMoraleLoss
) : TickEvent(Tick);

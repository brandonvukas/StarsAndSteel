namespace StarsAndSteel.Game.Tick.Events;

/// <summary>Emitted by MovementStep when a unit successfully relocates.</summary>
public sealed record UnitMovedEvent(
    int Tick,
    Guid UnitId,
    Guid OwnerPlayerId,
    Guid FromProvinceId,
    Guid ToProvinceId
) : TickEvent(Tick);

namespace StarsAndSteel.Game.Tick.Events;

/// <summary>Emitted when a unit stack drops to 0 strength.</summary>
public sealed record UnitDestroyedEvent(
    int Tick,
    Guid UnitId,
    Guid OwnerPlayerId,
    Guid? LocationProvinceId,
    string Cause                    // "Combat" | "AirStrike" | "Attrition"
) : TickEvent(Tick);

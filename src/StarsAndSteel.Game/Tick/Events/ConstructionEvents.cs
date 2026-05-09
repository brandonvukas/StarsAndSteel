using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Tick.Events;

/// <summary>Emitted by ConstructionStep when a unit-build completes.</summary>
public sealed record UnitBuiltEvent(
    int Tick,
    Guid UnitId,
    Guid OwnerPlayerId,
    Guid ProvinceId,
    UnitType Type,
    int Strength
) : TickEvent(Tick);

/// <summary>Emitted by ConstructionStep when a building completes.</summary>
public sealed record BuildingCompletedEvent(
    int Tick,
    Guid BuildingId,
    Guid OwnerPlayerId,
    Guid ProvinceId,
    BuildingType Type,
    int Level
) : TickEvent(Tick);

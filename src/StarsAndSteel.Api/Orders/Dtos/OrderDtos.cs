namespace StarsAndSteel.Api.Orders.Dtos;

/// <summary>Move a ground unit to an adjacent province.</summary>
public sealed record MoveOrderRequest(Guid UnitId, Guid TargetProvinceId);

/// <summary>Ground attack into an adjacent province.</summary>
public sealed record AttackOrderRequest(Guid UnitId, Guid TargetProvinceId);

/// <summary>Air strike from an Air Base–equipped province.</summary>
public sealed record AirStrikeOrderRequest(Guid UnitId, Guid TargetProvinceId);

/// <summary>Build N units of <paramref name="UnitType"/> at <paramref name="ProvinceId"/>.</summary>
public sealed record BuildUnitOrderRequest(Guid ProvinceId, string UnitType, int Quantity);

/// <summary>Build a building of <paramref name="BuildingType"/> at <paramref name="ProvinceId"/>.</summary>
public sealed record BuildBuildingOrderRequest(Guid ProvinceId, string BuildingType);

/// <summary>
/// Returned on accepted unit-scoped orders (Move/Attack/AirStrike). The order is
/// queued for processing at <see cref="IssuedAtTick"/> (= world.CurrentTick + 1
/// at submission time).
/// </summary>
public sealed record UnitOrderAccepted(
    Guid OrderId,
    Guid UnitId,
    string OrderType,
    Guid? TargetProvinceId,
    int IssuedAtTick);

/// <summary>
/// Returned on accepted construction orders. <see cref="TicksRemaining"/> ticks down
/// at each subsequent tick; the unit/building is instantiated when it hits zero.
/// </summary>
public sealed record ConstructionOrderAccepted(
    Guid OrderId,
    Guid ProvinceId,
    string OrderType,
    string? UnitType,
    int? Quantity,
    string? BuildingType,
    int IssuedAtTick,
    int TicksRemaining);

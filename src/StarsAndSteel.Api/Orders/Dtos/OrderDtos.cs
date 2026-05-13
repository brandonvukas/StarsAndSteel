namespace StarsAndSteel.Api.Orders.Dtos;

/// <summary>Move a ground unit to an adjacent province.</summary>
public sealed record MoveOrderRequest(Guid UnitId, Guid TargetProvinceId);

/// <summary>Ground attack into an adjacent province.</summary>
public sealed record AttackOrderRequest(Guid UnitId, Guid TargetProvinceId);

/// <summary>Air strike from an Air Base–equipped province.</summary>
public sealed record AirStrikeOrderRequest(Guid UnitId, Guid TargetProvinceId);

/// <summary>
/// Phase 3a: launch a stockpiled missile at any province (global range). The unit
/// must be a CruiseMissile or NuclearMissile stationed at a friendly Missile Silo;
/// the launch consumes the entire stack.
/// </summary>
public sealed record MissileLaunchOrderRequest(Guid UnitId, Guid TargetProvinceId);

/// <summary>
/// Phase 3d: launch a player-level cyber attack from a friendly province with a
/// CyberOperationsCenter against any other province (global range). Requires
/// the cyber_warfare tech and resolves at the next tick.
/// </summary>
public sealed record CyberAttackOrderRequest(Guid LaunchProvinceId, Guid TargetProvinceId);

/// <summary>
/// Phase 3e: order a Special Forces ground unit to sabotage an adjacent enemy
/// province. Resolves at the next tick: destroys one random enemy building,
/// inflicts province morale damage, and the SF stack takes light casualties.
/// </summary>
public sealed record SabotageOrderRequest(Guid UnitId, Guid TargetProvinceId);

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

/// <summary>
/// Phase 3d: returned on accepted CyberAttack orders. The effect is rolled at
/// resolution time, so this DTO doesn't carry an EffectKind — clients learn
/// the outcome via the SignalR <c>CyberAttackResolved</c> event next tick.
/// </summary>
public sealed record CyberAttackOrderAccepted(
    Guid OrderId,
    Guid LaunchProvinceId,
    Guid TargetProvinceId,
    int IssuedAtTick);

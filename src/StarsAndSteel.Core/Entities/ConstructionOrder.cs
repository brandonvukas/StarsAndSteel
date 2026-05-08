using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// A pending or in-progress build of a <see cref="Unit"/> or <see cref="Building"/> at a
/// <see cref="Province"/>. Stamped server-side with <see cref="IssuedAtTick"/> = the tick
/// it becomes eligible (= world.CurrentTick + 1 at submission), then the
/// <c>ConstructionStep</c> decrements <see cref="TicksRemaining"/> each tick and instantiates
/// the finished entity (Unit or Building) when it hits zero.
/// <para/>
/// Resources are debited from the player at submission time (the cost catalog snapshot is
/// implicit — we don't store it here). Cancelling a pending order can refund partials in a
/// later phase; MVP just deletes the row with no refund.
/// </summary>
public class ConstructionOrder
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid OwnerPlayerId { get; set; }
    public Player OwnerPlayer { get; set; } = default!;

    public Guid ProvinceId { get; set; }
    public Province Province { get; set; } = default!;

    /// <summary>Discriminator. Exactly one of <see cref="UnitType"/> / <see cref="BuildingType"/> is set.</summary>
    public OrderType OrderType { get; set; }

    /// <summary>Set when <see cref="OrderType"/> is <see cref="OrderType.BuildUnit"/>.</summary>
    public UnitType? UnitType { get; set; }

    /// <summary>Stack size for unit builds (e.g. 1000 mech infantry). Always 1 for buildings.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Set when <see cref="OrderType"/> is <see cref="OrderType.BuildBuilding"/>.</summary>
    public BuildingType? BuildingType { get; set; }

    /// <summary>The tick at which this order becomes eligible for processing.</summary>
    public int IssuedAtTick { get; set; }

    /// <summary>
    /// Decremented each tick by <c>ConstructionStep</c>. When it hits zero the unit / building
    /// is instantiated and the order row is marked <see cref="OrderStatus.Complete"/>.
    /// </summary>
    public int TicksRemaining { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
}

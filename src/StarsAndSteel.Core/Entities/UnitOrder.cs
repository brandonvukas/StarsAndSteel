using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// A pending or in-flight order against a <see cref="Unit"/>.
/// <para/>
/// <see cref="IssuedAtTick"/> is stamped server-side at submission time as
/// <c>world.CurrentTick + 1</c> under the per-world lock, guaranteeing that orders submitted
/// while a tick is processing land in the *next* tick. See <c>docs/07-GAME-LOOP.md</c>
/// for the cutoff rules.
/// </summary>
public class UnitOrder
{
    public Guid Id { get; set; }

    public Guid UnitId { get; set; }
    public Unit Unit { get; set; } = default!;

    public OrderType OrderType { get; set; }

    public Guid? TargetProvinceId { get; set; }
    public Province? TargetProvince { get; set; }

    /// <summary>The tick at which this order becomes eligible for processing.</summary>
    public int IssuedAtTick { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
}

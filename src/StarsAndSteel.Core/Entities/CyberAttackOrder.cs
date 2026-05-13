using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// Phase 3d: a pending player-to-province cyber attack. Unlike <see cref="UnitOrder"/>
/// this has no associated <see cref="Unit"/> — the attack is a player capability gated
/// by an unlocked <c>cyber_warfare</c> tech and a <see cref="BuildingType.CyberOperationsCenter"/>
/// at the launch province. Resolved by <c>CyberAttackStep</c> at <see cref="IssuedAtTick"/>.
/// <para/>
/// <see cref="EffectKind"/> is chosen deterministically by the per-world RNG inside the
/// resolution step, NOT at submission time. The field is nullable for "not yet rolled"
/// and gets stamped to a concrete value when the step processes it (the row is then
/// marked Complete; the value is preserved for replay/news).
/// </summary>
public class CyberAttackOrder
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid AttackerPlayerId { get; set; }
    public Player AttackerPlayer { get; set; } = default!;

    /// <summary>Province with the friendly CyberOperationsCenter that "hosts" the attack.</summary>
    public Guid LaunchProvinceId { get; set; }
    public Province LaunchProvince { get; set; } = default!;

    public Guid TargetProvinceId { get; set; }
    public Province TargetProvince { get; set; } = default!;

    /// <summary>
    /// The effect rolled at resolution time. Null while Pending; stamped by
    /// <c>CyberAttackStep</c> on resolution.
    /// </summary>
    public CyberEffectKind? EffectKind { get; set; }

    /// <summary>The tick at which this order becomes eligible for processing.</summary>
    public int IssuedAtTick { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
}

using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// A stack of military units. One row = "12,000 mech infantry in Detroit owned by USA",
/// not a single soldier. <see cref="Strength"/> 0 means destroyed (cleaned up by tick processor).
/// </summary>
public class Unit
{
    public Guid Id { get; set; }

    public Guid GameWorldId { get; set; }
    public GameWorld GameWorld { get; set; } = default!;

    public Guid OwnerPlayerId { get; set; }
    public Player OwnerPlayer { get; set; } = default!;

    /// <summary>Null while <see cref="IsInTransit"/> is true.</summary>
    public Guid? LocationProvinceId { get; set; }
    public Province? LocationProvince { get; set; }

    public UnitType Type { get; set; }

    /// <summary>
    /// Cached domain derived from <see cref="Type"/>. Stored on the row so we can index it
    /// for filters like "all enemy aircraft this tick" without a CASE expression.
    /// </summary>
    public UnitDomain Domain { get; set; }

    public int Strength { get; set; }
    public int Morale { get; set; } = 100;
    public int Experience { get; set; }

    public bool IsInTransit { get; set; }
    public Guid? TransitFromProvinceId { get; set; }
    public Province? TransitFromProvince { get; set; }
    public Guid? TransitToProvinceId { get; set; }
    public Province? TransitToProvince { get; set; }

    /// <summary>The tick on which an in-transit unit will arrive at its destination.</summary>
    public int? TransitArrivalTick { get; set; }

    /// <summary>
    /// Used by air units for range calculations. A combat drone can only strike provinces
    /// within range of its home base.
    /// </summary>
    public Guid? HomeBaseProvinceId { get; set; }
    public Province? HomeBaseProvince { get; set; }

    /// <summary>
    /// Phase 2b: optional parent-unit FK. A <see cref="UnitType.CarrierAirWing"/> stack
    /// is parented to its host <see cref="UnitType.AircraftCarrier"/> via this field.
    /// When the carrier moves, its embarked wings move with it (MovementStep). When the
    /// carrier is destroyed, all wings parented to it are destroyed too. The carrier
    /// also acts as the wing's "AirBase equivalent" for AirStrike eligibility.
    /// <para/>
    /// Self-FK on Units. Null for everything else.
    /// </summary>
    public Guid? ParentUnitId { get; set; }
    public Unit? ParentUnit { get; set; }

    public ICollection<UnitOrder> Orders { get; set; } = new List<UnitOrder>();
}

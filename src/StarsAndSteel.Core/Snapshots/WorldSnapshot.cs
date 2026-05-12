namespace StarsAndSteel.Core.Snapshots;

/// <summary>
/// Top-level world snapshot for the calling player. Shape mirrors
/// <c>docs/06-BACKEND-API.md</c> §"DTOs (shape sketch)". Filtered by fog of war:
/// what the player can see, the player gets; everything else is masked or omitted.
/// <para/>
/// Lives in Core (not Api) so <see cref="StarsAndSteel.Game"/>'s <c>SnapshotService</c>
/// can produce these records directly without a Game→Api dependency. The controller
/// returns them as-is — they're pure data, no MVC concerns.
/// </summary>
public sealed record WorldSnapshot(
    Guid WorldId,
    string Name,
    string Status,
    int CurrentTick,
    int TickIntervalSeconds,
    DateTime? NextTickDueUtc,
    SnapshotMe Me,
    IReadOnlyList<SnapshotPlayerSummary> Players,
    IReadOnlyList<SnapshotProvince> Provinces,
    IReadOnlyList<SnapshotMyUnit> MyUnits,
    IReadOnlyList<SnapshotEnemyUnit> VisibleEnemyUnits);

/// <summary>The calling player's own row, full detail.</summary>
public sealed record SnapshotMe(
    Guid PlayerId,
    string NationName,
    string FlagPrimaryHex,
    string FlagSecondaryHex,
    SnapshotResources Resources,
    bool IsAlive);

/// <summary>Resource bag — same fields as <c>Player</c>'s denormalized columns.</summary>
public sealed record SnapshotResources(
    long Money,
    long Oil,
    long Steel,
    long Electronics,
    long Food,
    long Manpower);

/// <summary>
/// Public-facing summary of every player in the world. Resources are NOT included
/// (those leak strategic info to opponents). Use the <see cref="SnapshotMe"/>
/// field for the caller's own resources.
/// </summary>
public sealed record SnapshotPlayerSummary(
    Guid PlayerId,
    string NationName,
    string FlagPrimaryHex,
    string FlagSecondaryHex,
    bool IsAi,
    bool IsAlive,
    int OwnedProvinceCount);

/// <summary>
/// One province row. Non-visible provinces still appear (so the client can render
/// the polygon and ownership color for any province the player has previously
/// seen) but their <see cref="MoraleLevel"/>, building list, and garrison
/// strength are nulled / emptied — those leak intel.
/// <para/>
/// MVP visibility rule: a province is <see cref="Visible"/> if the calling player
/// owns it OR is adjacent to one they own. <see cref="ProvinceAdjacency"/> is
/// undirected so adjacency lookups must check BOTH sides.
/// </summary>
public sealed record SnapshotProvince(
    Guid Id,
    string Name,
    string Type,
    bool IsCoastal,
    float CenterX,
    float CenterY,
    Guid? OwnerPlayerId,
    string? OwnerColorHex,
    bool Visible,
    int? MoraleLevel,
    int? GarrisonStrength,
    IReadOnlyList<SnapshotBuilding> Buildings,
    IReadOnlyList<Guid> AdjacentProvinceIds);

/// <summary>
/// Building on a visible province. Empty list when the province is not visible
/// (parent <see cref="SnapshotProvince.Visible"/> = false).
/// </summary>
public sealed record SnapshotBuilding(
    Guid Id,
    string Type,
    int Level);

/// <summary>The calling player's own unit, full detail.</summary>
public sealed record SnapshotMyUnit(
    Guid Id,
    string Type,
    string Domain,
    int Strength,
    int Morale,
    int Experience,
    Guid? LocationProvinceId,
    bool IsInTransit,
    Guid? TransitFromProvinceId,
    Guid? TransitToProvinceId,
    int? TransitArrivalTick,
    /// <summary>
    /// Phase 2b: parent unit id when this stack is embarked on a carrier.
    /// Null for everything that isn't a CarrierAirWing. The client uses this
    /// to render the carrier-composition view in the province panel.
    /// </summary>
    Guid? ParentUnitId);

/// <summary>
/// Enemy unit visible to the caller — only stationed (non-transit) units in
/// visible provinces are surfaced. Morale and experience are masked.
/// </summary>
public sealed record SnapshotEnemyUnit(
    Guid Id,
    Guid OwnerPlayerId,
    string Type,
    string Domain,
    int Strength,
    Guid LocationProvinceId);

namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Operating environment. Computed from <see cref="UnitType"/> at construction time and
/// stored on the row for cheap "all enemy aircraft" filters.
/// </summary>
public enum UnitDomain
{
    Ground = 0,
    Air = 1,
    Naval = 2,
    /// <summary>Phase 3a: cruise / ballistic / nuclear missiles. Cannot move in the
    /// conventional sense; consumed by <c>OrderType.MissileLaunch</c>.</summary>
    Missile = 3,
}

namespace StarsAndSteel.Core.Enums;

public enum OrderType
{
    Move = 0,
    Attack = 1,
    Hold = 2,
    Patrol = 3,
    AirStrike = 4,
    ReconSweep = 5,
    BuildUnit = 6,
    BuildBuilding = 7,
    /// <summary>
    /// Phase 3a: a CruiseMissile or NuclearMissile unit, stationed at a friendly
    /// MissileSilo, fires at any province (global range). The launching unit-stack
    /// is consumed (one missile per launch) and resolved by MissileImpactStep.
    /// </summary>
    MissileLaunch = 8
}

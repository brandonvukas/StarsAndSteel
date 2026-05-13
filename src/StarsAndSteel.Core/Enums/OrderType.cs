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
    MissileLaunch = 8,
    /// <summary>
    /// Phase 3d: a player launches a cyber attack from a province with a
    /// CyberOperationsCenter against any other province (global range). No unit
    /// involved; resolved by CyberAttackStep at next tick. Tech-gated behind
    /// "cyber_warfare". One of two random effects (slow research / drain money)
    /// is applied to the target province's owner.
    /// </summary>
    CyberAttack = 9,
    /// <summary>
    /// Phase 3e: a SpecialForces ground unit, stationed at a friendly province
    /// adjacent to an enemy-owned province, conducts a covert sabotage raid.
    /// Resolved by SabotageStep at next tick: destroys one random enemy
    /// building at the target, inflicts province morale damage, and the SF
    /// stack takes light casualties on extraction. The SF unit does NOT
    /// move; it stays in its launch province.
    /// </summary>
    Sabotage = 10
}

namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Concrete unit type. The full catalogue per <c>docs/03-DATABASE-SCHEMA.md</c>; MVP uses a subset
/// (MechInfantry, MainBattleTank, AABattery, CombatDrone, MultiroleFighter).
/// </summary>
public enum UnitType
{
    // Ground
    MechInfantry = 0,
    NationalGuard = 1,
    SpecialForces = 2,
    MainBattleTank = 3,
    MobileArtillery = 4,
    AABattery = 5,

    // Air
    ReconDrone = 100,
    CombatDrone = 101,
    AttackHelicopter = 102,
    MultiroleFighter = 103,
    StrategicBomber = 104,
    StealthBomber = 105,

    // Naval (Phase 2I MVP-lite). Frigate = cheap escort, Destroyer = heavier multi-role.
    // Both require a NavalYard at a coastal province; movement traverses sea-crossing
    // edges between coastal land provinces (no true ocean tiles in MVP).
    Frigate = 200,
    Destroyer = 201
}

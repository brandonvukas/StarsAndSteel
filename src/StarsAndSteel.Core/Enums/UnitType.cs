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
    // Phase 3b: stealth recon — small, fast, hard to spot. Tech-gated behind
    // "stealth_drones". Treated as Air domain like other drones; combat steps
    // give it a detection-evasion bonus (see CombatStats).
    StealthDrone = 107,

    // Naval (Phase 2I MVP-lite). Frigate = cheap escort, Destroyer = heavier multi-role.
    // Both require a NavalYard at a coastal province; movement traverses sea-crossing
    // edges between coastal land provinces (no true ocean tiles in MVP).
    Frigate = 200,
    Destroyer = 201,
    // Phase 3c: Submarine — stealth naval unit. Built at NavalYard like other ships
    // but hidden from enemy snapshots unless an enemy Frigate/Destroyer is co-located
    // (sonar detection). Strong vs surface ships, weak vs ASW.
    Submarine = 203,

    // Naval Aviation (Phase 2b). AircraftCarrier is a heavy capital ship built at a
    // NavalYard; it CARRIES air units. CarrierAirWing is a special air unit that
    // is built only when the building province hosts a Carrier owned by the caller,
    // and is parented to that carrier via Unit.ParentUnitId. Wings move with their
    // parent carrier and can sortie (AirStrike) from wherever the carrier is, even
    // without an AirBase building. Sinking the carrier kills its wings.
    AircraftCarrier = 202,
    CarrierAirWing = 106,

    // Strategic Missiles (Phase 3a). Built and stationed at a MissileSilo; launched via
    // OrderType.MissileLaunch with global range (no adjacency check). Each launch consumes
    // one stack-strength of missile. CruiseMissile = conventional warhead (kills units +
    // damages buildings). NuclearMissile = also applies permanent RadiationLevel to the
    // target province; gated behind GameWorld.NukesEnabled. Domain is Air for routing
    // through validation but they don't use AirBases or carriers; the silo is their host.
    CruiseMissile = 300,
    NuclearMissile = 301,
}

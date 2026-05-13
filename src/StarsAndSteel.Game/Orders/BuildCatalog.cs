using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Orders;

/// <summary>
/// Static cost / build-time catalogue for units and buildings. Sourced from
/// <c>docs/04-GAME-MECHANICS.md</c>. Costs are <i>per stack of 1000</i> for ground/air
/// units (matching how <see cref="StarsAndSteel.Game.Worlds.StarterPackage"/> seeds
/// stacks); a <see cref="UnitBuildSpec.CostPerThousand"/> + a Quantity of 2000 means
/// pay 2x the row's costs.
/// <para/>
/// Building costs aren't tabled in docs/04 (only their per-level production multipliers
/// are). MVP picks pragmatic values on the same scale as the unit costs and the starter
/// resource pool (5000/1000/1000/500/1000/2000) so a player can afford a second building
/// within a few ticks of resource accumulation. Numbers are subject to balance passes.
/// </summary>
public static class BuildCatalog
{
    /// <summary>Cost + build-time for one unit type. Costs are per 1000 strength.</summary>
    /// <param name="RequiresCarrier">
    /// Phase 2b: if true, the building province must also host a friendly
    /// <see cref="UnitType.AircraftCarrier"/> with spare capacity, and the resulting
    /// unit is parented to that carrier rather than living on the province.
    /// <see cref="RequiredBuilding"/> is still enforced (NavalYard for wings) so we
    /// don't have to special-case unrelated land provinces.
    /// </param>
    /// <param name="RequiredTechId">
    /// Phase 3b: optional tech catalogue key (see <see cref="StarsAndSteel.Game.Research.TechCatalog"/>).
    /// When set, the caller must have an unlocked <see cref="StarsAndSteel.Core.Entities.ResearchProgress"/>
    /// row for this tech or the order is rejected with <c>RequiredTechMissing</c>.
    /// Null means "always available" (every MVP unit before Phase 3b).
    /// </param>
    public sealed record UnitBuildSpec(
        UnitType Type,
        UnitDomain Domain,
        long Money,
        long Oil,
        long Steel,
        long Electronics,
        long Food,
        long Manpower,
        int TicksToBuild,
        BuildingType RequiredBuilding,
        bool RequiresCarrier = false,
        string? RequiredTechId = null);

    /// <summary>Cost + build-time for one building (level 1; higher levels deferred to Phase 2).</summary>
    public sealed record BuildingBuildSpec(
        BuildingType Type,
        long Money,
        long Oil,
        long Steel,
        long Electronics,
        long Food,
        long Manpower,
        int TicksToBuild);

    // Per docs/04 table. RequiredBuilding follows §"How production works": ground units
    // need a Military Base (or RecruitmentCenter for infantry classes), air needs Air Base.
    // For MVP we accept either RC or MB as enabling for ground units — checked in OrderService.
    private static readonly IReadOnlyDictionary<UnitType, UnitBuildSpec> Units = new[]
    {
        new UnitBuildSpec(UnitType.MechInfantry,     UnitDomain.Ground,  Money: 200,  Oil: 0,   Steel: 100, Electronics: 0,    Food: 0, Manpower: 100, TicksToBuild: 5,  RequiredBuilding: BuildingType.RecruitmentCenter),
        new UnitBuildSpec(UnitType.NationalGuard,    UnitDomain.Ground,  Money: 100,  Oil: 0,   Steel: 50,  Electronics: 0,    Food: 0, Manpower: 100, TicksToBuild: 4,  RequiredBuilding: BuildingType.RecruitmentCenter),
        new UnitBuildSpec(UnitType.SpecialForces,    UnitDomain.Ground,  Money: 500,  Oil: 0,   Steel: 50,  Electronics: 50,   Food: 0, Manpower: 50,  TicksToBuild: 10, RequiredBuilding: BuildingType.MilitaryBase),
        new UnitBuildSpec(UnitType.MainBattleTank,   UnitDomain.Ground,  Money: 600,  Oil: 100, Steel: 400, Electronics: 0,    Food: 0, Manpower: 50,  TicksToBuild: 12, RequiredBuilding: BuildingType.MilitaryBase),
        new UnitBuildSpec(UnitType.MobileArtillery,  UnitDomain.Ground,  Money: 500,  Oil: 50,  Steel: 250, Electronics: 0,    Food: 0, Manpower: 50,  TicksToBuild: 10, RequiredBuilding: BuildingType.MilitaryBase),
        new UnitBuildSpec(UnitType.AABattery,        UnitDomain.Ground,  Money: 400,  Oil: 0,   Steel: 200, Electronics: 100,  Food: 0, Manpower: 0,   TicksToBuild: 8,  RequiredBuilding: BuildingType.MilitaryBase),

        new UnitBuildSpec(UnitType.ReconDrone,       UnitDomain.Air,     Money: 200,  Oil: 50,  Steel: 0,   Electronics: 100,  Food: 0, Manpower: 0,   TicksToBuild: 4,  RequiredBuilding: BuildingType.AirBase),
        new UnitBuildSpec(UnitType.CombatDrone,      UnitDomain.Air,     Money: 400,  Oil: 100, Steel: 0,   Electronics: 200,  Food: 0, Manpower: 0,   TicksToBuild: 8,  RequiredBuilding: BuildingType.AirBase),
        new UnitBuildSpec(UnitType.AttackHelicopter, UnitDomain.Air,     Money: 700,  Oil: 200, Steel: 200, Electronics: 200,  Food: 0, Manpower: 0,   TicksToBuild: 10, RequiredBuilding: BuildingType.AirBase),
        new UnitBuildSpec(UnitType.MultiroleFighter, UnitDomain.Air,     Money: 1200, Oil: 300, Steel: 500, Electronics: 400,  Food: 0, Manpower: 0,   TicksToBuild: 14, RequiredBuilding: BuildingType.AirBase),
        new UnitBuildSpec(UnitType.StrategicBomber,  UnitDomain.Air,     Money: 2000, Oil: 500, Steel: 800, Electronics: 400,  Food: 0, Manpower: 0,   TicksToBuild: 18, RequiredBuilding: BuildingType.AirBase),
        // Phase 3b: research-gated. Stealth Bomber needs "stealth_systems" unlocked;
        // Stealth Drone needs "stealth_drones" (added in TechCatalog Phase 3b).
        new UnitBuildSpec(UnitType.StealthBomber,    UnitDomain.Air,     Money: 3500, Oil: 500, Steel: 800, Electronics: 1200, Food: 0, Manpower: 0,   TicksToBuild: 24, RequiredBuilding: BuildingType.AirBase, RequiredTechId: "stealth_systems"),
        new UnitBuildSpec(UnitType.StealthDrone,     UnitDomain.Air,     Money: 1200, Oil: 100, Steel: 0,   Electronics: 600,  Food: 0, Manpower: 0,   TicksToBuild: 10, RequiredBuilding: BuildingType.AirBase, RequiredTechId: "stealth_drones"),

        // Naval (Phase 2I). Both gated to NavalYard, which is itself gated to coastal
        // provinces in OrderService.ValidateBuildBuilding.
        new UnitBuildSpec(UnitType.Frigate,          UnitDomain.Naval,   Money: 800,  Oil: 150, Steel: 600, Electronics: 100,  Food: 0, Manpower: 50,  TicksToBuild: 12, RequiredBuilding: BuildingType.NavalYard),
        new UnitBuildSpec(UnitType.Destroyer,        UnitDomain.Naval,   Money: 1500, Oil: 300, Steel: 1000,Electronics: 250,  Food: 0, Manpower: 80,  TicksToBuild: 16, RequiredBuilding: BuildingType.NavalYard),
        // Phase 3c: Submarine. Tech-gated behind "submarine_warfare". Heavy on
        // electronics (sonar/comms suite); pricey because stealth is a force multiplier.
        new UnitBuildSpec(UnitType.Submarine,        UnitDomain.Naval,   Money: 2200, Oil: 250, Steel: 1200,Electronics: 600,  Food: 0, Manpower: 60,  TicksToBuild: 20, RequiredBuilding: BuildingType.NavalYard, RequiredTechId: "submarine_warfare"),

        // Naval Aviation (Phase 2b). The carrier itself is a heavy expensive ship that
        // ferries CarrierAirWings. Wings have RequiresCarrier=true so the order service
        // looks for a friendly carrier with spare capacity in the building province.
        new UnitBuildSpec(UnitType.AircraftCarrier,  UnitDomain.Naval,   Money: 6000, Oil: 800, Steel: 3500,Electronics: 800,  Food: 0, Manpower: 200, TicksToBuild: 30, RequiredBuilding: BuildingType.NavalYard),
        new UnitBuildSpec(UnitType.CarrierAirWing,   UnitDomain.Air,     Money: 1500, Oil: 300, Steel: 400, Electronics: 400,  Food: 0, Manpower: 50,  TicksToBuild: 12, RequiredBuilding: BuildingType.NavalYard, RequiresCarrier: true),

        // Strategic missiles (Phase 3a). Both gated to MissileSilo. The unit row
        // represents a stockpile (one stack-of-1 = one missile); MissileLaunch
        // consumes the entire stack on fire (single-shot semantics).
        // Nuclear is dramatically more expensive and slower.
        new UnitBuildSpec(UnitType.CruiseMissile,    UnitDomain.Missile, Money: 1500, Oil: 200, Steel: 400, Electronics: 400,  Food: 0, Manpower: 0,   TicksToBuild: 10, RequiredBuilding: BuildingType.MissileSilo),
        new UnitBuildSpec(UnitType.NuclearMissile,   UnitDomain.Missile, Money: 8000, Oil: 500, Steel: 1500,Electronics: 2000, Food: 0, Manpower: 0,   TicksToBuild: 30, RequiredBuilding: BuildingType.MissileSilo),
    }.ToDictionary(s => s.Type);

    // Building costs aren't in docs/04. MVP values are scoped so the level-1 starter pool
    // (5000 money etc.) plus a few ticks of capital production buys a second building.
    // All MVP buildings (RC, MB, AB, SteelMill, Refinery, FinancialDistrict) are listed.
    private static readonly IReadOnlyDictionary<BuildingType, BuildingBuildSpec> Buildings = new[]
    {
        new BuildingBuildSpec(BuildingType.RecruitmentCenter, Money: 1000, Oil: 0,   Steel: 200, Electronics: 0,   Food: 0, Manpower: 100, TicksToBuild: 10),
        new BuildingBuildSpec(BuildingType.MilitaryBase,      Money: 2000, Oil: 100, Steel: 500, Electronics: 100, Food: 0, Manpower: 100, TicksToBuild: 15),
        new BuildingBuildSpec(BuildingType.AirBase,           Money: 2500, Oil: 200, Steel: 400, Electronics: 200, Food: 0, Manpower: 50,  TicksToBuild: 18),
        new BuildingBuildSpec(BuildingType.SteelMill,         Money: 1500, Oil: 50,  Steel: 100, Electronics: 0,   Food: 0, Manpower: 100, TicksToBuild: 12),
        new BuildingBuildSpec(BuildingType.Refinery,          Money: 1500, Oil: 0,   Steel: 200, Electronics: 50,  Food: 0, Manpower: 50,  TicksToBuild: 12),
        new BuildingBuildSpec(BuildingType.FinancialDistrict, Money: 2000, Oil: 0,   Steel: 100, Electronics: 100, Food: 0, Manpower: 50,  TicksToBuild: 14),
        // Naval Yard (Phase 2I). Coastal-only, enforced in OrderService.ValidateBuildBuilding.
        new BuildingBuildSpec(BuildingType.NavalYard,         Money: 3000, Oil: 100, Steel: 800, Electronics: 200, Food: 0, Manpower: 100, TicksToBuild: 20),
        // Missile Silo (Phase 3a). Land-based; gates CruiseMissile + NuclearMissile builds
        // and is the launch host for OrderType.MissileLaunch.
        new BuildingBuildSpec(BuildingType.MissileSilo,       Money: 4000, Oil: 100, Steel: 1500,Electronics: 500, Food: 0, Manpower: 100, TicksToBuild: 25),
    }.ToDictionary(s => s.Type);

    /// <summary>True if this unit type is buildable in MVP (i.e. has a spec).</summary>
    public static bool IsUnitBuildable(UnitType type) => Units.ContainsKey(type);

    /// <summary>
    /// Phase 2b: Maximum number of <see cref="UnitType.CarrierAirWing"/> stacks that may
    /// be parented to a single <see cref="UnitType.AircraftCarrier"/>. Counts in-flight
    /// build orders too so spam-queueing wings doesn't bypass the cap.
    /// </summary>
    public const int CarrierWingCapacity = 4;

    /// <summary>True if this building type is buildable in MVP.</summary>
    public static bool IsBuildingBuildable(BuildingType type) => Buildings.ContainsKey(type);

    /// <summary>Lookup unit spec; throws if the type isn't in the MVP catalogue.</summary>
    public static UnitBuildSpec GetUnit(UnitType type) =>
        Units.TryGetValue(type, out var spec)
            ? spec
            : throw new ArgumentException($"Unit type {type} is not in the MVP build catalogue.", nameof(type));

    /// <summary>Lookup building spec; throws if the type isn't in the MVP catalogue.</summary>
    public static BuildingBuildSpec GetBuilding(BuildingType type) =>
        Buildings.TryGetValue(type, out var spec)
            ? spec
            : throw new ArgumentException($"Building type {type} is not in the MVP build catalogue.", nameof(type));
}

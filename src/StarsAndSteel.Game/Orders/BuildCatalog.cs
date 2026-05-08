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
        BuildingType RequiredBuilding);

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
        new UnitBuildSpec(UnitType.StealthBomber,    UnitDomain.Air,     Money: 3500, Oil: 500, Steel: 800, Electronics: 1200, Food: 0, Manpower: 0,   TicksToBuild: 24, RequiredBuilding: BuildingType.AirBase),
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
    }.ToDictionary(s => s.Type);

    /// <summary>True if this unit type is buildable in MVP (i.e. has a spec).</summary>
    public static bool IsUnitBuildable(UnitType type) => Units.ContainsKey(type);

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

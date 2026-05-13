namespace StarsAndSteel.Core.Enums;

/// <summary>
/// Building catalogue. MVP ships: RecruitmentCenter, MilitaryBase, AirBase, SteelMill,
/// Refinery, FinancialDistrict. NavalYard, TechPark, AgriculturalSector, LogisticsHub,
/// HardenedBunker, MissileSilo, CyberOperationsCenter land in later phases.
/// <para/>
/// Phase 4b1: wonders are modeled as building rows (see <see cref="WonderCatalog"/>) so
/// they reuse the existing build-building order pipeline. Effects are applied in dedicated
/// tick steps that scan owned provinces for wonder buildings each tick.
/// </summary>
public enum BuildingType
{
    RecruitmentCenter = 0,
    MilitaryBase = 1,
    AirBase = 2,
    NavalYard = 3,
    SteelMill = 4,
    Refinery = 5,
    TechPark = 6,
    AgriculturalSector = 7,
    FinancialDistrict = 8,
    LogisticsHub = 9,
    HardenedBunker = 10,
    MissileSilo = 11,
    CyberOperationsCenter = 12,

    // ---- Wonders (Phase 4b1) ----
    // Numbered from 100 to leave headroom for non-wonder buildings without renumbering.
    // WonderCatalog.IsWonder() is the source-of-truth predicate; the value range is just
    // a convention to keep the enum readable.
    /// <summary>Permanent +50% production for all owner-controlled provinces.</summary>
    HooverDamReborn = 100,
    /// <summary>Owner's provinces have a 50% chance to intercept each incoming missile.</summary>
    StrategicDefenseInitiative = 101,
    /// <summary>Permanent global recon: owner sees every province + every enemy unit, ignoring fog of war and submarine stealth.</summary>
    GpsConstellation = 102,
    /// <summary>On completion, spawns a free veteran Aircraft Carrier + 2 Carrier Air Wings at the wonder's (coastal) province.</summary>
    CarrierStrikeGroup = 103,
    /// <summary>Owner's CyberAttack orders cost 50% less money + electronics. The HQ itself counts as a CyberOperationsCenter for the launch-province requirement.</summary>
    CyberCommandHq = 104,
}

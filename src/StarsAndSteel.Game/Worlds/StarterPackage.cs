namespace StarsAndSteel.Game.Worlds;

/// <summary>
/// Per-player starter package values from <c>docs/03-DATABASE-SCHEMA.md</c>
/// §"Nation starting state". MVP is symmetric: every nation starts with these
/// regardless of which capital they're assigned. Balance comes from province
/// placement, not asymmetric starts.
/// <para/>
/// These constants are deliberately public so unit tests can reference them
/// instead of hard-coding magic numbers.
/// </summary>
public static class StarterPackage
{
    // Resources
    public const long StartingMoney = 5_000;
    public const long StartingOil = 1_000;
    public const long StartingSteel = 1_000;
    public const long StartingElectronics = 500;
    public const long StartingFood = 1_000;
    public const long StartingManpower = 2_000;

    // Starter units stationed on the capital
    public const int MechInfantryStrength = 1_000;
    public const int MechInfantryStackCount = 2;
    public const int AaBatteryStrength = 500;
    public const int AaBatteryStackCount = 1;

    // Capital starts with these buildings at level 1
    // (RecruitmentCenter, MilitaryBase, AirBase, FinancialDistrict)
    public const int StartingBuildingLevel = 1;
}

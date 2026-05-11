using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Orders;

/// <summary>
/// Per-tick upkeep cost for one stack of 1000 strength. Drained by
/// <see cref="Tick.Steps.LogisticsUpkeepStep"/> from the owning player's pool;
/// when the pool is insufficient the unit takes a morale hit instead.
/// <para/>
/// Numbers are pragmatic MVP values scoped against the starter pool
/// (5000/1000/1000/500/1000/2000) so a player can field a small army indefinitely
/// off province income but a large one will burn through stockpiles. Subject to
/// balance passes once the live-tick metrics come in.
/// </summary>
public static class UpkeepCatalog
{
    public sealed record UnitUpkeepSpec(
        UnitType Type,
        long Money,
        long Oil,
        long Food,
        long Manpower);

    // Ground units consume food + manpower (replacement losses); air units consume
    // money + oil (fuel + maintenance contracts). Per-1000 strength, per-tick.
    private static readonly IReadOnlyDictionary<UnitType, UnitUpkeepSpec> Upkeep = new[]
    {
        new UnitUpkeepSpec(UnitType.MechInfantry,     Money: 5,  Oil: 1, Food: 5, Manpower: 1),
        new UnitUpkeepSpec(UnitType.NationalGuard,    Money: 2,  Oil: 0, Food: 5, Manpower: 1),
        new UnitUpkeepSpec(UnitType.SpecialForces,    Money: 10, Oil: 1, Food: 3, Manpower: 1),
        new UnitUpkeepSpec(UnitType.MainBattleTank,   Money: 8,  Oil: 5, Food: 2, Manpower: 1),
        new UnitUpkeepSpec(UnitType.MobileArtillery,  Money: 6,  Oil: 3, Food: 2, Manpower: 1),
        new UnitUpkeepSpec(UnitType.AABattery,        Money: 5,  Oil: 1, Food: 1, Manpower: 0),

        new UnitUpkeepSpec(UnitType.ReconDrone,       Money: 3,  Oil: 2, Food: 0, Manpower: 0),
        new UnitUpkeepSpec(UnitType.CombatDrone,      Money: 5,  Oil: 4, Food: 0, Manpower: 0),
        new UnitUpkeepSpec(UnitType.AttackHelicopter, Money: 10, Oil: 6, Food: 0, Manpower: 0),
        new UnitUpkeepSpec(UnitType.MultiroleFighter, Money: 15, Oil: 8, Food: 0, Manpower: 0),
        new UnitUpkeepSpec(UnitType.StrategicBomber,  Money: 25, Oil: 15, Food: 0, Manpower: 0),
        new UnitUpkeepSpec(UnitType.StealthBomber,    Money: 40, Oil: 20, Food: 0, Manpower: 0),

        // Naval (Phase 2I). Money + oil heavy (fuel + crew + maintenance).
        new UnitUpkeepSpec(UnitType.Frigate,          Money: 12, Oil: 8,  Food: 1, Manpower: 0),
        new UnitUpkeepSpec(UnitType.Destroyer,        Money: 20, Oil: 14, Food: 1, Manpower: 0),
    }.ToDictionary(s => s.Type);

    /// <summary>Lookup upkeep spec; returns zero-cost if the type is not catalogued.</summary>
    public static UnitUpkeepSpec Get(UnitType type) =>
        Upkeep.TryGetValue(type, out var spec)
            ? spec
            : new UnitUpkeepSpec(type, 0, 0, 0, 0);
}

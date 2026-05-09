using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Worlds;

/// <summary>
/// Shared, pure helper that seats a new <see cref="Player"/> in a <see cref="GameWorld"/>:
/// picks a starting province, applies the <see cref="StarterPackage"/> resources, plants
/// the four starter buildings, and stations the starter unit stacks.
/// <para/>
/// Used by both <see cref="WorldJoinService"/> (human joins) and <see cref="WorldFactory"/>
/// (AI auto-spawn at world creation). Does not flip the world status — that's the human-join
/// path's job.
/// <para/>
/// Province preference: a free province with <see cref="ProvinceType.Capital"/> if any exist,
/// otherwise any free province. Stub maps with only one capital still need to seat an AI
/// alongside the human; a real USA-scale map will have plenty of capital candidates and the
/// fallback is rarely hit. Returns <c>null</c> if no free provinces remain at all.
/// </summary>
internal static class PlayerSpawner
{
    /// <summary>
    /// Seat <paramref name="player"/> in <paramref name="world"/>. The player must already
    /// have its identity fields (UserId/IsAi/AiPersonality/NationName/Flag*) populated; this
    /// helper writes the resource columns, picks a province, and seeds buildings + units.
    /// Returns the assigned starting province, or <c>null</c> if none was available.
    /// </summary>
    public static Province? Spawn(GameWorld world, Player player)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);

        var province = PickStartingProvince(world);
        if (province is null) return null;

        // Apply starter resources. Caller may already have set them; we overwrite to keep
        // a single source of truth.
        player.IsAlive = true;
        player.Money = StarterPackage.StartingMoney;
        player.Oil = StarterPackage.StartingOil;
        player.Steel = StarterPackage.StartingSteel;
        player.Electronics = StarterPackage.StartingElectronics;
        player.Food = StarterPackage.StartingFood;
        player.Manpower = StarterPackage.StartingManpower;

        if (!world.Players.Contains(player))
        {
            world.Players.Add(player);
        }
        player.GameWorld = world;
        player.GameWorldId = world.Id;

        // Claim the province. ResourceProductionStep keys off OwnerPlayerId.
        province.OwnerPlayerId = player.Id;
        province.OwnerPlayer = player;
        if (!player.OwnedProvinces.Contains(province))
        {
            player.OwnedProvinces.Add(province);
        }

        // Starter buildings (docs/03 §"Starting province").
        AddBuilding(province, BuildingType.RecruitmentCenter, world.CurrentTick);
        AddBuilding(province, BuildingType.MilitaryBase, world.CurrentTick);
        AddBuilding(province, BuildingType.AirBase, world.CurrentTick);
        AddBuilding(province, BuildingType.FinancialDistrict, world.CurrentTick);

        // Starter units stationed at the starting province.
        for (var i = 0; i < StarterPackage.MechInfantryStackCount; i++)
        {
            AddUnit(world, player, province, UnitType.MechInfantry, UnitDomain.Ground,
                StarterPackage.MechInfantryStrength);
        }
        for (var i = 0; i < StarterPackage.AaBatteryStackCount; i++)
        {
            AddUnit(world, player, province, UnitType.AABattery, UnitDomain.Ground,
                StarterPackage.AaBatteryStrength);
        }

        return province;
    }

    /// <summary>
    /// Free-province selection: prefer a Capital-type province, fall back to any unowned
    /// province. Deterministic via lex Guid order so two callers seating in the same tick
    /// pick the same candidate (the caller chooses the order).
    /// </summary>
    private static Province? PickStartingProvince(GameWorld world)
    {
        var capital = world.Provinces
            .Where(p => p.Type == ProvinceType.Capital && p.OwnerPlayerId is null)
            .OrderBy(p => p.Id)
            .FirstOrDefault();
        if (capital is not null) return capital;

        return world.Provinces
            .Where(p => p.OwnerPlayerId is null)
            .OrderBy(p => p.Id)
            .FirstOrDefault();
    }

    private static void AddBuilding(Province province, BuildingType type, int currentTick)
    {
        province.Buildings.Add(new Building
        {
            Id = Guid.NewGuid(),
            ProvinceId = province.Id,
            Province = province,
            Type = type,
            Level = StarterPackage.StartingBuildingLevel,
            ConstructedAtTick = currentTick,
        });
    }

    private static void AddUnit(GameWorld world, Player owner, Province location,
        UnitType type, UnitDomain domain, int strength)
    {
        var unit = new Unit
        {
            Id = Guid.NewGuid(),
            GameWorldId = world.Id,
            GameWorld = world,
            OwnerPlayerId = owner.Id,
            OwnerPlayer = owner,
            LocationProvinceId = location.Id,
            LocationProvince = location,
            Type = type,
            Domain = domain,
            Strength = strength,
            Morale = 100,
            Experience = 0,
            IsInTransit = false,
        };
        owner.OwnedUnits.Add(unit);
        location.UnitsStationed.Add(unit);
    }
}

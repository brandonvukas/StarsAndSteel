using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Worlds;

/// <summary>
/// Pure logic for adding a player to a <see cref="GameWorld"/> and applying the
/// starter package from <c>docs/03-DATABASE-SCHEMA.md</c> §"Nation starting state".
/// <para/>
/// The Api project owns the DbContext + transaction; this service mutates a
/// pre-loaded entity graph in memory exactly the way <see cref="Tick.TickProcessor"/>
/// does. Stateless: every call gets the world graph passed in.
/// <para/>
/// Capital assignment: takes the lexicographically-smallest unowned province with
/// <see cref="ProvinceType.Capital"/>. Lex order on Guid is stable across runs (the
/// caller can pre-sort however they want for fairness; for MVP join-order
/// determinism is enough). Returns failure if no candidate capitals remain.
/// </summary>
public sealed class WorldJoinService
{
    /// <summary>
    /// Add a human player to <paramref name="world"/>. Mutates the graph in place
    /// and returns the new <see cref="Player"/> on success. If no candidate-capital
    /// province is free, returns <c>null</c>; the caller should surface 409 Conflict.
    /// <para/>
    /// On the first join, the world is also flipped from <see cref="GameWorldStatus.Lobby"/>
    /// to <see cref="GameWorldStatus.Active"/> so the tick service starts processing it.
    /// MVP doesn't have a true lobby flow yet — see <c>docs/11-ROADMAP.md</c>.
    /// </summary>
    public Player? AddHumanPlayer(
        GameWorld world,
        Guid userId,
        string nationName,
        string flagPrimaryHex,
        string flagSecondaryHex,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(nationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagPrimaryHex);
        ArgumentException.ThrowIfNullOrWhiteSpace(flagSecondaryHex);

        if (world.Status == GameWorldStatus.Ended)
        {
            return null;
        }

        // The same user can't take two seats in the same world.
        if (world.Players.Any(p => p.UserId == userId))
        {
            return null;
        }

        var capital = world.Provinces
            .Where(p => p.Type == ProvinceType.Capital && p.OwnerPlayerId is null)
            .OrderBy(p => p.Id) // deterministic — first lex-Guid wins
            .FirstOrDefault();

        if (capital is null)
        {
            return null;
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameWorldId = world.Id,
            GameWorld = world,
            IsAi = false,
            AiPersonality = null,
            NationName = nationName,
            FlagPrimaryHex = flagPrimaryHex,
            FlagSecondaryHex = flagSecondaryHex,
            IsAlive = true,
            Money = StarterPackage.StartingMoney,
            Oil = StarterPackage.StartingOil,
            Steel = StarterPackage.StartingSteel,
            Electronics = StarterPackage.StartingElectronics,
            Food = StarterPackage.StartingFood,
            Manpower = StarterPackage.StartingManpower,
        };

        world.Players.Add(player);

        // Claim the capital. ResourceProductionStep keys off OwnerPlayerId, so
        // setting it here is what turns the capital into a producing province.
        capital.OwnerPlayerId = player.Id;
        capital.OwnerPlayer = player;
        player.OwnedProvinces.Add(capital);

        // Starter buildings on the capital. Each at level 1.
        // Air Base inclusion is intentional — see docs/03 §"Starting province".
        AddBuilding(capital, BuildingType.RecruitmentCenter, world.CurrentTick);
        AddBuilding(capital, BuildingType.MilitaryBase, world.CurrentTick);
        AddBuilding(capital, BuildingType.AirBase, world.CurrentTick);
        AddBuilding(capital, BuildingType.FinancialDistrict, world.CurrentTick);

        // Starter units stationed at the capital.
        for (var i = 0; i < StarterPackage.MechInfantryStackCount; i++)
        {
            AddUnit(world, player, capital, UnitType.MechInfantry, UnitDomain.Ground,
                StarterPackage.MechInfantryStrength);
        }
        for (var i = 0; i < StarterPackage.AaBatteryStackCount; i++)
        {
            AddUnit(world, player, capital, UnitType.AABattery, UnitDomain.Ground,
                StarterPackage.AaBatteryStrength);
        }

        // First player flips the world live so the tick service starts processing.
        // Phase 2 will gate this on a "lobby full + start" trigger instead.
        if (world.Status == GameWorldStatus.Lobby)
        {
            world.Status = GameWorldStatus.Active;
            world.StartedAt = nowUtc;
            world.NextTickDueUtc = nowUtc.AddSeconds(world.TickIntervalSeconds);
        }

        return player;
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

    private static void AddUnit(
        GameWorld world,
        Player owner,
        Province location,
        UnitType type,
        UnitDomain domain,
        int strength)
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

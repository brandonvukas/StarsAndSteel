using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Step 3 of the tick pipeline (docs/07 §"ResourceProductionStep"):
/// every player accumulates resources from each owned province, with
/// per-building multiplicative bonuses and a morale multiplier.
///
/// Formula: <c>pool += baseOutput * sumOf(building bonuses) * moraleMultiplier</c>
///
/// Building bonuses (docs/04 §"resources"): only the matching resource is
/// boosted. A SteelMill at level 2 gives <c>1 + 0.25 * 2 = 1.5x</c> steel.
/// Multiple buildings of the same type stack additively (uncommon in MVP,
/// but legal): two SteelMills at level 1 each = <c>1 + 0.25 + 0.25 = 1.5x</c>.
///
/// Morale (docs/04 §"Morale"):
/// - &lt; 10  → province produces nothing
/// - &lt; 30  → 50% production
/// - otherwise full production
/// </summary>
public sealed class ResourceProductionStep : ITickStep
{
    public string Name => "ResourceProduction";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Group provinces by owner so we emit one event per player.
        // (A player with no provinces produces nothing and emits nothing.)
        var byOwner = context.World.Provinces
            .Where(p => p.OwnerPlayerId.HasValue)
            .GroupBy(p => p.OwnerPlayerId!.Value);

        // Map ownerId -> Player so we can apply the deltas to the denormalized
        // resource columns. Players without a row in Players (shouldn't happen,
        // but defensive) are silently skipped.
        var playersById = context.World.Players.ToDictionary(p => p.Id);

        foreach (var group in byOwner)
        {
            if (!playersById.TryGetValue(group.Key, out var player))
            {
                continue;
            }

            long money = 0, oil = 0, steel = 0, electronics = 0, food = 0, manpower = 0;

            foreach (var province in group)
            {
                var moraleFactor = MoraleFactor(province.MoraleLevel);
                if (moraleFactor <= 0)
                {
                    continue;
                }

                var (mMoney, mOil, mSteel, mElec, mFood, mMan) = BuildingMultipliers(province.Buildings);

                money += (long)Math.Round(province.MoneyPerTick * mMoney * moraleFactor);
                oil += (long)Math.Round(province.OilPerTick * mOil * moraleFactor);
                steel += (long)Math.Round(province.SteelPerTick * mSteel * moraleFactor);
                electronics += (long)Math.Round(province.ElectronicsPerTick * mElec * moraleFactor);
                food += (long)Math.Round(province.FoodPerTick * mFood * moraleFactor);
                manpower += (long)Math.Round(province.ManpowerPerTick * mMan * moraleFactor);
            }

            // Skip the no-op event when the player owns provinces but every one is
            // suppressed by morale (rare but not impossible).
            if ((money | oil | steel | electronics | food | manpower) == 0)
            {
                continue;
            }

            player.Money += money;
            player.Oil += oil;
            player.Steel += steel;
            player.Electronics += electronics;
            player.Food += food;
            player.Manpower += manpower;

            context.Events.Add(new ResourcesProducedEvent(
                Tick: context.ProcessingTick,
                PlayerId: player.Id,
                MoneyDelta: money,
                OilDelta: oil,
                SteelDelta: steel,
                ElectronicsDelta: electronics,
                FoodDelta: food,
                ManpowerDelta: manpower));
        }
    }

    /// <summary>Aggregate multipliers per resource from all buildings on a province.</summary>
    private static (double Money, double Oil, double Steel, double Electronics, double Food, double Manpower)
        BuildingMultipliers(IEnumerable<Building> buildings)
    {
        // Each multiplier starts at 1.0 (i.e. base output) and accumulates
        // bonus * level for the matching resource.
        double money = 1.0, oil = 1.0, steel = 1.0, electronics = 1.0, food = 1.0, manpower = 1.0;

        foreach (var b in buildings)
        {
            switch (b.Type)
            {
                case BuildingType.FinancialDistrict:
                    money += 0.20 * b.Level;
                    break;
                case BuildingType.Refinery:
                    oil += 0.30 * b.Level;
                    break;
                case BuildingType.SteelMill:
                    steel += 0.25 * b.Level;
                    break;
                case BuildingType.TechPark:
                    electronics += 0.25 * b.Level;
                    break;
                case BuildingType.AgriculturalSector:
                    food += 0.25 * b.Level;
                    break;
                case BuildingType.RecruitmentCenter:
                    manpower += 0.15 * b.Level;
                    break;
                // The other buildings (MilitaryBase, AirBase, NavalYard,
                // LogisticsHub, HardenedBunker, MissileSilo, CyberOpsCenter)
                // don't modify resource production.
            }
        }

        return (money, oil, steel, electronics, food, manpower);
    }

    private static double MoraleFactor(int moraleLevel) => moraleLevel switch
    {
        < 10 => 0.0,
        < 30 => 0.5,
        _ => 1.0,
    };
}

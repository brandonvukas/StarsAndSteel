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
///
/// Phase 2F logistics network bonus: provinces in a connected component (BFS over
/// land adjacencies between own-owned provinces, AT LEAST one of which has a
/// MilitaryBase) of size ≥ 2 receive an additional <see cref="LogisticsBonus"/>
/// production multiplier. Acts on top of building/morale multipliers.
/// </summary>
public sealed class ResourceProductionStep : ITickStep
{
    public string Name => "ResourceProduction";

    /// <summary>Multiplicative bonus applied to all 6 resources for each province in a qualifying logistics network.</summary>
    public const double LogisticsBonus = 1.10;

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var byOwner = context.World.Provinces
            .Where(p => p.OwnerPlayerId.HasValue)
            .GroupBy(p => p.OwnerPlayerId!.Value);

        var playersById = context.World.Players.ToDictionary(p => p.Id);

        // Per-owner set of province ids that participate in a logistics network
        // (a connected component containing ≥ 1 MilitaryBase and size ≥ 2).
        var logisticsByOwner = ComputeLogisticsNetworks(context);

        foreach (var group in byOwner)
        {
            if (!playersById.TryGetValue(group.Key, out var player))
            {
                continue;
            }

            var logisticsSet = logisticsByOwner.GetValueOrDefault(group.Key) ?? new HashSet<Guid>();

            long money = 0, oil = 0, steel = 0, electronics = 0, food = 0, manpower = 0;

            foreach (var province in group)
            {
                var moraleFactor = MoraleFactor(province.MoraleLevel);
                if (moraleFactor <= 0)
                {
                    continue;
                }

                var (mMoney, mOil, mSteel, mElec, mFood, mMan) = BuildingMultipliers(province.Buildings);

                var logistics = logisticsSet.Contains(province.Id) ? LogisticsBonus : 1.0;
                var combined = moraleFactor * logistics;

                money += (long)Math.Round(province.MoneyPerTick * mMoney * combined);
                oil += (long)Math.Round(province.OilPerTick * mOil * combined);
                steel += (long)Math.Round(province.SteelPerTick * mSteel * combined);
                electronics += (long)Math.Round(province.ElectronicsPerTick * mElec * combined);
                food += (long)Math.Round(province.FoodPerTick * mFood * combined);
                manpower += (long)Math.Round(province.ManpowerPerTick * mMan * combined);
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

    /// <summary>
    /// Per-owner set of province ids that participate in a logistics network. A network is
    /// a connected component of own-owned provinces (edges = land adjacencies between two
    /// own-owned provinces) that contains at least one MilitaryBase and has size ≥ 2. All
    /// provinces in a qualifying component receive the bonus, including those without a
    /// MilitaryBase themselves (one base hubs the network).
    /// </summary>
    private static Dictionary<Guid, HashSet<Guid>> ComputeLogisticsNetworks(TickContext context)
    {
        var result = new Dictionary<Guid, HashSet<Guid>>();
        var ownerByProvince = context.World.Provinces
            .Where(p => p.OwnerPlayerId.HasValue)
            .ToDictionary(p => p.Id, p => p.OwnerPlayerId!.Value);
        var hasBaseByProvince = context.World.Provinces
            .ToDictionary(p => p.Id, p => p.Buildings.Any(b => b.Type == BuildingType.MilitaryBase));

        // Per-owner adjacency: only edges where BOTH endpoints belong to the same owner
        // and the edge is land (no sea crossings — naval logistics would need NavalYard).
        var adjByProvince = new Dictionary<Guid, List<Guid>>();
        foreach (var edge in context.Adjacencies)
        {
            if (edge.IsSeaCrossing) continue;
            if (!ownerByProvince.TryGetValue(edge.ProvinceAId, out var ownerA)) continue;
            if (!ownerByProvince.TryGetValue(edge.ProvinceBId, out var ownerB)) continue;
            if (ownerA != ownerB) continue;
            if (!adjByProvince.TryGetValue(edge.ProvinceAId, out var listA))
            {
                listA = new List<Guid>();
                adjByProvince[edge.ProvinceAId] = listA;
            }
            listA.Add(edge.ProvinceBId);
            if (!adjByProvince.TryGetValue(edge.ProvinceBId, out var listB))
            {
                listB = new List<Guid>();
                adjByProvince[edge.ProvinceBId] = listB;
            }
            listB.Add(edge.ProvinceAId);
        }

        var visited = new HashSet<Guid>();
        foreach (var (provinceId, ownerId) in ownerByProvince)
        {
            if (visited.Contains(provinceId)) continue;

            // BFS within this owner's connected component.
            var component = new List<Guid>();
            var anyBase = false;
            var queue = new Queue<Guid>();
            queue.Enqueue(provinceId);
            visited.Add(provinceId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);
                if (hasBaseByProvince.GetValueOrDefault(current)) anyBase = true;
                if (adjByProvince.TryGetValue(current, out var neighbors))
                {
                    foreach (var n in neighbors)
                    {
                        if (visited.Add(n)) queue.Enqueue(n);
                    }
                }
            }

            if (!anyBase || component.Count < 2) continue;

            if (!result.TryGetValue(ownerId, out var bag))
            {
                bag = new HashSet<Guid>();
                result[ownerId] = bag;
            }
            foreach (var id in component) bag.Add(id);
        }

        return result;
    }
}

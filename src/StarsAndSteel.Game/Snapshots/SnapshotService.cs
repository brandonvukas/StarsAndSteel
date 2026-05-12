using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Snapshots;

namespace StarsAndSteel.Game.Snapshots;

/// <summary>
/// Pure projection from a fully-loaded <see cref="GameWorld"/> entity graph to
/// a fog-of-war-filtered <see cref="WorldSnapshot"/> for one specific player.
/// <para/>
/// The Api layer is responsible for loading the graph (with the right Includes)
/// and the adjacency rows (which aren't a navigation property on
/// <see cref="GameWorld"/>) and passing them in. This keeps the projection
/// pure-testable without a DbContext, mirroring the pattern used by
/// <see cref="Tick.TickProcessor"/>.
/// <para/>
/// MVP visibility rule (docs/05 §"Reconnaissance &amp; fog of war"):
/// a province is visible if the calling player owns it, or if it shares an
/// adjacency edge with a province the calling player owns. Recon Drones, Recon
/// Satellites, and a "previously seen" ledger come later.
/// </summary>
public sealed class SnapshotService
{
    /// <summary>
    /// Build a snapshot for <paramref name="callingPlayerId"/>. Throws
    /// <see cref="InvalidOperationException"/> if that player isn't actually
    /// in the world — the controller is expected to verify membership before
    /// calling this.
    /// </summary>
    public WorldSnapshot Build(
        GameWorld world,
        IReadOnlyCollection<ProvinceAdjacency> adjacencies,
        IReadOnlyCollection<Unit> units,
        Guid callingPlayerId)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(adjacencies);
        ArgumentNullException.ThrowIfNull(units);

        var me = world.Players.FirstOrDefault(p => p.Id == callingPlayerId)
            ?? throw new InvalidOperationException(
                $"Player {callingPlayerId} is not in world {world.Id}.");

        // Build adjacency adjacency-index keyed by province id (undirected:
        // every edge appears under both endpoints). Used for both visibility
        // checks and the AdjacentProvinceIds field on each province row.
        var neighborsOf = BuildNeighborIndex(adjacencies);

        // Visibility set: provinces I own + provinces adjacent to one I own.
        var ownedProvinceIds = world.Provinces
            .Where(p => p.OwnerPlayerId == callingPlayerId)
            .Select(p => p.Id)
            .ToHashSet();

        var visibleProvinceIds = new HashSet<Guid>(ownedProvinceIds);
        foreach (var ownedId in ownedProvinceIds)
        {
            if (neighborsOf.TryGetValue(ownedId, out var neighbors))
            {
                foreach (var n in neighbors)
                {
                    visibleProvinceIds.Add(n);
                }
            }
        }

        // Player color lookup (for province ownership coloring on the client).
        var playerColor = world.Players.ToDictionary(p => p.Id, p => p.FlagPrimaryHex);

        // Garrison strength per province = sum of strengths of stationed units
        // (anyone's). Computed up-front so we can mask it in one place.
        var garrisonByProvince = units
            .Where(u => !u.IsInTransit && u.LocationProvinceId is not null)
            .GroupBy(u => u.LocationProvinceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Strength));

        // --- Provinces ----------------------------------------------------
        var provinceDtos = new List<SnapshotProvince>(world.Provinces.Count);
        foreach (var province in world.Provinces)
        {
            var visible = visibleProvinceIds.Contains(province.Id);
            string? ownerColor = null;
            if (province.OwnerPlayerId is { } ownerId
                && playerColor.TryGetValue(ownerId, out var color))
            {
                ownerColor = color;
            }

            var adjacentIds = neighborsOf.TryGetValue(province.Id, out var ns)
                ? (IReadOnlyList<Guid>)ns.ToArray()
                : Array.Empty<Guid>();

            // Mask intel-leaking fields when the province isn't visible.
            int? morale = visible ? province.MoraleLevel : null;
            int? garrison = visible
                ? garrisonByProvince.TryGetValue(province.Id, out var g) ? g : 0
                : null;

            IReadOnlyList<SnapshotBuilding> buildings = visible
                ? province.Buildings
                    .Select(b => new SnapshotBuilding(b.Id, b.Type.ToString(), b.Level))
                    .ToArray()
                : Array.Empty<SnapshotBuilding>();

            provinceDtos.Add(new SnapshotProvince(
                Id: province.Id,
                Name: province.Name,
                Type: province.Type.ToString(),
                IsCoastal: province.IsCoastal,
                CenterX: province.CenterX,
                CenterY: province.CenterY,
                OwnerPlayerId: province.OwnerPlayerId,
                OwnerColorHex: ownerColor,
                Visible: visible,
                MoraleLevel: morale,
                GarrisonStrength: garrison,
                Buildings: buildings,
                AdjacentProvinceIds: adjacentIds));
        }

        // --- My units (full detail) --------------------------------------
        var myUnits = units
            .Where(u => u.OwnerPlayerId == callingPlayerId)
            .Select(u => new SnapshotMyUnit(
                Id: u.Id,
                Type: u.Type.ToString(),
                Domain: u.Domain.ToString(),
                Strength: u.Strength,
                Morale: u.Morale,
                Experience: u.Experience,
                LocationProvinceId: u.LocationProvinceId,
                IsInTransit: u.IsInTransit,
                TransitFromProvinceId: u.TransitFromProvinceId,
                TransitToProvinceId: u.TransitToProvinceId,
                TransitArrivalTick: u.TransitArrivalTick,
                ParentUnitId: u.ParentUnitId))
            .ToArray();

        // --- Visible enemy units -----------------------------------------
        // Only stationed units in visible provinces are surfaced. In-transit
        // enemy units are invisible to MVP fog (they're "between" provinces).
        var visibleEnemyUnits = units
            .Where(u => u.OwnerPlayerId != callingPlayerId
                && !u.IsInTransit
                && u.LocationProvinceId is { } loc
                && visibleProvinceIds.Contains(loc))
            .Select(u => new SnapshotEnemyUnit(
                Id: u.Id,
                OwnerPlayerId: u.OwnerPlayerId,
                Type: u.Type.ToString(),
                Domain: u.Domain.ToString(),
                Strength: u.Strength,
                LocationProvinceId: u.LocationProvinceId!.Value))
            .ToArray();

        // --- Player summaries (everyone, fog-safe) -----------------------
        var ownedCounts = world.Provinces
            .Where(p => p.OwnerPlayerId is not null)
            .GroupBy(p => p.OwnerPlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var playerSummaries = world.Players
            .Select(p => new SnapshotPlayerSummary(
                PlayerId: p.Id,
                NationName: p.NationName,
                FlagPrimaryHex: p.FlagPrimaryHex,
                FlagSecondaryHex: p.FlagSecondaryHex,
                IsAi: p.IsAi,
                IsAlive: p.IsAlive,
                OwnedProvinceCount: ownedCounts.TryGetValue(p.Id, out var c) ? c : 0))
            .ToArray();

        // --- Me block -----------------------------------------------------
        var meBlock = new SnapshotMe(
            PlayerId: me.Id,
            NationName: me.NationName,
            FlagPrimaryHex: me.FlagPrimaryHex,
            FlagSecondaryHex: me.FlagSecondaryHex,
            Resources: new SnapshotResources(
                Money: me.Money,
                Oil: me.Oil,
                Steel: me.Steel,
                Electronics: me.Electronics,
                Food: me.Food,
                Manpower: me.Manpower),
            IsAlive: me.IsAlive);

        return new WorldSnapshot(
            WorldId: world.Id,
            Name: world.Name,
            Status: world.Status.ToString(),
            CurrentTick: world.CurrentTick,
            TickIntervalSeconds: world.TickIntervalSeconds,
            NextTickDueUtc: world.NextTickDueUtc,
            Me: meBlock,
            Players: playerSummaries,
            Provinces: provinceDtos,
            MyUnits: myUnits,
            VisibleEnemyUnits: visibleEnemyUnits);
    }

    /// <summary>
    /// Build an undirected neighbor index from the adjacency rows. The PK
    /// invariant guarantees ProvinceAId &lt; ProvinceBId, but adjacency lookups
    /// must work in both directions, so each edge is registered under both ids.
    /// </summary>
    private static Dictionary<Guid, List<Guid>> BuildNeighborIndex(
        IReadOnlyCollection<ProvinceAdjacency> adjacencies)
    {
        var index = new Dictionary<Guid, List<Guid>>(adjacencies.Count * 2);

        foreach (var edge in adjacencies)
        {
            if (!index.TryGetValue(edge.ProvinceAId, out var aList))
            {
                aList = new List<Guid>();
                index[edge.ProvinceAId] = aList;
            }
            aList.Add(edge.ProvinceBId);

            if (!index.TryGetValue(edge.ProvinceBId, out var bList))
            {
                bList = new List<Guid>();
                index[edge.ProvinceBId] = bList;
            }
            bList.Add(edge.ProvinceAId);
        }

        return index;
    }
}

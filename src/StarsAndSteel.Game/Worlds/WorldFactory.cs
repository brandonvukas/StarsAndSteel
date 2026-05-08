using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Core.Seeding;

namespace StarsAndSteel.Game.Worlds;

/// <summary>
/// Builds a fresh <see cref="GameWorld"/> entity graph from <see cref="MapSeedData"/>.
/// Pure: takes data in, returns a POCO graph out. The Api project is responsible
/// for persisting the result inside a transaction (see <c>WorldsController</c>).
/// <para/>
/// The returned world is in <see cref="GameWorldStatus.Lobby"/> with no players. A
/// human player joins via <c>WorldJoinService</c>, which assigns a candidate-capital
/// province and applies the starter package from <c>docs/03-DATABASE-SCHEMA.md</c>.
/// <para/>
/// Each call re-stamps province Guids: the seeder produces deterministic IDs that
/// would collide if two worlds shared a database. Adjacency edges are translated
/// through the old→new id map and re-normalized to the
/// <c>ProvinceAId &lt; ProvinceBId</c> invariant (docs/03-DATABASE-SCHEMA.md).
/// </summary>
public sealed class WorldFactory
{
    private readonly TimeProvider _clock;

    public WorldFactory(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Build a new world graph. The world is created in
    /// <see cref="GameWorldStatus.Lobby"/> — players join via
    /// <see cref="WorldJoinService"/>, which also flips the status to
    /// <see cref="GameWorldStatus.Active"/> once the lobby criteria are met.
    /// The tick service only ticks Active worlds.
    /// </summary>
    /// <param name="name">Display name shown in the lobby list.</param>
    /// <param name="seed">
    /// Initial <c>MapSeed</c>. Also used as the starting <c>RngState</c> so the
    /// first tick's RNG is deterministic from this single value.
    /// </param>
    /// <param name="map">Pure data loaded from <c>shared/map-data.json</c>.</param>
    public WorldBuildResult Build(string name, int seed, MapSeedData map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(map);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var world = new GameWorld
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = GameWorldStatus.Lobby,
            CurrentTick = 0,
            TickIntervalSeconds = 60,
            // First tick fires immediately once the world goes Active.
            NextTickDueUtc = nowUtc,
            CreatedAt = nowUtc,
            MapSeed = seed,
            // RngState seeded from MapSeed; the LCG advances it every tick.
            RngState = seed,
            // SQL Server fills RowVersion server-side on insert; the bytes here
            // are just a placeholder so EF doesn't complain about a null array.
            RowVersion = Array.Empty<byte>(),
        };

        // Re-stamp every province with a fresh per-world Guid so two worlds in
        // the same database don't collide on PK. Track old→new mapping to
        // translate adjacency edges after.
        var idMap = new Dictionary<Guid, Guid>(map.Provinces.Count);

        foreach (var row in map.Provinces)
        {
            var freshId = Guid.NewGuid();
            idMap[row.Id] = freshId;

            var province = new Province
            {
                Id = freshId,
                GameWorldId = world.Id,
                GameWorld = world,
                Name = row.Name,
                Type = row.Type,
                IsCoastal = row.IsCoastal,
                CenterX = row.CenterX,
                CenterY = row.CenterY,
                MoraleLevel = 100,
                BasePopulation = row.BasePopulation,
                MoneyPerTick = row.MoneyPerTick,
                OilPerTick = row.OilPerTick,
                SteelPerTick = row.SteelPerTick,
                ElectronicsPerTick = row.ElectronicsPerTick,
                FoodPerTick = row.FoodPerTick,
                ManpowerPerTick = row.ManpowerPerTick,
                OwnerPlayerId = null, // neutral until claimed
            };

            world.Provinces.Add(province);
        }

        // Translate adjacencies through the id map and re-enforce the
        // ProvinceAId < ProvinceBId invariant from docs/03 (Guid order changes
        // when we re-stamp). Adjacency rows aren't children of GameWorld in the
        // model; the caller adds them to the DbSet directly.
        var adjacencies = new List<ProvinceAdjacency>(map.Adjacencies.Count);
        foreach (var edge in map.Adjacencies)
        {
            var aGuid = idMap[edge.ProvinceAId];
            var bGuid = idMap[edge.ProvinceBId];
            if (aGuid.CompareTo(bGuid) > 0)
            {
                (aGuid, bGuid) = (bGuid, aGuid);
            }

            adjacencies.Add(new ProvinceAdjacency
            {
                ProvinceAId = aGuid,
                ProvinceBId = bGuid,
                TerrainCost = edge.TerrainCost,
                IsSeaCrossing = edge.IsSeaCrossing,
            });
        }

        return new WorldBuildResult(world, adjacencies);
    }
}

/// <summary>
/// Result of <see cref="WorldFactory.Build"/>. The <see cref="World"/> already has
/// its <c>Provinces</c> collection populated (so <c>db.GameWorlds.Add</c> cascades
/// the inserts), but adjacencies are returned separately because they aren't a
/// navigation property on <c>GameWorld</c>.
/// </summary>
public sealed record WorldBuildResult(
    GameWorld World,
    IReadOnlyList<ProvinceAdjacency> Adjacencies);

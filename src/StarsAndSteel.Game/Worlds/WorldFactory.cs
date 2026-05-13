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
    /// <param name="aiOpponentCount">
    /// Number of AI opponents to auto-seat at world creation. MVP supports 0 or 1; the
    /// single AI is a Hawk per <c>docs/09-AI-OPPONENTS.md</c>. The world stays in Lobby
    /// (the tick service does not process it) until a human joins via
    /// <see cref="WorldJoinService"/>; this prevents AI-only worlds from ticking forever.
    /// </param>
    public WorldBuildResult Build(string name, int seed, MapSeedData map, int aiOpponentCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(map);
        if (aiOpponentCount < 0 || aiOpponentCount > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(aiOpponentCount),
                aiOpponentCount, "MVP supports 0 or 1 AI opponents per world.");
        }

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

        var result = new WorldBuildResult(world, adjacencies);
        if (aiOpponentCount > 0)
        {
            SeatAiOpponents(world, aiOpponentCount);
        }
        return result;
    }

    /// <summary>
    /// Seat <paramref name="count"/> AI opponents with diverse personalities. Phase 2J.
    /// Personality assignment is deterministic on (world.MapSeed, seat index): the rotation
    /// is Hawk, Industrialist, Hawk, Isolationist, Hawk, Schemer (Hawk-weighted per
    /// <c>docs/09-AI-OPPONENTS.md</c> §"We default each new AI player to a personality
    /// randomly, weighted toward Hawk in MVP because it creates the most action").
    /// Insurgent is reserved for Phase 3.
    /// </summary>
    internal static void SeatAiOpponents(GameWorld world, int count)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (count <= 0) return;

        var rotation = new[]
        {
            (AiPersonality.Hawk,          "Iron Coalition", "#7a0c0c", "#1c1c1c"),
            (AiPersonality.Industrialist, "Trade Concord",  "#0c4a7a", "#d4af37"),
            (AiPersonality.Hawk,          "Crimson Pact",   "#8b1a1a", "#2a2a2a"),
            (AiPersonality.Isolationist,  "Northern Watch", "#1c4a3a", "#cccccc"),
            (AiPersonality.Insurgent,     "Free Cadres",    "#a04a14", "#1a1a1a"),
            (AiPersonality.Schemer,       "Shadow Bureau",  "#2a1a4a", "#7a5a9a"),
        };

        // Deterministic offset by world seed so different worlds rotate differently.
        var offset = (int)(((uint)world.MapSeed) % (uint)rotation.Length);

        for (int i = 0; i < count; i++)
        {
            var (personality, nation, primary, secondary) = rotation[(offset + i) % rotation.Length];
            SeatAiPlayer(world, personality, nation, primary, secondary);
        }
    }

    /// <summary>
    /// Backward-compat shim retained for tests that explicitly seated a Hawk. Prefer
    /// <see cref="SeatAiOpponents"/>.
    /// </summary>
    internal static void SeatHawkAi(GameWorld world) =>
        SeatAiPlayer(world, AiPersonality.Hawk, "Iron Coalition", "#7a0c0c", "#1c1c1c");

    private static void SeatAiPlayer(GameWorld world, AiPersonality personality,
        string nationName, string primaryHex, string secondaryHex)
    {
        var ai = new Player
        {
            Id = Guid.NewGuid(),
            UserId = null,
            GameWorldId = world.Id,
            GameWorld = world,
            IsAi = true,
            AiPersonality = personality,
            NationName = nationName,
            FlagPrimaryHex = primaryHex,
            FlagSecondaryHex = secondaryHex,
        };
        ai.AiMemory = new AiMemory
        {
            PlayerId = ai.Id,
            Player = ai,
            MemoryJson = "{}",
        };

        // Spawn returns null only if no provinces are free; on an empty fresh world this
        // can't happen, but we still tolerate it (no-op) rather than throw — caller can
        // observe by checking world.Players for an AI seat.
        _ = PlayerSpawner.Spawn(world, ai);
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

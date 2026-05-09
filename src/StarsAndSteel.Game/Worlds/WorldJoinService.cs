using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Worlds;

/// <summary>
/// Pure logic for adding a human player to a <see cref="GameWorld"/> and applying the
/// starter package from <c>docs/03-DATABASE-SCHEMA.md</c> §"Nation starting state". The
/// shared seating logic (province pick + buildings + units + resources) lives in
/// <see cref="PlayerSpawner"/> so AI auto-spawn (Phase 1L) reuses the same code path.
/// <para/>
/// The Api project owns the DbContext + transaction; this service mutates a pre-loaded
/// entity graph in memory exactly the way <see cref="Tick.TickProcessor"/> does. Stateless.
/// </summary>
public sealed class WorldJoinService
{
    /// <summary>
    /// Add a human player to <paramref name="world"/>. Mutates the graph in place and
    /// returns the new <see cref="Player"/> on success. Returns <c>null</c> if no free
    /// starting province remains; caller should surface 409 Conflict.
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
        };

        var province = PlayerSpawner.Spawn(world, player);
        if (province is null)
        {
            return null;
        }

        // First human join flips the world live so the tick service starts processing.
        // Phase 2 will gate this on a "lobby full + start" trigger instead. AI-only worlds
        // intentionally stay in Lobby until a human shows up.
        if (world.Status == GameWorldStatus.Lobby)
        {
            world.Status = GameWorldStatus.Active;
            world.StartedAt = nowUtc;
            world.NextTickDueUtc = nowUtc.AddSeconds(world.TickIntervalSeconds);
        }

        return player;
    }
}

using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Penultimate step (immediately before <see cref="NewsStep"/> so victory headlines
/// land the same tick they're declared). Implements the docs/04 §"Victory conditions"
/// total-domination rule for MVP:
/// <list type="bullet">
///   <item>If a single living player owns at least <see cref="DominationThreshold"/>
///   of the world's provinces, they win — flip the world to
///   <see cref="GameWorldStatus.Ended"/>, set <c>EndedAt</c>, mark every other
///   player <c>IsAlive = false</c>, and emit <see cref="VictoryAchievedEvent"/>.</item>
///   <item>Independently, any player who currently owns zero provinces is flipped to
///   <c>IsAlive = false</c> and emits <see cref="PlayerEliminatedEvent"/>. (Docs/07
///   §EventStep specifies a 3-tick grace period; MVP eliminates immediately and the
///   delay table lands in a later phase alongside surrender mechanics.)</item>
/// </list>
/// Idempotent: if the world is already <see cref="GameWorldStatus.Ended"/> the step
/// is a no-op.
/// </summary>
public sealed class VictoryCheckStep : ITickStep
{
    /// <summary>Fraction of provinces a single player must own to trigger a total-domination victory.</summary>
    public const double DominationThreshold = 0.80;

    public string Name => "VictoryCheck";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.World.Status == GameWorldStatus.Ended) return;

        // Fast lookup of owner -> province count. Neutral (no owner) is intentionally excluded.
        var ownedCounts = new Dictionary<Guid, int>();
        var totalProvinces = 0;
        foreach (var province in context.World.Provinces)
        {
            totalProvinces++;
            if (province.OwnerPlayerId is { } ownerId)
            {
                ownedCounts.TryGetValue(ownerId, out var count);
                ownedCounts[ownerId] = count + 1;
            }
        }

        if (totalProvinces == 0) return;

        // Eliminations first — a player who lost their last province this tick should
        // be eliminated even if someone else is about to win.
        foreach (var player in context.World.Players)
        {
            if (!player.IsAlive) continue;
            if (ownedCounts.GetValueOrDefault(player.Id) > 0) continue;

            player.IsAlive = false;
            context.Events.Add(new PlayerEliminatedEvent(
                Tick: context.ProcessingTick,
                PlayerId: player.Id,
                NationName: player.NationName));
        }

        // Victory check. Pick the leader; if their share clears the threshold, end the world.
        var threshold = (int)Math.Ceiling(totalProvinces * DominationThreshold);
        var winner = context.World.Players
            .Where(p => p.IsAlive)
            .Select(p => (Player: p, Owned: ownedCounts.GetValueOrDefault(p.Id)))
            .Where(t => t.Owned >= threshold)
            .OrderByDescending(t => t.Owned)
            .FirstOrDefault();

        if (winner.Player is null) return;

        context.World.Status = GameWorldStatus.Ended;
        context.World.EndedAt = DateTime.UtcNow;

        // Mark every non-winner dead so the leaderboard renders correctly.
        foreach (var loser in context.World.Players)
        {
            if (loser.Id == winner.Player.Id) continue;
            if (!loser.IsAlive) continue;
            loser.IsAlive = false;
            context.Events.Add(new PlayerEliminatedEvent(
                Tick: context.ProcessingTick,
                PlayerId: loser.Id,
                NationName: loser.NationName));
        }

        context.Events.Add(new VictoryAchievedEvent(
            Tick: context.ProcessingTick,
            WinnerPlayerId: winner.Player.Id,
            WinnerNationName: winner.Player.NationName,
            OwnedProvinceCount: winner.Owned,
            TotalProvinceCount: totalProvinces));
    }
}

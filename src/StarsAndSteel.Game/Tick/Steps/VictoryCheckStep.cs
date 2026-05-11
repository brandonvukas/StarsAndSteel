using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick.Steps;

/// <summary>
/// Penultimate step (immediately before <see cref="NewsStep"/> so victory headlines
/// land the same tick they're declared). Implements the docs/04 §"Victory conditions"
/// total-domination rule for MVP plus the Phase 2F coalition variant:
/// <list type="bullet">
///   <item>If a single living player owns at least <see cref="DominationThreshold"/>
///   of the world's provinces, they win solo — emit <see cref="VictoryAchievedEvent"/>.</item>
///   <item>Otherwise, if a fully-connected clique of mutually-allied living players
///   collectively owns ≥ threshold, the entire clique wins — emit
///   <see cref="CoalitionVictoryAchievedEvent"/> (all clique members are winners,
///   non-clique players are eliminated). Solo wins always take priority over coalition
///   wins (a player who already qualifies alone never needs to share credit).</item>
///   <item>Independently, any player who currently owns zero provinces is flipped to
///   <c>IsAlive = false</c> and emits <see cref="PlayerEliminatedEvent"/>.</item>
/// </list>
/// Idempotent: if the world is already <see cref="GameWorldStatus.Ended"/> the step
/// is a no-op.
/// </summary>
public sealed class VictoryCheckStep : ITickStep
{
    /// <summary>Fraction of provinces a player or coalition must own to win.</summary>
    public const double DominationThreshold = 0.80;

    public string Name => "VictoryCheck";

    public void Execute(TickContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.World.Status == GameWorldStatus.Ended) return;

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

        // Eliminations first.
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

        var threshold = (int)Math.Ceiling(totalProvinces * DominationThreshold);

        // Solo victory takes precedence.
        var soloWinner = context.World.Players
            .Where(p => p.IsAlive)
            .Select(p => (Player: p, Owned: ownedCounts.GetValueOrDefault(p.Id)))
            .Where(t => t.Owned >= threshold)
            .OrderByDescending(t => t.Owned)
            .FirstOrDefault();

        if (soloWinner.Player is not null)
        {
            EndWorld(context);
            EliminateNonWinners(context, new HashSet<Guid> { soloWinner.Player.Id });
            context.Events.Add(new VictoryAchievedEvent(
                Tick: context.ProcessingTick,
                WinnerPlayerId: soloWinner.Player.Id,
                WinnerNationName: soloWinner.Player.NationName,
                OwnedProvinceCount: soloWinner.Owned,
                TotalProvinceCount: totalProvinces));
            return;
        }

        // Coalition victory: find the largest clique of mutually-allied living players
        // whose combined holdings meet the threshold. With ≤ ~8 players we can afford
        // a brute-force search of all subsets via greedy clique expansion; in practice
        // the alliance graph is sparse so candidate cliques are short.
        var coalition = FindWinningCoalition(context, ownedCounts, threshold);
        if (coalition is { Count: > 0 })
        {
            EndWorld(context);
            var winnerIds = new HashSet<Guid>(coalition.Select(p => p.Id));
            EliminateNonWinners(context, winnerIds);
            var orderedWinners = coalition.OrderBy(p => p.Id).ToList();
            var totalOwned = orderedWinners.Sum(p => ownedCounts.GetValueOrDefault(p.Id));
            context.Events.Add(new CoalitionVictoryAchievedEvent(
                Tick: context.ProcessingTick,
                WinnerPlayerIds: orderedWinners.Select(p => p.Id).ToList(),
                WinnerNationNames: orderedWinners.Select(p => p.NationName).ToList(),
                OwnedProvinceCount: totalOwned,
                TotalProvinceCount: totalProvinces));
        }
    }

    private static void EndWorld(TickContext context)
    {
        context.World.Status = GameWorldStatus.Ended;
        context.World.EndedAt = DateTime.UtcNow;
    }

    private static void EliminateNonWinners(TickContext context, HashSet<Guid> winnerIds)
    {
        foreach (var loser in context.World.Players)
        {
            if (winnerIds.Contains(loser.Id)) continue;
            if (!loser.IsAlive) continue;
            loser.IsAlive = false;
            context.Events.Add(new PlayerEliminatedEvent(
                Tick: context.ProcessingTick,
                PlayerId: loser.Id,
                NationName: loser.NationName));
        }
    }

    /// <summary>
    /// Greedy maximum-clique search over the alliance graph. Returns the winning
    /// coalition (≥ threshold combined provinces) or null if none qualifies.
    /// </summary>
    private static List<Player>? FindWinningCoalition(
        TickContext context,
        Dictionary<Guid, int> ownedCounts,
        int threshold)
    {
        var alive = context.World.Players
            .Where(p => p.IsAlive && ownedCounts.GetValueOrDefault(p.Id) > 0)
            .OrderByDescending(p => ownedCounts.GetValueOrDefault(p.Id))
            .ToList();
        if (alive.Count < 2) return null;

        // Quick reject: if even all living players combined fall short, no coalition wins.
        var totalAliveOwned = alive.Sum(p => ownedCounts.GetValueOrDefault(p.Id));
        if (totalAliveOwned < threshold) return null;

        List<Player>? best = null;

        // Try cliques seeded by each player; greedy-add allied members in descending
        // ownership order, requiring full mutual alliance with every existing member.
        foreach (var seed in alive)
        {
            var clique = new List<Player> { seed };
            var owned = ownedCounts.GetValueOrDefault(seed.Id);
            foreach (var candidate in alive)
            {
                if (candidate.Id == seed.Id) continue;
                if (clique.All(m => context.Relations.AreAllied(m.Id, candidate.Id)))
                {
                    clique.Add(candidate);
                    owned += ownedCounts.GetValueOrDefault(candidate.Id);
                    if (owned >= threshold)
                    {
                        // Found a winning clique. Prefer the smallest qualifying clique
                        // (fewer winners = stronger claim) but if same size prefer more owned.
                        if (best is null
                            || clique.Count < best.Count
                            || (clique.Count == best.Count && owned > best.Sum(p => ownedCounts.GetValueOrDefault(p.Id))))
                        {
                            best = new List<Player>(clique);
                        }
                        break;
                    }
                }
            }
        }

        return best;
    }
}

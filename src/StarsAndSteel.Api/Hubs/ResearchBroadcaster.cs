using Microsoft.AspNetCore.SignalR;
using StarsAndSteel.Api.Hubs.Dtos;
using StarsAndSteel.Api.Research.Dtos;

namespace StarsAndSteel.Api.Hubs;

/// <summary>
/// Out-of-tick broadcaster for player-initiated research actions. Mirrors
/// <see cref="DiplomacyBroadcaster"/>: <c>ResearchController</c> calls this
/// AFTER its DB save commits so subscribers never observe a state the
/// database doesn't already hold.
/// <para/>
/// We broadcast to the whole world group (single hub group per world); the
/// payload identifies the owner so non-owners can ignore it client-side.
/// Tech unlocks are emitted by <see cref="TickBroadcaster"/> from the
/// in-tick <see cref="StarsAndSteel.Game.Tick.Events.TechUnlockedEvent"/>.
/// </summary>
public sealed class ResearchBroadcaster
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<ResearchBroadcaster> _logger;

    public ResearchBroadcaster(IHubContext<GameHub> hub, ILogger<ResearchBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task BroadcastResearchStartedAsync(
        Guid worldId,
        Guid playerId,
        string techId,
        int ticksToResearch,
        CancellationToken ct)
    {
        var group = _hub.Clients.Group(GameHub.WorldGroup(worldId));
        try
        {
            await group.SendAsync(
                TickEventNames.ResearchStarted,
                new ResearchStartedEvent(playerId, techId, ticksToResearch),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to broadcast ResearchStarted for world={WorldId} player={PlayerId} tech={TechId}",
                worldId, playerId, techId);
        }
    }
}

/// <summary>Wire payload for the out-of-tick ResearchStarted hub event.</summary>
public sealed record ResearchStartedEvent(Guid PlayerId, string TechId, int TicksToResearch);

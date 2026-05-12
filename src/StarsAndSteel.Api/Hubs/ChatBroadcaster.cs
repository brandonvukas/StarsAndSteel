using Microsoft.AspNetCore.SignalR;
using StarsAndSteel.Api.Chat.Dtos;
using StarsAndSteel.Api.Hubs.Dtos;
using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Api.Hubs;

/// <summary>
/// Out-of-tick broadcaster for chat messages. Mirrors <see cref="ResearchBroadcaster"/>
/// and <see cref="DiplomacyBroadcaster"/>: <c>ChatController</c> calls this AFTER
/// the DB save commits.
/// <para/>
/// Routing strategy:
/// <list type="bullet">
///   <item><see cref="ChatScope.Global"/>: broadcast to the world group; everyone in the
///         world sees it.</item>
///   <item><see cref="ChatScope.Direct"/>: broadcast to the world group; clients filter on
///         <c>ToPlayerId == myPlayerId || FromPlayerId == myPlayerId</c>.</item>
///   <item><see cref="ChatScope.Alliance"/>: broadcast to the world group; clients filter
///         using their own alliance set (already known client-side via the diplomacy
///         snapshot). Not denormalized server-side per Phase 2K design.</item>
/// </list>
/// Server-side per-player routing arrives in a later phase if/when leakage matters.
/// </summary>
public sealed class ChatBroadcaster
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<ChatBroadcaster> _logger;

    public ChatBroadcaster(IHubContext<GameHub> hub, ILogger<ChatBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task BroadcastChatMessageAsync(
        Guid worldId,
        ChatMessageDto message,
        CancellationToken ct)
    {
        var group = _hub.Clients.Group(GameHub.WorldGroup(worldId));
        try
        {
            await group.SendAsync(TickEventNames.ChatMessageReceived, message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to broadcast ChatMessageReceived for world={WorldId} message={MessageId}",
                worldId, message.Id);
        }
    }
}

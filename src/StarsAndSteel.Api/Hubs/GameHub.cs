using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Data;

namespace StarsAndSteel.Api.Hubs;

/// <summary>
/// Live-state hub for one in-progress game world. Mounted at <c>/hubs/game</c>.
/// <para/>
/// Auth: JWT bearer only (the SignalR-over-WebSocket pattern can't carry
/// cookies reliably). Clients pass the token via <c>?access_token=…</c>; see
/// <c>Program.cs</c> JwtBearerEvents.OnMessageReceived.
/// <para/>
/// Group strategy (docs/06 §"Hub semantics"): clients explicitly call
/// <see cref="JoinWorld"/> after connecting; the hub validates that the caller
/// is a Player in that world and adds them to the <c>world:{worldId}</c> group.
/// The tick layer broadcasts every event to that group.
/// <para/>
/// Per-player events (resources, fog-aware unit positions) are NOT yet
/// filtered server-side — Phase 1J broadcasts everything to the world group
/// and clients filter. Server-side per-user routing arrives in Phase 1K.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class GameHub : Hub
{
    public const string Path = "/hubs/game";

    /// <summary>SignalR group name format. Public so the broadcaster can build it identically.</summary>
    public static string WorldGroup(Guid worldId) => $"world:{worldId:N}";

    private readonly StarsAndSteelDbContext _db;
    private readonly ILogger<GameHub> _logger;

    public GameHub(StarsAndSteelDbContext db, ILogger<GameHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Hub connected: connection={ConnectionId} user={UserId}",
            Context.ConnectionId, GetUserIdOrNull());
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
        {
            _logger.LogWarning(exception,
                "Hub disconnected with error: connection={ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation(
                "Hub disconnected: connection={ConnectionId}", Context.ConnectionId);
        }
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe the calling connection to a world's broadcast group. Throws
    /// <see cref="HubException"/> if the caller is not a Player in the world —
    /// SignalR surfaces this to the client as a method-invocation error so the
    /// UI can react (e.g., "you have not joined this world yet").
    /// </summary>
    public async Task JoinWorld(Guid worldId)
    {
        var userId = GetUserIdOrThrow();

        var isMember = await _db.Players
            .AnyAsync(p => p.GameWorldId == worldId && p.UserId == userId);
        if (!isMember)
        {
            _logger.LogWarning(
                "JoinWorld denied: user={UserId} is not a player in world={WorldId}",
                userId, worldId);
            throw new HubException("You are not a player in this world.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, WorldGroup(worldId));

        _logger.LogInformation(
            "JoinWorld: connection={ConnectionId} user={UserId} world={WorldId}",
            Context.ConnectionId, userId, worldId);
    }

    /// <summary>Remove the calling connection from a world's broadcast group.</summary>
    public async Task LeaveWorld(Guid worldId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, WorldGroup(worldId));

        _logger.LogInformation(
            "LeaveWorld: connection={ConnectionId} world={WorldId}",
            Context.ConnectionId, worldId);
    }

    /// <summary>
    /// Keepalive. SignalR's transport already handles ping/pong, but exposing
    /// an application-level Ping lets the client surface "round-trip OK" to
    /// the user without ambiguity about transport-level vs app-level health.
    /// </summary>
    public Task<long> Ping() => Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private Guid GetUserIdOrThrow()
    {
        var id = GetUserIdOrNull();
        if (id is null)
        {
            throw new HubException("Authenticated user identifier is missing.");
        }
        return id.Value;
    }

    private Guid? GetUserIdOrNull()
    {
        var raw = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

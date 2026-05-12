using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Chat.Dtos;
using StarsAndSteel.Api.Hubs;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
using StarsAndSteel.Game.Chat;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// Phase 2K. Chat read + send for one game world. Three scopes (Global / Alliance /
/// Direct) are persisted on the message; alliance recipients are computed at READ
/// time from <see cref="DiplomaticRelation"/> (no denormalization at send).
/// <para/>
/// Like <see cref="ResearchController"/>, sends take the per-world tick lock so the
/// SaveChanges + broadcast pair serializes against the tick processor.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/chat")]
[Authorize]
public sealed class ChatController : ControllerBase
{
    /// <summary>Default page size for GET history when no <c>take</c> is provided.</summary>
    private const int DefaultPageSize = 50;
    /// <summary>Hard cap to keep a single page bounded regardless of client hint.</summary>
    private const int MaxPageSize = 200;

    private readonly StarsAndSteelDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly ChatService _service;
    private readonly ChatBroadcaster _broadcaster;
    private readonly WorldLockRegistry _locks;
    private readonly IValidator<SendChatMessageRequest> _sendValidator;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        StarsAndSteelDbContext db,
        UserManager<User> userManager,
        ChatService service,
        ChatBroadcaster broadcaster,
        WorldLockRegistry locks,
        IValidator<SendChatMessageRequest> sendValidator,
        TimeProvider clock,
        ILogger<ChatController> logger)
    {
        _db = db;
        _userManager = userManager;
        _service = service;
        _broadcaster = broadcaster;
        _locks = locks;
        _sendValidator = sendValidator;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Returns the most recent chat messages visible to the caller in this world.
    /// Visibility:
    /// <list type="bullet">
    ///   <item>Global — everyone sees them.</item>
    ///   <item>Alliance — sender + every player currently <see cref="DiplomaticStatus.Allied"/>
    ///         with the sender (computed from the canonical relation table).</item>
    ///   <item>Direct — sender + the named recipient only.</item>
    /// </list>
    /// Returned in chronological order (oldest first) so the client can append directly.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetHistory(
        Guid worldId,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var caller = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameWorldId == worldId && p.UserId == user.Id, ct);
        if (caller is null) return Forbid();

        var pageSize = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);

        // Caller's current allies in this world (symmetric pair table — query both directions
        // and union for safety even though pairs are kept symmetric by DiplomacyService).
        var allyIds = await _db.DiplomaticRelations
            .AsNoTracking()
            .Where(r => r.GameWorldId == worldId
                        && r.Status == DiplomaticStatus.Allied
                        && (r.FromPlayerId == caller.Id || r.ToPlayerId == caller.Id))
            .Select(r => r.FromPlayerId == caller.Id ? r.ToPlayerId : r.FromPlayerId)
            .ToListAsync(ct);
        var alliesPlusSelf = new HashSet<Guid>(allyIds) { caller.Id };

        // Pull the most recent N candidates server-side, then filter in memory. We intentionally
        // overshoot a bit (×2) so visibility filtering doesn't shrink a Global-heavy page below
        // pageSize; this keeps the index seek cheap (GameWorldId, SentAtUtc DESC) without a
        // recursive top-up loop. Good enough for Phase 2K — a chunked cursor lands later.
        var candidateLimit = pageSize * 2;
        var candidates = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.GameWorldId == worldId)
            .OrderByDescending(m => m.SentAtUtc)
            .Take(candidateLimit)
            .ToListAsync(ct);

        var visible = candidates
            .Where(m => IsVisibleTo(m, caller.Id, alliesPlusSelf))
            .Take(pageSize)
            .OrderBy(m => m.SentAtUtc)
            .Select(ToDto)
            .ToList();

        return Ok(visible);
    }

    [HttpPost("send")]
    public async Task<ActionResult<SendChatMessageResponse>> Send(
        Guid worldId,
        [FromBody] SendChatMessageRequest request,
        CancellationToken ct)
    {
        if (await ValidateAsync(_sendValidator, request, ct) is { } badRequest) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(ct);
        try
        {
            var world = await _db.GameWorlds.FirstOrDefaultAsync(w => w.Id == worldId, ct);
            if (world is null) return NotFound();

            var sender = await _db.Players.FirstOrDefaultAsync(
                p => p.GameWorldId == worldId && p.UserId == user.Id, ct);
            if (sender is null) return Forbid();

            Player? recipient = null;
            if (request.Scope == ChatScope.Direct)
            {
                recipient = await _db.Players.FirstOrDefaultAsync(
                    p => p.GameWorldId == worldId && p.Id == request.ToPlayerId, ct);
                // Service handles the null/eliminated cases — we still load here so the
                // service stays pure and DB-free.
            }

            var gameEnded = world.Status != GameWorldStatus.Active;
            var nowUtc = _clock.GetUtcNow().UtcDateTime;
            var result = _service.Send(sender, request.Scope, recipient, request.Body, gameEnded, nowUtc);
            if (!result.IsAccepted) return RejectionToActionResult(result);

            var message = result.Mutation!;
            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Chat sent: world={WorldId} from={FromPlayerId} scope={Scope} to={ToPlayerId} bytes={Length}",
                worldId, sender.Id, message.Scope, message.ToPlayerId, message.Body.Length);

            await _broadcaster.BroadcastChatMessageAsync(worldId, ToDto(message), ct);

            return Ok(new SendChatMessageResponse(message.Id, message.SentAtUtc));
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsVisibleTo(ChatMessage m, Guid callerId, HashSet<Guid> alliesPlusSelf) =>
        m.Scope switch
        {
            ChatScope.Global => true,
            ChatScope.Direct => m.FromPlayerId == callerId || m.ToPlayerId == callerId,
            // Alliance: the sender's *current* allies (recomputed at read time). The caller
            // sees it if the sender is themselves or one of their allies. This means leaving
            // an alliance hides historical alliance chat — by design (forward-only secrecy).
            ChatScope.Alliance => alliesPlusSelf.Contains(m.FromPlayerId),
            _ => false,
        };

    private static ChatMessageDto ToDto(ChatMessage m) => new(
        m.Id, m.FromPlayerId, m.ToPlayerId, m.Scope, m.Body, m.SentAtUtc);

    private ActionResult<SendChatMessageResponse> RejectionToActionResult(ChatResult result)
    {
        var msg = result.RejectionMessage ?? "Action rejected.";
        return result.Rejection switch
        {
            ChatRejectionReason.GameEnded            => Conflict(new { error = msg }),
            ChatRejectionReason.SelfTargeted         => BadRequest(new { error = msg }),
            ChatRejectionReason.RecipientNotInWorld  => NotFound(new { error = msg }),
            ChatRejectionReason.RecipientEliminated  => Conflict(new { error = msg }),
            ChatRejectionReason.BodyEmpty            => BadRequest(new { error = msg }),
            ChatRejectionReason.BodyTooLong          => BadRequest(new { error = msg }),
            ChatRejectionReason.InvalidScopePayload  => BadRequest(new { error = msg }),
            _ => BadRequest(new { error = msg }),
        };
    }

    private async Task<ActionResult?> ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken ct)
    {
        var v = await validator.ValidateAsync(request, ct);
        if (v.IsValid) return null;

        var modelState = new ModelStateDictionary();
        foreach (var failure in v.Errors)
        {
            modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }
        return ValidationProblem(modelState);
    }
}

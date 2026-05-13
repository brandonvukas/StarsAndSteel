using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Diplomacy.Dtos;
using StarsAndSteel.Api.Hubs;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
using StarsAndSteel.Game.Diplomacy;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// Player-driven diplomatic actions: declare war (instant), propose treaty (creates
/// a Pending offer), accept / reject (receiver only), revoke (sender only).
/// <para/>
/// All five take the per-world tick lock to serialize against the tick processor;
/// after persisting the mutation we broadcast via <see cref="DiplomacyBroadcaster"/>
/// out-of-tick so connected clients see the change immediately.
/// <para/>
/// The directional <see cref="DiplomaticRelation"/> rows are written in symmetric
/// pairs (A→B and B→A both upserted). Querying either direction yields the same
/// status; the absence of a row implies <see cref="DiplomaticStatus.Peace"/>.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/diplomacy")]
[Authorize]
public sealed class DiplomacyController : ControllerBase
{
    private readonly StarsAndSteelDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly DiplomacyService _service;
    private readonly DiplomacyBroadcaster _broadcaster;
    private readonly WorldLockRegistry _locks;
    private readonly IValidator<DeclareWarRequest> _declareWarValidator;
    private readonly IValidator<ProposeTreatyRequest> _proposeValidator;
    private readonly IValidator<OfferActionRequest> _offerActionValidator;
    private readonly IValidator<SanctionRequest> _sanctionValidator;
    private readonly ILogger<DiplomacyController> _logger;

    public DiplomacyController(
        StarsAndSteelDbContext db,
        UserManager<User> userManager,
        DiplomacyService service,
        DiplomacyBroadcaster broadcaster,
        WorldLockRegistry locks,
        IValidator<DeclareWarRequest> declareWarValidator,
        IValidator<ProposeTreatyRequest> proposeValidator,
        IValidator<OfferActionRequest> offerActionValidator,
        IValidator<SanctionRequest> sanctionValidator,
        ILogger<DiplomacyController> logger)
    {
        _db = db;
        _userManager = userManager;
        _service = service;
        _broadcaster = broadcaster;
        _locks = locks;
        _declareWarValidator = declareWarValidator;
        _proposeValidator = proposeValidator;
        _offerActionValidator = offerActionValidator;
        _sanctionValidator = sanctionValidator;
        _logger = logger;
    }

    /// <summary>
    /// Whole-world diplomacy state from the caller's perspective: the player roster,
    /// every relation pair (canonical PartyA &lt; PartyB ordering, default Peace omitted),
    /// the caller's pending inbox (offers addressed to them), and outbox (offers they
    /// sent). Terminal offers are excluded — clients reconstruct lifecycle from
    /// <c>OfferReceived</c> / <c>OfferResolved</c> hub events.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DiplomacyStateDto>> GetState(Guid worldId, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var caller = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameWorldId == worldId && p.UserId == user.Id, ct);
        if (caller is null) return Forbid();

        var players = await _db.Players
            .AsNoTracking()
            .Where(p => p.GameWorldId == worldId)
            .Select(p => new DiplomacyPlayerDto(
                p.Id, p.NationName, p.FlagPrimaryHex, p.FlagSecondaryHex, p.IsAi, p.IsAlive))
            .ToListAsync(ct);

        // Collapse the directional rows to canonical pairs (PartyA < PartyB by Guid).
        var directional = await _db.DiplomaticRelations
            .AsNoTracking()
            .Where(r => r.GameWorldId == worldId)
            .Select(r => new { r.FromPlayerId, r.ToPlayerId, r.Status, r.LastChangedAtTick, r.IsSanctioning })
            .ToListAsync(ct);

        // Status flips are always written as symmetric pairs so either direction's row
        // carries the same Status; sanctions are directional and tracked per-row. We
        // walk both rows and merge: status = newest-wins; AtoB / BtoA flags pulled from
        // their respective directional rows.
        var pairAccum = new Dictionary<(Guid, Guid), (DiplomaticStatus Status, int Tick, bool AtoB, bool BtoA)>();
        foreach (var r in directional)
        {
            var (a, b) = OrderedPair(r.FromPlayerId, r.ToPlayerId);
            var atoB = r.FromPlayerId == a; // this row represents the A→B direction iff from==a
            pairAccum.TryGetValue((a, b), out var cur);
            var newTick = Math.Max(cur.Tick, r.LastChangedAtTick);
            var newStatus = r.LastChangedAtTick >= cur.Tick ? r.Status : cur.Status;
            pairAccum[(a, b)] = (
                newStatus,
                newTick,
                atoB ? r.IsSanctioning : cur.AtoB,
                atoB ? cur.BtoA : r.IsSanctioning);
        }

        var relationPairs = pairAccum
            .Select(kv => new DiplomacyRelationDto(
                kv.Key.Item1, kv.Key.Item2, kv.Value.Status, kv.Value.Tick, kv.Value.AtoB, kv.Value.BtoA))
            .ToList();

        var offers = await _db.TreatyOffers
            .AsNoTracking()
            .Where(o => o.GameWorldId == worldId
                        && o.Status == TreatyOfferStatus.Pending
                        && (o.SenderPlayerId == caller.Id || o.ReceiverPlayerId == caller.Id))
            .Select(o => new DiplomacyOfferDto(
                o.Id, o.SenderPlayerId, o.ReceiverPlayerId,
                o.Kind, o.Status, o.ProposedAtTick, o.ExpiresAtTick, o.ResolvedAtTick))
            .ToListAsync(ct);

        var inbox = offers.Where(o => o.ReceiverPlayerId == caller.Id).ToList();
        var outbox = offers.Where(o => o.SenderPlayerId == caller.Id).ToList();

        return Ok(new DiplomacyStateDto(
            caller.Id, players, relationPairs, inbox, outbox));
    }

    [HttpPost("declare-war")]
    public async Task<ActionResult<DiplomacyActionAccepted>> DeclareWar(
        Guid worldId,
        [FromBody] DeclareWarRequest request,
        CancellationToken ct)
    {
        if (await ValidateAsync(_declareWarValidator, request, ct) is { } badRequest) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(ct);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, ct);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var target = await _db.Players.FirstOrDefaultAsync(
                p => p.Id == request.TargetPlayerId && p.GameWorldId == worldId, ct);
            if (target is null) return NotFound(new { error = "Target player not found in this world." });

            var currentStatus = await GetCurrentStatusAsync(worldId, ctx.Player!.Id, target.Id, ct);
            var pending = await LoadPendingOffersBetweenAsync(worldId, ctx.Player.Id, target.Id, ct);

            var result = _service.DeclareWar(ctx.World!, ctx.Player, target, currentStatus, pending);
            if (!result.IsAccepted) return RejectionToActionResult<DiplomacyActionAccepted>(result);

            await PersistAndBroadcastAsync(ctx.World!, result.Mutation!, ct);

            var (a, b) = OrderedPair(ctx.Player.Id, target.Id);
            return Ok(new DiplomacyActionAccepted(a, b, DiplomaticStatus.War, ctx.World!.CurrentTick));
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("propose")]
    public async Task<ActionResult<OfferCreated>> Propose(
        Guid worldId,
        [FromBody] ProposeTreatyRequest request,
        CancellationToken ct)
    {
        if (await ValidateAsync(_proposeValidator, request, ct) is { } badRequest) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var kind = Enum.Parse<TreatyOfferKind>(request.Kind, ignoreCase: false);

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(ct);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, ct);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var receiver = await _db.Players.FirstOrDefaultAsync(
                p => p.Id == request.ReceiverPlayerId && p.GameWorldId == worldId, ct);
            if (receiver is null) return NotFound(new { error = "Receiver player not found in this world." });

            var currentStatus = await GetCurrentStatusAsync(worldId, ctx.Player!.Id, receiver.Id, ct);
            var pendingFromSender = await _db.TreatyOffers
                .Where(o => o.GameWorldId == worldId
                            && o.SenderPlayerId == ctx.Player.Id
                            && o.ReceiverPlayerId == receiver.Id
                            && o.Status == TreatyOfferStatus.Pending)
                .ToListAsync(ct);

            var result = _service.ProposeTreaty(
                ctx.World!, ctx.Player, receiver, kind, currentStatus, pendingFromSender);
            if (!result.IsAccepted) return RejectionToActionResult<OfferCreated>(result);

            var createdOffer = result.Mutation!.OfferChanges.Single(o => o.Kind == OfferChangeKind.Create).Offer;
            await PersistAndBroadcastAsync(ctx.World!, result.Mutation, ct);

            return Ok(new OfferCreated(
                createdOffer.Id, createdOffer.SenderPlayerId, createdOffer.ReceiverPlayerId,
                createdOffer.Kind, createdOffer.ProposedAtTick, createdOffer.ExpiresAtTick));
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("accept")]
    public Task<ActionResult<DiplomacyActionAccepted>> Accept(
        Guid worldId, [FromBody] OfferActionRequest request, CancellationToken ct)
        => ResolveOfferAsync(worldId, request, ct, kind: OfferResolutionKind.Accept);

    [HttpPost("reject")]
    public Task<ActionResult<DiplomacyActionAccepted>> Reject(
        Guid worldId, [FromBody] OfferActionRequest request, CancellationToken ct)
        => ResolveOfferAsync(worldId, request, ct, kind: OfferResolutionKind.Reject);

    [HttpPost("revoke")]
    public Task<ActionResult<DiplomacyActionAccepted>> Revoke(
        Guid worldId, [FromBody] OfferActionRequest request, CancellationToken ct)
        => ResolveOfferAsync(worldId, request, ct, kind: OfferResolutionKind.Revoke);

    /// <summary>
    /// Phase 4e: impose an economic sanction. Free, instant, asymmetric. Stacks
    /// multiplicatively with other inbound sanctions on the target — see
    /// <c>ResourceProductionStep</c>.
    /// </summary>
    [HttpPost("sanction")]
    public Task<ActionResult<SanctionActionAccepted>> Sanction(
        Guid worldId, [FromBody] SanctionRequest request, CancellationToken ct)
        => ToggleSanctionAsync(worldId, request, ct, impose: true);

    /// <summary>Phase 4e: lift a previously imposed sanction. Free, instant.</summary>
    [HttpPost("lift-sanction")]
    public Task<ActionResult<SanctionActionAccepted>> LiftSanction(
        Guid worldId, [FromBody] SanctionRequest request, CancellationToken ct)
        => ToggleSanctionAsync(worldId, request, ct, impose: false);

    private async Task<ActionResult<SanctionActionAccepted>> ToggleSanctionAsync(
        Guid worldId, SanctionRequest request, CancellationToken ct, bool impose)
    {
        if (await ValidateAsync(_sanctionValidator, request, ct) is { } badRequest)
        {
            return badRequest;
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(ct);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, ct);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var target = await _db.Players.FirstOrDefaultAsync(
                p => p.Id == request.TargetPlayerId && p.GameWorldId == worldId, ct);
            if (target is null) return NotFound(new { error = "Target player not found in this world." });

            // Look up current caller→target sanction state (directional).
            var existing = await _db.DiplomaticRelations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.GameWorldId == worldId
                         && r.FromPlayerId == ctx.Player!.Id
                         && r.ToPlayerId == target.Id, ct);
            var currentlySanctioning = existing?.IsSanctioning ?? false;

            var result = impose
                ? _service.Sanction(ctx.World!, ctx.Player!, target, currentlySanctioning)
                : _service.LiftSanction(ctx.World!, ctx.Player!, target, currentlySanctioning);

            if (!result.IsAccepted) return RejectionToActionResult<SanctionActionAccepted>(result);

            await PersistAndBroadcastAsync(ctx.World!, result.Mutation!, ct);

            // Re-read both directional rows to populate canonical pair flags.
            var afterRows = await _db.DiplomaticRelations
                .AsNoTracking()
                .Where(r => r.GameWorldId == worldId
                            && ((r.FromPlayerId == ctx.Player!.Id && r.ToPlayerId == target.Id)
                                || (r.FromPlayerId == target.Id && r.ToPlayerId == ctx.Player.Id)))
                .Select(r => new { r.FromPlayerId, r.ToPlayerId, r.IsSanctioning })
                .ToListAsync(ct);

            var (a, b) = OrderedPair(ctx.Player!.Id, target.Id);
            var aToB = afterRows.FirstOrDefault(r => r.FromPlayerId == a && r.ToPlayerId == b)?.IsSanctioning ?? false;
            var bToA = afterRows.FirstOrDefault(r => r.FromPlayerId == b && r.ToPlayerId == a)?.IsSanctioning ?? false;

            return Ok(new SanctionActionAccepted(a, b, aToB, bToA, ctx.World!.CurrentTick));
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- Helpers ----------------------------------------------------------

    private enum OfferResolutionKind { Accept, Reject, Revoke }

    private async Task<ActionResult<DiplomacyActionAccepted>> ResolveOfferAsync(
        Guid worldId, OfferActionRequest request, CancellationToken ct, OfferResolutionKind kind)
    {
        if (await ValidateAsync(_offerActionValidator, request, ct) is { } badRequest)
        {
            return badRequest;
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(ct);
        try
        {
            var ctx = await LoadCallerContextAsync(worldId, user.Id, ct);
            if (ctx.NotFound) return NotFound();
            if (ctx.Forbidden) return Forbid();

            var offer = await _db.TreatyOffers.FirstOrDefaultAsync(
                o => o.Id == request.OfferId && o.GameWorldId == worldId, ct);

            var result = kind switch
            {
                OfferResolutionKind.Accept => _service.AcceptOffer(ctx.World!, ctx.Player!, offer),
                OfferResolutionKind.Reject => _service.RejectOffer(ctx.World!, ctx.Player!, offer),
                OfferResolutionKind.Revoke => _service.RevokeOffer(ctx.World!, ctx.Player!, offer),
                _ => throw new InvalidOperationException($"Unhandled resolution kind: {kind}"),
            };
            if (!result.IsAccepted) return RejectionToActionResult<DiplomacyActionAccepted>(result);

            await PersistAndBroadcastAsync(ctx.World!, result.Mutation!, ct);

            // Accept yields a new DiplomaticStatus; reject/revoke don't change relations.
            if (kind == OfferResolutionKind.Accept && result.Mutation!.RelationChanges.Count > 0)
            {
                var rc = result.Mutation.RelationChanges[0];
                var (a, b) = OrderedPair(rc.FromPlayerId, rc.ToPlayerId);
                return Ok(new DiplomacyActionAccepted(a, b, rc.NewStatus, rc.AtTick));
            }

            // Reject/Revoke: report the offer pair with whatever status the relation currently holds.
            var pair = OrderedPair(offer!.SenderPlayerId, offer.ReceiverPlayerId);
            var status = await GetCurrentStatusAsync(worldId, offer.SenderPlayerId, offer.ReceiverPlayerId, ct);
            return Ok(new DiplomacyActionAccepted(pair.A, pair.B, status, ctx.World!.CurrentTick));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Reads the existing relation row in the SenderId→ReceiverId direction. If neither
    /// directional row exists the parties are at <see cref="DiplomaticStatus.Peace"/> by
    /// default.
    /// </summary>
    private async Task<DiplomaticStatus> GetCurrentStatusAsync(Guid worldId, Guid a, Guid b, CancellationToken ct)
    {
        var row = await _db.DiplomaticRelations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GameWorldId == worldId
                                       && ((r.FromPlayerId == a && r.ToPlayerId == b)
                                           || (r.FromPlayerId == b && r.ToPlayerId == a)), ct);
        return row?.Status ?? DiplomaticStatus.Peace;
    }

    private async Task<List<TreatyOffer>> LoadPendingOffersBetweenAsync(
        Guid worldId, Guid a, Guid b, CancellationToken ct) =>
        await _db.TreatyOffers
            .Where(o => o.GameWorldId == worldId
                        && o.Status == TreatyOfferStatus.Pending
                        && ((o.SenderPlayerId == a && o.ReceiverPlayerId == b)
                            || (o.SenderPlayerId == b && o.ReceiverPlayerId == a)))
            .ToListAsync(ct);

    /// <summary>
    /// Applies a <see cref="DiplomacyMutation"/> against the tracked context: upserts
    /// the symmetric relation pair (Add or Update on each direction), attaches the
    /// new offer (or relies on already-tracked offer for status flips), inserts news,
    /// saves, then broadcasts.
    /// </summary>
    private async Task PersistAndBroadcastAsync(GameWorld world, DiplomacyMutation mutation, CancellationToken ct)
    {
        // Relations: upsert each directional row.
        foreach (var rc in mutation.RelationChanges)
        {
            var existing = await _db.DiplomaticRelations.FirstOrDefaultAsync(
                r => r.GameWorldId == rc.GameWorldId
                     && r.FromPlayerId == rc.FromPlayerId
                     && r.ToPlayerId == rc.ToPlayerId, ct);
            if (existing is null)
            {
                _db.DiplomaticRelations.Add(new DiplomaticRelation
                {
                    Id = Guid.NewGuid(),
                    GameWorldId = rc.GameWorldId,
                    FromPlayerId = rc.FromPlayerId,
                    ToPlayerId = rc.ToPlayerId,
                    Status = rc.NewStatus,
                    LastChangedAtTick = rc.AtTick,
                });
            }
            else
            {
                existing.Status = rc.NewStatus;
                existing.LastChangedAtTick = rc.AtTick;
            }
        }

        // Offer changes: only Create needs Add; status flips on tracked offers are already tracked.
        foreach (var oc in mutation.OfferChanges)
        {
            if (oc.Kind == OfferChangeKind.Create)
            {
                _db.TreatyOffers.Add(oc.Offer);
            }
            // Mark* mutations were applied to the tracked entity in the service; nothing to do.
        }

        // Phase 4e: directional sanction toggles. Upsert the From→To row, creating it
        // at default Peace if missing. Sanctions are independent of status — we never
        // touch the Status field here.
        foreach (var sc in mutation.SanctionChanges)
        {
            var existing = await _db.DiplomaticRelations.FirstOrDefaultAsync(
                r => r.GameWorldId == sc.GameWorldId
                     && r.FromPlayerId == sc.FromPlayerId
                     && r.ToPlayerId == sc.ToPlayerId, ct);
            if (existing is null)
            {
                _db.DiplomaticRelations.Add(new DiplomaticRelation
                {
                    Id = Guid.NewGuid(),
                    GameWorldId = sc.GameWorldId,
                    FromPlayerId = sc.FromPlayerId,
                    ToPlayerId = sc.ToPlayerId,
                    Status = DiplomaticStatus.Peace,
                    LastChangedAtTick = sc.AtTick,
                    IsSanctioning = sc.IsSanctioning,
                });
            }
            else
            {
                existing.IsSanctioning = sc.IsSanctioning;
            }
        }

        // News.
        if (mutation.News.Count > 0)
        {
            _db.NewsItems.AddRange(mutation.News);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Diplomacy mutation persisted: world={WorldId} relations={RelCount} offerChanges={OfferCount} sanctions={SanctionCount} news={NewsCount}",
            world.Id, mutation.RelationChanges.Count, mutation.OfferChanges.Count, mutation.SanctionChanges.Count, mutation.News.Count);

        await _broadcaster.BroadcastAsync(world.Id, mutation, mutation.News, ct);
    }

    private async Task<CallerContext> LoadCallerContextAsync(Guid worldId, Guid userId, CancellationToken ct)
    {
        var world = await _db.GameWorlds.FirstOrDefaultAsync(w => w.Id == worldId, ct);
        if (world is null) return CallerContext.NotFoundResult;

        var player = await _db.Players.FirstOrDefaultAsync(
            p => p.GameWorldId == worldId && p.UserId == userId, ct);
        if (player is null) return CallerContext.ForbiddenResult;

        return new CallerContext(world, player, false, false);
    }

    private ActionResult<T> RejectionToActionResult<T>(DiplomacyResult result)
    {
        var msg = result.RejectionMessage ?? "Action rejected.";
        return result.Rejection switch
        {
            DiplomacyRejectionReason.GameEnded              => Conflict(new { error = msg }),
            DiplomacyRejectionReason.SelfTargeted           => BadRequest(new { error = msg }),
            DiplomacyRejectionReason.PlayerNotInWorld       => NotFound(new { error = msg }),
            DiplomacyRejectionReason.PlayerEliminated       => Conflict(new { error = msg }),
            DiplomacyRejectionReason.AlreadyAtWar           => Conflict(new { error = msg }),
            DiplomacyRejectionReason.AlreadyAllied          => Conflict(new { error = msg }),
            DiplomacyRejectionReason.AlreadyAtPeace         => Conflict(new { error = msg }),
            DiplomacyRejectionReason.DuplicatePendingOffer  => Conflict(new { error = msg }),
            DiplomacyRejectionReason.OfferNotFound          => NotFound(new { error = msg }),
            DiplomacyRejectionReason.OfferNotPending        => Conflict(new { error = msg }),
            DiplomacyRejectionReason.OfferNotForCaller      => Forbid(),
            DiplomacyRejectionReason.NotOfferReceiver       => Forbid(),
            DiplomacyRejectionReason.NotOfferSender         => Forbid(),
            DiplomacyRejectionReason.AlreadySanctioning     => Conflict(new { error = msg }),
            DiplomacyRejectionReason.NotCurrentlySanctioning => Conflict(new { error = msg }),
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

    private static (Guid A, Guid B) OrderedPair(Guid x, Guid y) =>
        x.CompareTo(y) <= 0 ? (x, y) : (y, x);

    private sealed record CallerContext(GameWorld? World, Player? Player, bool NotFound, bool Forbidden)
    {
        public static readonly CallerContext NotFoundResult = new(null, null, true, false);
        public static readonly CallerContext ForbiddenResult = new(null, null, false, true);
    }
}

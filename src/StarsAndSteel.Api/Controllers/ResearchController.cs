using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Hubs;
using StarsAndSteel.Api.Research.Dtos;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
using StarsAndSteel.Game.Research;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// Phase 2G research endpoints. The catalogue + caller's per-tech progress is
/// returned by GET; POST /start validates + debits resources + inserts a
/// <see cref="ResearchProgress"/> row at <c>ProgressPoints=0</c>. Per-tick
/// advancement happens server-side in <see cref="Game.Tick.Steps.ResearchStep"/>.
/// <para/>
/// Mirrors <see cref="DiplomacyController"/>: all writes take the per-world tick
/// lock to serialize against the tick processor and broadcast out-of-tick after
/// the DB save commits.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/research")]
[Authorize]
public sealed class ResearchController : ControllerBase
{
    private readonly StarsAndSteelDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly ResearchService _service;
    private readonly ResearchBroadcaster _broadcaster;
    private readonly WorldLockRegistry _locks;
    private readonly IValidator<StartResearchRequest> _startValidator;
    private readonly ILogger<ResearchController> _logger;

    public ResearchController(
        StarsAndSteelDbContext db,
        UserManager<User> userManager,
        ResearchService service,
        ResearchBroadcaster broadcaster,
        WorldLockRegistry locks,
        IValidator<StartResearchRequest> startValidator,
        ILogger<ResearchController> logger)
    {
        _db = db;
        _userManager = userManager;
        _service = service;
        _broadcaster = broadcaster;
        _locks = locks;
        _startValidator = startValidator;
        _logger = logger;
    }

    /// <summary>
    /// Returns the static tech catalogue + the caller's per-tech progress in
    /// this world. Pure read — no tick lock taken. Caller must be a player in
    /// the world (else 403).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ResearchStateDto>> GetState(Guid worldId, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var caller = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameWorldId == worldId && p.UserId == user.Id, ct);
        if (caller is null) return Forbid();

        var rows = await _db.ResearchProgress
            .AsNoTracking()
            .Where(r => r.PlayerId == caller.Id)
            .ToListAsync(ct);

        var catalog = TechCatalog.All
            .Select(t => new TechSpecDto(
                t.Id, t.Name, t.Category, t.Summary,
                t.MoneyCost, t.ElectronicsCost, t.TicksToResearch, t.Prerequisites))
            .ToList();

        var progress = rows
            .Select(r =>
            {
                var spec = TechCatalog.Find(r.TechId);
                return new ResearchProgressDto(
                    r.TechId, r.ProgressPoints,
                    spec?.TicksToResearch ?? 0, r.IsUnlocked);
            })
            .ToList();

        return Ok(new ResearchStateDto(caller.Id, catalog, progress));
    }

    [HttpPost("start")]
    public async Task<ActionResult<ResearchStarted>> Start(
        Guid worldId,
        [FromBody] StartResearchRequest request,
        CancellationToken ct)
    {
        if (await ValidateAsync(_startValidator, request, ct) is { } badRequest) return badRequest;

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var gate = _locks.GetOrCreate(worldId);
        await gate.WaitAsync(ct);
        try
        {
            var world = await _db.GameWorlds.FirstOrDefaultAsync(w => w.Id == worldId, ct);
            if (world is null) return NotFound();

            var player = await _db.Players.FirstOrDefaultAsync(
                p => p.GameWorldId == worldId && p.UserId == user.Id, ct);
            if (player is null) return Forbid();

            var existing = await _db.ResearchProgress
                .Where(r => r.PlayerId == player.Id)
                .ToListAsync(ct);

            var gameEnded = world.Status != GameWorldStatus.Active;
            var result = _service.StartResearch(player, request.TechId, gameEnded, existing);
            if (!result.IsAccepted) return RejectionToActionResult(result);

            // Apply mutations: insert the new progress row + debit resources.
            _db.ResearchProgress.Add(result.Mutation!);
            if (result.DebitMoney) player.Money -= result.MoneyDelta;
            if (result.DebitElectronics) player.Electronics -= result.ElectronicsDelta;

            await _db.SaveChangesAsync(ct);

            var spec = TechCatalog.Find(request.TechId)!;
            _logger.LogInformation(
                "Research started: world={WorldId} player={PlayerId} tech={TechId} cost=${Money}+{Electronics}E",
                worldId, player.Id, spec.Id, spec.MoneyCost, spec.ElectronicsCost);

            await _broadcaster.BroadcastResearchStartedAsync(
                worldId, player.Id, spec.Id, spec.TicksToResearch, ct);

            return Ok(new ResearchStarted(spec.Id, spec.TicksToResearch));
        }
        finally
        {
            gate.Release();
        }
    }

    private ActionResult<ResearchStarted> RejectionToActionResult(ResearchResult result)
    {
        var msg = result.RejectionMessage ?? "Action rejected.";
        return result.Rejection switch
        {
            ResearchRejectionReason.GameEnded             => Conflict(new { error = msg }),
            ResearchRejectionReason.UnknownTech           => NotFound(new { error = msg }),
            ResearchRejectionReason.AlreadyUnlocked       => Conflict(new { error = msg }),
            ResearchRejectionReason.AlreadyInProgress     => Conflict(new { error = msg }),
            ResearchRejectionReason.PrerequisiteMissing   => Conflict(new { error = msg }),
            ResearchRejectionReason.InsufficientResources => Conflict(new { error = msg }),
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

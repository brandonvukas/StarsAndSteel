using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Generals.Dtos;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
using StarsAndSteel.Game.Generals;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// Phase 3f: theater-commander (general) endpoints. A general is a non-combat
/// persistent leader figure a player buys for a fixed money cost and pins to
/// one friendly province; while assigned, defenders at that province get a
/// flat effective-strength bonus during ground combat (applied by
/// <c>CombatStep</c> via <c>CombatResolver.ResolveGround</c> overload).
/// <para/>
/// All writes take the per-world tick lock to serialize against the tick
/// processor, matching the ResearchController / OrdersController pattern.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/generals")]
[Authorize]
public sealed class GeneralsController : ControllerBase
{
    private readonly StarsAndSteelDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly GeneralsService _service;
    private readonly WorldLockRegistry _locks;
    private readonly IValidator<RecruitGeneralRequest> _recruitValidator;
    private readonly IValidator<AssignGeneralRequest> _assignValidator;
    private readonly ILogger<GeneralsController> _logger;

    public GeneralsController(
        StarsAndSteelDbContext db,
        UserManager<User> userManager,
        GeneralsService service,
        WorldLockRegistry locks,
        IValidator<RecruitGeneralRequest> recruitValidator,
        IValidator<AssignGeneralRequest> assignValidator,
        ILogger<GeneralsController> logger)
    {
        _db = db;
        _userManager = userManager;
        _service = service;
        _locks = locks;
        _recruitValidator = recruitValidator;
        _assignValidator = assignValidator;
        _logger = logger;
    }

    /// <summary>
    /// Returns every general the caller owns in this world. Pure read — no tick
    /// lock taken. Caller must be a player in the world (else 403).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GeneralDto>>> GetMine(Guid worldId, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var caller = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.GameWorldId == worldId && p.UserId == user.Id, ct);
        if (caller is null) return Forbid();

        var rows = await _db.Generals
            .AsNoTracking()
            .Where(g => g.GameWorldId == worldId && g.OwnerPlayerId == caller.Id)
            .Select(g => new GeneralDto(g.Id, g.OwnerPlayerId, g.Name, g.AssignedProvinceId, g.XpLevel))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<GeneralRecruited>> Recruit(
        Guid worldId,
        [FromBody] RecruitGeneralRequest request,
        CancellationToken ct)
    {
        if (await ValidateAsync(_recruitValidator, request, ct) is { } badRequest) return badRequest;

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

            // One-per-player cap is enforced by the service; load the caller's
            // existing generals (cheap — at most 1 row in MVP).
            var existing = await _db.Generals
                .Where(g => g.GameWorldId == worldId && g.OwnerPlayerId == player.Id)
                .ToListAsync(ct);

            var result = _service.RecruitGeneral(player, existing, request.Name, world.Status);
            if (!result.IsAccepted) return RejectionToActionResult<GeneralRecruited>(result);

            _db.Generals.Add(result.General!);
            if (result.DebitMoney) GeneralsService.DebitForRecruit(player);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "General recruited: world={WorldId} player={PlayerId} general={GeneralId} name={Name} cost=${Cost}",
                worldId, player.Id, result.General!.Id, result.General.Name, result.MoneyDelta);

            return Ok(new GeneralRecruited(result.General.Id, result.General.Name, result.MoneyDelta));
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("{generalId:guid}/assign")]
    public async Task<ActionResult<GeneralAssigned>> Assign(
        Guid worldId,
        Guid generalId,
        [FromBody] AssignGeneralRequest request,
        CancellationToken ct)
    {
        if (await ValidateAsync(_assignValidator, request, ct) is { } badRequest) return badRequest;

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

            var general = await _db.Generals.FirstOrDefaultAsync(
                g => g.Id == generalId && g.GameWorldId == worldId, ct);
            if (general is null) return NotFound(new { error = "General not found in this world." });

            var province = await _db.Provinces.FirstOrDefaultAsync(
                p => p.Id == request.ProvinceId && p.GameWorldId == worldId, ct);
            if (province is null) return NotFound(new { error = "Province not found in this world." });

            var result = _service.AssignGeneral(player, general, province, world.Status);
            if (!result.IsAccepted) return RejectionToActionResult<GeneralAssigned>(result);

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "General assigned: world={WorldId} general={GeneralId} province={ProvinceId}",
                worldId, general.Id, province.Id);

            return Ok(new GeneralAssigned(general.Id, province.Id));
        }
        finally
        {
            gate.Release();
        }
    }

    private ActionResult<T> RejectionToActionResult<T>(GeneralsResult result)
    {
        var msg = result.RejectionMessage ?? "Action rejected.";
        return result.Rejection switch
        {
            GeneralsRejectionReason.GameEnded                 => Conflict(new { error = msg }),
            GeneralsRejectionReason.InsufficientResources     => Conflict(new { error = msg }),
            GeneralsRejectionReason.AlreadyHasGeneral         => Conflict(new { error = msg }),
            GeneralsRejectionReason.UnknownGeneral            => NotFound(new { error = msg }),
            GeneralsRejectionReason.UnknownProvince           => NotFound(new { error = msg }),
            GeneralsRejectionReason.GeneralNotOwnedByCaller   => Forbid(),
            GeneralsRejectionReason.ProvinceNotOwnedByCaller  => Forbid(),
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

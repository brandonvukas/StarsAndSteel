using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.BackgroundServices;
using StarsAndSteel.Api.Worlds.Dtos;
using StarsAndSteel.Core.Entities;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
using StarsAndSteel.Data.Seeding;
using StarsAndSteel.Game.Worlds;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// World creation, listing, and join endpoints. Authenticated via cookie (the
/// default scheme); SignalR-backed clients pass a JWT separately on the hub
/// connection — see <c>docs/10-SECURITY.md</c>.
/// <para/>
/// World creation goes through a SQL transaction so a partial insert (provinces
/// without their parent world, etc.) can never be observed by the tick service.
/// Join goes through the per-world <see cref="WorldLockRegistry"/> semaphore so
/// it cannot race with an in-flight tick.
/// </summary>
[ApiController]
[Route("api/worlds")]
[Authorize]
public sealed class WorldsController : ControllerBase
{
    private readonly StarsAndSteelDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly WorldFactory _worldFactory;
    private readonly WorldJoinService _joinService;
    private readonly WorldLockRegistry _locks;
    private readonly TimeProvider _clock;
    private readonly IValidator<CreateWorldRequest> _createValidator;
    private readonly IValidator<JoinWorldRequest> _joinValidator;
    private readonly ILogger<WorldsController> _logger;

    public WorldsController(
        StarsAndSteelDbContext db,
        UserManager<User> userManager,
        WorldFactory worldFactory,
        WorldJoinService joinService,
        WorldLockRegistry locks,
        TimeProvider clock,
        IValidator<CreateWorldRequest> createValidator,
        IValidator<JoinWorldRequest> joinValidator,
        ILogger<WorldsController> logger)
    {
        _db = db;
        _userManager = userManager;
        _worldFactory = worldFactory;
        _joinService = joinService;
        _locks = locks;
        _clock = clock;
        _createValidator = createValidator;
        _joinValidator = joinValidator;
        _logger = logger;
    }

    /// <summary>
    /// Lists all worlds (Lobby + Active + Ended). MVP: any authenticated user
    /// can see every world. Filtering / pagination land in Phase 2.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorldSummary>>> List(CancellationToken cancellationToken)
    {
        var rows = await _db.GameWorlds
            .AsNoTracking()
            .Select(w => new WorldSummary(
                w.Id,
                w.Name,
                w.Status.ToString(),
                w.CurrentTick,
                w.TickIntervalSeconds,
                w.MapSeed,
                w.Players.Count,
                w.Provinces.Count,
                w.CreatedAt,
                w.StartedAt))
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    /// <summary>
    /// Creates a new world from <c>shared/map-data.json</c>. The world starts in
    /// Lobby state with no players. The first <c>POST /api/worlds/{id}/join</c>
    /// flips it to Active and seeds the tick clock.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<WorldSummary>> Create(
        [FromBody] CreateWorldRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(BuildModelState(validation.Errors));
        }

        // If the caller didn't provide a seed, pick one and return it so the
        // result is reproducible. Random.Shared.Next() is fine — it doesn't need
        // to be cryptographic, and the per-world LCG is what actually drives the
        // game RNG (seeded from this value below).
        var seed = request.MapSeed ?? Random.Shared.Next();

        var map = MapSeeder.Load();
        var built = _worldFactory.Build(request.Name, seed, map);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.GameWorlds.Add(built.World); // cascades into Provinces via the navigation collection
        _db.ProvinceAdjacencies.AddRange(built.Adjacencies);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "World created: {WorldId} '{Name}' seed={Seed} provinces={ProvinceCount} adjacencies={AdjacencyCount}",
            built.World.Id, built.World.Name, seed,
            built.World.Provinces.Count, built.Adjacencies.Count);

        var summary = new WorldSummary(
            built.World.Id,
            built.World.Name,
            built.World.Status.ToString(),
            built.World.CurrentTick,
            built.World.TickIntervalSeconds,
            built.World.MapSeed,
            PlayerCount: 0,
            ProvinceCount: built.World.Provinces.Count,
            built.World.CreatedAt,
            built.World.StartedAt);

        return CreatedAtAction(nameof(GetById), new { id = built.World.Id }, summary);
    }

    /// <summary>
    /// Single-world detail. Used by the join page and shapes the same as the
    /// list response. The full snapshot endpoint with fog-of-war is separate.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorldSummary>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var summary = await _db.GameWorlds
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new WorldSummary(
                w.Id,
                w.Name,
                w.Status.ToString(),
                w.CurrentTick,
                w.TickIntervalSeconds,
                w.MapSeed,
                w.Players.Count,
                w.Provinces.Count,
                w.CreatedAt,
                w.StartedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return summary is null ? NotFound() : Ok(summary);
    }

    /// <summary>
    /// Adds the calling user as a human player. Assigns a free candidate-capital
    /// province, applies the starter package (resources + buildings + units),
    /// and on the first join flips the world from Lobby to Active.
    /// <para/>
    /// Held under the per-world <see cref="WorldLockRegistry"/> semaphore so
    /// joins serialize against ticks (a tick that started just before the join
    /// won't see the new player; the next one will).
    /// </summary>
    [HttpPost("{id:guid}/join")]
    public async Task<ActionResult<JoinWorldResponse>> Join(
        Guid id,
        [FromBody] JoinWorldRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _joinValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(BuildModelState(validation.Errors));
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var gate = _locks.GetOrCreate(id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Eager-load the graph WorldJoinService mutates: provinces (so it can
            // pick a capital) + players (so it can dedupe by UserId).
            var world = await _db.GameWorlds
                .Include(w => w.Players)
                .Include(w => w.Provinces)
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

            if (world is null)
            {
                return NotFound();
            }

            if (world.Status == GameWorldStatus.Ended)
            {
                return Conflict(new { error = "This world has ended." });
            }

            // Surface the dedupe explicitly so the client gets a clearer error
            // than "no capital available" if the user already joined.
            if (world.Players.Any(p => p.UserId == user.Id))
            {
                return Conflict(new { error = "You have already joined this world." });
            }

            var nowUtc = _clock.GetUtcNow().UtcDateTime;
            var player = _joinService.AddHumanPlayer(
                world,
                userId: user.Id,
                nationName: request.NationName,
                flagPrimaryHex: request.FlagPrimaryHex,
                flagSecondaryHex: request.FlagSecondaryHex,
                nowUtc: nowUtc);

            if (player is null)
            {
                return Conflict(new { error = "No candidate-capital provinces are available in this world." });
            }

            await _db.SaveChangesAsync(cancellationToken);

            var capital = world.Provinces.Single(p => p.OwnerPlayerId == player.Id);

            _logger.LogInformation(
                "Player joined: user={UserId} player={PlayerId} world={WorldId} capital={CapitalProvinceId} ({CapitalName})",
                user.Id, player.Id, world.Id, capital.Id, capital.Name);

            return Ok(new JoinWorldResponse(
                PlayerId: player.Id,
                GameWorldId: world.Id,
                NationName: player.NationName,
                CapitalProvinceId: capital.Id,
                CapitalProvinceName: capital.Name,
                Money: player.Money,
                Oil: player.Oil,
                Steel: player.Steel,
                Electronics: player.Electronics,
                Food: player.Food,
                Manpower: player.Manpower));
        }
        finally
        {
            gate.Release();
        }
    }

    private static ModelStateDictionary BuildModelState(IEnumerable<FluentValidation.Results.ValidationFailure> failures)
    {
        var modelState = new ModelStateDictionary();
        foreach (var failure in failures)
        {
            modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }
        return modelState;
    }
}

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
using StarsAndSteel.Core.Snapshots;
using StarsAndSteel.Data;
using StarsAndSteel.Data.Seeding;
using StarsAndSteel.Game.Snapshots;
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
    private readonly SnapshotService _snapshotService;
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
        SnapshotService snapshotService,
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
        _snapshotService = snapshotService;
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
        // Project to an anonymous shape EF can translate (Status as the enum, not ToString'd
        // inline — EF can't translate Enum.ToString in the SELECT). Map to the wire DTO
        // in memory after the query lands.
        var rows = await _db.GameWorlds
            .AsNoTracking()
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.Status,
                w.CurrentTick,
                w.TickIntervalSeconds,
                w.MapSeed,
                PlayerCount = w.Players.Count,
                ProvinceCount = w.Provinces.Count,
                w.CreatedAt,
                w.StartedAt,
            })
            .ToListAsync(cancellationToken);

        var summaries = rows.Select(w => new WorldSummary(
            w.Id,
            w.Name,
            w.Status.ToString(),
            w.CurrentTick,
            w.TickIntervalSeconds,
            w.MapSeed,
            w.PlayerCount,
            w.ProvinceCount,
            w.CreatedAt,
            w.StartedAt));

        return Ok(summaries);
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
        var aiOpponentCount = request.AiOpponentCount ?? 0;

        var map = MapSeeder.Load();
        var built = _worldFactory.Build(request.Name, seed, map, aiOpponentCount);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.GameWorlds.Add(built.World); // cascades into Provinces via the navigation collection
        _db.ProvinceAdjacencies.AddRange(built.Adjacencies);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "World created: {WorldId} '{Name}' seed={Seed} provinces={ProvinceCount} adjacencies={AdjacencyCount} ai={AiOpponentCount}",
            built.World.Id, built.World.Name, seed,
            built.World.Provinces.Count, built.Adjacencies.Count, aiOpponentCount);

        var summary = new WorldSummary(
            built.World.Id,
            built.World.Name,
            built.World.Status.ToString(),
            built.World.CurrentTick,
            built.World.TickIntervalSeconds,
            built.World.MapSeed,
            PlayerCount: built.World.Players.Count,
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
        var row = await _db.GameWorlds
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.Status,
                w.CurrentTick,
                w.TickIntervalSeconds,
                w.MapSeed,
                PlayerCount = w.Players.Count,
                ProvinceCount = w.Provinces.Count,
                w.CreatedAt,
                w.StartedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return NotFound();

        return Ok(new WorldSummary(
            row.Id,
            row.Name,
            row.Status.ToString(),
            row.CurrentTick,
            row.TickIntervalSeconds,
            row.MapSeed,
            row.PlayerCount,
            row.ProvinceCount,
            row.CreatedAt,
            row.StartedAt));
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

            // PlayerSpawner pre-assigns Guids on Player/Building/Unit so the
            // in-memory graph (Province.OwnerPlayerId, etc.) is wired up before
            // SaveChanges. EF's "Added vs Modified" heuristic for value-generated
            // Guid keys uses default(Guid) as the signal that an entity is new.
            // A pre-assigned Guid attached via a tracked navigation collection
            // therefore comes out as Modified, and EF emits an UPDATE against a
            // row that doesn't exist (DbUpdateConcurrencyException, "0 rows
            // affected"). Force a DetectChanges sweep then flip any newly-grafted
            // entities (Player/Building/Unit) from Modified → Added so EF emits
            // INSERTs.
            _db.ChangeTracker.DetectChanges();
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
            {
                if (entry.State != EntityState.Modified) continue;
                if (entry.Entity is Player or Building or Unit)
                {
                    entry.State = EntityState.Added;
                }
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Defensive logging: surface which entity tripped the concurrency
                // check so we don't have to guess from a generic "0 rows affected"
                // next time. Cheap on the happy path (no allocation unless thrown).
                var details = string.Join(" | ", ex.Entries.Select(e =>
                    $"{e.Entity.GetType().Name} state={e.State} " +
                    $"props=[{string.Join(",", e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name))}]"));
                _logger.LogError(ex, "Join SaveChanges concurrency failure. Conflicting entries: {Details}", details);
                throw;
            }

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

    /// <summary>
    /// Returns the calling user's fog-of-war-filtered view of the world. Used
    /// by the client to hydrate its local store on page load and on SignalR
    /// reconnect (see docs/06-BACKEND-API.md §"How the client uses both").
    /// <para/>
    /// 404 if the world doesn't exist; 403 if the caller hasn't joined it
    /// (we don't leak world contents to non-members). Read-only — no lock.
    /// </summary>
    [HttpGet("{id:guid}/snapshot")]
    public async Task<ActionResult<WorldSnapshot>> Snapshot(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var world = await _db.GameWorlds
            .AsNoTracking()
            .Include(w => w.Players)
            .Include(w => w.Provinces)
                .ThenInclude(p => p.Buildings)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

        if (world is null)
        {
            return NotFound();
        }

        // Resolve calling player. We don't leak snapshots of worlds the user
        // hasn't joined — that's a fog-of-war hole.
        var me = world.Players.FirstOrDefault(p => p.UserId == user.Id);
        if (me is null)
        {
            return Forbid();
        }

        // Adjacencies aren't a navigation collection on GameWorld, so query them
        // through the Provinces FK. Province IDs are unique to this world (the
        // WorldFactory re-stamps them on creation), so this is tight.
        var provinceIds = world.Provinces.Select(p => p.Id).ToHashSet();
        var adjacencies = await _db.ProvinceAdjacencies
            .AsNoTracking()
            .Where(a => provinceIds.Contains(a.ProvinceAId) || provinceIds.Contains(a.ProvinceBId))
            .ToListAsync(cancellationToken);

        // Units aren't on GameWorld either (NoAction cascade — see UnitConfiguration).
        var units = await _db.Units
            .AsNoTracking()
            .Where(u => u.GameWorldId == id)
            .ToListAsync(cancellationToken);

        var snapshot = _snapshotService.Build(world, adjacencies, units, callingPlayerId: me.Id);
        return Ok(snapshot);
    }

    /// <summary>
    /// Returns persisted news headlines for this world with <c>Tick &gt; since</c>,
    /// ordered ascending. Used by the client on SignalR reconnect to backfill any
    /// <c>NewsPublished</c> hub events it missed while disconnected (per
    /// <c>docs/06-BACKEND-API.md</c>). Read-only — no lock.
    /// <para/>
    /// 404 if the world doesn't exist; 403 if the caller isn't a player. Capped at
    /// 200 rows to bound the response — older history is still in the DB but a
    /// reconnect doesn't need it.
    /// </summary>
    [HttpGet("{id:guid}/news")]
    public async Task<ActionResult<IEnumerable<NewsItemDto>>> News(
        Guid id,
        [FromQuery] int since = 0,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        // Cheap existence + membership check without loading the whole world graph.
        var membership = await _db.GameWorlds
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { Exists = true, IsMember = w.Players.Any(p => p.UserId == user.Id) })
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return NotFound();
        }

        if (!membership.IsMember)
        {
            return Forbid();
        }

        var rows = await _db.NewsItems
            .AsNoTracking()
            .Where(n => n.GameWorldId == id && n.Tick > since)
            .OrderBy(n => n.Tick)
            .ThenBy(n => n.Id)
            .Take(200)
            .Select(n => new NewsItemDto(
                n.Id,
                n.Tick,
                n.Headline,
                n.Body,
                n.Severity,
                n.Category,
                n.RelatedPlayerId))
            .ToListAsync(cancellationToken);

        return Ok(rows);
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

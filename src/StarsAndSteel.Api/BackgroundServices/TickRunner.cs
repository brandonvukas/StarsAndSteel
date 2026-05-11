using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Api.Hubs;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
using StarsAndSteel.Game.Diplomacy;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Api.BackgroundServices;

/// <summary>
/// Scoped service that owns the read-process-save sequence for a single world.
/// Split out from <see cref="GameTickService"/> so the BackgroundService stays
/// concerned only with scheduling, and so the runner can be unit-tested with
/// a real DbContext (against Testcontainers) without spinning up the host.
/// </summary>
public sealed class TickRunner
{
    private readonly StarsAndSteelDbContext _db;
    private readonly TickProcessor _processor;
    private readonly TickBroadcaster _broadcaster;
    private readonly TimeProvider _clock;
    private readonly ILogger<TickRunner> _logger;

    public TickRunner(
        StarsAndSteelDbContext db,
        TickProcessor processor,
        TickBroadcaster broadcaster,
        TimeProvider clock,
        ILogger<TickRunner> logger)
    {
        _db = db;
        _processor = processor;
        _broadcaster = broadcaster;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Loads the world (with players, provinces, buildings, units, pending orders, and
    /// adjacencies eager-loaded), runs the tick pipeline against the in-memory graph,
    /// and saves all mutations atomically. Returns the events emitted by the steps; the
    /// caller is responsible for broadcasting them. <c>null</c> means the world wasn't
    /// due / was missing / lost an optimistic-concurrency race.
    /// </summary>
    public async Task<TickResult?> RunAsync(Guid worldId, CancellationToken cancellationToken)
    {
        // Eager-load the entire graph the steps mutate. One query each, no N+1.
        var world = await _db.GameWorlds
            .Include(w => w.Players)
            .Include(w => w.Provinces)
                .ThenInclude(p => p.Buildings)
            .FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);

        if (world is null)
        {
            _logger.LogWarning("Tick requested for unknown world {WorldId}", worldId);
            return null;
        }

        if (world.Status != GameWorldStatus.Active)
        {
            // Not an error — the scheduling query may have raced with an
            // admin pause. Just no-op.
            return null;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        if (world.NextTickDueUtc > now)
        {
            // Belt-and-suspenders: another instance/loop iteration may have
            // already advanced this world. Skip silently.
            return null;
        }

        // Phase 1I: load tick-step inputs that aren't reachable via GameWorld navs.
        var processingTick = world.CurrentTick + 1;

        var units = await _db.Units
            .Where(u => u.GameWorldId == worldId)
            .ToListAsync(cancellationToken);

        var pendingUnitOrders = await _db.UnitOrders
            .Where(o => o.Unit.GameWorldId == worldId
                && o.IssuedAtTick <= processingTick
                && o.Status == OrderStatus.Pending)
            .ToListAsync(cancellationToken);

        var pendingConstructionOrders = await _db.ConstructionOrders
            .Where(o => o.GameWorldId == worldId
                && o.IssuedAtTick <= processingTick
                && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.InProgress))
            .ToListAsync(cancellationToken);

        // Adjacency edges scoped to this world's provinces. We pull every adjacency
        // touching any province in this world; the world's province set is the join key.
        var provinceIds = world.Provinces.Select(p => p.Id).ToHashSet();
        var adjacencies = await _db.ProvinceAdjacencies
            .Where(a => provinceIds.Contains(a.ProvinceAId) || provinceIds.Contains(a.ProvinceBId))
            .ToListAsync(cancellationToken);

        // Phase 2D: load every Pending offer in this world. OfferExpiryStep mutates
        // their Status in place; EF picks them up because they're tracked.
        var pendingTreatyOffers = await _db.TreatyOffers
            .Where(o => o.GameWorldId == worldId
                && o.Status == TreatyOfferStatus.Pending)
            .ToListAsync(cancellationToken);

        // Phase 2E: snapshot diplomatic relations as of tick start. Read-only — gameplay
        // steps consult RelationLookup to gate combat / movement / air strikes against
        // active treaties. AsNoTracking because we never mutate these in the tick.
        var relationRows = await _db.DiplomaticRelations
            .AsNoTracking()
            .Where(r => r.GameWorldId == worldId)
            .ToListAsync(cancellationToken);
        var relations = new RelationLookup(relationRows);

        // Phase 2G: load every per-player research row that's still in progress
        // (IsUnlocked == false). ResearchStep increments ProgressPoints and may
        // flip IsUnlocked; rows are EF-tracked so SaveChanges below picks them up.
        var activeResearch = await _db.ResearchProgress
            .Where(r => r.Player.GameWorldId == worldId && !r.IsUnlocked)
            .ToListAsync(cancellationToken);

        TickResult result;
        try
        {
            result = _processor.ProcessOneTick(world, now,
                units: units,
                pendingUnitOrders: pendingUnitOrders,
                pendingConstructionOrders: pendingConstructionOrders,
                adjacencies: adjacencies,
                pendingTreatyOffers: pendingTreatyOffers,
                relations: relations,
                activeResearch: activeResearch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TickProcessor threw for world {WorldId} at tick {Tick}",
                worldId, world.CurrentTick + 1);
            return null;
        }

        // Apply structural changes the processor queued. Mutations to existing rows
        // (resources, unit Strength, order Status, building lists) are already tracked
        // by EF because the loaded entities are tracked.
        if (result.UnitsToInsert is { Count: > 0 } toInsert)
            _db.Units.AddRange(toInsert);

        if (result.BuildingsToInsert is { Count: > 0 } bToInsert)
            _db.Buildings.AddRange(bToInsert);

        if (result.UnitsToDelete is { Count: > 0 } toDelete)
        {
            // Cascade clean-up: any pending UnitOrders for these units must go too,
            // otherwise the FK to a removed Unit row will trip on save.
            var deadIds = toDelete.Select(u => u.Id).ToHashSet();
            var orphanedOrders = await _db.UnitOrders
                .Where(o => deadIds.Contains(o.UnitId))
                .ToListAsync(cancellationToken);
            if (orphanedOrders.Count > 0) _db.UnitOrders.RemoveRange(orphanedOrders);
            _db.Units.RemoveRange(toDelete);
        }

        if (result.NewsItemsToInsert is { Count: > 0 } newsToInsert)
            _db.NewsItems.AddRange(newsToInsert);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer touched the world between our read and save.
            // Per docs/07, this should never happen given the per-world
            // semaphore, but we treat it as a clean abort just in case.
            _logger.LogWarning(
                "Optimistic concurrency loss saving tick {Tick} for world {WorldId}; skipping",
                result.Tick, worldId);
            return null;
        }

        // Broadcast AFTER save commits — clients must never observe a state
        // the database doesn't also hold. Failures inside the broadcaster are
        // swallowed there per-event, so a flaky subscriber can't undo a tick.
        await _broadcaster.BroadcastAsync(worldId, result, cancellationToken);

        return result;
    }
}

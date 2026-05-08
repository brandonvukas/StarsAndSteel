using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;
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
    private readonly TimeProvider _clock;
    private readonly ILogger<TickRunner> _logger;

    public TickRunner(
        StarsAndSteelDbContext db,
        TickProcessor processor,
        TimeProvider clock,
        ILogger<TickRunner> logger)
    {
        _db = db;
        _processor = processor;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Loads the world (with players, provinces, and buildings eager-loaded),
    /// runs the tick pipeline against the in-memory graph, and saves all
    /// mutations atomically. Returns the events emitted by the steps; the
    /// caller is responsible for broadcasting them. <c>null</c> means the
    /// world wasn't due / was missing / lost an optimistic-concurrency race.
    /// </summary>
    public async Task<TickResult?> RunAsync(Guid worldId, CancellationToken cancellationToken)
    {
        // Eager-load the entire graph the steps mutate. One query, no N+1.
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

        TickResult result;
        try
        {
            result = _processor.ProcessOneTick(world, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TickProcessor threw for world {WorldId} at tick {Tick}",
                worldId, world.CurrentTick + 1);
            return null;
        }

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

        return result;
    }
}

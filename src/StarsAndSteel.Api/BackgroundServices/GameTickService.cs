using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using StarsAndSteel.Core.Enums;
using StarsAndSteel.Data;

namespace StarsAndSteel.Api.BackgroundServices;

/// <summary>
/// Polls every <see cref="PollInterval"/> for worlds whose
/// <c>NextTickDueUtc</c> has passed and processes them in parallel. Per-world
/// re-entrancy is enforced via <see cref="WorldLockRegistry"/>: if a tick
/// somehow takes longer than the interval, the next iteration sees the world
/// as "due" again but skips it because the previous one still holds the lock.
///
/// SignalR broadcast is deliberately deferred to a future commit (Phase 1F+).
/// For now we just log the result so you can watch ticks in the API console.
/// </summary>
public sealed class GameTickService : BackgroundService
{
    /// <summary>
    /// Wake-up cadence. Independent of any world's tick interval — a fast
    /// poll lets us react quickly to newly-created worlds and to worlds with
    /// non-default tick intervals.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Per-tick wall-clock budget for warning logs. docs/07 sets this at
    /// 150ms in steady state; 1000ms is the "something is wrong" threshold.
    /// </summary>
    public static readonly TimeSpan TickBudgetForWarning = TimeSpan.FromMilliseconds(1000);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorldLockRegistry _locks;
    private readonly ILogger<GameTickService> _logger;

    public GameTickService(
        IServiceScopeFactory scopeFactory,
        WorldLockRegistry locks,
        ILogger<GameTickService> logger)
    {
        _scopeFactory = scopeFactory;
        _locks = locks;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GameTickService starting; poll interval {Interval}", PollInterval);

        // Initial delay so the host has time to apply migrations / warm up.
        try
        {
            await Task.Delay(PollInterval, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let the loop die. Log and keep going.
                _logger.LogError(ex, "GameTickService loop iteration failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("GameTickService stopping");
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        Guid[] dueWorldIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
            var nowUtc = DateTime.UtcNow;
            dueWorldIds = await db.GameWorlds
                .Where(w => w.Status == GameWorldStatus.Active && w.NextTickDueUtc <= nowUtc)
                .Select(w => w.Id)
                .ToArrayAsync(cancellationToken);
        }

        if (dueWorldIds.Length == 0)
        {
            return;
        }

        // Worlds tick in parallel — one slow world must not starve others.
        var tasks = dueWorldIds.Select(id => ProcessWorldSafelyAsync(id, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessWorldSafelyAsync(Guid worldId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrCreate(worldId);

        // Non-blocking acquire: if a previous tick is still running for this
        // world, we silently skip and try again next poll.
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<TickRunner>();

            var sw = Stopwatch.StartNew();
            var result = await runner.RunAsync(worldId, cancellationToken);
            sw.Stop();

            if (result is null)
            {
                return;
            }

            if (sw.Elapsed > TickBudgetForWarning)
            {
                _logger.LogWarning(
                    "Tick {Tick} for world {WorldId} took {Ms}ms (warning threshold {Budget}ms)",
                    result.Tick, worldId, sw.ElapsedMilliseconds, TickBudgetForWarning.TotalMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "Tick {Tick} for world {WorldId} completed in {Ms}ms with {EventCount} events",
                    result.Tick, worldId, sw.ElapsedMilliseconds, result.Events.Count);
            }

            // TODO Phase 1F+: broadcast result.Events via the GameHub.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception ticking world {WorldId}", worldId);
        }
        finally
        {
            gate.Release();
        }
    }
}

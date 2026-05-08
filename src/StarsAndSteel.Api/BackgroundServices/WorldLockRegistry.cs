using System.Collections.Concurrent;

namespace StarsAndSteel.Api.BackgroundServices;

/// <summary>
/// Singleton registry of per-world re-entrancy locks. Shared between
/// <see cref="GameTickService"/> (which holds the lock for the duration of a
/// tick) and the order-submission endpoints (which take it briefly to read
/// <c>world.CurrentTick</c> and stamp <c>IssuedAtTick = CurrentTick + 1</c>).
/// See docs/07 §"Concurrency &amp; determinism contract".
///
/// One <see cref="SemaphoreSlim"/> per world id, lazily allocated. We never
/// remove entries — at our scale (≤ a few dozen worlds ever) the memory cost
/// is negligible and removal would race with new acquisitions.
/// </summary>
public sealed class WorldLockRegistry
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public SemaphoreSlim GetOrCreate(Guid worldId) =>
        _locks.GetOrAdd(worldId, _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
}

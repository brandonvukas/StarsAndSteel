using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Core.Entities;

/// <summary>
/// One game / scenario. The <see cref="GameTickService"/> background service ticks every
/// active world independently using <see cref="RngState"/> for deterministic per-world RNG and
/// <see cref="RowVersion"/> as an optimistic-concurrency token. See <c>docs/07-GAME-LOOP.md</c>.
/// </summary>
public class GameWorld
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public GameWorldStatus Status { get; set; }

    /// <summary>Monotonically increasing tick counter; advanced by the tick processor.</summary>
    public int CurrentTick { get; set; }

    /// <summary>Wall-clock seconds between ticks. Default 60 per the design doc.</summary>
    public int TickIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Phase 3a: per-world toggle for nuclear weapons. When false, the OrderService
    /// rejects MissileLaunch orders for nuclear warheads (conventional cruise missiles
    /// are still permitted). Default true so existing worlds get the spicy late-game.
    /// </summary>
    public bool NukesEnabled { get; set; } = true;

    /// <summary>When the tick service should next process this world.</summary>
    public DateTime NextTickDueUtc { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>Initial seed used to derive <see cref="RngState"/> at world start.</summary>
    public int MapSeed { get; set; }

    /// <summary>
    /// Persisted state for the per-world deterministic RNG. Seeded from <see cref="MapSeed"/>
    /// at world start, advanced and re-saved every tick. The contract
    /// <c>state(T+1) = f(state(T), orders, rng(T))</c> requires this be persisted.
    /// </summary>
    public long RngState { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c>. Used by the tick processor as an optimistic-concurrency
    /// token to detect concurrent writes against a world that's mid-tick.
    /// </summary>
    public byte[] RowVersion { get; set; } = default!;

    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<Province> Provinces { get; set; } = new List<Province>();
    public ICollection<NewsItem> NewsItems { get; set; } = new List<NewsItem>();
}

using StarsAndSteel.Core.Entities;
using StarsAndSteel.Game.Tick.Steps;

namespace StarsAndSteel.Game.Tick;

/// <summary>
/// Pure orchestrator over an in-memory world graph. Knows nothing about EF
/// or SQL Server: callers load <see cref="GameWorld"/> with the appropriate
/// eager-loads, hand it to <see cref="ProcessOneTick"/>, then persist.
///
/// Phase 1E only wires <see cref="ResourceProductionStep"/>. Subsequent phases
/// will register the remaining 13 steps in their canonical order (docs/07 §
/// "What happens in a single tick").
/// </summary>
public sealed class TickProcessor
{
    private readonly IReadOnlyList<ITickStep> _steps;

    public TickProcessor(IEnumerable<ITickStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        _steps = steps.ToArray();
        if (_steps.Count == 0)
        {
            throw new ArgumentException("At least one tick step must be registered.", nameof(steps));
        }
    }

    /// <summary>
    /// Default constructor for Phase 1E: only resource production runs.
    /// </summary>
    public TickProcessor() : this(new ITickStep[]
    {
        new ResourceProductionStep(),
    })
    {
    }

    /// <summary>
    /// Computes one tick over the supplied <paramref name="world"/>. On return:
    /// - <c>world.CurrentTick</c> has advanced by 1.
    /// - <c>world.RngState</c> holds the post-tick RNG state.
    /// - <c>world.NextTickDueUtc</c> is set to <c>now + TickIntervalSeconds</c>.
    /// - resource columns on each <see cref="Player"/> have been updated.
    /// - the returned <see cref="TickResult"/> carries the events emitted.
    ///
    /// Persistence and concurrency control (the optimistic <c>RowVersion</c>
    /// check) are the caller's responsibility — see <c>StarsAndSteel.Api/BackgroundServices/TickRunner</c>.
    /// </summary>
    public TickResult ProcessOneTick(GameWorld world, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(world);

        var processingTick = world.CurrentTick + 1;
        var rng = new DeterministicRandom(world.RngState);

        var context = new TickContext(world, processingTick, rng);

        foreach (var step in _steps)
        {
            step.Execute(context);
        }

        // PersistRngState (step 12). Done by writing the current RNG state
        // back to the world; EF picks up the change on SaveChanges.
        world.RngState = rng.State;

        // AdvanceTick (step 14).
        world.CurrentTick = processingTick;
        world.NextTickDueUtc = utcNow.AddSeconds(world.TickIntervalSeconds);

        return new TickResult(processingTick, context.Events.ToArray());
    }
}

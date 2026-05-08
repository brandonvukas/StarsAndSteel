using StarsAndSteel.Core.Entities;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick;

/// <summary>
/// In-memory bag passed through every <see cref="ITickStep"/> in a single tick.
/// Holds the eagerly-loaded world graph (so steps don't hit the DB), the
/// per-world deterministic RNG, the tick number being computed, and the
/// growing list of events the steps emit.
/// </summary>
public sealed class TickContext
{
    public TickContext(GameWorld world, int processingTick, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rng);

        World = world;
        ProcessingTick = processingTick;
        Rng = rng;
        Events = new List<TickEvent>();
    }

    /// <summary>The eagerly-loaded world. Mutations to this graph are what get persisted.</summary>
    public GameWorld World { get; }

    /// <summary>The tick number being computed (i.e. <c>world.CurrentTick + 1</c>).</summary>
    public int ProcessingTick { get; }

    public IRandomSource Rng { get; }

    public IList<TickEvent> Events { get; }
}

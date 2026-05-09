using StarsAndSteel.Core.Entities;
using StarsAndSteel.Game.Tick.Steps;

namespace StarsAndSteel.Game.Tick;

/// <summary>
/// Pure orchestrator over an in-memory world graph. Knows nothing about EF
/// or SQL Server: callers load <see cref="GameWorld"/> with the appropriate
/// eager-loads, hand it to <see cref="ProcessOneTick(GameWorld, DateTime)"/>
/// (or the richer 1I overload), then persist.
///
/// Phase 1I wires Production → Movement → AirStrike → Combat → Construction.
/// Subsequent phases will register the remaining 9 steps in their canonical
/// order (docs/07 §"What happens in a single tick").
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
    /// Default constructor — registers the canonical Phase 1O step list in docs/07 order:
    /// AiTurn → ResourceProduction → LogisticsUpkeep → Attrition → Movement → AirStrike →
    /// Combat → Construction → MoraleRecovery → VictoryCheck → News.
    /// LogisticsUpkeep follows ResourceProduction so freshly-produced income pays this tick's bills.
    /// VictoryCheck runs immediately before News so the victory headline emits the same tick.
    /// Cyber / random-event steps land in later phases.
    /// </summary>
    public TickProcessor() : this(new ITickStep[]
    {
        new AiTurnStep(),
        new ResourceProductionStep(),
        new LogisticsUpkeepStep(),
        new AttritionStep(),
        new MovementStep(),
        new AirStrikeStep(),
        new CombatStep(),
        new ConstructionStep(),
        new MoraleRecoveryStep(),
        new VictoryCheckStep(),
        new NewsStep(),
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
    public TickResult ProcessOneTick(GameWorld world, DateTime utcNow) =>
        ProcessOneTick(world, utcNow,
            units: Array.Empty<Unit>(),
            pendingUnitOrders: Array.Empty<UnitOrder>(),
            pendingConstructionOrders: Array.Empty<ConstructionOrder>(),
            adjacencies: Array.Empty<ProvinceAdjacency>());

    /// <summary>
    /// Phase 1I overload that accepts the additional graphs (units, pending orders,
    /// adjacencies) the new steps need. The caller passes the same in-memory tracked
    /// instances EF Core will SaveChanges later — the steps mutate them in place,
    /// plus they may emit new <see cref="Unit"/> / <see cref="Building"/> rows via
    /// <see cref="TickContext.UnitsToInsert"/> and <see cref="TickContext.BuildingsToInsert"/>
    /// and queue dead stacks via <see cref="TickContext.UnitsToDelete"/>.
    /// </summary>
    public TickResult ProcessOneTick(
        GameWorld world,
        DateTime utcNow,
        IList<Unit> units,
        IList<UnitOrder> pendingUnitOrders,
        IList<ConstructionOrder> pendingConstructionOrders,
        IList<ProvinceAdjacency> adjacencies)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(pendingUnitOrders);
        ArgumentNullException.ThrowIfNull(pendingConstructionOrders);
        ArgumentNullException.ThrowIfNull(adjacencies);

        var processingTick = world.CurrentTick + 1;
        var rng = new DeterministicRandom(world.RngState);

        var context = new TickContext(
            world, processingTick, rng,
            units: new List<Unit>(units),
            pendingUnitOrders: pendingUnitOrders,
            pendingConstructionOrders: pendingConstructionOrders,
            adjacencies: adjacencies);

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

        return new TickResult(
            processingTick,
            context.Events.ToArray(),
            UnitsToInsert: context.UnitsToInsert.ToArray(),
            BuildingsToInsert: context.BuildingsToInsert.ToArray(),
            UnitsToDelete: context.UnitsToDelete.ToArray(),
            NewsItemsToInsert: context.NewsItemsToInsert.ToArray());
    }
}

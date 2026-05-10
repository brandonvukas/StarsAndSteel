using StarsAndSteel.Core.Entities;
using StarsAndSteel.Game.Diplomacy;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick;

/// <summary>
/// In-memory bag passed through every <see cref="ITickStep"/> in a single tick.
/// Holds the eagerly-loaded world graph (so steps don't hit the DB), the
/// per-world deterministic RNG, the tick number being computed, and the
/// growing list of events the steps emit.
/// <para/>
/// The collections beyond <see cref="World"/> exist because <see cref="GameWorld"/>
/// has no navigation properties to <see cref="Unit"/>, <see cref="UnitOrder"/>,
/// <see cref="ConstructionOrder"/>, or <see cref="ProvinceAdjacency"/> (those are
/// keyed off Provinces and Players). The runner loads them in dedicated queries
/// and hands them in here so steps stay pure.
/// </summary>
public sealed class TickContext
{
    public TickContext(
        GameWorld world,
        int processingTick,
        IRandomSource rng,
        IList<Unit> units,
        IList<UnitOrder> pendingUnitOrders,
        IList<ConstructionOrder> pendingConstructionOrders,
        IList<ProvinceAdjacency> adjacencies,
        IList<TreatyOffer>? pendingTreatyOffers = null,
        RelationLookup? relations = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(pendingUnitOrders);
        ArgumentNullException.ThrowIfNull(pendingConstructionOrders);
        ArgumentNullException.ThrowIfNull(adjacencies);

        World = world;
        ProcessingTick = processingTick;
        Rng = rng;
        Units = units;
        PendingUnitOrders = pendingUnitOrders;
        PendingConstructionOrders = pendingConstructionOrders;
        Adjacencies = adjacencies;
        PendingTreatyOffers = pendingTreatyOffers ?? new List<TreatyOffer>();
        Relations = relations ?? RelationLookup.Empty;
        UnitsToInsert = new List<Unit>();
        BuildingsToInsert = new List<Building>();
        UnitsToDelete = new List<Unit>();
        NewsItemsToInsert = new List<NewsItem>();
        Events = new List<TickEvent>();
    }

    /// <summary>
    /// Convenience overload for callers (Phase 1E/1F tests) that don't yet care about
    /// units / orders / adjacencies. Creates empty collections.
    /// </summary>
    public TickContext(GameWorld world, int processingTick, IRandomSource rng)
        : this(world, processingTick, rng,
              units: new List<Unit>(),
              pendingUnitOrders: new List<UnitOrder>(),
              pendingConstructionOrders: new List<ConstructionOrder>(),
              adjacencies: new List<ProvinceAdjacency>())
    {
    }

    /// <summary>The eagerly-loaded world. Mutations to this graph are what get persisted.</summary>
    public GameWorld World { get; }

    /// <summary>The tick number being computed (i.e. <c>world.CurrentTick + 1</c>).</summary>
    public int ProcessingTick { get; }

    public IRandomSource Rng { get; }

    /// <summary>All units in the world (loaded by the runner; tracked by EF).</summary>
    public IList<Unit> Units { get; }

    /// <summary>
    /// All <see cref="UnitOrder"/> rows whose <c>IssuedAtTick &lt;= ProcessingTick</c> and
    /// <c>Status == Pending</c>. Steps mutate <c>Status</c> as they consume them; a step
    /// must never insert here (the API does that under the per-world lock).
    /// </summary>
    public IList<UnitOrder> PendingUnitOrders { get; }

    /// <summary>
    /// All <see cref="ConstructionOrder"/> rows whose <c>IssuedAtTick &lt;= ProcessingTick</c>
    /// and <c>Status</c> is Pending or InProgress. ConstructionStep decrements them.
    /// </summary>
    public IList<ConstructionOrder> PendingConstructionOrders { get; }

    /// <summary>All adjacency edges in the world.</summary>
    public IList<ProvinceAdjacency> Adjacencies { get; }

    /// <summary>
    /// All <see cref="TreatyOffer"/> rows in the world whose <c>Status == Pending</c>. Loaded by
    /// the runner; the offer-expiry tick step mutates <c>Status</c> on the in-place rows so EF
    /// picks them up on SaveChanges. Out-of-tick controllers operate on a separate scoped query.
    /// </summary>
    public IList<TreatyOffer> PendingTreatyOffers { get; }

    /// <summary>
    /// Snapshot of the world's diplomatic relations as of tick start (Phase 2E). Movement,
    /// air strikes, and combat consult this to skip actions targeting players the actor is
    /// not at war with. Empty by default — older tests treat unset pairs as Peace, which
    /// preserves their assumptions about hostility being implicit.
    /// </summary>
    public RelationLookup Relations { get; }

    /// <summary>
    /// Units instantiated by this tick (e.g. ConstructionStep completions). The runner
    /// adds these to the EF context after ProcessOneTick returns.
    /// </summary>
    public IList<Unit> UnitsToInsert { get; }

    /// <summary>
    /// Buildings instantiated by this tick. Runner adds them post-process.
    /// </summary>
    public IList<Building> BuildingsToInsert { get; }

    /// <summary>
    /// Units destroyed by this tick (Strength &lt;= 0 after combat). Runner removes
    /// them from EF post-process.
    /// </summary>
    public IList<Unit> UnitsToDelete { get; }

    /// <summary>
    /// News headlines emitted this tick by <see cref="Steps.NewsStep"/>. Runner inserts
    /// them with the rest of the SaveChanges so a row is never visible without the world
    /// state that produced it.
    /// </summary>
    public IList<NewsItem> NewsItemsToInsert { get; }

    public IList<TickEvent> Events { get; }
}

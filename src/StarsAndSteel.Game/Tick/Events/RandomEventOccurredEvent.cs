using StarsAndSteel.Core.Enums;

namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Phase 4c: a random world event fired during this tick. The
/// <see cref="RandomEventStep"/> rolls per tick and emits exactly one of these
/// when an event triggers; <see cref="Steps.NewsStep"/> turns it into a
/// breaking-news headline.
/// <para/>
/// Effects are already applied to the world graph by the time this event is
/// emitted — the event is purely informational. Concrete subjects
/// (<see cref="ProvinceId"/>, <see cref="AffectedPlayerId"/>) are nullable so
/// global / unattributed events stay representable.
/// </summary>
public sealed record RandomEventOccurredEvent(
    int Tick,
    RandomEventKind Kind,
    Guid? ProvinceId,
    Guid? AffectedPlayerId,
    /// <summary>Free-form integer payload — meaning depends on Kind. E.g. ResourceBoom = bonus %, MarketCrash = $ lost, CivilUnrest = morale lost.</summary>
    long Magnitude
) : TickEvent(Tick);

/// <summary>
/// Phase 4c catalog of random world events. Persisted via the
/// <c>RandomEventOccurredEvent</c> wire payload and the corresponding
/// <c>NewsItem</c> headline; no DB column stores the enum value directly.
/// </summary>
public enum RandomEventKind
{
    /// <summary>Earthquake/hurricane: destroys a single random non-wonder building in a random province.</summary>
    NaturalDisaster = 1,
    /// <summary>Random owned province produces +50% extra of every resource on the *next* ResourceProductionStep (one-shot).</summary>
    ResourceBoom = 2,
    /// <summary>Random player with active research gets +25% progress on their lowest-progress in-flight tech.</summary>
    ScientificBreakthrough = 3,
    /// <summary>Random owned province loses 20 morale (clamped to 0).</summary>
    CivilUnrest = 4,
    /// <summary>Random player loses 10% of their money (min 100, only fires when they have &gt;= 1000).</summary>
    MarketCrash = 5,
}

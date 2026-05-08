namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Emitted by <see cref="Steps.ResourceProductionStep"/> once per player per
/// tick. Carries deltas (not absolute totals) so the SignalR diff is small
/// and the client animates increments.
/// </summary>
public sealed record ResourcesProducedEvent(
    int Tick,
    Guid PlayerId,
    long MoneyDelta,
    long OilDelta,
    long SteelDelta,
    long ElectronicsDelta,
    long FoodDelta,
    long ManpowerDelta) : TickEvent(Tick);

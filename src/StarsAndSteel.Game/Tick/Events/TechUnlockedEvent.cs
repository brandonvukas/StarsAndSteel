namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Emitted by <see cref="Steps.ResearchStep"/> when a player's research effort
/// completes (ProgressPoints reaches the tech's TicksToResearch threshold).
/// </summary>
public sealed record TechUnlockedEvent(
    int Tick,
    Guid PlayerId,
    string PlayerNationName,
    string TechId,
    string TechName
) : TickEvent(Tick);

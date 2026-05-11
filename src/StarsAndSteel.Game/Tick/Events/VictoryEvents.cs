namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Emitted by <see cref="Steps.VictoryCheckStep"/> when a single living player owns
/// at least 80% of the world's provinces (total-domination victory per
/// <c>docs/04 §"Victory conditions"</c>). The step also flips the world to
/// <see cref="Core.Enums.GameWorldStatus.Ended"/>, sets <c>EndedAt</c>, and marks
/// every other player <c>IsAlive = false</c>.
/// </summary>
public sealed record VictoryAchievedEvent(
    int Tick,
    Guid WinnerPlayerId,
    string WinnerNationName,
    int OwnedProvinceCount,
    int TotalProvinceCount
) : TickEvent(Tick);

/// <summary>
/// Emitted by <see cref="Steps.VictoryCheckStep"/> when a player loses their last
/// province. MVP eliminates immediately; the docs/07 §EventStep "3 ticks" grace
/// period lands in a later phase alongside the elimination-delay table.
/// </summary>
public sealed record PlayerEliminatedEvent(
    int Tick,
    Guid PlayerId,
    string NationName
) : TickEvent(Tick);

/// <summary>
/// Emitted by <see cref="Steps.VictoryCheckStep"/> when a coalition of mutually-allied
/// living players collectively owns at least 80% of the world's provinces (Phase 2F
/// coalition-victory rule). All members in <see cref="WinnerPlayerIds"/> share the win
/// and the world flips to <see cref="Core.Enums.GameWorldStatus.Ended"/>; non-coalition
/// living players are eliminated. <see cref="WinnerPlayerIds"/> is sorted ascending by
/// Guid for deterministic news rendering and replay.
/// </summary>
public sealed record CoalitionVictoryAchievedEvent(
    int Tick,
    IReadOnlyList<Guid> WinnerPlayerIds,
    IReadOnlyList<string> WinnerNationNames,
    int OwnedProvinceCount,
    int TotalProvinceCount
) : TickEvent(Tick);

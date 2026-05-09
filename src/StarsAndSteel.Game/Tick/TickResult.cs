using StarsAndSteel.Core.Entities;
using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick;

/// <summary>
/// What <see cref="TickProcessor.ProcessOneTick"/> hands back to the caller.
/// <para/>
/// <see cref="Events"/> is what the SignalR layer broadcasts after the DB save commits.
/// <para/>
/// <see cref="UnitsToInsert"/> / <see cref="BuildingsToInsert"/> / <see cref="UnitsToDelete"/>
/// are the entities the processor created or destroyed during the tick — the runner
/// adds/removes them in the EF context before calling SaveChanges. They default to empty
/// arrays for backward compatibility with Phase 1E/1F call sites.
/// </summary>
public sealed record TickResult(
    int Tick,
    IReadOnlyList<TickEvent> Events,
    IReadOnlyList<Unit>? UnitsToInsert = null,
    IReadOnlyList<Building>? BuildingsToInsert = null,
    IReadOnlyList<Unit>? UnitsToDelete = null);

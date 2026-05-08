using StarsAndSteel.Game.Tick.Events;

namespace StarsAndSteel.Game.Tick;

/// <summary>
/// What <see cref="TickProcessor.ProcessOneTick"/> hands back to the caller.
/// The events list is what the SignalR layer broadcasts after the DB save
/// commits.
/// </summary>
public sealed record TickResult(int Tick, IReadOnlyList<TickEvent> Events);

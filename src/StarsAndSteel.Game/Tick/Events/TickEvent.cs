namespace StarsAndSteel.Game.Tick.Events;

/// <summary>
/// Base type for anything a tick step emits. The Api layer serializes these
/// for SignalR. Kept as a sealed-hierarchy / discriminated union via
/// pattern matching in C#.
/// </summary>
public abstract record TickEvent(int Tick);

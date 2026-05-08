namespace StarsAndSteel.Game.Tick;

/// <summary>
/// One discrete operation in the 14-step tick pipeline (see docs/07).
/// Steps are pure: they read and mutate <see cref="TickContext.World"/>
/// in-memory, append to <see cref="TickContext.Events"/>, and pull any
/// randomness from <see cref="TickContext.Rng"/>. They never touch the DB
/// directly; persistence is the caller's job.
/// </summary>
public interface ITickStep
{
    /// <summary>Human-readable name used in logs and step ordering.</summary>
    string Name { get; }

    void Execute(TickContext context);
}

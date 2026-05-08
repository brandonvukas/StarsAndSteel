namespace StarsAndSteel.Game.Tick;

/// <summary>
/// Abstraction over the per-world deterministic RNG. Keeps the tick steps
/// testable without coupling to <see cref="System.Random"/> or any specific
/// algorithm. Implementations must be reproducible: given the same
/// <see cref="State"/>, the same sequence of calls must produce the same
/// outputs forever.
/// </summary>
public interface IRandomSource
{
    /// <summary>The current persistable state of the generator.</summary>
    long State { get; }

    /// <summary>Returns a non-negative <see cref="int"/> in [0, <paramref name="exclusiveMax"/>).</summary>
    int NextInt(int exclusiveMax);

    /// <summary>Returns a <see cref="double"/> in [0.0, 1.0).</summary>
    double NextDouble();
}

namespace StarsAndSteel.Game.Tick;

/// <summary>
/// 64-bit linear-congruential generator (LCG) using Donald Knuth's MMIX constants.
/// Chosen over <see cref="System.Random"/> for two reasons:
/// 1. The full state is a single <c>long</c>, so it serializes trivially to
///    <c>GameWorld.RngState</c> (column type bigint).
/// 2. The algorithm is fixed across .NET versions; <see cref="System.Random"/>'s
///    output is documented as implementation-defined and changed in .NET 6.
///    Replays must be bit-exact across runtimes — see docs/07 §"Concurrency
///    &amp; determinism contract".
/// Statistical quality is fine for game RNG; it is NOT cryptographically secure
/// and must not be used for anything security-relevant.
/// </summary>
public sealed class DeterministicRandom : IRandomSource
{
    // Knuth's MMIX LCG constants. period = 2^64.
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Increment = 1442695040888963407UL;

    private ulong _state;

    public DeterministicRandom(long seed)
    {
        // Reinterpret the signed seed as unsigned so negative seeds produce a
        // distinct stream from their positive counterparts (instead of being
        // silently mapped to the same bit pattern by an unwrap).
        _state = unchecked((ulong)seed);

        // Avoid a degenerate all-zero state by stepping once if zero. LCGs with
        // these constants don't actually require this (Increment is odd) but
        // it makes the test "different seeds produce different sequences"
        // robust against the specific seed=0 case.
        if (_state == 0)
        {
            Advance();
        }
    }

    public long State => unchecked((long)_state);

    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax), exclusiveMax,
                "exclusiveMax must be positive.");
        }

        // Use the upper 32 bits — those have the strongest statistical properties
        // in an LCG. (The lowest bits cycle quickly.)
        var top32 = (uint)(Advance() >> 32);
        return (int)((ulong)top32 * (ulong)exclusiveMax >> 32);
    }

    public double NextDouble()
    {
        // 53 bits of randomness, mapped to [0, 1).
        var bits = Advance() >> 11;
        return bits * (1.0 / (1UL << 53));
    }

    private ulong Advance()
    {
        _state = unchecked(_state * Multiplier + Increment);
        return _state;
    }
}

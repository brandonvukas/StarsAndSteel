using FluentAssertions;
using StarsAndSteel.Game.Tick;

namespace StarsAndSteel.Tests.Game.Tick;

public class DeterministicRandomTests
{
    [Fact]
    public void Same_seed_produces_identical_sequence()
    {
        var a = new DeterministicRandom(seed: 42L);
        var b = new DeterministicRandom(seed: 42L);

        var aSequence = Enumerable.Range(0, 100).Select(_ => a.NextInt(1_000_000)).ToArray();
        var bSequence = Enumerable.Range(0, 100).Select(_ => b.NextInt(1_000_000)).ToArray();

        aSequence.Should().Equal(bSequence);
    }

    [Fact]
    public void Different_seeds_produce_different_sequences()
    {
        var a = new DeterministicRandom(seed: 1L);
        var b = new DeterministicRandom(seed: 2L);

        var aSequence = Enumerable.Range(0, 50).Select(_ => a.NextInt(int.MaxValue)).ToArray();
        var bSequence = Enumerable.Range(0, 50).Select(_ => b.NextInt(int.MaxValue)).ToArray();

        aSequence.Should().NotEqual(bSequence);
    }

    [Fact]
    public void State_after_N_calls_can_resume_the_sequence()
    {
        // Replay invariant: a tick can stop, persist State, and a fresh
        // generator constructed from that State must continue exactly where
        // the original left off.
        var original = new DeterministicRandom(seed: 12345L);

        var firstHalf = Enumerable.Range(0, 25).Select(_ => original.NextInt(1024)).ToArray();
        var snapshot = original.State;
        var secondHalfFromOriginal = Enumerable.Range(0, 25).Select(_ => original.NextInt(1024)).ToArray();

        var resumed = new DeterministicRandom(seed: snapshot);
        // The seeded constructor advances once when state==0 to avoid degeneracy;
        // for any non-zero snapshot it does not, so the resumed sequence starts
        // exactly with the next value the original would have produced.
        snapshot.Should().NotBe(0L, "the LCG state should be non-zero after 25 advances from seed 12345");
        var secondHalfFromResume = Enumerable.Range(0, 25).Select(_ => resumed.NextInt(1024)).ToArray();

        secondHalfFromResume.Should().Equal(secondHalfFromOriginal);
        firstHalf.Should().NotBeNull(); // sanity
    }

    [Fact]
    public void NextInt_respects_exclusiveMax()
    {
        var rng = new DeterministicRandom(seed: 7L);
        for (var i = 0; i < 10_000; i++)
        {
            var v = rng.NextInt(10);
            v.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(10);
        }
    }

    [Fact]
    public void NextDouble_is_in_unit_interval()
    {
        var rng = new DeterministicRandom(seed: 7L);
        for (var i = 0; i < 10_000; i++)
        {
            var d = rng.NextDouble();
            d.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(1.0);
        }
    }

    [Fact]
    public void NextInt_throws_for_non_positive_max()
    {
        var rng = new DeterministicRandom(seed: 1L);
        Action zero = () => rng.NextInt(0);
        Action negative = () => rng.NextInt(-5);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }
}

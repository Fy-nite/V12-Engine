using System;

namespace V12.Core.Timing
{
    /// <summary>
    /// Seeded, engine-owned random source. SplitMix64 is small, fast, and
    /// stable across platforms, so replaying the same seed and tick order
    /// produces the same sequence.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed = 0ul)
        {
            Reset(seed);
        }

        /// <summary>Current internal state. Capture it to resume a replay exactly.</summary>
        public ulong State => _state;

        public void Reset(ulong seed)
        {
            // Zero is a valid seed, but the additive SplitMix sequence needs a
            // non-zero starting state to avoid a short degenerate opening.
            _state = seed == 0ul ? 0x9E3779B97F4A7C15UL : seed;
        }

        public void RestoreState(ulong state)
        {
            _state = state == 0ul ? 0x9E3779B97F4A7C15UL : state;
        }

        public ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>Uniform value in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform value in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1d / 9007199254740992d);

        public float Range(float minInclusive, float maxInclusive)
            => minInclusive + (maxInclusive - minInclusive) * NextFloat();

        public double Range(double minInclusive, double maxInclusive)
            => minInclusive + (maxInclusive - minInclusive) * NextDouble();
    }
}

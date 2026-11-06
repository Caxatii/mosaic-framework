using System;
using System.Collections.Generic;
using System.Linq;

namespace Mosaic.Core
{
    /// <summary>
    /// Seeded random numbers for game rules. The whole state is one number (<see cref="State"/>), so it can be saved
    /// with the game and restored, and the sequence is the same on every platform and runtime (SplitMix64).
    /// </summary>
    public sealed class GameRandom
    {
        private const double ToUnitDouble = 1.0 / (1UL << 53);
        private const float ToUnitFloat = 1f / (1 << 24);

        public GameRandom(ulong seed) => State = seed;

        public GameRandom(int seed) : this(unchecked((ulong)seed)) { }

        /// <summary>Save this value; a generator created from it continues the same sequence.</summary>
        public ulong State { get; set; }

        public ulong NextULong()
        {
            unchecked
            {
                var z = State += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>A number in [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be positive");

            return (int)((NextUInt() * (ulong)maxExclusive) >> 32);
        }

        /// <summary>A number in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be greater than min");

            return (int)(min + (long)((NextUInt() * (ulong)((long)maxExclusive - min)) >> 32));
        }

        /// <summary>A number in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * ToUnitDouble;

        /// <summary>A number in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * ToUnitFloat;

        /// <summary>A number in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>True with the given probability (0..1).</summary>
        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>
        /// A generator for one named stream of a match: the same seed and name always give the same sequence,
        /// whatever other streams do. Names are hashed with a stable hash, not string.GetHashCode.
        /// </summary>
        public static GameRandom ForStream(int seed, string stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var hash = 14695981039346656037UL;
            foreach (var c in stream)
                unchecked { hash = (hash ^ c) * 1099511628211UL; }

            var mixer = new GameRandom(unchecked((ulong)seed) ^ hash);
            return new GameRandom(mixer.NextULong());
        }
    }

    /// <summary>
    /// The named random streams of one match: one per feature, so adding a roll in loot does not change the AI.
    /// <see cref="Capture"/> and <see cref="Restore"/> save and load all of them together.
    /// </summary>
    public sealed class RandomStreams
    {
        private readonly Dictionary<string, GameRandom> _streams = new Dictionary<string, GameRandom>();

        public RandomStreams(int seed) => Seed = seed;

        public int Seed { get; }

        public GameRandom this[string stream]
        {
            get
            {
                if (!_streams.TryGetValue(stream, out var random))
                    _streams.Add(stream, random = GameRandom.ForStream(Seed, stream));
                return random;
            }
        }

        public RandomStreamState[] Capture() =>
            _streams
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RandomStreamState { Name = pair.Key, State = pair.Value.State })
                .ToArray();

        /// <summary>Continues the saved streams. Streams that were not saved keep their current state.</summary>
        public void Restore(IEnumerable<RandomStreamState> states)
        {
            if (states == null)
                return;

            foreach (var state in states)
                if (state != null && !string.IsNullOrEmpty(state.Name))
                    this[state.Name].State = state.State;
        }
    }

    /// <summary>Save data of one random stream.</summary>
    [Serializable]
    public sealed class RandomStreamState
    {
        public string Name;
        public ulong State;
    }
}

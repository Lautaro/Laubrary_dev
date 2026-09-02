namespace Laubrary.Chunks
{
    /// The one generator every capability draws from once it has a seed.
    ///
    /// Neither <c>UnityEngine.Random</c> nor <c>System.Random</c> is usable here. Unity's shared generator has
    /// no seed of its own that a caller can hold, so two runs of the same recipe cannot be made to agree; and
    /// <c>System.Random</c>'s sequence is an implementation detail of the runtime, so the same seed is not
    /// guaranteed to give the same arrangement across .NET versions or platforms. A preview that must draw
    /// exactly what a burst will do needs a sequence that is reproducible from one integer and identical
    /// everywhere, so the generator is written out here: a mixed seed feeding an xorshift32 step.
    ///
    /// A struct, so a capability can hold one on the stack for the duration of one fire and nothing allocates.
    public struct ChunkRng
    {
        uint _state;

        public ChunkRng(int seed) { _state = Mix(unchecked((uint)seed ^ 0x9E3779B9u)); }

        /// A second, independent sequence off the same seed. Used where one authored seed has to drive several
        /// things that must not move together — the pieces a fracture cuts and the way those pieces fly, say,
        /// so a dial that only changes motion never re-rolls the cut.
        public ChunkRng(int seed, int stream)
        {
            _state = Mix(unchecked((uint)seed * 0x9E3779B9u) ^ Mix(unchecked((uint)stream + 0x85EBCA6Bu)));
        }

        // A finalising avalanche (the murmur3-style mixer) so neighbouring seeds — 1, 2, 3, the values people
        // actually type — start in completely different places instead of producing near-identical sequences.
        static uint Mix(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7FEB352Du;
                x ^= x >> 15; x *= 0x846CA68Bu;
                x ^= x >> 16;
            }
            return x == 0u ? 0x1u : x;   // xorshift is stuck at zero forever, so zero is never a valid state
        }

        public uint NextUInt()
        {
            unchecked
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
            }
            return _state;
        }

        /// A float in [0, 1). Built from the top 24 bits, which are the well-mixed ones.
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Next01();

        /// An int in [0, exclusiveMax). 0 for a non-positive bound, so an empty list needs no guard at the call site.
        public int Next(int exclusiveMax)
            => exclusiveMax <= 1 ? 0 : (int)(NextUInt() % (uint)exclusiveMax);

        /// An int in [min, max], INCLUSIVE — the shape every count/size range in Chunks is authored as.
        public int RangeInclusive(int min, int max)
            => max <= min ? min : min + Next(max - min + 1);

        public bool NextBool() => (NextUInt() & 1u) != 0u;

        /// +1 or -1 — the sign a spin or a jitter is given.
        public float NextSign() => NextBool() ? 1f : -1f;
    }
}

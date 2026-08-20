// PlusNumpyRng — a bit-exact replica of numpy's `np.random.default_rng(int)` stream: SeedSequence (pool 4) →
// PCG64 (XSL-RR 128/64) → `random()` doubles, `random((rows, cols))` fills and `shuffle()` (random_interval with
// masked rejection on buffered 32-bit halves).
//
// Why it exists: the Kiln Energy Explosion agent fixes every piece of its populations and every noise lattice with
// `default_rng(seed)` draws (contract rng.json documents the order). Without the same numbers a port can only match
// the source STATISTICALLY — same distributions, different picture — and the parity harness's field / silhouette
// stages can never pass. With them, a port places the same piece at the same angle with the same size. Seeded only
// from the spec seed (+ layer salt), so the determinism contract holds: no Time, no UnityEngine.Random.
//
// Verified against numpy 1.26 / 2.x: first doubles of default_rng(9083093), a 26-element shuffle, and a (24, 7)
// fill (see PlasmaBloomTests.NumpyRng_MatchesRecordedStream).
using System;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    public sealed class PlusNumpyRng
    {
        // ── PCG64 state: 128-bit state and increment as (hi, lo) ──
        ulong _sHi, _sLo, _iHi, _iLo;
        bool _hasU32; uint _u32;

        const ulong MultHi = 2549297995355413924UL, MultLo = 4865540595714422341UL;   // PCG_DEFAULT_MULTIPLIER_128

        public PlusNumpyRng(uint entropy)
        {
            // SeedSequence(entropy).generate_state(4, uint64) → 8 uint32 words
            var pool = MixEntropy(new[] { entropy });
            var words = GenerateState(pool, 8);
            ulong s0 = words[0] | ((ulong)words[1] << 32), s1 = words[2] | ((ulong)words[3] << 32);
            ulong i0 = words[4] | ((ulong)words[5] << 32), i1 = words[6] | ((ulong)words[7] << 32);
            // pcg64_set_seed: seed = PCG_128BIT_CONSTANT(state[0], state[1]) → high = state[0], low = state[1]
            SetSeq(s0, s1, i0, i1);
        }

        // ── SeedSequence ──
        const uint InitA = 0x43b0d7e5u, MultA = 0x931e8875u, InitB = 0x8b51f9ddu, MultB = 0x58f38dedu;
        const uint MixMultL = 0xca01f9ddu, MixMultR = 0x4973f715u;
        const int PoolSize = 4;

        static uint HashMix(uint value, ref uint hashConst)
        {
            unchecked
            {
                value ^= hashConst;
                hashConst *= MultA;
                value *= hashConst;
                value ^= value >> 16;
                return value;
            }
        }

        static uint Mix(uint x, uint y)
        {
            unchecked
            {
                uint r = MixMultL * x - MixMultR * y;
                r ^= r >> 16;
                return r;
            }
        }

        static uint[] MixEntropy(uint[] entropy)
        {
            var mixer = new uint[PoolSize];
            uint hc = InitA;
            for (int i = 0; i < PoolSize; i++) mixer[i] = HashMix(i < entropy.Length ? entropy[i] : 0u, ref hc);
            for (int src = 0; src < PoolSize; src++)
                for (int dst = 0; dst < PoolSize; dst++)
                    if (src != dst) mixer[dst] = Mix(mixer[dst], HashMix(mixer[src], ref hc));
            for (int src = PoolSize; src < entropy.Length; src++)
                for (int dst = 0; dst < PoolSize; dst++) mixer[dst] = Mix(mixer[dst], HashMix(entropy[src], ref hc));
            return mixer;
        }

        static uint[] GenerateState(uint[] pool, int nWords)
        {
            var state = new uint[nWords];
            uint hc = InitB;
            for (int i = 0; i < nWords; i++)
            {
                unchecked
                {
                    uint v = pool[i % pool.Length];
                    v ^= hc;
                    hc *= MultB;
                    v *= hc;
                    v ^= v >> 16;
                    state[i] = v;
                }
            }
            return state;
        }

        // ── PCG64 ──
        void SetSeq(ulong seedHi, ulong seedLo, ulong incHi, ulong incLo)
        {
            _sHi = 0; _sLo = 0;
            // inc = (initseq << 1) | 1
            _iHi = (incHi << 1) | (incLo >> 63); _iLo = (incLo << 1) | 1UL;
            Step();
            Add128(ref _sHi, ref _sLo, seedHi, seedLo);
            Step();
            _hasU32 = false;
        }

        static void Add128(ref ulong hi, ref ulong lo, ulong bHi, ulong bLo)
        {
            unchecked
            {
                ulong l = lo + bLo;
                hi = hi + bHi + (l < lo ? 1UL : 0UL);
                lo = l;
            }
        }

        /// 64×64 → 128 (hi, lo) without BigMul (not in this runtime profile).
        static void Mul64(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            unchecked
            {
                ulong a0 = (uint)a, a1 = a >> 32, b0 = (uint)b, b1 = b >> 32;
                ulong p00 = a0 * b0, p01 = a0 * b1, p10 = a1 * b0, p11 = a1 * b1;
                ulong mid = (p00 >> 32) + (uint)p01 + (uint)p10;
                lo = (mid << 32) | (uint)p00;
                hi = p11 + (p01 >> 32) + (p10 >> 32) + (mid >> 32);
            }
        }

        void Step()
        {
            unchecked
            {
                // state = state * MULT + inc  (mod 2^128)
                Mul64(_sLo, MultLo, out ulong hi, out ulong lo);
                hi += _sHi * MultLo + _sLo * MultHi;
                _sHi = hi; _sLo = lo;
                Add128(ref _sHi, ref _sLo, _iHi, _iLo);
            }
        }

        public ulong NextU64()
        {
            Step();
            unchecked
            {
                ulong x = _sHi ^ _sLo;
                int rot = (int)(_sHi >> 58);
                return (x >> rot) | (x << ((-rot) & 63));
            }
        }

        public uint NextU32()
        {
            if (_hasU32) { _hasU32 = false; return _u32; }
            ulong n = NextU64();
            _hasU32 = true; _u32 = (uint)(n >> 32);
            return (uint)n;
        }

        /// numpy `Generator.random()`: 53 random bits → [0, 1).
        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

        public float[] Random(int n)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = (float)NextDouble();
            return a;
        }

        /// numpy `Generator.uniform(low, high)`: `low + (high − low) · random()` (the scale is formed first, as numpy does).
        public double Uniform(double low, double high) => low + (high - low) * NextDouble();

        /// numpy `Generator.integers(low, high)` (half-open, int64 dtype) for ranges that fit 32 bits: Lemire's
        /// multiply-shift with rejection on 32-bit halves — `buffered_bounded_lemire_uint32` — so it shares the
        /// buffered high word with the previous 32-bit draw exactly as the C does. `choice([a, b])` is `Integers(0, 2)`.
        public long Integers(long low, long high)
        {
            ulong rng = (ulong)(high - 1 - low);
            if (rng == 0) return low;
            if (rng > 0xFFFFFFFFUL) throw new ArgumentOutOfRangeException(nameof(high), "ranges wider than 32 bits are not replicated");
            if (rng == 0xFFFFFFFFUL) return low + NextU32();
            ulong rngExcl = rng + 1;
            ulong m = NextU32() * rngExcl;
            ulong leftover = m & 0xFFFFFFFFUL;
            if (leftover < rngExcl)
            {
                ulong threshold = (0xFFFFFFFFUL - rng) % rngExcl;
                while (leftover < threshold) { m = NextU32() * rngExcl; leftover = m & 0xFFFFFFFFUL; }
            }
            return low + (long)(m >> 32);
        }

        /// numpy `Generator.choice([-1.0, 1.0])`.
        public double ChoiceSign() => Integers(0, 2) == 0 ? -1.0 : 1.0;

        /// numpy `random_interval(max)`: masked rejection on 32-bit draws while max fits in 32 bits.
        public ulong RandomInterval(ulong max)
        {
            if (max == 0) return 0;
            ulong mask = max;
            mask |= mask >> 1; mask |= mask >> 2; mask |= mask >> 4; mask |= mask >> 8; mask |= mask >> 16; mask |= mask >> 32;
            ulong v;
            if (max <= 0xffffffffUL) { do v = NextU32() & mask; while (v > max); }
            else { do v = NextU64() & mask; while (v > max); }
            return v;
        }

        /// numpy `Generator.shuffle(x)` on a 1-D array: Fisher-Yates from the top with random_interval.
        public void Shuffle(float[] x)
        {
            for (int i = x.Length - 1; i > 0; i--)
            {
                int j = (int)RandomInterval((ulong)i);
                (x[i], x[j]) = (x[j], x[i]);
            }
        }
    }
}

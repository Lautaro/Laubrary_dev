// PlusPyRandom — a bit-exact replica of CPython's `random.Random(int)`: MT19937 seeded with init_by_array over the
// seed's 32-bit chunks, `random()` (53-bit double from two words), `getrandbits`, `_randbelow` rejection sampling
// (randrange / randint / choice), `uniform`, `gauss` (Box-Muller with the cached second value).
//
// Why it exists: the Kiln Energy Explosion "arc burst" agent (agent4) rolls every bolt tree, veil and body profile
// from `random.Random(seed + k)` / `random.Random(seed * m + frame)` streams. With the same numbers the port draws
// the SAME bolts the contract recorded; without them only the statistics could match. Seeded from the spec seed
// (+ layer salt) only, so the determinism contract holds: no Time, no UnityEngine.Random.
//
// Verified against CPython 3.12 (ArcBurstTests.PyRandom_MatchesRecordedStream): first doubles of Random(4308), its
// first gauss values, randint / randrange / choice / uniform on Random(2637739), Random(0), a 40-bit getrandbits
// and a two-word (> 2^32) seed.
using System;

namespace Laubrary.PyrePlus.Forms.Kiln
{
    public sealed class PlusPyRandom
    {
        const int N = 624, M = 397;
        const uint MatrixA = 0x9908b0dfu, UpperMask = 0x80000000u, LowerMask = 0x7fffffffu;
        readonly uint[] _mt = new uint[N];
        int _mti = N + 1;
        double _gaussNext; bool _hasGauss;

        /// `random.Random(seed)` for a non-negative Python int (CPython seeds from abs(seed)).
        public PlusPyRandom(long seed)
        {
            ulong a = (ulong)Math.Abs(seed);
            uint[] key = a > uint.MaxValue ? new[] { (uint)a, (uint)(a >> 32) } : new[] { (uint)a };
            InitByArray(key);
        }

        void InitGenRand(uint s)
        {
            _mt[0] = s;
            for (_mti = 1; _mti < N; _mti++)
                _mt[_mti] = 1812433253u * (_mt[_mti - 1] ^ (_mt[_mti - 1] >> 30)) + (uint)_mti;
        }

        void InitByArray(uint[] key)
        {
            InitGenRand(19650218u);
            int i = 1, j = 0, k = N > key.Length ? N : key.Length;
            for (; k > 0; k--)
            {
                _mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525u)) + key[j] + (uint)j;
                i++; j++;
                if (i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
                if (j >= key.Length) j = 0;
            }
            for (k = N - 1; k > 0; k--)
            {
                _mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941u)) - (uint)i;
                i++;
                if (i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
            }
            _mt[0] = 0x80000000u;
        }

        uint GenRand()
        {
            uint y;
            if (_mti >= N)
            {
                int kk;
                for (kk = 0; kk < N - M; kk++)
                {
                    y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                    _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ ((y & 1u) != 0 ? MatrixA : 0u);
                }
                for (; kk < N - 1; kk++)
                {
                    y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                    _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ ((y & 1u) != 0 ? MatrixA : 0u);
                }
                y = (_mt[N - 1] & UpperMask) | (_mt[0] & LowerMask);
                _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ ((y & 1u) != 0 ? MatrixA : 0u);
                _mti = 0;
            }
            y = _mt[_mti++];
            y ^= y >> 11;
            y ^= (y << 7) & 0x9d2c5680u;
            y ^= (y << 15) & 0xefc60000u;
            y ^= y >> 18;
            return y;
        }

        /// `random()`: a double in [0, 1) with 53 random bits.
        public double Random()
        {
            uint a = GenRand() >> 5, b = GenRand() >> 6;
            return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
        }

        /// `getrandbits(k)` for 0 < k ≤ 64 (the source never asks for more).
        public ulong GetRandBits(int k)
        {
            if (k <= 32) return GenRand() >> (32 - k);
            ulong lo = GenRand();
            uint hi = GenRand();
            k -= 32;
            if (k < 32) hi >>= 32 - k;
            return lo | ((ulong)hi << 32);
        }

        /// `_randbelow(n)`: rejection sampling on bit_length(n) bits.
        public long RandBelow(long n)
        {
            if (n <= 0) return 0;
            int k = 0; for (long v = n; v > 0; v >>= 1) k++;
            long r = (long)GetRandBits(k);
            while (r >= n) r = (long)GetRandBits(k);
            return r;
        }

        /// `randrange(start, stop)`.
        public long RandRange(long start, long stop) => start + RandBelow(stop - start);
        /// `randrange(n)`.
        public long RandRange(long n) => RandBelow(n);
        /// `randint(a, b)` — inclusive both ends.
        public long RandInt(long a, long b) => a + RandBelow(b - a + 1);
        /// `choice(seq)` — the index it would pick.
        public int ChoiceIndex(int count) => (int)RandBelow(count);
        /// `choice([-1.0, 1.0])`, the source's coin.
        public double Sign() => ChoiceIndex(2) == 0 ? -1.0 : 1.0;
        /// `uniform(a, b)`.
        public double Uniform(double a, double b) => a + (b - a) * Random();

        /// `gauss(mu, sigma)` — Box-Muller, the second value cached exactly as CPython does.
        public double Gauss(double mu, double sigma)
        {
            double z;
            if (_hasGauss) { z = _gaussNext; _hasGauss = false; }
            else
            {
                double x2pi = Random() * (2.0 * Math.PI);
                double g2rad = Math.Sqrt(-2.0 * Math.Log(1.0 - Random()));
                z = Math.Cos(x2pi) * g2rad;
                _gaussNext = Math.Sin(x2pi) * g2rad;
                _hasGauss = true;
            }
            return mu + z * sigma;
        }
    }
}

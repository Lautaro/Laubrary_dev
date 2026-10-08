// Hash, seeded random and value noise, bit-exact with the web prototype (JavaScript Math.imul / >>> semantics).
using System;

namespace Laubrary.GoreLab
{
    public static class GoreRng
    {
        /// <summary>Position hash in [0, 1). Same value as the prototype's GL.hash for the same integer arguments.</summary>
        public static double Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 ^ y * 668265263 ^ seed * 1274126177;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
            }
        }

        /// <summary>The prototype's seeded generator (mulberry32). Each call of the returned function gives the next value in [0, 1).</summary>
        public static Func<double> Rng(int seed)
        {
            uint a = unchecked((uint)seed);
            return () =>
            {
                unchecked
                {
                    a += 0x6D2B79F5u;
                    uint t = a;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t ^= t + (t ^ (t >> 7)) * (t | 61u);
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }

        /// <summary>3D value noise in [0, 1), smoothstep-interpolated over a hash lattice.</summary>
        public static double VNoise3(double x, double y, double z, int seed)
        {
            double fxl = Math.Floor(x), fyl = Math.Floor(y), fzl = Math.Floor(z);
            int ix = ToInt32(fxl), iy = ToInt32(fyl), iz = ToInt32(fzl);
            double fx = x - fxl, fy = y - fyl, fz = z - fzl;
            double ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy), uz = fz * fz * (3 - 2 * fz);
            unchecked
            {
                int z0 = 131 * iz, z1 = 131 * (iz + 1);
                double c00 = Lerp(Hash(ix + z0, iy, seed), Hash(ix + 1 + z0, iy, seed), ux);
                double c10 = Lerp(Hash(ix + z0, iy + 1, seed), Hash(ix + 1 + z0, iy + 1, seed), ux);
                double c01 = Lerp(Hash(ix + z1, iy, seed), Hash(ix + 1 + z1, iy, seed), ux);
                double c11 = Lerp(Hash(ix + z1, iy + 1, seed), Hash(ix + 1 + z1, iy + 1, seed), ux);
                return Lerp(Lerp(c00, c10, uy), Lerp(c01, c11, uy), uz);
            }
        }

        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        /// <summary>
        /// JavaScript's ToInt32 (what `v | 0` does): truncate toward zero, then wrap modulo 2^32. NaN and infinities give 0.
        /// Use it wherever ported code feeds a computed double into Hash.
        /// </summary>
        public static int ToInt32(double v)
        {
            if (v >= -2147483648.0 && v <= 2147483647.0) return (int)v;   // also false for NaN
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            double m = Math.Truncate(v) % 4294967296.0;
            if (m < 0) m += 4294967296.0;
            return unchecked((int)(uint)m);
        }
    }

    public static class GoreColour
    {
        /// <summary>Per-channel linear mix of two 0xAABBGGRR colours, each channel truncated to an integer; the result is opaque.</summary>
        public static uint Mix(uint c1, uint c2, double t)
        {
            double r1 = c1 & 255, g1 = (c1 >> 8) & 255, b1 = (c1 >> 16) & 255;
            double r2 = c2 & 255, g2 = (c2 >> 8) & 255, b2 = (c2 >> 16) & 255;
            int r = GoreRng.ToInt32(r1 + (r2 - r1) * t), g = GoreRng.ToInt32(g1 + (g2 - g1) * t), b = GoreRng.ToInt32(b1 + (b2 - b1) * t);
            return unchecked(0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | (uint)r);
        }
    }
}

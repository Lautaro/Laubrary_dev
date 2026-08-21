// PlusFieldOps — the float-plane toolkit every form may use between "accumulate a field" and "shade it".
//
// The Kiln catalogue found ~20 agents that blur / smear / warp / bloom their pre-shade scalar field; none of those
// passes can be a post modifier (post runs on RGBA, after the smooth field is gone), so they live here as pure
// functions over float[W*H] planes. Conventions shared by every function:
//   • planes are row-major, index = y*W + x, row 0 = bottom (the renderer's buffer orientation) — nothing here
//     cares about up/down except the angle helpers, which use the renderer's math angle (CCW from +x, y-up);
//   • out-of-range reads clamp to the edge pixel (a blur never darkens the border);
//   • caller-provided scratch buffers are reused when given (length W*H) and allocated when null — so a form that
//     keeps its scratch across frames does zero allocation in the hot loop;
//   • nothing reads UnityEngine.Random or Time: every noise is a pure hash of (coords, seed).
using System;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public static class PlusFieldOps
    {
        // ── blur ────────────────────────────────────────────────────────────────────────────────────────────

        /// A normalised 1-D Gaussian kernel of `radius` taps each side (2r+1 entries). radius < 0 ⇒ ceil(3σ).
        public static float[] GaussianKernel(float sigma, int radius = -1)
        {
            sigma = Mathf.Max(1e-4f, sigma);
            if (radius < 0) radius = Mathf.CeilToInt(3f * sigma);
            var k = new float[2 * radius + 1];
            float sum = 0f, inv = -0.5f / (sigma * sigma);
            for (int i = -radius; i <= radius; i++) { float w = Mathf.Exp(i * i * inv); k[i + radius] = w; sum += w; }
            for (int i = 0; i < k.Length; i++) k[i] /= sum;
            return k;
        }

        /// Separable Gaussian blur, in place, edge-clamped. `scratch` (W*H) is reused when given.
        public static void GaussianBlur(float[] field, int W, int H, float sigma, float[] scratch = null, int radius = -1)
        {
            if (sigma <= 0f) return;
            var k = GaussianKernel(sigma, radius);
            Convolve1D(field, W, H, k, scratch);
        }

        /// Separable convolution with a symmetric 1-D kernel (length 2r+1), rows then columns, edge-clamped.
        public static void Convolve1D(float[] field, int W, int H, float[] kernel, float[] scratch = null)
        {
            int r = kernel.Length / 2;
            scratch = Ensure(scratch, W * H);
            // rows: field → scratch
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    float acc = 0f;
                    for (int i = -r; i <= r; i++)
                    {
                        int sx = x + i; if (sx < 0) sx = 0; else if (sx >= W) sx = W - 1;
                        acc += field[row + sx] * kernel[i + r];
                    }
                    scratch[row + x] = acc;
                }
            }
            // columns: scratch → field
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    float acc = 0f;
                    for (int i = -r; i <= r; i++)
                    {
                        int sy = y + i; if (sy < 0) sy = 0; else if (sy >= H) sy = H - 1;
                        acc += scratch[sy * W + x] * kernel[i + r];
                    }
                    field[y * W + x] = acc;
                }
        }

        /// Box blur of half-width `radius` (window 2r+1), `passes` times (3 passes ≈ Gaussian), in place, edge-
        /// clamped. Running-window sums: O(W·H) per pass regardless of radius.
        public static void BoxBlur(float[] field, int W, int H, int radius, int passes = 1, float[] scratch = null)
        {
            if (radius <= 0 || passes <= 0) return;
            scratch = Ensure(scratch, W * H);
            float inv = 1f / (2 * radius + 1);
            for (int p = 0; p < passes; p++)
            {
                // rows: field → scratch
                for (int y = 0; y < H; y++)
                {
                    int row = y * W;
                    float sum = 0f;
                    for (int i = -radius; i <= radius; i++) sum += field[row + Clamp(i, W)];
                    for (int x = 0; x < W; x++)
                    {
                        scratch[row + x] = sum * inv;
                        sum += field[row + Clamp(x + radius + 1, W)] - field[row + Clamp(x - radius, W)];
                    }
                }
                // columns: scratch → field
                for (int x = 0; x < W; x++)
                {
                    float sum = 0f;
                    for (int i = -radius; i <= radius; i++) sum += scratch[Clamp(i, H) * W + x];
                    for (int y = 0; y < H; y++)
                    {
                        field[y * W + x] = sum * inv;
                        sum += scratch[Clamp(y + radius + 1, H) * W + x] - scratch[Clamp(y - radius, H) * W + x];
                    }
                }
            }
        }

        // ── smear ───────────────────────────────────────────────────────────────────────────────────────────

        /// N-tap directional smear: dst(p) = Σ_k w_k · src(p − k·step·dir), k = 0..taps−1, weights (1 − k/taps)^falloff,
        /// normalised. `angleDeg` is the direction the smear TRAILS toward (math angle, CCW from +x, y-up);
        /// `lengthPx` the total trail length. src and dst must differ.
        public static void Smear(float[] src, float[] dst, int W, int H, float angleDeg, float lengthPx, int taps, float falloff = 1f)
        {
            taps = Mathf.Max(1, taps);
            float a = angleDeg * Mathf.Deg2Rad, dx = Mathf.Cos(a), dy = Mathf.Sin(a);
            float step = taps > 1 ? lengthPx / (taps - 1) : 0f;
            float wsum = 0f;
            var w = new float[taps];
            for (int k = 0; k < taps; k++) { w[k] = Mathf.Pow(1f - k / (float)taps, Mathf.Max(0f, falloff)); wsum += w[k]; }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float acc = 0f;
                    for (int k = 0; k < taps; k++)
                        acc += w[k] * SampleBilinear(src, W, H, x - dx * step * k, y - dy * step * k);
                    dst[y * W + x] = acc / wsum;
                }
        }

        /// Whole-pixel shift smear along −x (the Kiln "speed smudge"): out(x) = Σ_{k=0..taps} decay^k · field(x + k), the
        /// content displaced toward −x in integer steps with geometric weights, zero-filled past the right edge — so the
        /// leading edge stays crisp and only the trail spreads. `normalise` divides by Σ decay^k (a motion blur: energy
        /// redistributed); off, it accumulates (a light streak brighter than its source). In place; `scratch` ≥ W·H.
        public static void SmearShiftX(float[] field, int W, int H, int taps, float decay, bool normalise = true, float[] scratch = null)
        {
            taps = Mathf.Max(0, taps);
            if (taps == 0) return;
            if (scratch == null || scratch.Length < W * H) scratch = new float[W * H];
            Array.Copy(field, scratch, W * H);
            double tot = 1.0, w = 1.0;
            for (int k = 1; k <= taps; k++) { w *= decay; tot += w; }
            float inv = normalise ? (float)(1.0 / tot) : 1f;
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    double acc = scratch[row + x];
                    double wk = 1.0;
                    int kMax = Math.Min(taps, W - 1 - x);
                    for (int k = 1; k <= kMax; k++) { wk *= decay; acc += wk * scratch[row + x + k]; }
                    field[row + x] = (float)(acc * inv);
                }
            }
        }

        /// Separable binomial [¼ ½ ¼] blur, x then y, ZERO-padded (a pixel at the edge sees empty space beyond it, not
        /// itself), `passes` times — the Kiln "soften" that fuses the seams between summed shapes without moving the
        /// composition. In place; `scratch` ≥ W·H.
        public static void BinomialBlur(float[] field, int W, int H, int passes = 1, float[] scratch = null)
        {
            if (passes <= 0) return;
            if (scratch == null || scratch.Length < W * H) scratch = new float[W * H];
            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < H; y++)
                {
                    int row = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        float l = x > 0 ? field[row + x - 1] : 0f, r = x < W - 1 ? field[row + x + 1] : 0f;
                        scratch[row + x] = 0.25f * (l + r) + 0.5f * field[row + x];
                    }
                }
                for (int y = 0; y < H; y++)
                {
                    int row = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        float u = y > 0 ? scratch[row - W + x] : 0f, d = y < H - 1 ? scratch[row + W + x] : 0f;
                        field[row + x] = 0.25f * (u + d) + 0.5f * scratch[row + x];
                    }
                }
            }
        }

        /// Exponential (IIR) smear along `angleDeg`: out(p) = src(p) + decay · out(p − dir), swept so the predecessor is
        /// always already computed (a bilinear read between the two pixels of the previous column/row along the
        /// dominant axis). Unbounded trail with a geometric tail — the cheap "motion streak" most Kiln agents use.
        /// In place. `decay` in [0,1).
        public static void SmearIIR(float[] field, int W, int H, float angleDeg, float decay)
        {
            decay = Mathf.Clamp(decay, 0f, 0.9999f);
            if (decay <= 0f) return;
            float a = angleDeg * Mathf.Deg2Rad, dx = Mathf.Cos(a), dy = Mathf.Sin(a);
            if (Mathf.Abs(dx) >= Mathf.Abs(dy))
            {
                // sweep along x in the direction of dx; predecessor one column back, shifted by dy/|dx| rows
                int sx = dx >= 0 ? 1 : -1;
                float slope = dy / Mathf.Abs(dx);
                for (int i = 1; i < W; i++)
                {
                    int x = sx > 0 ? i : W - 1 - i;
                    int px = x - sx;
                    for (int y = 0; y < H; y++)
                    {
                        float fy = y - slope;
                        int y0 = Mathf.FloorToInt(fy); float ty = fy - y0;
                        float v0 = field[Clamp(y0, H) * W + px], v1 = field[Clamp(y0 + 1, H) * W + px];
                        field[y * W + x] += decay * (v0 + (v1 - v0) * ty);
                    }
                }
            }
            else
            {
                int sy = dy >= 0 ? 1 : -1;
                float slope = dx / Mathf.Abs(dy);
                for (int i = 1; i < H; i++)
                {
                    int y = sy > 0 ? i : H - 1 - i;
                    int py = y - sy;
                    for (int x = 0; x < W; x++)
                    {
                        float fx = x - slope;
                        int x0 = Mathf.FloorToInt(fx); float tx = fx - x0;
                        float v0 = field[py * W + Clamp(x0, W)], v1 = field[py * W + Clamp(x0 + 1, W)];
                        field[y * W + x] += decay * (v0 + (v1 - v0) * tx);
                    }
                }
            }
        }

        // ── warp / resample ─────────────────────────────────────────────────────────────────────────────────

        /// Bilinear read at pixel coordinates (integer = pixel centre), edge-clamped.
        public static float SampleBilinear(float[] f, int W, int H, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            int xa = Clamp(x0, W), xb = Clamp(x0 + 1, W), ya = Clamp(y0, H), yb = Clamp(y0 + 1, H);
            float v00 = f[ya * W + xa], v10 = f[ya * W + xb], v01 = f[yb * W + xa], v11 = f[yb * W + xb];
            float top = v00 + (v10 - v00) * tx, bot = v01 + (v11 - v01) * tx;
            return top + (bot - top) * ty;
        }

        /// Domain warp by two offset planes: dst(x,y) = src(x + amount·dxPlane, y + amount·dyPlane). src != dst.
        public static void Warp(float[] src, float[] dst, int W, int H, float[] dxPlane, float[] dyPlane, float amount = 1f)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    dst[i] = SampleBilinear(src, W, H, x + amount * dxPlane[i], y + amount * dyPlane[i]);
                }
        }

        /// Domain warp by a callback offset (x, y) → (dx, dy) in pixels. src != dst.
        public static void Warp(float[] src, float[] dst, int W, int H, Func<int, int, Vector2> offset)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var o = offset(x, y);
                    dst[y * W + x] = SampleBilinear(src, W, H, x + o.x, y + o.y);
                }
        }

        // ── combine ─────────────────────────────────────────────────────────────────────────────────────────

        /// Bloom: field += gain · GaussianBlur(field, sigma). `scratchA/B` (W*H) reused when given.
        public static void Bloom(float[] field, int W, int H, float sigma, float gain, float[] scratchA = null, float[] scratchB = null)
        {
            if (gain == 0f || sigma <= 0f) return;
            scratchA = Ensure(scratchA, field.Length);
            Array.Copy(field, scratchA, field.Length);
            GaussianBlur(scratchA, W, H, sigma, scratchB);
            for (int i = 0; i < field.Length; i++) field[i] += gain * scratchA[i];
        }

        /// field *= 1 + amount · (2·fbm − 1): a multiplicative texture that breaks a smooth field into grain.
        /// `cellPx` is the lattice cell size; `ox/oy` scroll the lattice (px) — animate them for drifting grain.
        public static void MultiplyNoise(float[] field, int W, int H, float cellPx, float amount, int seed,
                                         float ox = 0f, float oy = 0f, int octaves = 2, float lacunarity = 2f, float gain = 0.5f)
        {
            if (amount == 0f) return;
            float inv = 1f / Mathf.Max(1e-3f, cellPx);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = Fbm2D((x + ox) * inv, (y + oy) * inv, seed, octaves, lacunarity, gain);
                    field[y * W + x] *= 1f + amount * (2f * n - 1f);
                }
        }

        public static void MaxInto(float[] dst, float[] src) { for (int i = 0; i < dst.Length; i++) if (src[i] > dst[i]) dst[i] = src[i]; }
        public static void AddInto(float[] dst, float[] src, float gain = 1f) { for (int i = 0; i < dst.Length; i++) dst[i] += src[i] * gain; }
        public static void Lerp(float[] dst, float[] a, float[] b, float t) { for (int i = 0; i < dst.Length; i++) dst[i] = a[i] + (b[i] - a[i]) * t; }
        public static void Scale(float[] dst, float k) { for (int i = 0; i < dst.Length; i++) dst[i] *= k; }
        public static void Clear(float[] dst) => Array.Clear(dst, 0, dst.Length);
        public static float Sum(float[] f) { double s = 0; for (int i = 0; i < f.Length; i++) s += f[i]; return (float)s; }

        // ── statistics ──────────────────────────────────────────────────────────────────────────────────────

        /// Field statistics over the LIT pixels (value > lo) — the numbers Kiln's `stats.json` records per frame.
        /// Quantiles are over lit pixels (over the whole plane when nothing is lit); peak is the plane max.
        public struct Stats
        {
            public float peak, mean, q50, q90, q99;
            public int litPx;
            public override string ToString() => $"peak {peak:0.###} q50 {q50:0.###} q90 {q90:0.###} q99 {q99:0.###} lit {litPx}";
        }

        /// Compute Stats. `scratch` (length ≥ plane.Length) is the sort buffer, reused when given.
        public static Stats ComputeStats(float[] plane, float lo, float[] scratch = null)
        {
            scratch = Ensure(scratch, plane.Length);
            int n = 0; float peak = float.NegativeInfinity; double sum = 0;
            for (int i = 0; i < plane.Length; i++)
            {
                float v = plane[i];
                if (v > peak) peak = v;
                if (v > lo) { scratch[n++] = v; sum += v; }
            }
            var s = new Stats { litPx = n, peak = plane.Length > 0 ? peak : 0f };
            if (n == 0) { Array.Copy(plane, scratch, plane.Length); n = plane.Length; sum = 0; for (int i = 0; i < n; i++) sum += plane[i]; }
            if (n == 0) return s;
            Array.Sort(scratch, 0, n);
            s.mean = (float)(sum / n);
            s.q50 = Quantile(scratch, n, 0.50f);
            s.q90 = Quantile(scratch, n, 0.90f);
            s.q99 = Quantile(scratch, n, 0.99f);
            return s;
        }

        /// Linear-interpolated quantile of the first n entries of an ASCENDING-sorted buffer (numpy default).
        public static float Quantile(float[] sorted, int n, float q)
        {
            if (n <= 0) return 0f;
            float pos = Mathf.Clamp01(q) * (n - 1);
            int i0 = (int)pos; int i1 = Mathf.Min(n - 1, i0 + 1);
            return sorted[i0] + (sorted[i1] - sorted[i0]) * (pos - i0);
        }

        // ── noise ───────────────────────────────────────────────────────────────────────────────────────────
        // Value noise on an integer lattice with a quintic fade (Perlin's 6t⁵−15t⁴+10t³, C² continuous), 0..1.
        // `period` > 0 wraps the lattice so the field tiles every `period` lattice cells (for a loop: sample
        // x/cell with period = canvasPx/cell, or time with period = the loop length). Seeded by hashing, never
        // by RNG state, so the same coordinates always give the same value.

        public static float ValueNoise2D(float x, float y, int seed, int period = 0)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = Fade(x - x0), ty = Fade(y - y0);
            int x1 = x0 + 1, y1 = y0 + 1;
            if (period > 0) { x0 = Wrap(x0, period); x1 = Wrap(x1, period); y0 = Wrap(y0, period); y1 = Wrap(y1, period); }
            float h00 = Hash01(seed, x0, y0, 0), h10 = Hash01(seed, x1, y0, 0);
            float h01 = Hash01(seed, x0, y1, 0), h11 = Hash01(seed, x1, y1, 0);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), ty);
        }

        public static float ValueNoise3D(float x, float y, float z, int seed, int period = 0)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
            float tx = Fade(x - x0), ty = Fade(y - y0), tz = Fade(z - z0);
            int x1 = x0 + 1, y1 = y0 + 1, z1 = z0 + 1;
            if (period > 0)
            {
                x0 = Wrap(x0, period); x1 = Wrap(x1, period); y0 = Wrap(y0, period);
                y1 = Wrap(y1, period); z0 = Wrap(z0, period); z1 = Wrap(z1, period);
            }
            float a = Mathf.Lerp(Mathf.Lerp(Hash01(seed, x0, y0, z0), Hash01(seed, x1, y0, z0), tx),
                                 Mathf.Lerp(Hash01(seed, x0, y1, z0), Hash01(seed, x1, y1, z0), tx), ty);
            float b = Mathf.Lerp(Mathf.Lerp(Hash01(seed, x0, y0, z1), Hash01(seed, x1, y0, z1), tx),
                                 Mathf.Lerp(Hash01(seed, x0, y1, z1), Hash01(seed, x1, y1, z1), tx), ty);
            return Mathf.Lerp(a, b, tz);
        }

        /// Fractal Brownian motion over ValueNoise2D, normalised back to 0..1. With `period` > 0 each octave wraps at
        /// period·lacunarity^k (rounded) — tiles exactly when those are integers (lacunarity 2 always is).
        public static float Fbm2D(float x, float y, int seed, int octaves, float lacunarity = 2f, float gain = 0.5f, int period = 0)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int o = 0; o < Mathf.Max(1, octaves); o++)
            {
                int p = period > 0 ? Mathf.Max(1, Mathf.RoundToInt(period * freq)) : 0;
                sum += amp * ValueNoise2D(x * freq, y * freq, seed + o * 1013, p);
                norm += amp; amp *= gain; freq *= lacunarity;
            }
            return sum / norm;
        }

        public static float Fbm3D(float x, float y, float z, int seed, int octaves, float lacunarity = 2f, float gain = 0.5f, int period = 0)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int o = 0; o < Mathf.Max(1, octaves); o++)
            {
                int p = period > 0 ? Mathf.Max(1, Mathf.RoundToInt(period * freq)) : 0;
                sum += amp * ValueNoise3D(x * freq, y * freq, z * freq, seed + o * 1013, p);
                norm += amp; amp *= gain; freq *= lacunarity;
            }
            return sum / norm;
        }

        /// Curl of the 2-D fbm scalar field — a divergence-free vector (∂N/∂y, −∂N/∂x) by central differences, the
        /// classic "curl noise" advection direction. Units: noise per lattice cell; scale to px yourself.
        public static Vector2 Curl2D(float x, float y, int seed, int octaves, float lacunarity = 2f, float gain = 0.5f, int period = 0, float eps = 0.01f)
        {
            float nx1 = Fbm2D(x + eps, y, seed, octaves, lacunarity, gain, period), nx0 = Fbm2D(x - eps, y, seed, octaves, lacunarity, gain, period);
            float ny1 = Fbm2D(x, y + eps, seed, octaves, lacunarity, gain, period), ny0 = Fbm2D(x, y - eps, seed, octaves, lacunarity, gain, period);
            float dNdx = (nx1 - nx0) / (2f * eps), dNdy = (ny1 - ny0) / (2f * eps);
            return new Vector2(dNdy, -dNdx);
        }

        // ── classic gradient (Perlin) noise, periodic per axis ──

        /// Classic 3-D Perlin GRADIENT noise (12 edge gradients, quintic fade), −1..1, periodic with INTEGER periods
        /// (px, py, pz) per axis: the wrap is applied to the lattice INDEX before hashing, so the gradients on either
        /// side of the seam agree (wrapping the coordinate instead leaves a one-lattice-cell discontinuity that reads
        /// as a flicker once per loop). `perm` is a permutation of 0..255 DOUBLED to 512 entries — the Kiln flame
        /// agents build it with numpy's `default_rng(seed).permutation(256)` (PlusNumpyRng reproduces it) — and the
        /// hash is perm[(perm[(perm[ix] + iy) & 255] + iz) & 255] % 12. ix must stay below 512, i.e. px ≤ 512.
        /// Seeded only by the table, so the same coordinates always give the same value.
        public static double GradientNoise3Periodic(double x, double y, double z, int px, int py, int pz, int[] perm)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            long xi = (long)fx, yi = (long)fy, zi = (long)fz;
            double xf = x - fx, yf = y - fy, zf = z - fz;
            int x0 = WrapL(xi, px), x1 = WrapL(xi + 1, px);
            int y0 = WrapL(yi, py), y1 = WrapL(yi + 1, py);
            int z0 = WrapL(zi, pz), z1 = WrapL(zi + 1, pz);
            double u = FadeD(xf), v = FadeD(yf), w = FadeD(zf);
            double xf1 = xf - 1.0, yf1 = yf - 1.0, zf1 = zf - 1.0;
            double n000 = GradDot(perm, x0, y0, z0, xf, yf, zf), n100 = GradDot(perm, x1, y0, z0, xf1, yf, zf);
            double n010 = GradDot(perm, x0, y1, z0, xf, yf1, zf), n110 = GradDot(perm, x1, y1, z0, xf1, yf1, zf);
            double n001 = GradDot(perm, x0, y0, z1, xf, yf, zf1), n101 = GradDot(perm, x1, y0, z1, xf1, yf, zf1);
            double n011 = GradDot(perm, x0, y1, z1, xf, yf1, zf1), n111 = GradDot(perm, x1, y1, z1, xf1, yf1, zf1);
            double a = n000 + u * (n100 - n000), b = n010 + u * (n110 - n010);
            double c = n001 + u * (n101 - n001), d = n011 + u * (n111 - n011);
            double ab = a + v * (b - a);
            return ab + w * ((c + v * (d - c)) - ab);
        }

        static readonly sbyte[] Grad3 =
        {
            1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0,
            1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
            0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1,
        };

        static double GradDot(int[] perm, int ix, int iy, int iz, double dx, double dy, double dz)
        {
            int h = perm[(perm[(perm[ix & 511] + iy) & 255] + iz) & 255] % 12;
            int g = h * 3;
            return Grad3[g] * dx + Grad3[g + 1] * dy + Grad3[g + 2] * dz;
        }

        static double FadeD(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        static int WrapL(long i, int p) { long m = i % p; return (int)(m < 0 ? m + p : m); }

        /// FNV-1a over (seed, a, b, c) → 0..1. The one hash behind every noise here.
        public static float Hash01(int seed, int a, int b, int c)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)seed) * 16777619u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;   // final avalanche so neighbouring lattice points decorrelate
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static int Wrap(int i, int p) { i %= p; return i < 0 ? i + p : i; }
        static int Clamp(int i, int n) => i < 0 ? 0 : (i >= n ? n - 1 : i);
        static float[] Ensure(float[] buf, int len) => buf != null && buf.Length >= len ? buf : new float[len];
    }
}

// SpriteFxAuxMap.cs
// Deterministic, pure per-pixel "auxiliary map" (heatmap) generation, ported 1:1 from a proof-of-concept HTML
// lab (pixel_fx_aux_map_lab.html) that demonstrated generating grayscale metadata from a sprite's own pixel
// content and using it to modulate an effect's strength per pixel. Every function here is plain float/array
// math over a Color32[] buffer — no UnityEngine.Random / Mathf.PerlinNoise anywhere — so preview, bake and
// runtime playback are always byte-identical for the same inputs (Laubrary's determinism rule). Formulas and
// constants match the source lab exactly; see SpriteFxAuxMapGate for the gate that drives this — the map
// never recolours a pixel itself, it only gauges how strongly the modifier list it's attached to applies.
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// Which content-derived signal the map is built from.
    /// APPEND-ONLY: serialized as an int on every authored SpriteFxAuxMapGate, so an existing gate's generator
    /// must never change index.
    public enum AuxMapGenerator
    {
        Luma,             // brightness (0.299R+0.587G+0.114B)
        Dark,             // 1 - luma
        Saturation,       // HSV saturation
        Alpha,            // straight alpha
        HueProximity,     // how close each pixel's hue is to a chosen centre hue
        Edge,             // Sobel gradient magnitude + alpha-silhouette edge
        Detail,           // high-pass: |luma - 3x3 neighbour average| * 4 (painted seams/highlights, not geometry)
        EdgeDistanceIn,   // chamfer distance from the nearest edge seed — deeper interior reads stronger
        EdgeDistanceOut,  // same distance field, inverted — near edges read stronger, fading inward
        Interior          // solid areas well away from any edge (distance minus 1, re-normalized)
    }

    public static class SpriteFxAuxMap
    {
        /// Builds the shaped 0..1 auxiliary map for `generator` over the whole `w`x`h` picture in `px`
        /// (row-major, y*w+x). Mirrors the POC lab's buildMap() exactly: generate → gain/bias → power →
        /// threshold → blur → invert → respect-alpha, in that order. Returns a new float[w*h]; never null
        /// (an invalid/mismatched buffer returns an all-zero map of the requested size).
        public static float[] BuildMap(Color32[] px, int w, int h, AuxMapGenerator generator,
            float edgeThreshold, float distanceRadius, float hueCenter, float hueWidth,
            float gain, float bias, float power, float threshold, int blurPasses,
            bool invertMap, bool respectAlpha)
        {
            int n = Mathf.Max(0, w) * Mathf.Max(0, h);
            if (px == null || w <= 0 || h <= 0 || px.Length < n) return new float[Mathf.Max(0, n)];

            var luma = new float[n];
            var alpha = new float[n];
            for (int i = 0; i < n; i++)
            {
                Color32 c = px[i];
                luma[i] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                alpha[i] = c.a / 255f;
            }

            // Hue and saturation cost an HSV conversion per pixel plus two more whole-picture float arrays, and
            // exactly two of the ten generators read them. Built on demand so every other generator — and this is
            // called per frame by the whole package, not just by the gate — pays nothing for them. Same values,
            // same formula, just not computed when nothing looks at them.
            float[] sat = null, hue = null;
            if (generator == AuxMapGenerator.Saturation || generator == AuxMapGenerator.HueProximity)
            {
                sat = new float[n];
                hue = new float[n];
                for (int i = 0; i < n; i++)
                {
                    Color32 c = px[i];
                    RgbToHsv(c.r, c.g, c.b, out float hh, out float ss);
                    hue[i] = hh; sat[i] = ss;
                }
            }

            float[] map = new float[n];
            switch (generator)
            {
                case AuxMapGenerator.Luma:
                    System.Array.Copy(luma, map, n);
                    break;
                case AuxMapGenerator.Dark:
                    for (int i = 0; i < n; i++) map[i] = 1f - luma[i];
                    break;
                case AuxMapGenerator.Saturation:
                    System.Array.Copy(sat, map, n);
                    break;
                case AuxMapGenerator.Alpha:
                    System.Array.Copy(alpha, map, n);
                    break;
                case AuxMapGenerator.HueProximity:
                {
                    float width = Mathf.Max(0.0001f, hueWidth);
                    for (int i = 0; i < n; i++)
                    {
                        float d = Mathf.Abs(hue[i] - hueCenter);
                        d = Mathf.Min(d, 360f - d);
                        map[i] = Mathf.Clamp01(1f - d / width);
                    }
                    break;
                }
                default:
                {
                    float[] edge = Sobel(luma, alpha, w, h);
                    if (generator == AuxMapGenerator.Edge)
                    {
                        map = edge;
                    }
                    else if (generator == AuxMapGenerator.Detail)
                    {
                        map = HighPass(luma, alpha, w, h);
                    }
                    else
                    {
                        bool[] seeds = MakeEdgeSeeds(edge, alpha, w, h, edgeThreshold);
                        float[] dist = DistanceFromSeeds(seeds, alpha, w, h);
                        float rad = Mathf.Max(0.0001f, distanceRadius);
                        for (int i = 0; i < n; i++)
                        {
                            if (alpha[i] == 0f) { map[i] = 0f; continue; }
                            float d = Mathf.Clamp01(dist[i] / rad);
                            if (generator == AuxMapGenerator.EdgeDistanceIn) map[i] = d;
                            else if (generator == AuxMapGenerator.EdgeDistanceOut) map[i] = 1f - d;
                            else if (generator == AuxMapGenerator.Interior)
                                map[i] = Mathf.Clamp01((dist[i] - 1f) / Mathf.Max(1f, rad - 1f));
                        }
                    }
                    break;
                }
            }

            // Shape: gain + bias, then power (contrast), then threshold (with rescale). Clamped every step,
            // matching the lab's buildMap() exactly (including its own edge case: threshold == 1 divides by
            // zero, same as the source's `(v-th)/(1-th)` — never reachable at the slider's max via normal use).
            for (int i = 0; i < n; i++)
            {
                float v = Mathf.Clamp01(map[i] * gain + bias);
                v = Mathf.Pow(v, power);
                if (threshold > 0f) v = v < threshold ? 0f : (v - threshold) / (1f - threshold);
                map[i] = Mathf.Clamp01(v);
            }

            if (blurPasses > 0) map = BoxBlur(map, w, h, blurPasses);

            for (int i = 0; i < n; i++)
            {
                float v = invertMap ? 1f - map[i] : map[i];
                if (respectAlpha) v *= alpha[i];
                map[i] = Mathf.Clamp01(v);
            }

            return map;
        }

        // rgb in 0..255; outputs hue in degrees (0..360) and saturation (0..1). Value/brightness isn't needed
        // by any generator here (Luma is computed separately with its own perceptual weights), so it's dropped.
        static void RgbToHsv(byte r8, byte g8, byte b8, out float h, out float s)
        {
            float r = r8 / 255f, g = g8 / 255f, b = b8 / 255f;
            float mx = Mathf.Max(r, Mathf.Max(g, b));
            float mn = Mathf.Min(r, Mathf.Min(g, b));
            float d = mx - mn;
            float hh = 0f;
            if (d > 0f)
            {
                if (mx == r) hh = 60f * (((g - b) / d) % 6f);
                else if (mx == g) hh = 60f * (((b - r) / d) + 2f);
                else hh = 60f * (((r - g) / d) + 4f);
            }
            if (hh < 0f) hh += 360f;
            h = hh;
            s = mx == 0f ? 0f : d / mx;
        }

        // Sobel gradient magnitude over luma, combined with an alpha-silhouette edge term (max of the two) so a
        // flat-coloured sprite still gets a clean outline from its own alpha edge, not just painted contrast.
        static float[] Sobel(float[] luma, float[] alpha, int w, int h)
        {
            var outArr = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (alpha[i] == 0f) { outArr[i] = 0f; continue; }

                    float gx = -At(luma, w, h, x - 1, y - 1) + At(luma, w, h, x + 1, y - 1)
                              - 2f * At(luma, w, h, x - 1, y) + 2f * At(luma, w, h, x + 1, y)
                              - At(luma, w, h, x - 1, y + 1) + At(luma, w, h, x + 1, y + 1);
                    float gy = -At(luma, w, h, x - 1, y - 1) - 2f * At(luma, w, h, x, y - 1) - At(luma, w, h, x + 1, y - 1)
                              + At(luma, w, h, x - 1, y + 1) + 2f * At(luma, w, h, x, y + 1) + At(luma, w, h, x + 1, y + 1);
                    float v = Mathf.Sqrt(gx * gx + gy * gy) / 4f;

                    float a = alpha[i];
                    float n0 = alpha[Mathf.Max(0, y - 1) * w + x];
                    float n1 = alpha[Mathf.Min(h - 1, y + 1) * w + x];
                    float n2 = alpha[y * w + Mathf.Max(0, x - 1)];
                    float n3 = alpha[y * w + Mathf.Min(w - 1, x + 1)];
                    float ae = 0f;
                    ae = Mathf.Max(ae, Mathf.Abs(a - n0));
                    ae = Mathf.Max(ae, Mathf.Abs(a - n1));
                    ae = Mathf.Max(ae, Mathf.Abs(a - n2));
                    ae = Mathf.Max(ae, Mathf.Abs(a - n3));

                    outArr[i] = Mathf.Clamp01(Mathf.Max(v, ae));
                }
            }
            return outArr;
        }

        static float At(float[] luma, int w, int h, int x, int y)
            => luma[Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1)];

        // High-pass / local-contrast detector: |luma - 3x3 neighbour average| * 4. Deliberately not a geometric
        // line detector — for pixel art this finds painted seams, panel lines and small internal highlights
        // more usefully than a Hough-style line fit would.
        static float[] HighPass(float[] luma, float[] alpha, int w, int h)
        {
            var outArr = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (alpha[i] == 0f) continue;   // leaves 0, matching the source's untouched-default behaviour
                    float sum = 0f; int c = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int xx = Mathf.Clamp(x + ox, 0, w - 1);
                            int yy = Mathf.Clamp(y + oy, 0, h - 1);
                            int j = yy * w + xx;
                            if (alpha[j] > 0f) { sum += luma[j]; c++; }
                        }
                    }
                    float avg = c > 0 ? sum / c : luma[i];
                    outArr[i] = Mathf.Clamp01(Mathf.Abs(luma[i] - avg) * 4f);
                }
            }
            return outArr;
        }

        // Seed pixels for the chamfer distance transform: any pixel whose edge value clears `thr`, PLUS every
        // opaque pixel that borders a transparent one or the buffer edge — so even a perfectly flat-coloured
        // sprite (zero Sobel response) still seeds its own silhouette outline.
        static bool[] MakeEdgeSeeds(float[] edge, float[] alpha, int w, int h, float thr)
        {
            var seeds = new bool[w * h];
            for (int i = 0; i < seeds.Length; i++) if (alpha[i] > 0f && edge[i] >= thr) seeds[i] = true;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (alpha[i] <= 0f) continue;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) { seeds[i] = true; continue; }
                    if (alpha[i - 1] == 0f || alpha[i + 1] == 0f || alpha[i - w] == 0f || alpha[i + w] == 0f)
                        seeds[i] = true;
                }
            }
            return seeds;
        }

        // Two-pass chamfer distance transform (forward + backward raster sweep, 1/√2 weights) from the seed
        // set, restricted to opaque pixels — the standard cheap approximation to a true Euclidean distance
        // field, exactly as the source lab implements it.
        static float[] DistanceFromSeeds(bool[] seeds, float[] alpha, int w, int h)
        {
            const float inf = 1e9f;
            const float diag = 1.41421356f;
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = seeds[i] ? 0f : inf;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (alpha[i] == 0f) continue;
                    float v = d[i];
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + 1f);
                    if (y > 0) v = Mathf.Min(v, d[i - w] + 1f);
                    if (x > 0 && y > 0) v = Mathf.Min(v, d[i - w - 1] + diag);
                    if (x < w - 1 && y > 0) v = Mathf.Min(v, d[i - w + 1] + diag);
                    d[i] = v;
                }
            }
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x;
                    if (alpha[i] == 0f) continue;
                    float v = d[i];
                    if (x < w - 1) v = Mathf.Min(v, d[i + 1] + 1f);
                    if (y < h - 1) v = Mathf.Min(v, d[i + w] + 1f);
                    if (x < w - 1 && y < h - 1) v = Mathf.Min(v, d[i + w + 1] + diag);
                    if (x > 0 && y < h - 1) v = Mathf.Min(v, d[i + w - 1] + diag);
                    d[i] = v;
                }
            }
            return d;
        }

        // N passes of a plain 3x3 box blur (edge-clamped, variable sample count at the border like the source).
        static float[] BoxBlur(float[] arr, int w, int h, int passes)
        {
            float[] a = arr;
            for (int pass = 0; pass < passes; pass++)
            {
                var b = new float[a.Length];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float s = 0f; int c = 0;
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int xx = x + ox, yy = y + oy;
                                if (xx >= 0 && xx < w && yy >= 0 && yy < h) { s += a[yy * w + xx]; c++; }
                            }
                        }
                        b[y * w + x] = s / c;
                    }
                }
                a = b;
            }
            return a;
        }
    }
}

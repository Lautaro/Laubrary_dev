using System;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// The ONE deterministic, pure, runtime-safe renderer shared by the editor preview, the asset baker and the
    /// runtime player — so preview == bake == runtime. Never references UnityEditor. Every per-shape random value
    /// derives from (seed, layerIndex, waveIndex, shapeIndex) via System.Random / a stable pixel hash — NOT
    /// UnityEngine.Random — so the same blast produces byte-identical frames on every render.
    ///
    /// Pixel-art crisp: membership is a hard in/out test, never anti-aliased. Downstream uses FilterMode.Point.
    public static class BlastRenderer
    {
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // ── the frame → Color32[] core ─────────────────────────────────────────
        public static Color32[] RenderFrame(BlastSpec spec, int frameIndex)
        {
            int size = Mathf.Max(1, spec != null ? spec.canvasSize : 1);
            var buf = new Color32[size * size];

            Color32 bg = spec != null ? (Color32)spec.background : Transparent;
            for (int i = 0; i < buf.Length; i++) buf[i] = bg;
            if (spec == null || spec.layers == null) return buf;

            float center = size * 0.5f;
            int frameCount = Mathf.Max(1, spec.frameCount);
            float framePhase = frameCount > 1 ? (frameIndex / (float)frameCount) * Mathf.PI * 2f : 0f;

            // Composite strictly back-to-front: layers in list order, waves in list order, shapes 0..count-1.
            for (int li = 0; li < spec.layers.Count; li++)
            {
                var layer = spec.layers[li];
                if (layer == null || !layer.visible || layer.waves == null) continue;

                for (int wi = 0; wi < layer.waves.Count; wi++)
                {
                    var wave = layer.waves[wi];
                    if (wave == null || wave.count <= 0) continue;

                    for (int si = 0; si < wave.count; si++)
                    {
                        // Deterministic per-shape rng — same across every frame, so scatter is stable.
                        int shapeSeed = ShapeSeed(spec.seed, li, wi, si);
                        var rng = new System.Random(shapeSeed);

                        // Per-shape life window, optionally jittered so shapes don't pop in unison.
                        float span = Mathf.Max(1f, wave.endFrame - wave.startFrame);
                        float lifeJit = (float)(rng.NextDouble() * 2.0 - 1.0) * wave.perShapeLifeJitter * span * 0.5f;
                        float start = wave.startFrame + lifeJit;
                        float end = wave.endFrame + lifeJit;
                        if (end <= start) end = start + 1f;
                        if (frameIndex < start || frameIndex > end) continue;      // not alive this frame
                        float t = Mathf.Clamp01((frameIndex - start) / (end - start));

                        // Scatter the centre within spawnRadius (uniform disc), plus square jitter.
                        double ang = rng.NextDouble() * Math.PI * 2.0;
                        double rad = Math.Sqrt(rng.NextDouble()) * wave.spawnRadius;
                        float jx = (float)(rng.NextDouble() * 2.0 - 1.0) * wave.posJitter;
                        float jy = (float)(rng.NextDouble() * 2.0 - 1.0) * wave.posJitter;
                        Vector2 c = new Vector2((float)(Math.Cos(ang) * rad) + jx, (float)(Math.Sin(ang) * rad) + jy);

                        // Radius over life, plus grow/shrink envelope from the spawn/end modes.
                        float radius = Mathf.Lerp(wave.startSize, wave.endSize, t);
                        const float spawnFrac = 0.25f, endFrac = 0.25f;
                        if (wave.spawnMode == SpawnMode.GrowIn) radius *= Mathf.Clamp01(t / spawnFrac);
                        if (wave.endMode == EndMode.Shrink) radius *= Mathf.Clamp01((1f - t) / endFrac);
                        if (radius < 0.25f) continue;

                        // Alpha over life × spawn/end fade envelope.
                        float spawnEnv = wave.spawnMode == SpawnMode.FadeIn ? Mathf.Clamp01(t / spawnFrac) : 1f;
                        float endEnv = wave.endMode == EndMode.FadeOut ? Mathf.Clamp01((1f - t) / endFrac) : 1f;
                        float curveA = wave.alphaOverLife != null ? Mathf.Clamp01(wave.alphaOverLife.Evaluate(t)) : 1f;
                        float alpha = curveA * spawnEnv * endEnv;
                        if (alpha <= 0.001f) continue;

                        Color baseCol = wave.colorOverLife != null ? wave.colorOverLife.Evaluate(t) : Color.white;

                        // Disintegrate: near the end, drop an increasing fraction of pixels.
                        float disProb = 0f;
                        if (wave.endMode == EndMode.Disintegrate)
                        {
                            const float ds = 0.6f;
                            disProb = t > ds ? (t - ds) / (1f - ds) : 0f;
                        }

                        RasterShape(buf, size, center, spec, framePhase, wave, c, radius, baseCol, alpha, t, shapeSeed, disProb);
                    }
                }
            }
            return buf;
        }

        // ── per-shape rasteriser ───────────────────────────────────────────────
        // Iterates the whole canvas, maps each pixel back through the global deform into undeformed "blast
        // space", runs the hard shape test there, then composites source-over. Whole-canvas iteration keeps the
        // deform correct with zero clipping risk; canvases are small and the runtime player caches its frames.
        static void RasterShape(Color32[] buf, int size, float center, BlastSpec spec, float framePhase,
                                Wave wave, Vector2 c, float radius, Color baseCol, float alpha, float t,
                                int shapeSeed, float disProb)
        {
            float sq = Mathf.Approximately(spec.squash, 0f) ? 1f : spec.squash;

            for (int y = 0; y < size; y++)
            {
                float uy = (y + 0.5f) - center;
                float wob = spec.wobbleAmplitude * Mathf.Sin(uy * spec.wobbleFrequency * 0.1f + framePhase);
                for (int x = 0; x < size; x++)
                {
                    // Inverse deform: y maps straight, x un-shears / un-squashes / un-wobbles.
                    float ux = ((x + 0.5f) - center - spec.skew * uy - wob) / sq;

                    if (!ShapeHit(wave, ux, uy, c, radius, t, shapeSeed, x, y, baseCol, out Color col)) continue;

                    // Disintegrate drop-out (deterministic per pixel).
                    if (disProb > 0f && Hash01(shapeSeed ^ 0x1B873593, x, y) < disProb) continue;

                    Over(buf, y * size + x, col.r, col.g, col.b, alpha * col.a);
                }
            }
        }

        // Returns whether this undeformed pixel is inside the shape, and the colour to lay down.
        static bool ShapeHit(Wave wave, float ux, float uy, Vector2 c, float radius, float t,
                             int shapeSeed, int x, int y, Color baseCol, out Color col)
        {
            col = baseCol;
            float dx = ux - c.x, dy = uy - c.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);

            switch (wave.shape)
            {
                case WaveShape.Disc:
                    return dist <= radius;

                case WaveShape.Ring:
                {
                    float thk = Mathf.Max(1f, wave.ringThickness);
                    return dist <= radius && dist >= radius - thk;
                }

                case WaveShape.DissolvingDisc:
                {
                    if (dist > radius) return false;
                    float holeR = t * radius;                                  // hole grows to full by end
                    Vector2 hc = c + Vector2.right * (wave.dissolveCenter * radius);
                    float hdist = Vector2.Distance(new Vector2(ux, uy), hc);
                    bool border = wave.dissolveKeepBorder && dist >= radius - 1f;
                    return hdist >= holeR || border;
                }

                case WaveShape.SparkleField:
                {
                    if (dist > radius) return false;
                    float h = Hash01(shapeSeed, x, y);
                    if (h >= wave.sparkleDensity) return false;
                    // Shimmer: shift each lit pixel along the gradient by its own hash and by life.
                    if (wave.colorOverLife != null)
                        col = wave.colorOverLife.Evaluate(Mathf.Repeat(t + h, 1f));
                    return true;
                }

                case WaveShape.Crescent:
                {
                    if (dist > radius) return false;
                    Vector2 mc = c + wave.crescentOffset;
                    float mdist = Vector2.Distance(new Vector2(ux, uy), mc);
                    return mdist > radius;                                      // masked out where the offset disc overlaps
                }
            }
            return false;
        }

        // ── source-over compositing in straight alpha ──────────────────────────
        static void Over(Color32[] buf, int i, float r, float g, float b, float a)
        {
            if (a <= 0f) return;
            a = Mathf.Clamp01(a);
            Color32 d = buf[i];
            float da = d.a / 255f;
            float outA = a + da * (1f - a);
            if (outA <= 0f) { buf[i] = Transparent; return; }
            float inv = 1f - a;
            float or = (r * a + (d.r / 255f) * da * inv) / outA;
            float og = (g * a + (d.g / 255f) * da * inv) / outA;
            float ob = (b * a + (d.b / 255f) * da * inv) / outA;
            buf[i] = new Color32(
                (byte)(Mathf.Clamp01(or) * 255f),
                (byte)(Mathf.Clamp01(og) * 255f),
                (byte)(Mathf.Clamp01(ob) * 255f),
                (byte)(Mathf.Clamp01(outA) * 255f));
        }

        // ── determinism helpers ────────────────────────────────────────────────
        static int ShapeSeed(int seed, int layer, int wave, int shape)
        {
            unchecked
            {
                int h = seed;
                h = h * 397 + layer;
                h = h * 397 + wave;
                h = h * 397 + shape;
                return h;
            }
        }

        // Stable 0..1 hash of three ints — used for per-pixel sparkle / disintegrate so results don't depend on
        // iteration order and never touch UnityEngine.Random.
        static float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        // ── texture helpers ────────────────────────────────────────────────────
        public static Texture2D RenderFrameTexture(BlastSpec spec, int frameIndex)
        {
            int size = Mathf.Max(1, spec != null ? spec.canvasSize : 1);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Pyre_Frame_" + frameIndex
            };
            tex.SetPixels32(RenderFrame(spec, frameIndex));
            tex.Apply();
            return tex;
        }

        /// Pack every frame into a grid sheet (cols left→right, rows top→bottom) for baking / preview.
        public static Texture2D RenderSheet(BlastSpec spec, out int cols, out int rows, int maxCols = 8)
        {
            int size = Mathf.Max(1, spec != null ? spec.canvasSize : 1);
            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            SheetLayout(frames, maxCols, out cols, out rows);

            var sheet = new Texture2D(cols * size, rows * size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Pyre_Sheet"
            };
            var clear = new Color32[cols * size * rows * size];
            for (int i = 0; i < clear.Length; i++) clear[i] = Transparent;
            sheet.SetPixels32(clear);

            for (int f = 0; f < frames; f++)
            {
                var px = RenderFrame(spec, f);
                Rect r = FrameRect(f, cols, rows, size);
                sheet.SetPixels32((int)r.x, (int)r.y, size, size, px);
            }
            sheet.Apply();
            return sheet;
        }

        /// Grid dimensions for a frame count. Shared by RenderSheet and the baker so their rects always agree.
        public static void SheetLayout(int frameCount, int maxCols, out int cols, out int rows)
        {
            frameCount = Mathf.Max(1, frameCount);
            cols = Mathf.Clamp(Mathf.Min(maxCols, frameCount), 1, frameCount);
            rows = Mathf.CeilToInt(frameCount / (float)cols);
        }

        /// The texture-space rect (y-up) of a frame in the packed sheet. Row 0 sits at the top visually.
        public static Rect FrameRect(int frame, int cols, int rows, int size)
        {
            int col = frame % cols;
            int row = frame / cols;
            int px = col * size;
            int py = (rows - 1 - row) * size;   // flip so row 0 is the top row in the image
            return new Rect(px, py, size, size);
        }
    }
}

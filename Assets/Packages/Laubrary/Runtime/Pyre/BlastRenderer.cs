using System;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// The ONE deterministic, pure, runtime-safe renderer shared by the editor preview, the asset baker and the
    /// runtime player — so preview == bake == runtime. Never references UnityEditor. Every per-shape / per-frame
    /// random value derives from a stable int hash of (seed, layerIndex, shapeIndex|frameIndex, fieldId) fed to
    /// System.Random — NOT UnityEngine.Random — so the same blast produces byte-identical frames on every render.
    ///
    /// Animatable knobs are ZUIValues, evaluated through <see cref="Eval"/>:
    ///   • Static → the constant.
    ///   • MinMax → a deterministic sample from a seeded System.Random (per-shape for scatter/size so it's stable
    ///     across frames; per-frame for deform so it reads as a shake).
    ///   • Curve  → ZUIEnvelopeEvaluator sampled at a normalized progress (blast progress for drift/scatter/count/
    ///     deform, the shape's own life t for size).
    ///
    /// Pixel-art crisp: membership is a hard in/out test, never anti-aliased. Downstream uses FilterMode.Point.
    public static class BlastRenderer
    {
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // ── field ids (make each ZUIValue's MinMax sample independent) ─────────────
        const int F_Count = 1, F_SpawnRadius = 2, F_PosX = 3, F_PosY = 4,
                  F_Size = 5, F_Alpha = 6, F_CrescentX = 7, F_CrescentY = 8,
                  F_EmitAngle = 9, F_Travel = 10, F_WindX = 11, F_WindY = 12, F_BarForward = 13,
                  F_BarSpacing = 14, F_BarWidth = 15, F_BarBackward = 16, F_BarAngle = 17,
                  F_OriginInset = 18, F_BarCount = 19;
        const int F_Squash = 20, F_Skew = 21, F_WobAmp = 22, F_WobFreq = 23, F_Rot = 24;
        const int F_BaseAngle = 25, F_SpreadDeg = 26, F_Taper = 27, F_Stagger = 28;
        const int F_CrossAmt = 30, F_Contrast = 31, F_Brightness = 32, F_Saturation = 33;
        const float DissolveBand = 0.22f;   // soft width of the bar-dissolve front
        const int GlobalLayerId = -1;   // stands in for "no layer" when hashing the global deform

        /// One deform block resolved to plain floats for a given frame.
        struct Deform { public float sq, skew, amp, freq, rotDeg; }
        static readonly Deform Identity = new Deform { sq = 1f, skew = 0f, amp = 0f, freq = 0f, rotDeg = 0f };

        // ── colour grade: a cross gradient (multiplies across the shape/bar) + contrast/brightness/saturation.
        // Resolved once per layer per frame (per-layer values × the blast-global values), applied per pixel.
        struct Grade
        {
            public Gradient crossL, crossG;
            public float amtL, amtG;
            public float contrast, brightness, saturation;
            public bool active;
        }

        static Grade BuildGrade(Layer layer, BlastSpec spec, int li, float lp, float bp, int frameIndex)
        {
            var g = new Grade
            {
                crossL = layer.crossGradient,
                amtL = Mathf.Clamp01(Eval(layer.crossAmount, lp, spec.seed, li, 0, F_CrossAmt)),
                crossG = spec.crossGradient,
                amtG = Mathf.Clamp01(Eval(spec.crossAmount, bp, spec.seed, GlobalLayerId, frameIndex, F_CrossAmt)),
                contrast   = Eval(layer.contrast,   lp, spec.seed, li, 0, F_Contrast)   * Eval(spec.contrast,   bp, spec.seed, GlobalLayerId, frameIndex, F_Contrast),
                brightness = Eval(layer.brightness, lp, spec.seed, li, 0, F_Brightness) * Eval(spec.brightness, bp, spec.seed, GlobalLayerId, frameIndex, F_Brightness),
                saturation = Eval(layer.saturation, lp, spec.seed, li, 0, F_Saturation) * Eval(spec.saturation, bp, spec.seed, GlobalLayerId, frameIndex, F_Saturation),
            };
            g.active = g.amtL > 0.001f || g.amtG > 0.001f
                     || Mathf.Abs(g.contrast - 1f) > 0.001f
                     || Mathf.Abs(g.brightness - 1f) > 0.001f
                     || Mathf.Abs(g.saturation - 1f) > 0.001f;
            return g;
        }

        // crossFrac 0..1 across the shape (centre→edge) or bar (back→tip).
        static Color ApplyGrade(Color c, float crossFrac, in Grade g)
        {
            if (!g.active) return c;
            crossFrac = Mathf.Clamp01(crossFrac);
            if (g.amtL > 0.001f && g.crossL != null) c = MulRGB(c, Color.Lerp(Color.white, g.crossL.Evaluate(crossFrac), g.amtL));
            if (g.amtG > 0.001f && g.crossG != null) c = MulRGB(c, Color.Lerp(Color.white, g.crossG.Evaluate(crossFrac), g.amtG));
            float r = c.r * g.brightness, gg = c.g * g.brightness, b = c.b * g.brightness;   // brightness
            r = (r - 0.5f) * g.contrast + 0.5f; gg = (gg - 0.5f) * g.contrast + 0.5f; b = (b - 0.5f) * g.contrast + 0.5f;  // contrast
            float lum = r * 0.299f + gg * 0.587f + b * 0.114f;                               // saturation
            r = Mathf.Lerp(lum, r, g.saturation); gg = Mathf.Lerp(lum, gg, g.saturation); b = Mathf.Lerp(lum, b, g.saturation);
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(gg), Mathf.Clamp01(b), c.a);
        }

        static Color MulRGB(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);

        // Running maximum of a value from 0..t — makes a Curve "expand and hold" (never contract). Static/MinMax
        // are already constant, so they pass straight through.
        static float EvalRisingMax(ZUIValue v, float t, int seed, int li, int si, int fid)
        {
            if (v == null) return 0f;
            if (v.mode != ZUIValue.Mode.Curve) return Eval(v, t, seed, li, si, fid);
            float m = 0f;
            const int N = 8;
            for (int k = 0; k <= N; k++) m = Mathf.Max(m, Eval(v, t * k / (float)N, seed, li, si, fid));
            return m;
        }

        // ── the frame → Color32[] core ─────────────────────────────────────────
        public static Color32[] RenderFrame(BlastSpec spec, int frameIndex)
        {
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var buf = new Color32[W * H];

            Color32 bg = spec != null ? (Color32)spec.background : Transparent;
            for (int i = 0; i < buf.Length; i++) buf[i] = bg;
            if (spec == null || spec.layers == null) return buf;

            float cx = W * 0.5f, cy = H * 0.5f;
            float half = Mathf.Min(W, H) * 0.5f;
            int frameCount = Mathf.Max(1, spec.frameCount);
            float bp = frameCount > 1 ? frameIndex / (float)(frameCount - 1) : 0f;   // blast progress 0..1
            float framePhase = frameCount > 1 ? (frameIndex / (float)frameCount) * Mathf.PI * 2f : 0f;

            // Global deform is evaluated once per frame (per-frame MinMax gives a whole-blast shake).
            Deform global = spec.deformEnabled
                ? EvalDeform(spec.squash, spec.skew, spec.wobbleAmplitude, spec.wobbleFrequency, spec.rotation,
                             bp, spec.seed, GlobalLayerId, frameIndex)
                : Identity;

            // Base angle & star spread arc are (animatable) blast-globals, evaluated once per frame.
            float baseA = Eval(spec.baseAngleDeg, bp, spec.seed, GlobalLayerId, frameIndex, F_BaseAngle);
            int spread = spec.star ? Mathf.Max(1, spec.spreadCount) : 1;
            float spreadDeg = spec.star ? Eval(spec.spreadDegrees, bp, spec.seed, GlobalLayerId, frameIndex, F_SpreadDeg) : 0f;

            // Composite strictly back-to-front. Bars render ALL their star arms internally (interleaved by bar
            // index so overlapping arms layer consistently); scatter layers draw once per star copy, rotated
            // about the centre.
            for (int li = 0; li < spec.layers.Count; li++)
            {
                var layer = spec.layers[li];
                if (layer == null || !layer.enabled) continue;

                // Layer life progress: the curve envelope of any animated value spans exactly the frames this
                // layer exists — frame startFrame → 0, frame endFrame → 1 — NOT the whole blast.
                float lp = Mathf.Clamp01((frameIndex - layer.startFrame) /
                                         (float)Mathf.Max(1, layer.endFrame - layer.startFrame));

                // Colour grade (cross gradient + contrast/brightness/saturation), resolved once for this layer/frame.
                Grade grade = BuildGrade(layer, spec, li, lp, bp, frameIndex);

                // Bars mode is a wholly different, directional composition — draws all its arms itself.
                if (layer.shape == LayerShape.Bars)
                {
                    RenderBarsLayerStar(buf, W, H, cx, cy, layer, li, spec, lp, baseA, spreadDeg, spread, grade);
                    continue;
                }

                // Per-layer deform, evaluated once per frame; composited UNDER the global deform.
                Deform local = layer.deformEnabled
                    ? EvalDeform(layer.deformSquash, layer.deformSkew, layer.deformWobbleAmplitude,
                                 layer.deformWobbleFrequency, layer.deformRotation, lp, spec.seed, li, frameIndex)
                    : Identity;

                // Count: Curve reads the layer's life progress; MinMax stays frame-stable (h2 = 0, no frame/shape).
                int count = Mathf.Max(0, Mathf.RoundToInt(Eval(layer.count, lp, spec.seed, li, 0, F_Count)));

                for (int inst = 0; inst < spread; inst++)
                {
                    float instRad = (spread > 1 ? (spreadDeg / spread) * inst : 0f) * Mathf.Deg2Rad;
                    for (int si = 0; si < count; si++)
                    {
                    // Deterministic per-shape rng — same across every frame, so scatter/life are stable.
                    int shapeSeed = ShapeSeed(spec.seed, li, si);
                    var rng = new System.Random(shapeSeed);

                    // Per-shape life window, optionally jittered so shapes don't pop in unison.
                    float span = Mathf.Max(1f, layer.endFrame - layer.startFrame);
                    float lifeJit = (float)(rng.NextDouble() * 2.0 - 1.0) * layer.perShapeLifeJitter * span * 0.5f;
                    float start = layer.startFrame + lifeJit;
                    float end = layer.endFrame + lifeJit;
                    if (end <= start) end = start + 1f;
                    if (frameIndex < start || frameIndex > end) continue;      // not alive this frame
                    float t = Mathf.Clamp01((frameIndex - start) / (end - start));

                    Vector2 c;
                    if (layer.emission == EmissionMode.Directional)
                    {
                        // Start on the (optionally bent) origin line and stream outward along its normal.
                        float niD = count > 1 ? si / (float)(count - 1) : 0.5f;
                        c = OriginPoint(niD, layer.originOffsetX, layer.originOffsetY, layer.originLength,
                                        layer.originBend, layer.originAngleDeg, out Vector2 normal);
                        float spreadJit = (float)(rng.NextDouble() * 2.0 - 1.0) * layer.emitSpreadDeg;
                        float emitDeg = Eval(layer.emitAngleDeg, lp, spec.seed, li, si, F_EmitAngle) + spreadJit;
                        Vector2 emitDir = Rotate(normal, emitDeg * Mathf.Deg2Rad);
                        float travelPx = Eval(layer.travel, lp, spec.seed, li, si, F_Travel);
                        c += emitDir * (travelPx * t);
                    }
                    else
                    {
                        // Radial: scatter the centre within spawnRadius (uniform disc). spawnRadius is 0..1 of the
                        // explosion; 1 reaches (almost) the canvas edge. Curve reads layer progress so it can expand.
                        double ang = rng.NextDouble() * Math.PI * 2.0;
                        double radFrac = Math.Sqrt(rng.NextDouble());
                        float sr01 = Mathf.Clamp01(Eval(layer.spawnRadius, lp, spec.seed, li, si, F_SpawnRadius));
                        float scatterPx = sr01 * half * 0.9f;   // 0.9 safe margin off the very edge
                        c = new Vector2((float)(Math.Cos(ang) * radFrac) * scatterPx,
                                        (float)(Math.Sin(ang) * radFrac) * scatterPx);
                    }

                    // Position offset (fixed / per-shape random / animated drift over layer progress).
                    c.x += Eval(layer.positionX, lp, spec.seed, li, si, F_PosX);
                    c.y += Eval(layer.positionY, lp, spec.seed, li, si, F_PosY);

                    // Wind drift: a directional push added to every shape, growing with its age t (any emission mode).
                    c.x += Eval(layer.windX, lp, spec.seed, li, si, F_WindX) * t;
                    c.y += Eval(layer.windY, lp, spec.seed, li, si, F_WindY) * t;

                    // Radius: one Size multicontrol over the shape's own life t (Curve = an envelope, Static =
                    // constant, MinMax = a per-shape-stable random size).
                    float radius = Eval(layer.size, t, spec.seed, li, si, F_Size);
                    if (radius < 0.25f) continue;

                    // Alpha: one multicontrol over life (Curve envelope by default) — the only thing that fades.
                    float alpha = Mathf.Clamp01(Eval(layer.alpha, t, spec.seed, li, si, F_Alpha));
                    if (alpha <= 0.001f) continue;

                    Color baseCol = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(t) : Color.white;

                    // Disintegrate: near the end, drop an increasing fraction (up to layer.disintegrate) of pixels.
                    float disProb = 0f;
                    if (layer.disintegrate > 0f)
                    {
                        const float ds = 0.6f;
                        disProb = t > ds ? (t - ds) / (1f - ds) * Mathf.Clamp01(layer.disintegrate) : 0f;
                    }

                    // Crescent mask offset (animatable).
                    float crescX = Eval(layer.crescentOffsetX, lp, spec.seed, li, si, F_CrescentX);
                    float crescY = Eval(layer.crescentOffsetY, lp, spec.seed, li, si, F_CrescentY);

                    // Circular spread: rotate this shape's offset around the centre for this instance.
                    if (spread > 1) c = Rotate(c, instRad);

                    // ── keep-on-screen guarantee (Radial only) ────────────────────
                    // A radial pixel-art explosion must never be clipped flat at the canvas edge, so cap the radius
                    // to the canvas half and clamp the centre so the whole shape fits. Directional / bar shapes are
                    // MEANT to stream off the frame, so they aren't clamped — the rasterizer still writes in-canvas.
                    float effR = Mathf.Min(radius, half);
                    if (layer.emission == EmissionMode.Radial)
                    {
                        float absX = Mathf.Clamp(cx + c.x, effR, W - effR);
                        float absY = Mathf.Clamp(cy + c.y, effR, H - effR);
                        c = new Vector2(absX - cx, absY - cy);
                    }
                    radius = effR;

                    RasterShape(buf, W, H, cx, cy, global, local, framePhase, layer, c, radius, baseCol, alpha,
                                t, shapeSeed, disProb, crescX, crescY, grade);
                }
              }
            }
            return buf;
        }

        // A point on the (optionally bent) origin line for parameter ni in [0,1], with the outward normal there.
        // bend 0 = a straight line of `length`; bend 1 = the line curled into a full circle (Choreographer-style).
        static Vector2 OriginPoint(float ni, float offX, float offY, float length, float bend, float angleDeg,
                                   out Vector2 normal)
        {
            Vector2 origin = new Vector2(offX, offY);
            float a = angleDeg * Mathf.Deg2Rad;
            Vector2 tangent = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 norm = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));   // +90° from the tangent = outward side
            if (bend < 0.001f)
            {
                normal = norm;
                return origin + tangent * ((ni - 0.5f) * length);
            }
            float arcSpan = bend * Mathf.PI * 2f;      // up to a full circle
            float radius = length / arcSpan;           // so the arc length stays == length
            float theta = (ni - 0.5f) * arcSpan;
            Vector2 arcCenter = origin - norm * radius; // arc bows toward +norm (the emission side)
            Vector2 p = arcCenter + norm * (radius * Mathf.Cos(theta)) + tangent * (radius * Mathf.Sin(theta));
            normal = (p - arcCenter).normalized;
            return p;
        }

        static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ── Bars mode: rows of forward-growing bars. Star off = one arm off the back edge; Star on = `spread`
        // arms sharing the centre and radiating outward (an asterisk). Each bar reaches FORWARD, its length scaled
        // by Taper (centre-longest → triangle/flame). Arms are drawn INTERLEAVED by bar index — the centre bar of
        // every arm, then the next bar out of every arm, … — so overlapping arms layer consistently instead of
        // each whole arm stacking over the previous one. Deform is not applied to bars.
        static void RenderBarsLayerStar(Color32[] buf, int W, int H, float cx, float cy,
                                        Layer layer, int li, BlastSpec spec, float lp,
                                        float baseA, float spreadDeg, int spread, Grade grade)
        {
            Vector2 center = new Vector2(cx, cy);
            bool star = spec.star;

            // per-layer bar knobs (multicontrols evaluated once over the layer's life)
            int B = Mathf.Max(0, Mathf.RoundToInt(Eval(layer.barCount, lp, spec.seed, li, 0, F_BarCount)));
            float spacing = Eval(layer.barSpacing, lp, spec.seed, li, 0, F_BarSpacing);
            float backFrac = Mathf.Clamp01(Eval(layer.barBackwardFrac, lp, spec.seed, li, 0, F_BarBackward));
            float inset = Eval(layer.originInset, lp, spec.seed, li, 0, F_OriginInset);
            float taper = Mathf.Clamp(Eval(layer.barTaper, lp, spec.seed, li, 0, F_Taper), -1f, 1f);
            float stagger = Mathf.Max(0f, Eval(layer.barStagger, lp, spec.seed, li, 0, F_Stagger));
            float ang = Eval(layer.barAngleDeg, lp, spec.seed, li, 0, F_BarAngle);

            bool dissolve = layer.barDecay == BarDecay.Dissolve;
            float width = dissolve ? EvalRisingMax(layer.barWidth, lp, spec.seed, li, 0, F_BarWidth)
                                   : Eval(layer.barWidth, lp, spec.seed, li, 0, F_BarWidth);
            float front = dissolve ? Mathf.Clamp01(Mathf.InverseLerp(layer.dissolveStart, 1f, lp)) * (1f + DissolveBand) : 0f;

            // Build each arm's geometry once. Mirror adds a second row per instance on the far side of the angle.
            bool mirror = layer.barMirror && Mathf.Abs(ang) > 0.001f;
            int rows = spread * (mirror ? 2 : 1);
            var dirs = new Vector2[rows];
            var perps = new Vector2[rows];
            var origins = new Vector2[rows];
            for (int r = 0; r < rows; r++)
            {
                int inst = mirror ? r / 2 : r;
                bool isMir = mirror && (r & 1) == 1;
                float instAngle = spread > 1 ? (spreadDeg / spread) * inst : 0f;
                float a = (baseA + instAngle + (isMir ? -ang : ang)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                dirs[r] = dir;
                perps[r] = new Vector2(-dir.y, dir.x);
                // Star off: origin on the back edge, arm reaches across the frame. Star on: origin at the shared
                // centre (just past it by `inset`), arm reaches outward — no backward tail through the middle.
                origins[r] = star ? center + dir * inset : EdgePoint(center, W, H, -dir) + dir * inset;
            }

            void DrawBar(int r, int i)
            {
                Vector2 dir = dirs[r], perp = perps[r];
                Vector2 barCenter = origins[r] + perp * (i * spacing);
                float d = B > 0 ? Mathf.Abs(i) / (float)B : 0f;                          // 0 = centre bar, 1 = outermost
                float lenMul = taper >= 0f ? 1f - taper * d : 1f + taper * (1f - d);      // Taper = the arm silhouette
                lenMul = Mathf.Max(0.04f, lenMul);

                float appear = Mathf.Abs(i) * stagger;                                    // Stagger = timing, centre first
                if (lp < appear) return;
                float tb = Mathf.Clamp01((lp - appear) / Mathf.Max(0.0001f, 1f - appear)); // bar's own life

                float fwd = (dissolve ? EvalRisingMax(layer.barForward, tb, spec.seed, li, i, F_BarForward)
                                      : Eval(layer.barForward, tb, spec.seed, li, i, F_BarForward)) * lenMul;
                if (fwd < 0.4f) return;
                float bwd = star ? 0f : fwd * backFrac;   // no backward spill when arms share the centre

                Color col = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(d) : Color.white;
                float alpha = Mathf.Clamp01(Eval(layer.alpha, tb, spec.seed, li, i, F_Alpha));
                if (dissolve) alpha *= 1f - Mathf.Clamp01((front - d) / DissolveBand);    // centre (d=0) dissolves first
                if (alpha < 0.004f) return;

                RasterBar(buf, W, H, barCenter, dir, perp, -bwd, fwd, width, col, alpha, grade);
            }

            // Interleave: centre bar of every arm first, then the ±1 bars of every arm, … outward. Within a bar
            // index both wings are drawn together so overlaps (width > spacing) stay symmetric — no centre drift.
            for (int k = 0; k <= B; k++)
                for (int r = 0; r < rows; r++)
                {
                    DrawBar(r, k);
                    if (k > 0) DrawBar(r, -k);
                }
        }

        // Fill a rotated rectangle: from alongMin..alongMax along `dir`, ±halfWidth across `perp`.
        static void RasterBar(Color32[] buf, int W, int H, Vector2 barCenter, Vector2 dir, Vector2 perp,
                              float alongMin, float alongMax, float width, Color col, float alpha, Grade grade)
        {
            float hw = width * 0.5f;
            float span = Mathf.Max(0.0001f, alongMax - alongMin);
            // bounding box over the 4 corners
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int sa = 0; sa < 2; sa++)
                for (int sp = 0; sp < 2; sp++)
                {
                    Vector2 p = barCenter + dir * (sa == 0 ? alongMin : alongMax) + perp * (sp == 0 ? -hw : hw);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = (x + 0.5f) - barCenter.x, py = (y + 0.5f) - barCenter.y;
                    float along = px * dir.x + py * dir.y;
                    float across = px * perp.x + py * perp.y;
                    if (along >= alongMin && along <= alongMax && across >= -hw && across <= hw)
                    {
                        Color fc = grade.active ? ApplyGrade(col, (along - alongMin) / span, grade) : col;
                        Over(buf, y * W + x, fc.r, fc.g, fc.b, alpha * fc.a);
                    }
                }
        }

        // From `from` (inside the rect), march along unit `d` to the [0,W]x[0,H] border; returns the hit point.
        static Vector2 EdgePoint(Vector2 from, int W, int H, Vector2 d)
        {
            float t = float.MaxValue;
            if (d.x > 1e-4f) t = Mathf.Min(t, (W - from.x) / d.x);
            else if (d.x < -1e-4f) t = Mathf.Min(t, -from.x / d.x);
            if (d.y > 1e-4f) t = Mathf.Min(t, (H - from.y) / d.y);
            else if (d.y < -1e-4f) t = Mathf.Min(t, -from.y / d.y);
            if (t == float.MaxValue || t < 0f) t = 0f;
            return from + d * t;
        }

        // ── per-shape rasteriser ───────────────────────────────────────────────
        // Iterates the whole canvas, maps each pixel back through (global ∘ per-layer) deform into undeformed
        // "blast space", runs the hard shape test there, then composites source-over. Whole-canvas iteration keeps
        // the deform correct with zero clipping risk; canvases are small and the runtime player caches its frames.
        static void RasterShape(Color32[] buf, int W, int H, float cx, float cy, Deform global, Deform local,
                                float framePhase, Layer layer, Vector2 c, float radius, Color baseCol, float alpha,
                                float t, int shapeSeed, float disProb, float crescX, float crescY, Grade grade)
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    // Forward map is screen = global(local(undeformed)); invert in reverse order.
                    Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                    off = InverseDeform(off, global, framePhase);
                    off = InverseDeform(off, local, framePhase);
                    float ux = off.x, uy = off.y;

                    if (!ShapeHit(layer, ux, uy, c, radius, t, shapeSeed, x, y, baseCol, crescX, crescY, out Color col))
                        continue;

                    // Disintegrate drop-out (deterministic per pixel).
                    if (disProb > 0f && Hash01(shapeSeed ^ 0x1B873593, x, y) < disProb) continue;

                    // Normalised distance from the shape centre (0 = centre, 1 = edge) — drives radial alpha AND
                    // the colour grade's cross gradient.
                    float nd = radius > 0.001f
                        ? Mathf.Clamp01(Mathf.Sqrt((ux - c.x) * (ux - c.x) + (uy - c.y) * (uy - c.y)) / radius) : 0f;

                    float pixelAlpha = alpha;
                    if (layer.radialAlpha != null && layer.radialAlpha.Count > 0)
                        pixelAlpha *= Mathf.Clamp01(ZUIEnvelopeEvaluator.Evaluate(layer.radialAlpha, nd, 1f));
                    if (pixelAlpha <= 0.001f) continue;

                    Color fc = grade.active ? ApplyGrade(col, nd, grade) : col;
                    Over(buf, y * W + x, fc.r, fc.g, fc.b, pixelAlpha * fc.a);
                }
            }
        }

        // Returns whether this undeformed pixel is inside the shape, and the colour to lay down.
        static bool ShapeHit(Layer layer, float ux, float uy, Vector2 c, float radius, float t,
                             int shapeSeed, int x, int y, Color baseCol, float crescX, float crescY, out Color col)
        {
            col = baseCol;
            float dx = ux - c.x, dy = uy - c.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);

            switch (layer.shape)
            {
                case LayerShape.Disc:
                    return dist <= radius;

                case LayerShape.Ring:
                {
                    float thk = Mathf.Max(1f, layer.ringThickness);
                    return dist <= radius && dist >= radius - thk;
                }

                case LayerShape.DissolvingDisc:
                {
                    if (dist > radius) return false;
                    float holeR = t * radius;                                  // hole grows to full by end
                    Vector2 hc = c + Vector2.right * (layer.dissolveCenter * radius);
                    float hdist = Vector2.Distance(new Vector2(ux, uy), hc);
                    bool border = layer.dissolveKeepBorder && dist >= radius - 1f;
                    return hdist >= holeR || border;
                }

                case LayerShape.SparkleField:
                {
                    if (dist > radius) return false;
                    float h = Hash01(shapeSeed, x, y);
                    if (h >= layer.sparkleDensity) return false;
                    // Shimmer: shift each lit pixel along the gradient by its own hash and by life.
                    if (layer.colorOverLife != null)
                        col = layer.colorOverLife.Evaluate(Mathf.Repeat(t + h, 1f));
                    return true;
                }

                case LayerShape.Crescent:
                {
                    if (dist > radius) return false;
                    Vector2 mc = c + new Vector2(crescX, crescY);
                    float mdist = Vector2.Distance(new Vector2(ux, uy), mc);
                    return mdist > radius;                                      // masked out where the offset disc overlaps
                }
            }
            return false;
        }

        // ── ZUIValue evaluation (deterministic) ─────────────────────────────────
        // curveProgress is the normalized time a Curve is sampled at (caller-chosen: blast progress or life t).
        // The four hash ints seed the MinMax System.Random; pass a frame-stable set for per-shape values and one
        // that includes the frame index for per-frame deform shakes.
        static float Eval(ZUIValue v, float curveProgress, int h0, int h1, int h2, int h3)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static:
                    return v.staticValue;
                case ZUIValue.Mode.MinMax:
                {
                    var rng = new System.Random(Hash(h0, h1, h2, h3));
                    return Mathf.Lerp(v.min, v.max, (float)rng.NextDouble());
                }
                case ZUIValue.Mode.Curve:
                    // Points are authored in normalized [0..1]; sample directly (ignore duration/warmup/cooldown —
                    // those are for the runtime-seconds use case, not our frame-baked timeline).
                    return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(curveProgress), v.yMax);
                default:
                    return v.staticValue;
            }
        }

        static Deform EvalDeform(ZUIValue squash, ZUIValue skew, ZUIValue wobAmp, ZUIValue wobFreq, ZUIValue rot,
                                 float bp, int seed, int layerId, int frameIndex)
        {
            return new Deform
            {
                sq = Eval(squash, bp, seed, layerId, frameIndex, F_Squash),
                skew = Eval(skew, bp, seed, layerId, frameIndex, F_Skew),
                amp = Eval(wobAmp, bp, seed, layerId, frameIndex, F_WobAmp),
                freq = Eval(wobFreq, bp, seed, layerId, frameIndex, F_WobFreq),
                rotDeg = Eval(rot, bp, seed, layerId, frameIndex, F_Rot),
            };
        }

        // Inverse of one deform block, applied to a pixel offset from the canvas centre. Forward order is
        // rotate → squash-x → skew → wobble (skew & wobble depend only on y, so the inverse is closed-form).
        static Vector2 InverseDeform(Vector2 off, Deform d, float phase)
        {
            float x = off.x, y = off.y;
            // un-wobble (depends on y only)
            if (d.amp != 0f) x -= d.amp * Mathf.Sin(y * d.freq * 0.1f + phase);
            // un-skew (depends on y only)
            if (d.skew != 0f) x -= d.skew * y;
            // un-squash
            float sq = Mathf.Approximately(d.sq, 0f) ? 1f : d.sq;
            x /= sq;
            // un-rotate by -θ (recover the pre-rotation coords)
            if (d.rotDeg != 0f)
            {
                float rad = -d.rotDeg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                return new Vector2(x * cos - y * sin, x * sin + y * cos);
            }
            return new Vector2(x, y);
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
        static int ShapeSeed(int seed, int layer, int shape)
        {
            unchecked
            {
                int h = seed;
                h = h * 397 + layer;
                h = h * 397 + shape;
                return h;
            }
        }

        // Combine four ints into a stable seed (31-multiply chain). No GetHashCode / no UnityEngine.Random, so the
        // result is identical on every platform and every render.
        static int Hash(int a, int b, int c, int d)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + a;
                h = h * 31 + b;
                h = h * 31 + c;
                h = h * 31 + d;
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
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
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
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            SheetLayout(frames, maxCols, out cols, out rows);

            var sheet = new Texture2D(cols * W, rows * H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Pyre_Sheet"
            };
            var clear = new Color32[cols * W * rows * H];
            for (int i = 0; i < clear.Length; i++) clear[i] = Transparent;
            sheet.SetPixels32(clear);

            for (int f = 0; f < frames; f++)
            {
                var px = RenderFrame(spec, f);
                Rect r = FrameRect(f, cols, rows, W, H);
                sheet.SetPixels32((int)r.x, (int)r.y, W, H, px);
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
        public static Rect FrameRect(int frame, int cols, int rows, int cellW, int cellH)
        {
            int col = frame % cols;
            int row = frame / cols;
            int px = col * cellW;
            int py = (rows - 1 - row) * cellH;   // flip so row 0 is the top row in the image
            return new Rect(px, py, cellW, cellH);
        }
    }
}

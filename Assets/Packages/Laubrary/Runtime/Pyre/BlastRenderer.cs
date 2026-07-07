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
                  F_WindX = 11, F_WindY = 12, F_BarForward = 13,
                  F_BarSpacing = 14, F_BarWidth = 15, F_BarBackward = 16, F_BarAngle = 17,
                  F_OriginInset = 18, F_BarCount = 19;
        const int F_Squash = 20, F_Skew = 21, F_WobAmp = 22, F_WobFreq = 23, F_Rot = 24;
        const int F_BaseAngle = 25, F_SpreadDeg = 26, F_Taper = 27, F_Stagger = 28;
        const int F_CrossAmt = 30, F_Contrast = 31, F_Brightness = 32, F_Saturation = 33;
        const int F_SparkleDensity = 34, F_HoleSize = 38, F_SpriteSpin = 39;
        const int F_InnerSoft = 40, F_OuterSoft = 41, F_ColorFlow = 42, F_ColorZoom = 43, F_SparkleSeed = 44;
        const int F_GradX = 45, F_GradY = 46, F_HoleOffX = 47, F_HoleOffY = 48, F_BarSoft = 49;
        const float DissolveBand = 0.22f;   // soft width of the bar-dissolve front
        const int GlobalLayerId = -1;   // stands in for "no layer" when hashing global modifiers

        // ── modifier pipeline (Pyre v2): opt-in geometry warps + pixel effects, per-layer and global ─────────
        readonly struct ModStack
        {
            public readonly GeometryModifier[] geo;   // forward order; inverse-apply in reverse
            public readonly PixelModifier[] pix;      // apply in order
            public ModStack(GeometryModifier[] g, PixelModifier[] p) { geo = g; pix = p; }
            public bool AnyGeo => geo.Length > 0;
            public bool AnyPix => pix.Length > 0;
        }
        static readonly ModStack EmptyStack = new ModStack(Array.Empty<GeometryModifier>(), Array.Empty<PixelModifier>());

        // Collect + Prepare the enabled modifiers of a layer (progress = layer life) and the blast (progress = blast
        // progress) for this frame. Layer mods come first (inner); global mods wrap them (outer).
        // A geometry modifier paired with its effective warp pass (higher = applied first = outermost warp).
        struct GeoEntry { public GeometryModifier mod; public int pass; }

        // Global modifiers get this added to their WarpPass so they sort ABOVE every layer modifier — a global warp
        // (Rotate/Skew/Wobble/Squash) is the OUTERMOST transform, wrapping each shape's own Ground/Profile/etc. That
        // makes a global Rotate spin the whole animation as one (every shape rotates identically about the same
        // pivot) while composing correctly with per-layer grounding — and, being a coordinate transform rather than
        // a buffer resample, it never clips the frame the way a post-pass on a non-square canvas would.
        const int GlobalPassOffset = 1000;

        static ModStack BuildStack(Layer layer, BlastSpec spec, int li, float lp, float bp, int frameIndex)
        {
            var geo = new System.Collections.Generic.List<GeoEntry>();
            var pix = new System.Collections.Generic.List<PixelModifier>();
            CollectMods(layer != null ? layer.modifiers : null, spec, li, lp, frameIndex, geo, pix, 0, 0);
            CollectMods(spec != null ? spec.globalModifiers : null, spec, GlobalLayerId, bp, frameIndex, geo, pix, 500, GlobalPassOffset);
            if (geo.Count == 0 && pix.Count == 0) return EmptyStack;
            // Sort ascending by effective pass so the array ends with the highest; ApplyGeo (back-to-front) then
            // applies them first. Insertion sort keeps it stable so same-pass modifiers keep authoring order.
            if (geo.Count > 1) SortGeoStable(geo);
            var arr = new GeometryModifier[geo.Count];
            for (int i = 0; i < geo.Count; i++) arr[i] = geo[i].mod;
            return new ModStack(arr, pix.ToArray());
        }

        static void CollectMods(System.Collections.Generic.List<PyreModifier> mods, BlastSpec spec, int layerId,
                                float progress, int frameIndex,
                                System.Collections.Generic.List<GeoEntry> geo,
                                System.Collections.Generic.List<PixelModifier> pix, int baseId, int passOffset)
        {
            if (mods == null) return;
            int seed = spec != null ? spec.seed : 0;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null || !m.enabled) continue;
                if (m is PostModifier) continue;   // whole-frame passes run after compositing, not in the per-shape stack
                int uid = baseId + i;
                m.Prepare((v, fid) => Eval(v, progress, seed, layerId, frameIndex, 1000 + uid * 8 + fid));
                if (m is GeometryModifier gm) geo.Add(new GeoEntry { mod = gm, pass = gm.WarpPass + passOffset });
                else if (m is PixelModifier pm) pix.Add(pm);
            }
        }

        static void SortGeoStable(System.Collections.Generic.List<GeoEntry> geo)
        {
            for (int i = 1; i < geo.Count; i++)
            {
                var e = geo[i]; int j = i - 1;
                while (j >= 0 && geo[j].pass > e.pass) { geo[j + 1] = geo[j]; j--; }
                geo[j + 1] = e;
            }
        }

        // Undo the geometry modifiers on a pixel offset from the canvas centre, in shape-aware context (so position-
        // dependent warps like Ground/Profile can work in the shape's own frame). Highest WarpPass is applied first.
        static Vector2 ApplyGeo(in ModStack s, Vector2 off, float phase, in GeoCtx ctx)
        {
            for (int i = s.geo.Length - 1; i >= 0; i--) off = s.geo[i].InverseWarp(off, phase, ctx);
            return off;
        }

        // Run the pixel modifiers on a hit pixel; false = drop it.
        static bool ApplyPix(in ModStack s, ref Color col, ref float alpha, int x, int y, int frame,
                             float crossFrac, float life, int hash, int W, int H)
        {
            var info = new PixelInfo(x, y, frame, crossFrac, life, hash, W, H);
            for (int i = 0; i < s.pix.Length; i++)
                if (!s.pix[i].ApplyPixel(ref col, ref alpha, info)) return false;
            return true;
        }

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

            // Composite strictly back-to-front. Bars render ALL their star arms internally (interleaved by bar
            // index so overlapping arms layer consistently); scatter layers draw once per star copy, rotated
            // about the centre. Star (base angle / arms / spread arc) is now PER-LAYER.
            for (int li = 0; li < spec.layers.Count; li++)
            {
                var layer = spec.layers[li];
                if (layer == null || !layer.enabled) continue;

                // Layer life progress: the curve envelope of any animated value spans exactly the frames this
                // layer exists — frame startFrame → 0, frame endFrame → 1 — NOT the whole blast.
                float lp = Mathf.Clamp01((frameIndex - layer.startFrame) /
                                         (float)Mathf.Max(1, layer.endFrame - layer.startFrame));

                ModStack stack = BuildStack(layer, spec, li, lp, bp, frameIndex);

                // Per-layer star: how many rotated copies and the arc they span.
                int spread = layer.star ? Mathf.Max(1, layer.spreadCount) : 1;
                float spreadDeg = layer.star ? Eval(layer.spreadDegrees, lp, spec.seed, li, 0, F_SpreadDeg) : 0f;

                // Bars mode is a wholly different, directional composition — draws all its arms itself.
                if (layer.shape == LayerShape.Bars)
                {
                    float baseA = Eval(layer.baseAngleDeg, lp, spec.seed, li, 0, F_BaseAngle);
                    RenderBarsLayerStar(buf, W, H, cx, cy, layer, li, spec, lp, frameIndex, framePhase, baseA, spreadDeg, spread, stack);
                    continue;
                }

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

                    // Per-shape life window. Spawn stagger distributes the Count shapes across the layer window,
                    // shortening each life so they tile it (0 = all live the full window; 1 = evenly spread, first
                    // spawn at the first frame, last spawn ending at the last). Jitter then nudges each one.
                    float span = Mathf.Max(1f, layer.endFrame - layer.startFrame);
                    float s = Mathf.Clamp01(layer.spawnStagger);
                    float life = span * (1f - s * (count - 1) / (float)Mathf.Max(1, count));
                    if (life < 1f) life = 1f;
                    float spawnAt = count > 1 ? (si / (float)(count - 1)) * (span - life) : 0f;
                    float lifeJit = (float)(rng.NextDouble() * 2.0 - 1.0) * layer.perShapeLifeJitter * life * 0.5f;
                    float start = layer.startFrame + spawnAt + lifeJit;
                    float end = start + life;
                    if (frameIndex < start || frameIndex > end) continue;      // not alive this frame
                    float t = Mathf.Clamp01((frameIndex - start) / Mathf.Max(0.0001f, end - start));

                    // Scatter the centre within spawnRadius (uniform disc). spawnRadius is 0..1 of the explosion; 1
                    // reaches (almost) the canvas edge. Curve reads layer progress so it can expand over life.
                    // (Directional placement is now the job of the Ground modifier + Bars, not a per-layer mode.)
                    Vector2 c;
                    {
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

                    // Crescent mask offset (animatable).
                    float crescX = Eval(layer.crescentOffsetX, lp, spec.seed, li, si, F_CrescentX);
                    float crescY = Eval(layer.crescentOffsetY, lp, spec.seed, li, si, F_CrescentY);

                    // Circular spread: rotate this shape's offset around the centre for this instance.
                    if (spread > 1) c = Rotate(c, instRad);

                    // ── keep-on-screen guarantee ──────────────────────────────────
                    // A radial pixel-art explosion must never be clipped flat at the canvas edge, so cap the radius to
                    // the canvas half and clamp the centre so the whole shape fits. (Ground/Bars deliberately push
                    // content off-frame via their own warp/placement, downstream of this pre-warp scatter clamp.)
                    float effR = Mathf.Min(radius, half);
                    {
                        float absX = Mathf.Clamp(cx + c.x, effR, W - effR);
                        float absY = Mathf.Clamp(cy + c.y, effR, H - effR);
                        c = new Vector2(absX - cx, absY - cy);
                    }
                    radius = effR;

                    if (layer.shape == LayerShape.Sprite)
                    {
                        if (layer.particleSprite != null)
                        {
                            float startAng = (float)(rng.NextDouble() * 360.0);
                            float spin = Eval(layer.spriteSpin, t, spec.seed, li, si, F_SpriteSpin);
                            RasterSprite(buf, W, H, cx, cy, layer.particleSprite, c, radius, startAng + spin,
                                         baseCol, alpha, stack, frameIndex, t, shapeSeed);
                        }
                        continue;
                    }

                    float sparkleD = layer.shape == LayerShape.SparkleField
                        ? Mathf.Clamp01(Eval(layer.sparkleDensity, t, spec.seed, li, si, F_SparkleDensity)) : 1f;
                    // Sparkle sub-seed: re-rolls which pixels light up. Evaluated with the frame in the hash so a
                    // Min-Max value twinkles per frame; a Static value freezes the pattern.
                    int sparkleSub = layer.shape == LayerShape.SparkleField
                        ? Mathf.RoundToInt(Eval(layer.sparkleSeed, t, spec.seed, li, frameIndex, F_SparkleSeed) * 100000f) : 0;
                    // Disc-like edge params (hole + softness) apply to Disc AND SparkleField (a sparkle field is a disc).
                    bool discLike = layer.shape == LayerShape.Disc || layer.shape == LayerShape.SparkleField;
                    float holeSize = discLike && layer.hollow ? Mathf.Clamp01(Eval(layer.holeSize, t, spec.seed, li, si, F_HoleSize)) : 0f;
                    float outerSoft = discLike ? Mathf.Clamp01(Eval(layer.outerSoftness, t, spec.seed, li, si, F_OuterSoft)) : 0f;
                    float innerSoft = discLike && layer.hollow ? Mathf.Clamp01(Eval(layer.innerSoftness, t, spec.seed, li, si, F_InnerSoft)) : 0f;
                    // Hole centre can be offset off the shape centre (an offset hole = a crescent).
                    float holeOffX = 0f, holeOffY = 0f;
                    if (discLike && layer.hollow)
                    {
                        holeOffX = Eval(layer.holeOffsetX, t, spec.seed, li, si, F_HoleOffX) * radius;
                        holeOffY = Eval(layer.holeOffsetY, t, spec.seed, li, si, F_HoleOffY) * radius;
                    }
                    // Gradient position/zoom + a movable gradient CORE apply to BOTH spatial fill modes (Fill and
                    // Flow fill). Core offset is in pixels (normalised −1..1 × radius) — offset it + a bright→dark
                    // gradient makes a 3D orb / energy-ball highlight; animate it for a moving core.
                    float flowPos = 0f, flowZoom = 1f, gradX = 0f, gradY = 0f;
                    if (layer.colorMode != ColorMode.OverLife)
                    {
                        flowPos = Eval(layer.colorFlow, t, spec.seed, li, si, F_ColorFlow);
                        flowZoom = Eval(layer.colorFlowZoom, t, spec.seed, li, si, F_ColorZoom);
                        gradX = Eval(layer.gradientOffsetX, t, spec.seed, li, si, F_GradX) * radius;
                        gradY = Eval(layer.gradientOffsetY, t, spec.seed, li, si, F_GradY) * radius;
                    }
                    RasterShape(buf, W, H, cx, cy, framePhase, layer, c, radius, baseCol, alpha,
                                t, shapeSeed, crescX, crescY, stack, frameIndex, sparkleD, sparkleSub, holeSize, outerSoft, innerSoft, flowPos, flowZoom, gradX, gradY, holeOffX, holeOffY);
                }
              }
            }

            // Whole-frame post passes (Bloom, Outline) from the global list, in order, after everything composites.
            if (spec.globalModifiers != null)
                for (int i = 0; i < spec.globalModifiers.Count; i++)
                {
                    var m = spec.globalModifiers[i];
                    if (m == null || !m.enabled || !(m is PostModifier post)) continue;
                    m.Prepare((v, fid) => Eval(v, bp, spec.seed, GlobalLayerId, frameIndex, 1000 + (700 + i) * 8 + fid));
                    post.Apply(buf, W, H);
                }
            return buf;
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
        // each whole arm stacking over the previous one. Geometry + pixel modifiers apply to bars (in RasterBar).
        static void RenderBarsLayerStar(Color32[] buf, int W, int H, float cx, float cy,
                                        Layer layer, int li, BlastSpec spec, float lp, int frameIndex, float framePhase,
                                        float baseA, float spreadDeg, int spread, ModStack stack)
        {
            Vector2 center = new Vector2(cx, cy);
            bool star = layer.star;

            // per-layer bar knobs (multicontrols evaluated once over the layer's life)
            int B = Mathf.Max(0, Mathf.RoundToInt(Eval(layer.barCount, lp, spec.seed, li, 0, F_BarCount)));
            // Spacing is measured in bar-WIDTHS: 1 = bars exactly touch (pitch = one bar, no gap), 2 = one bar of gap
            // between them, etc. Width is pixels, floored at 1. So the centre-to-centre pitch is spacing * width.
            float spacing = Mathf.Max(1f, Eval(layer.barSpacing, lp, spec.seed, li, 0, F_BarSpacing));
            float backFrac = Mathf.Clamp01(Eval(layer.barBackwardFrac, lp, spec.seed, li, 0, F_BarBackward));
            float inset = Eval(layer.originInset, lp, spec.seed, li, 0, F_OriginInset);
            float taper = Mathf.Clamp(Eval(layer.barTaper, lp, spec.seed, li, 0, F_Taper), -1f, 1f);
            float stagger = Mathf.Max(0f, Eval(layer.barStagger, lp, spec.seed, li, 0, F_Stagger));
            float ang = Eval(layer.barAngleDeg, lp, spec.seed, li, 0, F_BarAngle);
            float barSoft = Mathf.Clamp01(Eval(layer.barSoftness, lp, spec.seed, li, 0, F_BarSoft));   // soft sides + tip

            bool dissolve = layer.barDecay == BarDecay.Dissolve;
            float width = Mathf.Max(1f, dissolve ? EvalRisingMax(layer.barWidth, lp, spec.seed, li, 0, F_BarWidth)
                                                 : Eval(layer.barWidth, lp, spec.seed, li, 0, F_BarWidth));
            float pitch = spacing * width;   // centre-to-centre distance between neighbouring bars
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
                Vector2 barCenter = origins[r] + perp * (i * pitch);
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

                RasterBar(buf, W, H, cx, cy, barCenter, dir, perp, -bwd, fwd, width, col, alpha,
                          stack, frameIndex, framePhase, tb, ShapeSeed(spec.seed, li, i), barSoft);
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

        // Fill a rotated rectangle: from alongMin..alongMax along `dir`, ±halfWidth across `perp`. When geometry
        // modifiers are present each pixel is un-warped into blast space first (so skew/rotate/squash/wobble apply
        // to bars too); that pushes the bar out of its axis-aligned bbox, so the whole canvas is scanned.
        static void RasterBar(Color32[] buf, int W, int H, float cx, float cy, Vector2 barCenter, Vector2 dir, Vector2 perp,
                              float alongMin, float alongMax, float width, Color col, float alpha,
                              ModStack stack, int frameIndex, float framePhase, float life, int hash, float soft)
        {
            float hw = width * 0.5f;
            float span = Mathf.Max(0.0001f, alongMax - alongMin);
            // Edge softness: fade alpha within a band of the (half-)width from the two SIDES and the TIP (alongMax).
            // The base (alongMin) stays hard so bars stay connected to their origin. Bands are in pixels.
            float sideBand = soft > 0.001f ? Mathf.Max(0.5f, soft * hw) : 0f;
            float tipBand = soft > 0.001f ? Mathf.Max(0.5f, soft * span) : 0f;
            int x0, x1, y0, y1;
            if (stack.AnyGeo)
            {
                x0 = 0; x1 = W - 1; y0 = 0; y1 = H - 1;
            }
            else
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int sa = 0; sa < 2; sa++)
                    for (int sp = 0; sp < 2; sp++)
                    {
                        Vector2 p = barCenter + dir * (sa == 0 ? alongMin : alongMax) + perp * (sp == 0 ? -hw : hw);
                        minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                        minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                    }
                x0 = Mathf.Max(0, Mathf.FloorToInt(minX)); x1 = Mathf.Min(W - 1, Mathf.CeilToInt(maxX));
                y0 = Mathf.Max(0, Mathf.FloorToInt(minY)); y1 = Mathf.Min(H - 1, Mathf.CeilToInt(maxY));
            }
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float wx = x + 0.5f, wy = y + 0.5f;
                    if (stack.AnyGeo) { Vector2 o = ApplyGeo(stack, new Vector2(wx - cx, wy - cy), framePhase, new GeoCtx(W * 0.5f, H * 0.5f, Vector2.zero, 0f)); wx = cx + o.x; wy = cy + o.y; }
                    float px = wx - barCenter.x, py = wy - barCenter.y;
                    float along = px * dir.x + py * dir.y;
                    float across = px * perp.x + py * perp.y;
                    if (along >= alongMin && along <= alongMax && across >= -hw && across <= hw)
                    {
                        float crossFrac = (along - alongMin) / span;
                        Color fc = col;
                        float outA = alpha * fc.a;
                        if (soft > 0.001f)
                        {
                            float e = Mathf.Clamp01((hw - Mathf.Abs(across)) / sideBand);   // fade near both sides
                            e *= Mathf.Clamp01((alongMax - along) / tipBand);               // fade near the tip
                            outA *= e;
                            if (outA <= 0.002f) continue;
                        }
                        if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, frameIndex, crossFrac, life, hash, W, H))
                            continue;
                        Over(buf, y * W + x, fc.r, fc.g, fc.b, outA);
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
        // Iterates the whole canvas, maps each pixel back through the opt-in geometry modifiers into undeformed
        // "blast space", runs the hard shape test there, then composites source-over. Whole-canvas iteration keeps
        // the warp correct with zero clipping risk; canvases are small and the runtime player caches its frames.
        static void RasterShape(Color32[] buf, int W, int H, float cx, float cy,
                                float framePhase, Layer layer, Vector2 c, float radius, Color baseCol, float alpha,
                                float t, int shapeSeed, float crescX, float crescY,
                                ModStack stack, int frameIndex, float sparkleDensity, int sparkleSub, float holeSize,
                                float outerSoft, float innerSoft, float flowPos, float flowZoom, float gradX, float gradY,
                                float holeOffX, float holeOffY)
        {
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    // Undo the opt-in geometry modifiers (outermost first) to reach undeformed blast space.
                    Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                    if (stack.AnyGeo) off = ApplyGeo(stack, off, framePhase, new GeoCtx(W * 0.5f, H * 0.5f, c, radius));
                    float ux = off.x, uy = off.y;

                    if (!ShapeHit(layer, ux, uy, c, radius, t, shapeSeed, x, y, baseCol, crescX, crescY, sparkleDensity, sparkleSub, holeSize, holeOffX, holeOffY, out Color col))
                        continue;

                    // Normalised distance from the shape centre (0 = centre, 1 = edge) — drives the outer softness AND
                    // the Tint modifier's cross gradient / fill (crossFrac).
                    float nd = radius > 0.001f
                        ? Mathf.Clamp01(Mathf.Sqrt((ux - c.x) * (ux - c.x) + (uy - c.y) * (uy - c.y)) / radius) : 0f;

                    // Disc/Sparkle edge softness: fade alpha near the hole's inner edge (measured from the — possibly
                    // offset — HOLE centre) and near the outer edge (nd = 1). The inner fade scales by hole size, so it
                    // does nothing when there's no hole and grows with it.
                    float pixelAlpha = alpha;
                    if (layer.shape == LayerShape.Disc || layer.shape == LayerShape.SparkleField)
                    {
                        if (innerSoft > 0.001f && holeSize > 0.001f)
                        {
                            float hnd = radius > 0.001f
                                ? Mathf.Sqrt((ux - (c.x + holeOffX)) * (ux - (c.x + holeOffX)) +
                                             (uy - (c.y + holeOffY)) * (uy - (c.y + holeOffY))) / radius : 0f;
                            pixelAlpha *= Mathf.Clamp01((hnd - holeSize) / (innerSoft * holeSize));
                        }
                        if (outerSoft > 0.001f) pixelAlpha *= Mathf.Clamp01((1f - nd) / outerSoft);
                    }
                    if (pixelAlpha <= 0.001f) continue;

                    // Colour mode: Over life = the per-shape colour; Fill = the gradient across the shape (centre→
                    // edge); Flow fill = that spatial fill scrolled through the gradient over life. (Sparkle keeps
                    // its own per-pixel shimmer.)
                    Color fc = col;
                    if (layer.colorMode != ColorMode.OverLife && layer.shape != LayerShape.SparkleField && layer.colorOverLife != null)
                    {
                        // Colour distance is measured from the (optionally offset) gradient core, not the shape centre
                        // — so the highlight can sit off-centre for a 3D orb. Edge softness still uses `nd`.
                        float cnd = nd;
                        if (gradX != 0f || gradY != 0f)
                            cnd = radius > 0.001f
                                ? Mathf.Clamp01(Mathf.Sqrt((ux - (c.x + gradX)) * (ux - (c.x + gradX)) +
                                                           (uy - (c.y + gradY)) * (uy - (c.y + gradY))) / radius) : 0f;
                        float frac;
                        if (layer.colorMode == ColorMode.FlowingFill)
                        {
                            // Flow position scrolls the (mirrored) gradient; zoom sets how much of it spans the
                            // shape. Mirror (sample 0→1→0) so the scroll loops seamlessly with no hard seam.
                            float f = Mathf.Repeat(cnd * flowZoom + flowPos, 1f);
                            frac = 1f - Mathf.Abs(2f * f - 1f);
                        }
                        else frac = Mathf.Clamp01(cnd * flowZoom + flowPos);   // Fill: gradient position + zoom now apply here too
                        fc = layer.colorOverLife.Evaluate(frac);
                    }
                    float outA = pixelAlpha * fc.a;
                    if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, frameIndex, nd, t, shapeSeed, W, H))
                        continue;
                    Over(buf, y * W + x, fc.r, fc.g, fc.b, outA);
                }
            }
        }

        // ── Sprite particles: stamp a sprite's pixels at each particle, scaled + rotated. ─────────────────
        // Sprite pixels are read once and cached (GetPixels needs the texture read/write-enabled). Editor edits in
        // Aseprite reimport the texture; call ClearSpriteCache to see them without a domain reload.
        static readonly System.Collections.Generic.Dictionary<Sprite, (Color[] px, int w, int h)> spriteCache = new();

        /// Drop the cached sprite pixels (e.g. after a sprite was edited/reimported).
        public static void ClearSpriteCache() => spriteCache.Clear();

        static bool TryGetSpritePixels(Sprite s, out Color[] px, out int w, out int h)
        {
            px = null; w = 0; h = 0;
            if (s == null || s.texture == null) return false;
            if (spriteCache.TryGetValue(s, out var e)) { px = e.px; w = e.w; h = e.h; return px != null; }
            Color[] got = null; int gw = 0, gh = 0;
            try
            {
                Rect r = s.textureRect;
                gw = Mathf.Max(1, (int)r.width); gh = Mathf.Max(1, (int)r.height);
                got = s.texture.GetPixels((int)r.x, (int)r.y, gw, gh);
            }
            catch { got = null; }
            spriteCache[s] = (got, gw, gh);
            px = got; w = gw; h = gh;
            return got != null;
        }

        // Stamp one sprite particle: fit the sprite's larger dimension to 2*radius, rotate by angleDeg, tint by the
        // layer colour, composite. Pixel modifiers (tint/dissolve/mask) apply; geometry modifiers don't (like bars).
        static void RasterSprite(Color32[] buf, int W, int H, float cx, float cy, Sprite sprite, Vector2 c,
                                 float radius, float angleDeg, Color tint, float alpha,
                                 ModStack stack, int frameIndex, float life, int hash)
        {
            if (radius < 0.25f || !TryGetSpritePixels(sprite, out var spx, out int sw, out int sh)) return;
            float cxp = cx + c.x, cyp = cy + c.y;
            float scale = (2f * radius) / Mathf.Max(sw, sh);
            if (scale <= 1e-4f) return;
            float rad = -angleDeg * Mathf.Deg2Rad;                 // inverse-rotate canvas → sprite space
            float cosr = Mathf.Cos(rad), sinr = Mathf.Sin(rad);
            float box = radius * 1.5f;                              // generous bbox to cover rotation
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cxp - box)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cxp + box));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cyp - box)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cyp + box));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f) - cxp, dy = (y + 0.5f) - cyp;
                    float lx = dx * cosr - dy * sinr, ly = dx * sinr + dy * cosr;
                    int su = Mathf.FloorToInt(lx / scale + sw * 0.5f);
                    int sv = Mathf.FloorToInt(ly / scale + sh * 0.5f);
                    if (su < 0 || su >= sw || sv < 0 || sv >= sh) continue;
                    Color sc = spx[sv * sw + su];
                    if (sc.a <= 0.003f) continue;

                    Color fc = new Color(sc.r * tint.r, sc.g * tint.g, sc.b * tint.b, 1f);
                    float outA = sc.a * alpha * tint.a;
                    float crossFrac = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(0.001f, radius));
                    if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, frameIndex, crossFrac, life, hash, W, H))
                        continue;
                    Over(buf, y * W + x, fc.r, fc.g, fc.b, outA);
                }
        }

        // Returns whether this undeformed pixel is inside the shape, and the colour to lay down.
        static bool ShapeHit(Layer layer, float ux, float uy, Vector2 c, float radius, float t,
                             int shapeSeed, int x, int y, Color baseCol, float crescX, float crescY,
                             float sparkleDensity, int sparkleSub, float holeSize, float holeOffX, float holeOffY, out Color col)
        {
            col = baseCol;
            float dx = ux - c.x, dy = uy - c.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            // Hole distance is measured from the (optionally offset) hole centre — an offset hole carves a crescent.
            float hdx = ux - (c.x + holeOffX), hdy = uy - (c.y + holeOffY);
            float holeDist = Mathf.Sqrt(hdx * hdx + hdy * hdy);

            switch (layer.shape)
            {
                case LayerShape.Disc:
                {
                    if (dist > radius) return false;
                    // holeSize = inner radius fraction (0 = full disc; a Hollow disc carves a — possibly offset — hole).
                    return holeDist >= holeSize * radius;
                }

                case LayerShape.SparkleField:
                {
                    if (dist > radius || holeDist < holeSize * radius) return false;   // disc, with an optional hole
                    // Which pixels light up: hashed with the sub-seed so it re-rolls (twinkles) when animated.
                    float h = Hash01(shapeSeed ^ sparkleSub, x, y);
                    if (h >= sparkleDensity) return false;
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

        // Stable 0..1 hash of three ints — used for per-pixel sparkle / disintegrate / dissolve so results don't
        // depend on iteration order and never touch UnityEngine.Random. Internal so PixelModifiers can share it.
        internal static float Hash01(int a, int b, int c)
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

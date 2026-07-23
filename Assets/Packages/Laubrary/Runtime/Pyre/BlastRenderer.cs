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
                  F_BarForward = 13,
                  F_BarSpacing = 14, F_BarWidth = 15, F_BarBackward = 16, F_BarAngle = 17,
                  F_OriginInset = 18, F_BarCount = 19;
        const int F_Squash = 20, F_Skew = 21, F_WobAmp = 22, F_WobFreq = 23, F_Rot = 24;
        const int F_BaseAngle = 25, F_SpreadDeg = 26, F_Taper = 27, F_Stagger = 28;
        const int F_CrossAmt = 30, F_Contrast = 31, F_Brightness = 32, F_Saturation = 33;
        const int F_SparkleDensity = 34, F_HoleSize = 38, F_SpriteSpin = 39;
        const int F_InnerSoft = 40, F_OuterSoft = 41, F_ColorFlow = 42, F_ColorZoom = 43, F_SparkleSeed = 44;
        const int F_GradX = 45, F_GradY = 46, F_HoleOffX = 47, F_HoleOffY = 48, F_BarSoft = 49;
        const int F_MetaRadius = 50, F_MetaExpand = 51, F_MetaFlow = 52, F_MetaFlowZoom = 53;
        const int F_NoiseZoom = 54, F_NoiseRot = 55, F_NoiseDriftX = 56, F_NoiseDriftY = 57;
        const int F_RingStart = 58, F_RingArc = 59, F_SpinDegrees = 60;
        const int F_SparkleBlobRadius = 61, F_SparkleBlobLife = 62, F_SparkleBlobSoft = 63;
        const int F_RingExpand = 64;
        const int F_NoiseGradPos = 65;
        const int F_NoiseGradZoom = 66;
        const int F_NoiseWarp = 67;
        const int F_HbDensity = 70, F_HbBaseHeat = 71, F_HbChurn = 72;
        const int F_HbWavePush = 75, F_HbLightAngle = 76;
        const int F_HbSpread = 77, F_HbGroupAlpha = 78, F_HbRotation = 79;
        const int F_MatteStrength = 90, F_MatteAmount = 91, F_MatteHue = 92, F_MatteDisplace = 93;
        const int F_FireDir = 100, F_FireWidth = 101, F_FireInset = 102, F_FireHeat = 103, F_FireFuel = 104;
        const int F_FirePulse = 105, F_FireFlow = 106, F_FireBuoy = 107, F_FireCurl = 108, F_FireCurlScale = 109;
        const int F_FireFlicker = 110, F_FireDissip = 111, F_FireBurn = 112, F_FireReach = 113, F_FireEdgeCool = 114;
        const int F_FireStretch = 115, F_FirePinch = 116, F_FireBreakup = 117, F_FireIntensity = 118;
        const float DissolveBand = 0.22f;   // soft width of the bar-dissolve front
        const int GlobalLayerId = -1;   // stands in for "no layer" when hashing global modifiers

        // ── modifier pipeline (Pyre v2): opt-in geometry warps + pixel effects, per-layer and global ─────────
        readonly struct ModStack
        {
            public readonly GeometryModifier[] geo;   // forward order; inverse-apply in reverse
            public readonly PixelModifier[] pix;      // apply in order
            public readonly EdgeModifier[] edge;      // perturb only the outer silhouette test (RasterShape)
            public ModStack(GeometryModifier[] g, PixelModifier[] p, EdgeModifier[] ed) { geo = g; pix = p; edge = ed; }
            public bool AnyGeo => geo.Length > 0;
            public bool AnyPix => pix.Length > 0;
            public bool AnyEdge => edge.Length > 0;
        }
        static readonly ModStack EmptyStack = new ModStack(Array.Empty<GeometryModifier>(), Array.Empty<PixelModifier>(), Array.Empty<EdgeModifier>());

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

        static ModStack BuildStack(Layer layer, Pyre spec, int li, float lp, float bp, int frameIndex)
        {
            var geo = new System.Collections.Generic.List<GeoEntry>();
            var pix = new System.Collections.Generic.List<PixelModifier>();
            var edge = new System.Collections.Generic.List<EdgeModifier>();
            CollectMods(layer != null ? layer.modifiers : null, spec, li, lp, frameIndex, geo, pix, edge, 0, 0);
            CollectMods(spec != null ? spec.globalModifiers : null, spec, GlobalLayerId, bp, frameIndex, geo, pix, edge, 500, GlobalPassOffset);
            if (geo.Count == 0 && pix.Count == 0 && edge.Count == 0) return EmptyStack;
            // Sort ascending by effective pass so the array ends with the highest; ApplyGeo (back-to-front) then
            // applies them first. Insertion sort keeps it stable so same-pass modifiers keep authoring order.
            if (geo.Count > 1) SortGeoStable(geo);
            var arr = new GeometryModifier[geo.Count];
            for (int i = 0; i < geo.Count; i++) arr[i] = geo[i].mod;
            return new ModStack(arr, pix.ToArray(), edge.ToArray());
        }

        static void CollectMods(System.Collections.Generic.List<PyreModifier> mods, Pyre spec, int layerId,
                                float progress, int frameIndex,
                                System.Collections.Generic.List<GeoEntry> geo,
                                System.Collections.Generic.List<PixelModifier> pix,
                                System.Collections.Generic.List<EdgeModifier> edge, int baseId, int passOffset)
        {
            if (mods == null) return;
            int seed = spec != null ? spec.seed : 0;
            Vector2 originPx = spec != null ? new Vector2(spec.origin.x * spec.Width, spec.origin.y * spec.Height) : Vector2.zero;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null || !m.enabled) continue;
                if (m is PostModifier) continue;   // whole-frame passes run after compositing, not in the per-shape stack
                int uid = baseId + i;
                m.Prepare((v, fid) => Eval(v, progress, seed, layerId, frameIndex, 1000 + uid * 8 + fid));
                if (m is PinWarpModifier pinWarp) pinWarp.SetFrame(frameIndex);   // pins key off the raw frame, not a 0..1 progress
                if (m is VoronoiCrackModifier vcm) vcm.SetOrigin(originPx);       // rotation pivots on the blast's Origin marker
                if (m is GeometryModifier gm) geo.Add(new GeoEntry { mod = gm, pass = gm.WarpPass + passOffset });
                else if (m is EdgeModifier em) edge.Add(em);
                else if (m is PixelModifier pm) pix.Add(pm);
            }
        }

        // Runs a layer's own PostModifiers (Bloom/Outline/Fuse/...) on its isolated buffer, then composites the
        // result onto the frame. A no-op when hasPost is false — layerTarget IS buf already in that case, so
        // there is nothing to run or composite back. `life` is this layer's own life (lp), matching every other
        // layer-scoped animatable param — NOT the whole blast's progress (that's only meaningful for the GLOBAL
        // post pass at the bottom of RenderFrame, which is untouched by this).
        /// The matte in force while compositing a stack, carried between layers.
        struct MatteState
        {
            public float[] mask;
            public MatteChannel channel;
            public float blur, displace, hue;
            public bool oneShot;      // MatteScope.NextLayer — cleared after it has masked one drawn layer
        }

        static void FinishLayerPost(Color32[] buf, Color32[] layerTarget, bool hasPost, Layer layer, Pyre spec,
                                    int li, float life, int frameIndex, int W, int H, ref MatteState matte)
        {
            if (!hasPost) return;
            var mods = layer.modifiers;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m == null || !m.enabled) continue;
                if (m is PostModifier post)
                {
                    post.SetLife(life);
                    post.SetSeed(spec.seed);
                    post.SetFrameIndex(frameIndex);
                    m.Prepare((v, fid) => Eval(v, life, spec.seed, li, frameIndex, 1000 + i * 8 + fid));
                    post.Apply(layerTarget, W, H);
                }
            }

            // This layer's own SIMULATION modifier (its own dedicated slot, separate from Pyre's
            // blast-wide one) — runs last within THIS layer's own isolated buffer, before it composites onto
            // the frame. See Layer.simulationModifier's own doc for how it composes with the global slot.
            if (layer.simulationModifier is SimulationModifier layerSim && layerSim.enabled)
            {
                layerSim.SetSeed(spec.seed);
                Func<int, Func<ZUIValue, int, float>> layerSimParamsForFrame = f =>
                {
                    float flp = Mathf.Clamp01((f - layer.startFrame) / (float)Mathf.Max(1, layer.endFrame - layer.startFrame));
                    return (v, fid) => Eval(v, flp, spec.seed, li, f, 1000 + 800 * 8 + fid);
                };
                layerSim.EnsureFrame(frameIndex, layerTarget, W, H, layerSimParamsForFrame);
                layerSim.Render(layerTarget, W, H);
            }

            // A MATTE layer is captured here rather than composited — and it captures its FINISHED pixels, so
            // its own modifier stack (a blur, a warp, a noise fill) shapes the mask, which is most of why this
            // is worth having. A matte is never itself masked by a previous matte: stacking mattes on mattes
            // has no clear authoring meaning, and quietly doing something would be worse than not.
            if (layer.role == LayerRole.Matte)
            {
                float strength = Mathf.Clamp01(Eval(layer.matteStrength, life, spec.seed, li, frameIndex, F_MatteStrength));
                matte = new MatteState
                {
                    mask = BuildMatteMask(layerTarget, layer.matteInvert, strength),
                    channel = layer.matteChannel,
                    blur = Eval(layer.matteAmount, life, spec.seed, li, frameIndex, F_MatteAmount),
                    displace = Eval(layer.matteDisplaceAmount, life, spec.seed, li, frameIndex, F_MatteDisplace),
                    hue = Eval(layer.matteHueDegrees, life, spec.seed, li, frameIndex, F_MatteHue),
                    oneShot = layer.matteScope == MatteScope.NextLayer,
                };
                return;
            }

            // The matte acts AFTER this layer's own post/simulation pass — it masks the finished layer, not an
            // intermediate one — and before it composites, so it never touches what is already on the frame.
            if (matte.mask != null)
            {
                ApplyMatte(layerTarget, matte.mask, matte.channel, matte.blur, matte.displace, matte.hue, W, H);
                if (matte.oneShot) matte = default;   // NextLayer scope: spent on this one layer
            }

            for (int i = 0; i < layerTarget.Length; i++)
            {
                var c = layerTarget[i];
                if (c.a == 0) continue;
                Over(buf, i, c.r * (1f / 255f), c.g * (1f / 255f), c.b * (1f / 255f), c.a * (1f / 255f));
            }
        }

        // ── Fire ─────────────────────────────────────────────────────────────────────────────────────────
        // Reached by REPLAY, not by evaluation: a flame carries state, so frame N is produced by running the
        // simulation from a reset up to N. That is the same discipline SimulationModifier uses, and for the
        // same reason — it is the only version that cannot show a frame built under stale dial values. One
        // sim instance is reused so the arrays aren't reallocated every frame; it is always Reset first.
        // One FireSim per Layer, kept across calls so PLAYBACK can step forward one frame instead of replaying
        // the whole history every repaint — the O(frames²)-per-repaint cost that made the preview crawl. A
        // weak table means a deleted layer's sim is collected on its own, and a cloned layer (a fresh Layer
        // object) naturally gets its own entry rather than sharing one. Keyed by object identity, so two specs
        // that happen to hold equal layers still get separate sims.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layer, FireSim> _fireSims = new();

        static FireParams FireParamsAt(Layer layer, Pyre spec, int li, int frameIndex)
        {
            float lp = Mathf.Clamp01((frameIndex - layer.startFrame) /
                                     (float)Mathf.Max(1, layer.endFrame - layer.startFrame));
            float E(ZUIValue v, int fid) => Eval(v, lp, spec.seed, li, frameIndex, fid);
            // Intensity is the burn's progress envelope: it scales what the emitter puts out, so at 0 the
            // fire is off and the existing heat dies away, and at 1 it burns full. This is the "control the
            // progress with a curve, not a speed" dial.
            float intensity = Mathf.Clamp01(E(layer.fireIntensity, F_FireIntensity));
            return new FireParams
            {
                arms = Mathf.Max(1, layer.fireArms),
                armMode = layer.fireArmMode,
                steps = Mathf.Max(1, layer.fireSteps),
                directionDeg = E(layer.fireDirection, F_FireDir),
                emitterWidth = Mathf.Max(1f, E(layer.fireEmitterWidth, F_FireWidth)),
                emitterInset = E(layer.fireEmitterInset, F_FireInset),
                heat = Mathf.Clamp01(E(layer.fireHeat, F_FireHeat) * intensity),
                fuel = Mathf.Clamp01(E(layer.fireFuel, F_FireFuel) * intensity),
                pulse = Mathf.Max(0f, E(layer.firePulse, F_FirePulse)),
                flow = E(layer.fireFlow, F_FireFlow),
                buoyancy = E(layer.fireBuoyancy, F_FireBuoy),
                curl = Mathf.Max(0f, E(layer.fireCurl, F_FireCurl)),
                curlScale = Mathf.Max(2f, E(layer.fireCurlScale, F_FireCurlScale)),
                flicker = Mathf.Max(0f, E(layer.fireFlicker, F_FireFlicker)),
                stretch = Mathf.Max(0f, E(layer.fireStretch, F_FireStretch)),
                pinch = Mathf.Max(0f, E(layer.firePinch, F_FirePinch)),
                breakup = Mathf.Max(0f, E(layer.fireBreakup, F_FireBreakup)),
                dissipation = Mathf.Max(0f, E(layer.fireDissipation, F_FireDissip)),
                burn = Mathf.Max(0f, E(layer.fireBurn, F_FireBurn)),
                reach = Mathf.Clamp01(E(layer.fireReach, F_FireReach)),
                edgeCooling = Mathf.Clamp01(E(layer.fireEdgeCooling, F_FireEdgeCool)),
            };
        }

        // dt is ONE FRAME, split across the substeps — so every velocity dial reads in PIXELS PER FRAME and
        // every rate dial in per-frame terms. Anything else makes the numbers meaningless to author against:
        // an early version used 1/12 here and a buoyancy of 0.5 moved heat 0.02px per step, so the "flame" was
        // the emitter disc and nothing else.
        const float FireDt = 1f;

        static void StepFire(FireSim sim, Layer layer, Pyre spec, int li, int seed, int f)
        {
            var p = FireParamsAt(layer, spec, li, f);
            int steps = Mathf.Max(1, layer.fireSteps);
            float t = Mathf.Clamp01((f - layer.startFrame) / (float)Mathf.Max(1, layer.endFrame - layer.startFrame));
            for (int s = 0; s < steps; s++)
                sim.Step(p, seed, t + s / (float)steps * 0.01f, FireDt / steps);
        }

        static void RenderFire(Color32[] target, int W, int H, Layer layer, Pyre spec, int li,
                               int frameIndex, float layerAlpha)
        {
            var sim = _fireSims.GetValue(layer, _ => new FireSim());
            bool sizeChanged = sim.W != W || sim.H != H;
            sim.Allocate(W, H);

            int seed = ShapeSeed(spec.seed, li, 0);
            int last = Mathf.Clamp(frameIndex, layer.startFrame, layer.endFrame);

            // The cheap path: exactly one frame past where the sim already is (normal forward playback) → step
            // once. Any other request — a scrub backward, the same frame re-asked while a dial is being
            // dragged, a size change — replays from the layer's start. Replaying on a same-frame re-request is
            // what makes a dial edit take effect: the parameters of every earlier frame changed too, so the
            // whole history has to be re-run under the new values (the exact reason SimulationModifier dropped
            // its checkpoint shortcut). This mirrors SimulationModifier.EnsureFrame.
            if (!sizeChanged && sim.LastFrame >= layer.startFrame && last == sim.LastFrame + 1)
            {
                StepFire(sim, layer, spec, li, seed, last);
                sim.LastFrame = last;
            }
            else
            {
                sim.Reset();
                for (int f = layer.startFrame; f <= last; f++)
                {
                    StepFire(sim, layer, spec, li, seed, f);
                    sim.LastFrame = f;
                }
            }

            sim.Render(target, layer.colorOverLife, layerAlpha, layer.fireThreshold, layer.fireContrast);
        }

        // ── mattes ───────────────────────────────────────────────────────────────────────────────────────
        // A matte layer's rendered pixels are turned into a 0..1 mask and then drive the layers above it.
        // Mask = luminance × the layer's own alpha, so BOTH how bright a shape is and where its silhouette
        // actually falls count — a plain white disc on transparent gives exactly the disc.

        /// Rec. 601 luma, matching how the eye weights the channels; a green flame reads brighter than a blue
        /// one of the same numeric value, which is what you want when authoring a mask by eye.
        static float Luma(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * (1f / 255f);

        static float[] BuildMatteMask(Color32[] src, bool invert, float strength)
        {
            var mask = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                float m = Luma(c) * (c.a * (1f / 255f));
                if (invert) m = 1f - m;
                mask[i] = Mathf.Clamp01(m) * strength;
            }
            return mask;
        }

        static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
            => Color.RGBToHSV(new Color(r, g, b), out h, out s, out v);

        /// Apply a matte to one layer's isolated buffer, in place, just before it composites. `channel` is a
        /// FLAG SET — every enabled channel acts, in a fixed order: the spatial ones first (they move/soften
        /// pixels), then colour, then alpha last (so a masked-away pixel isn't recoloured pointlessly). None
        /// (an unset field on an old asset) falls back to Alpha, the original single-channel default.
        static void ApplyMatte(Color32[] target, float[] mask, MatteChannel channel, float blurAmount,
                               float displaceAmount, float hueDegrees, int W, int H)
        {
            if (channel == MatteChannel.None) channel = MatteChannel.Alpha;

            if ((channel & MatteChannel.Displace) != 0) MatteDisplace(target, mask, displaceAmount, W, H);
            if ((channel & MatteChannel.Blur) != 0) MatteBlur(target, mask, blurAmount, W, H);
            if ((channel & MatteChannel.Saturation) != 0) MatteSaturation(target, mask);
            if ((channel & MatteChannel.Hue) != 0) MatteHue(target, mask, hueDegrees);
            if ((channel & MatteChannel.Brightness) != 0) MatteBrightness(target, mask);
            if ((channel & MatteChannel.Alpha) != 0) MatteAlpha(target, mask);
        }

        static void MatteAlpha(Color32[] target, float[] mask)
        {
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                target[i] = new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(c.a * mask[i]));
            }
        }

        static void MatteBrightness(Color32[] target, float[] mask)
        {
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                float m = mask[i];
                target[i] = new Color32((byte)(c.r * m), (byte)(c.g * m), (byte)(c.b * m), c.a);
            }
        }

        static void MatteSaturation(Color32[] target, float[] mask)
        {
            // Mask LOW drains to grey, so a matte reads as "this is where the colour has burned out".
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                float y = Luma(c) * 255f;
                float m = mask[i];
                target[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.Lerp(y, c.r, m), 0f, 255f),
                    (byte)Mathf.Clamp(Mathf.Lerp(y, c.g, m), 0f, 255f),
                    (byte)Mathf.Clamp(Mathf.Lerp(y, c.b, m), 0f, 255f), c.a);
            }
        }

        static void MatteHue(Color32[] target, float[] mask, float hueDegrees)
        {
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                float m = mask[i];
                if (m <= 0.0001f) continue;
                RgbToHsv(c.r / 255f, c.g / 255f, c.b / 255f, out float h, out float s, out float v);
                h = Mathf.Repeat(h + (hueDegrees / 360f) * m, 1f);
                var rgb = Color.HSVToRGB(h, s, v);
                target[i] = new Color32((byte)(rgb.r * 255f), (byte)(rgb.g * 255f), (byte)(rgb.b * 255f), c.a);
            }
        }

        static void MatteBlur(Color32[] target, float[] mask, float amount, int W, int H)
        {
            // Per-pixel variable radius, so one matte can hold a shape sharp while its surroundings melt.
            // Separable would be faster but is wrong here: the radius differs per pixel, so the two passes
            // wouldn't agree on a kernel. Canvases are small (Pyre is pixel art), the radius is clamped, so a
            // direct box gather is affordable and exact.
            var src = (Color32[])target.Clone();
            int maxR = Mathf.Clamp(Mathf.CeilToInt(amount), 0, 12);
            if (maxR == 0) return;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int r = Mathf.RoundToInt(amount * mask[i]);
                    if (r <= 0) continue;
                    float ar = 0f, ag = 0f, ab = 0f, aa = 0f, n = 0f;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                            var s2 = src[sy * W + sx];
                            float a = s2.a * (1f / 255f);
                            ar += s2.r * a; ag += s2.g * a; ab += s2.b * a; aa += a; n += 1f;
                        }
                    if (n <= 0f || aa <= 0.0001f) continue;
                    target[i] = new Color32((byte)Mathf.Clamp(ar / aa, 0f, 255f),
                                            (byte)Mathf.Clamp(ag / aa, 0f, 255f),
                                            (byte)Mathf.Clamp(ab / aa, 0f, 255f),
                                            (byte)Mathf.Clamp(aa / n * 255f, 0f, 255f));
                }
        }

        static void MatteDisplace(Color32[] target, float[] mask, float amount, int W, int H)
        {
            // Push each pixel along the mask's own SLOPE (its gradient), not along the mask's value — that is
            // what makes it read as refraction: flat regions of the mask don't move at all, and only its
            // EDGES bend what's behind them, exactly like a lens.
            var src = (Color32[])target.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int xm = Mathf.Max(0, x - 1), xp = Mathf.Min(W - 1, x + 1);
                    int ym = Mathf.Max(0, y - 1), yp = Mathf.Min(H - 1, y + 1);
                    float gx = mask[y * W + xp] - mask[y * W + xm];
                    float gy = mask[yp * W + x] - mask[ym * W + x];
                    if (gx == 0f && gy == 0f) continue;
                    int sx = Mathf.Clamp(x + Mathf.RoundToInt(gx * amount), 0, W - 1);
                    int sy = Mathf.Clamp(y + Mathf.RoundToInt(gy * amount), 0, H - 1);
                    target[i] = src[sy * W + sx];
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

        // Run the pixel modifiers on a hit pixel; false = drop it. wx/wy = the geometry-warped canvas position
        // (see PixelInfo) — pass x+0.5/y+0.5 at call sites where no geometry warp applies (e.g. RasterSprite).
        static bool ApplyPix(in ModStack s, ref Color col, ref float alpha, int x, int y, float wx, float wy, int frame,
                             float crossFrac, float life, int hash, int W, int H)
        {
            var info = new PixelInfo(x, y, wx, wy, frame, crossFrac, life, hash, W, H);
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
        public static Color32[] RenderFrame(Pyre spec, int frameIndex)
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
            // The matte currently in force, if any. A Matte layer captures itself into this instead of
            // compositing; the layers above it then read it. Carried through FinishLayerPost by ref because
            // that is the ONE place every layer path converges — the render dispatch above it has a dozen
            // early exits, and duplicating capture logic at each was how this would rot.
            var matteState = default(MatteState);

            for (int li = 0; li < spec.layers.Count; li++)
            {
                var layer = spec.layers[li];
                if (layer == null || !layer.enabled) continue;

                // Layer life progress: the curve envelope of any animated value spans exactly the frames this
                // layer exists — frame startFrame → 0, frame endFrame → 1 — NOT the whole blast.
                float lp = Mathf.Clamp01((frameIndex - layer.startFrame) /
                                         (float)Mathf.Max(1, layer.endFrame - layer.startFrame));

                ModStack stack = BuildStack(layer, spec, li, lp, bp, frameIndex);

                // A layer with its own enabled PostModifiers (Bloom/Outline/Fuse/...) draws into an ISOLATED
                // buffer first, so its post pass only sees/affects this layer's own pixels — never the pixels of
                // layers already composited below it — then that buffer composites onto the frame. Layers with no
                // post modifiers (the overwhelming common case) skip this entirely and draw straight into `buf`.
                bool hasLayerSim = layer.simulationModifier is SimulationModifier layerSim0 && layerSim0.enabled;
                // A matte layer, and any layer a matte is acting on, MUST render into its own buffer: a matte
                // reads (or masks) exactly one layer's pixels, and drawing straight into `buf` would mean
                // reading — or worse, masking — everything already composited below it.
                // Fire ALSO needs its own buffer: FireSim.Render OVERWRITES its target pixels (it isn't a
                // per-pixel alpha compositor), so drawing it straight into `buf` would replace the layers
                // already composited below with the flame's own — often low — alpha, punching a hole clear
                // through to the background wherever the flame is dim. Its own transparent buffer + the
                // FinishLayerPost Over pass composites it correctly instead.
                bool hasLayerPost = hasLayerSim || layer.shape == LayerShape.Fire
                    || layer.role == LayerRole.Matte || matteState.mask != null ||
                    (layer.modifiers != null && layer.modifiers.Exists(m => m != null && m.enabled && m is PostModifier));
                Color32[] layerTarget = hasLayerPost ? new Color32[W * H] : buf;

                // Per-layer star: how many rotated copies and the arc they span.
                int spread = layer.star ? Mathf.Max(1, layer.spreadCount) : 1;
                float spreadDeg = layer.star ? Eval(layer.spreadDegrees, lp, spec.seed, li, 0, F_SpreadDeg) : 0f;

                // Bars mode is a wholly different, directional composition — draws all its arms itself.
                if (layer.shape == LayerShape.Bars)
                {
                    float baseA = Eval(layer.baseAngleDeg, lp, spec.seed, li, 0, F_BaseAngle);
                    RenderBarsLayerStar(layerTarget, W, H, cx, cy, layer, li, spec, lp, frameIndex, framePhase, baseA, spreadDeg, spread, stack);
                    FinishLayerPost(buf, layerTarget, hasLayerPost, layer, spec, li, lp, frameIndex, W, H, ref matteState);
                    continue;
                }

                // MetaBlob: the placed orbs fuse into one gradient-shaded shape (SDF metaballs). Drawn as a whole.
                if (layer.shape == LayerShape.MetaBlob)
                {
                    float mAlpha = Mathf.Clamp01(Eval(layer.alpha, lp, spec.seed, li, 0, F_Alpha));
                    // Layer-wide motion (one value shared by every orb, animated over the layer's life).
                    float radScale = Mathf.Max(0f, Eval(layer.metaRadiusScale, lp, spec.seed, li, 0, F_MetaRadius));
                    float expand = Eval(layer.metaExpand, lp, spec.seed, li, 0, F_MetaExpand);
                    // Expansion pivots on the blast origin (the spawn/pivot pixel), expressed as an offset from centre.
                    Vector2 originOff = new Vector2((spec.origin.x - 0.5f) * W, (spec.origin.y - 0.5f) * H);
                    // Same gradient options as Disc/Crescent's Fill/Flow fill: Over life needs neither (flat colour
                    // by life); Fill and Flow fill both honour Gradient position/zoom, only their frac wrap differs.
                    bool metaNoiseFill = layer.colorMode == ColorMode.NoiseFill;
                    bool metaNeedsFlow = layer.colorMode != ColorMode.OverLife && !metaNoiseFill;
                    float flowPos = metaNeedsFlow ? Eval(layer.colorFlow, lp, spec.seed, li, 0, F_MetaFlow) : 0f;
                    float flowZoom = metaNeedsFlow ? Eval(layer.colorFlowZoom, lp, spec.seed, li, 0, F_MetaFlowZoom) : 1f;
                    float metaNoiseZoom = 20f, metaNoiseRot = 0f, metaNoiseDriftX = 0f, metaNoiseDriftY = 0f, metaNoiseGradPos = 0f, metaNoiseGradZoom = 1f, metaNoiseWarp = 0.6f;
                    if (metaNoiseFill)
                    {
                        metaNoiseZoom = Mathf.Max(1f, Eval(layer.noiseZoom, lp, spec.seed, li, 0, F_NoiseZoom));
                        metaNoiseRot = Eval(layer.noiseRotation, lp, spec.seed, li, 0, F_NoiseRot) * Mathf.Deg2Rad;
                        metaNoiseDriftX = Eval(layer.noiseDriftX, lp, spec.seed, li, 0, F_NoiseDriftX);
                        metaNoiseDriftY = Eval(layer.noiseDriftY, lp, spec.seed, li, 0, F_NoiseDriftY);
                        metaNoiseGradPos = Eval(layer.noiseGradientPosition, lp, spec.seed, li, 0, F_NoiseGradPos);
                        metaNoiseGradZoom = Eval(layer.noiseGradientZoom, lp, spec.seed, li, 0, F_NoiseGradZoom);
                        metaNoiseWarp = Eval(layer.noiseWarp, lp, spec.seed, li, 0, F_NoiseWarp);
                    }
                    RenderMetaBlob(layerTarget, W, H, cx, cy, framePhase, layer, lp, mAlpha, radScale, expand, originOff,
                                   layer.colorMode, flowPos, flowZoom, metaNoiseZoom, metaNoiseRot, metaNoiseDriftX, metaNoiseDriftY, metaNoiseGradPos, metaNoiseGradZoom, metaNoiseWarp,
                                   stack, frameIndex, ShapeSeed(spec.seed, li, 0));
                    FinishLayerPost(buf, layerTarget, hasLayerPost, layer, spec, li, lp, frameIndex, W, H, ref matteState);
                    continue;
                }

                // Height balls: a whole cloud of soft balls fused into density/heat/height FIELDS and relief-lit
                // as one surface — a per-shape rasteriser can't produce that (the lighting normal needs the
                // neighbouring pixels of the FUSED field), so like MetaBlob it draws itself in one pass.
                if (layer.shape == LayerShape.HeightBalls)
                {
                    float hbAlpha = Mathf.Clamp01(Eval(layer.alpha, lp, spec.seed, li, 0, F_Alpha));
                    Vector2 hbOriginOff = new Vector2((spec.origin.x - 0.5f) * W, (spec.origin.y - 0.5f) * H);
                    RenderHeightBalls(layerTarget, W, H, cx, cy, framePhase, layer, spec, li, lp, hbAlpha,
                                      hbOriginOff, stack, frameIndex, ShapeSeed(spec.seed, li, 0));
                    FinishLayerPost(buf, layerTarget, hasLayerPost, layer, spec, li, lp, frameIndex, W, H, ref matteState);
                    continue;
                }

                if (layer.shape == LayerShape.Fire)
                {
                    float fAlpha = Mathf.Clamp01(Eval(layer.alpha, lp, spec.seed, li, 0, F_Alpha));
                    RenderFire(layerTarget, W, H, layer, spec, li, frameIndex, fAlpha);
                    FinishLayerPost(buf, layerTarget, hasLayerPost, layer, spec, li, lp, frameIndex, W, H, ref matteState);
                    continue;
                }

                // Count: Curve reads the layer's life progress; MinMax stays frame-stable (h2 = 0, no frame/shape).
                // Rosing ignores Count entirely — its total is the sum of every authored ring's own count.
                bool rosing = layer.scatterMode == ScatterMode.Rosing;
                int count = rosing ? RoseTotalCount(layer.roseRings)
                                    : Mathf.Max(0, Mathf.RoundToInt(Eval(layer.count, lp, spec.seed, li, 0, F_Count)));

                // Fuse: a Ring/Rosing Disc layer melts its own shapes into one metaball field (RenderFusedField)
                // instead of compositing them independently — collected below instead of rasterised per-shape.
                bool fuseActive = layer.fuse && layer.shape == LayerShape.Disc
                                  && (layer.scatterMode == ScatterMode.Ring || rosing);
                var fusionCircles = fuseActive ? new System.Collections.Generic.List<FusionCircle>(count) : null;

                for (int inst = 0; inst < spread; inst++)
                {
                    float instRad = (spread > 1 ? (spreadDeg / spread) * inst : 0f) * Mathf.Deg2Rad;
                    for (int si = 0; si < count; si++)
                    {
                    // Deterministic per-shape rng — same across every frame, so scatter/life are stable.
                    int shapeSeed = ShapeSeed(spec.seed, li, si);
                    var rng = new System.Random(shapeSeed);

                    // Rosing: each shape belongs to one authored ring (its own count/radius/timing); every other
                    // scatter mode uses the flat per-layer Count/Spawn radius instead.
                    RoseRing rose = null; int roseLocalIdx = 0, roseRingCount = 0;
                    if (rosing)
                    {
                        RoseRingLookup(layer.roseRings, si, layer.roseReverseDraw, out rose, out roseLocalIdx, out roseRingCount);
                        if (rose == null) continue;   // shouldn't happen (si is bounded by count), but stay safe
                    }

                    // Per-shape life window. Rosing: the shape's own ring sets Birth/Life directly (a coarse,
                    // authored bloom timing) — Spawn stagger doesn't apply. Area/Ring: Spawn stagger distributes
                    // the Count shapes across the layer window, shortening each life so they tile it (0 = all live
                    // the full window; 1 = evenly spread, first spawn at the first frame, last ending at the
                    // last). Life jitter then nudges each shape's start either way, scaled to its own life span.
                    float span = Mathf.Max(1f, layer.endFrame - layer.startFrame);
                    float life;
                    float spawnAt;
                    if (rosing)
                    {
                        life = Mathf.Max(1f, rose.life * span);
                        spawnAt = rose.birth * span;
                    }
                    else
                    {
                        float s = Mathf.Clamp01(layer.spawnStagger);
                        // lifeForSpacing only drives spawnAt's SPACING (how far apart consecutive shapes spawn),
                        // matching the original formula exactly — Sync death then overrides the actual life each
                        // shape gets, deriving it so every shape's own end lands on the layer's endFrame together,
                        // instead of everyone sharing lifeForSpacing (which makes later spawns end later too).
                        float lifeForSpacing = span * (1f - s * (count - 1) / (float)Mathf.Max(1, count));
                        if (lifeForSpacing < 1f) lifeForSpacing = 1f;
                        spawnAt = count > 1 ? (si / (float)(count - 1)) * (span - lifeForSpacing) : 0f;
                        life = layer.syncDeath ? Mathf.Max(1f, span - spawnAt) : lifeForSpacing;
                    }
                    float lifeJit = (float)(rng.NextDouble() * 2.0 - 1.0) * layer.perShapeLifeJitter * life * 0.5f;
                    float start = layer.startFrame + spawnAt + lifeJit;
                    float end = start + life;
                    if (frameIndex < start || frameIndex > end) continue;      // not alive this frame
                    float t = Mathf.Clamp01((frameIndex - start) / Mathf.Max(0.0001f, end - start));

                    // Scatter the centre within spawnRadius (uniform disc); in Ring/Rosing mode place it ON the rim
                    // at spawnRadius (Ring) or the shape's own ring's Radius (Rosing) instead, sharing the exact
                    // same Ring order/Start angle/Arc angle placement math either way. spawnRadius/ring radius are
                    // 0..1 of the explosion; 1 reaches (almost) the canvas edge. (Directional placement is
                    // otherwise the job of the Ground modifier + Bars, not a per-layer mode.)
                    Vector2 c;
                    // Ring/Rosing + Align rotation: rotate the shape's own frame to face its angle around the ring,
                    // so an asymmetric shape (Crescent, an offset hole) orients outward instead of every instance
                    // sharing one fixed orientation. Zero for Area scatter or when the toggle is off.
                    float shapeRotRad = 0f;
                    {
                        // This shape's own spawn-time layer-progress — used below so an animated Spawn radius
                        // locks each shape's placement permanently once it spawns (see the comment there).
                        float spawnLp = Mathf.Clamp01((start - layer.startFrame) / span);

                        float scatterPx;
                        if (rosing) scatterPx = Mathf.Clamp01(rose.radius) * half * 0.9f;
                        else
                        {
                            // Evaluated at THIS SHAPE's own spawn-time progress, not the current frame's lp — so
                            // an animated Spawn radius locks each shape's placement permanently once it spawns,
                            // instead of retroactively sliding every already-placed shape in/out every frame as
                            // lp keeps changing (that read as one shared position SCALE, not a spawn radius).
                            // The ring/area still visibly grows over life: NEW shapes just spawn further out as
                            // later shapes are born, exactly like Count/other per-shape values already do via t.
                            float sr01 = Mathf.Clamp01(Eval(layer.spawnRadius, spawnLp, spec.seed, li, si, F_SpawnRadius));
                            scatterPx = sr01 * half * 0.9f;   // 0.9 safe margin off the very edge
                        }

                        if (layer.scatterMode == ScatterMode.Ring || rosing)
                        {
                            // Ring expand: UNLIKE Spawn radius/a ring's own Radius (spawn-locked placement — see
                            // above), this is evaluated at the CURRENT frame's shared layer progress (lp), so it
                            // moves every shape ALREADY on the ring, together, every frame — the ring itself grows/
                            // shrinks as a live, ongoing transform (all shapes staying attached to it, keeping
                            // their angular slot) instead of only affecting where NEW shapes spawn. It scales
                            // scatterPx only — never Radius (the shape's own Size), so the ring can bloom outward
                            // while every disc/crescent on it stays whatever size Size/sizeScale already gave it.
                            // 1 = the authored Spawn radius / ring Radius, unchanged.
                            scatterPx *= Mathf.Max(0f, Eval(layer.ringExpand, lp, spec.seed, li, 0, F_RingExpand));

                            // Evaluated at THIS SHAPE's own spawn-time progress, exactly like Spawn radius above —
                            // Start angle/Arc degrees are a PLACEMENT decision (where does a new shape land), not a
                            // live ongoing transform, so once a shape spawns its angular slot stays fixed forever,
                            // the same way its radius already does. For a whole ring that visibly spins as time
                            // passes (a genuinely different, still fully supported effect), add a Rotate geometry
                            // modifier to the layer/globally — that's a live transform BY DESIGN and composes
                            // correctly with everything else, unlike overloading a placement field for the job.
                            float startDeg = Eval(layer.ringStartAngle, spawnLp, spec.seed, li, 0, F_RingStart);
                            float arcDeg = Eval(layer.ringArcDegrees, spawnLp, spec.seed, li, 0, F_RingArc);
                            int arcCount = rosing ? roseRingCount : count;
                            int arcIdx = rosing ? roseLocalIdx : si;
                            float angDeg = layer.ringOrder == RingOrder.Sequential
                                ? startDeg + (arcCount > 1 ? (arcDeg / arcCount) * arcIdx : 0f)
                                : startDeg + (float)(rng.NextDouble() * arcDeg);
                            float ang = angDeg * Mathf.Deg2Rad;
                            c = new Vector2(Mathf.Cos(ang) * scatterPx, Mathf.Sin(ang) * scatterPx);
                            if (layer.ringAlignRotation) shapeRotRad = ang;
                        }
                        else
                        {
                            double ang = rng.NextDouble() * Math.PI * 2.0;
                            double radFrac = Math.Sqrt(rng.NextDouble());
                            c = new Vector2((float)(Math.Cos(ang) * radFrac) * scatterPx,
                                            (float)(Math.Sin(ang) * radFrac) * scatterPx);
                        }
                    }

                    // Self-rotation: spins the shape around its OWN centre over its OWN life (t), independent of
                    // Ring/Rosing placement — adds onto whatever Align rotation set as the initial facing.
                    float spinDeg = Eval(layer.spinDegrees, t, spec.seed, li, si, F_SpinDegrees);
                    if (spinDeg != 0f) shapeRotRad += spinDeg * Mathf.Deg2Rad;

                    // Position offset (fixed / per-shape random / animated drift over layer progress).
                    c.x += Eval(layer.positionX, lp, spec.seed, li, si, F_PosX);
                    c.y += Eval(layer.positionY, lp, spec.seed, li, si, F_PosY);

                    // Radius: one Size multicontrol over the shape's own life t (Curve = an envelope, Static =
                    // constant, MinMax = a per-shape-stable random size). Rosing: each ring's own Size ×scales
                    // this on top — Radius (placement) and Size (disc scale) are independent per ring.
                    float radius = Eval(layer.size, t, spec.seed, li, si, F_Size);
                    if (rosing && rose != null) radius *= Mathf.Max(0f, rose.sizeScale);
                    if (radius < 0.25f) continue;

                    // Alpha: one multicontrol over life (Curve envelope by default) — the only thing that fades.
                    float alpha = Mathf.Clamp01(Eval(layer.alpha, t, spec.seed, li, si, F_Alpha));
                    if (alpha <= 0.001f) continue;

                    Color baseCol = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(t) : Color.white;

                    // Circular spread: rotate this shape's offset around the centre for this instance.
                    if (spread > 1) { c = Rotate(c, instRad); shapeRotRad += instRad; }

                    // ── keep-on-screen guarantee ──────────────────────────────────
                    // A radial pixel-art explosion must never be clipped flat at the canvas edge, so cap the radius to
                    // the canvas half and clamp the centre so the whole shape fits — AS LONG AS that still leaves
                    // room to move. Once a shape's own radius reaches canvas half, full containment and any
                    // placement freedom are mutually exclusive (the shape is as wide as the canvas): forcing full
                    // containment there collapses the clamp range to a single dead-centre point, silently erasing
                    // Position/Spawn radius/Ring angle/every other placement value for that shape. So past that
                    // point we back off to the weaker guarantee — keep the centre on-canvas — instead of cancelling
                    // authored placement. (Ground/Bars deliberately push content off-frame via their own
                    // warp/placement, downstream of this pre-warp scatter clamp.)
                    // Ring/Rosing: placement (Spawn radius / a ring's own Radius) is an authored, spawn-locked
                    // decision — exactly like Ring start angle/Arc degrees above — and must stay independent of
                    // Size, including this clamp. Re-centring shapes here as their Size animates would silently
                    // shrink/distort the ring's actual radius and inter-shape spacing as a side effect of a size
                    // curve, defeating that independence. So Ring/Rosing skip the centre re-clamp entirely (an
                    // oversized shape can clip past the canvas edge there — compensate with canvas size or the
                    // ring's own Radius, not by having the renderer override your placement).
                    float effR = Mathf.Min(radius, half);
                    if (!(layer.scatterMode == ScatterMode.Ring || rosing))
                    {
                        bool fitsWithRoom = radius < half;
                        float loX = fitsWithRoom ? effR : 0f, hiX = fitsWithRoom ? W - effR : W;
                        float loY = fitsWithRoom ? effR : 0f, hiY = fitsWithRoom ? H - effR : H;
                        float absX = Mathf.Clamp(cx + c.x, loX, hiX);
                        float absY = Mathf.Clamp(cy + c.y, loY, hiY);
                        c = new Vector2(absX - cx, absY - cy);
                    }
                    radius = effR;

                    // Fuse: collect this shape as a circle in the fused field instead of rasterising it on its
                    // own — c is already canvas-centre-relative, the exact space RenderFusedField samples in.
                    if (fuseActive) { fusionCircles.Add(new FusionCircle(c.x, c.y, radius, alpha)); continue; }

                    if (layer.shape == LayerShape.Sprite)
                    {
                        if (layer.particleSprite != null)
                        {
                            float startAng = (float)(rng.NextDouble() * 360.0);
                            float spin = Eval(layer.spriteSpin, t, spec.seed, li, si, F_SpriteSpin);
                            RasterSprite(layerTarget, W, H, cx, cy, layer.particleSprite, c, radius,
                                         startAng + spin + shapeRotRad * Mathf.Rad2Deg,
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
                    float sparkleBlobRadiusV = 2f, sparkleBlobLifeV = 8f, sparkleBlobSoftV = 0.6f;
                    if (layer.shape == LayerShape.SparkleField && layer.sparkleBlobs)
                    {
                        sparkleBlobRadiusV = Mathf.Max(0.5f, Eval(layer.sparkleBlobRadius, t, spec.seed, li, si, F_SparkleBlobRadius));
                        sparkleBlobLifeV = Mathf.Max(1f, Eval(layer.sparkleBlobLife, t, spec.seed, li, si, F_SparkleBlobLife));
                        sparkleBlobSoftV = Mathf.Clamp01(Eval(layer.sparkleBlobSoftness, t, spec.seed, li, si, F_SparkleBlobSoft));
                    }
                    // Disc-like edge params (hole + softness) apply to Disc AND SparkleField (a sparkle field is a disc).
                    bool discLike = layer.shape == LayerShape.Disc || layer.shape == LayerShape.SparkleField;
                    bool crescentShape = layer.shape == LayerShape.Crescent;
                    float holeSize = discLike && layer.hollow ? Mathf.Clamp01(Eval(layer.holeSize, t, spec.seed, li, si, F_HoleSize)) : 0f;
                    // Outer softness: Disc/SparkleField always, Crescent always too (its own outer boundary, same
                    // as Disc's). Inner softness: Disc/SparkleField only when Hollow; Crescent always (its BITE
                    // edge, which — unlike a Hollow hole — has no separate on/off toggle of its own).
                    float outerSoft = (discLike || crescentShape) ? Mathf.Clamp01(Eval(layer.outerSoftness, t, spec.seed, li, si, F_OuterSoft)) : 0f;
                    float innerSoft = (discLike && layer.hollow) || crescentShape ? Mathf.Clamp01(Eval(layer.innerSoftness, t, spec.seed, li, si, F_InnerSoft)) : 0f;
                    // Crescent mask offset — in units of the (post-clamp) radius, same convention as Hole offset
                    // X/Y just below, so the crescent's proportions stay put as Size changes instead of a fixed
                    // pixel offset distorting/collapsing the bite as the disc grows or shrinks. Magnitude 2 is
                    // where the (same-radius) mask disc fully clears the main one — no overlap, no bite left.
                    float crescX = layer.shape == LayerShape.Crescent
                        ? Eval(layer.crescentOffsetX, t, spec.seed, li, si, F_CrescentX) * radius : 0f;
                    float crescY = layer.shape == LayerShape.Crescent
                        ? Eval(layer.crescentOffsetY, t, spec.seed, li, si, F_CrescentY) * radius : 0f;
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
                    float noiseZoomV = 20f, noiseRotV = 0f, noiseDriftXV = 0f, noiseDriftYV = 0f, noiseGradPosV = 0f, noiseGradZoomV = 1f, noiseWarpV = 0.6f;
                    if (layer.colorMode == ColorMode.NoiseFill)
                    {
                        noiseZoomV = Mathf.Max(1f, Eval(layer.noiseZoom, t, spec.seed, li, si, F_NoiseZoom));
                        noiseRotV = Eval(layer.noiseRotation, t, spec.seed, li, si, F_NoiseRot) * Mathf.Deg2Rad;
                        noiseDriftXV = Eval(layer.noiseDriftX, t, spec.seed, li, si, F_NoiseDriftX);
                        noiseDriftYV = Eval(layer.noiseDriftY, t, spec.seed, li, si, F_NoiseDriftY);
                        noiseGradPosV = Eval(layer.noiseGradientPosition, t, spec.seed, li, si, F_NoiseGradPos);
                        noiseGradZoomV = Eval(layer.noiseGradientZoom, t, spec.seed, li, si, F_NoiseGradZoom);
                        noiseWarpV = Eval(layer.noiseWarp, t, spec.seed, li, si, F_NoiseWarp);
                    }
                    else if (layer.colorMode != ColorMode.OverLife)
                    {
                        flowPos = Eval(layer.colorFlow, t, spec.seed, li, si, F_ColorFlow);
                        flowZoom = Eval(layer.colorFlowZoom, t, spec.seed, li, si, F_ColorZoom);
                        gradX = Eval(layer.gradientOffsetX, t, spec.seed, li, si, F_GradX) * radius;
                        gradY = Eval(layer.gradientOffsetY, t, spec.seed, li, si, F_GradY) * radius;
                    }
                    RasterShape(layerTarget, W, H, cx, cy, framePhase, layer, c, radius, baseCol, alpha,
                                t, shapeSeed, crescX, crescY, stack, frameIndex, sparkleD, sparkleSub, holeSize, outerSoft, innerSoft,
                                flowPos, flowZoom, gradX, gradY, holeOffX, holeOffY, shapeRotRad,
                                noiseZoomV, noiseRotV, noiseDriftXV, noiseDriftYV, noiseGradPosV, noiseGradZoomV, noiseWarpV,
                                sparkleBlobRadiusV, sparkleBlobLifeV, sparkleBlobSoftV);
                }
              }
                if (fuseActive && fusionCircles.Count > 0)
                {
                    // Same gradient-options rule as MetaBlob: Over life needs neither flow nor noise params (one
                    // flat colour by the layer's own life); Fill/Flow fill honour Gradient position/zoom; Noise
                    // fill reuses the layer's own zoom/rotation/drift/warp/bands fields instead.
                    bool fuseNoiseFill = layer.colorMode == ColorMode.NoiseFill;
                    bool fuseNeedsFlow = layer.colorMode != ColorMode.OverLife && !fuseNoiseFill;
                    float fuseFlowPos = fuseNeedsFlow ? Eval(layer.colorFlow, lp, spec.seed, li, 0, F_MetaFlow) : 0f;
                    float fuseFlowZoom = fuseNeedsFlow ? Eval(layer.colorFlowZoom, lp, spec.seed, li, 0, F_MetaFlowZoom) : 1f;
                    float fuseNoiseZoom = 20f, fuseNoiseRot = 0f, fuseNoiseDriftX = 0f, fuseNoiseDriftY = 0f, fuseNoiseGradPos = 0f, fuseNoiseGradZoom = 1f, fuseNoiseWarp = 0.6f;
                    if (fuseNoiseFill)
                    {
                        fuseNoiseZoom = Mathf.Max(1f, Eval(layer.noiseZoom, lp, spec.seed, li, 0, F_NoiseZoom));
                        fuseNoiseRot = Eval(layer.noiseRotation, lp, spec.seed, li, 0, F_NoiseRot) * Mathf.Deg2Rad;
                        fuseNoiseDriftX = Eval(layer.noiseDriftX, lp, spec.seed, li, 0, F_NoiseDriftX);
                        fuseNoiseDriftY = Eval(layer.noiseDriftY, lp, spec.seed, li, 0, F_NoiseDriftY);
                        fuseNoiseGradPos = Eval(layer.noiseGradientPosition, lp, spec.seed, li, 0, F_NoiseGradPos);
                        fuseNoiseGradZoom = Eval(layer.noiseGradientZoom, lp, spec.seed, li, 0, F_NoiseGradZoom);
                        fuseNoiseWarp = Eval(layer.noiseWarp, lp, spec.seed, li, 0, F_NoiseWarp);
                    }
                    RenderFusedField(layerTarget, W, H, framePhase, layer, lp, fusionCircles, layer.colorMode,
                                     fuseFlowPos, fuseFlowZoom, fuseNoiseZoom, fuseNoiseRot, fuseNoiseDriftX, fuseNoiseDriftY, fuseNoiseGradPos, fuseNoiseGradZoom, fuseNoiseWarp,
                                     stack, frameIndex, ShapeSeed(spec.seed, li, 0));
                }
                FinishLayerPost(buf, layerTarget, hasLayerPost, layer, spec, li, lp, frameIndex, W, H, ref matteState);
            }

            // Whole-frame post passes (Bloom, Outline, ...) from the global list, in order, after everything
            // composites.
            if (spec.globalModifiers != null)
                for (int i = 0; i < spec.globalModifiers.Count; i++)
                {
                    var m = spec.globalModifiers[i];
                    if (m == null || !m.enabled) continue;
                    if (m is PostModifier post)
                    {
                        post.SetLife(bp);   // blast progress — see PostModifier.life (mirrors PinWarpModifier.SetFrame)
                        post.SetSeed(spec.seed);
                        post.SetFrameIndex(frameIndex);
                        m.Prepare((v, fid) => Eval(v, bp, spec.seed, GlobalLayerId, frameIndex, 1000 + (700 + i) * 8 + fid));
                        post.Apply(buf, W, H);
                    }
                }

            // The one, always-last SIMULATION modifier (its own dedicated slot, not part of globalModifiers
            // above) — see SimulationModifier's own class doc for why this lives outside the normal modifier
            // dispatch entirely. Runs after every other layer/global modifier has fully composited.
            if (spec.simulationModifier is SimulationModifier sim && sim.enabled)
            {
                sim.SetSeed(spec.seed);
                // Resolves how frame f itself would evaluate this modifier's ZUIValues — EnsureFrame calls this
                // once per Step, INCLUDING for frames it replays on a cold start/jump, so a replayed history
                // uses each of ITS OWN frames' parameters rather than whatever frameIndex itself resolves to.
                Func<int, Func<ZUIValue, int, float>> simParamsForFrame = f =>
                {
                    float fp = frameCount > 1 ? f / (float)(frameCount - 1) : 0f;
                    return (v, fid) => Eval(v, fp, spec.seed, GlobalLayerId, f, 1000 + 900 * 8 + fid);
                };
                sim.EnsureFrame(frameIndex, buf, W, H, simParamsForFrame);
                sim.Render(buf, W, H);
            }
            return buf;
        }

        // ── Isolated single-shape preview ──────────────────────────────────────────────────────────────────
        // ALWAYS exactly one shape, centred, sized to fill the given canvas — ignores Count/Position/Spawn
        // radius/Ring-Rosing placement entirely. For dialing in a layer's own per-shape look (gradient,
        // crescent bite, hollow, the Size curve's own SHAPE, spin, alpha, modifiers) without scatter/movement/
        // instance-count noise. `t` is 0..1 of the layer's own life (same meaning as the main loop's per-shape
        // `t`). Each `show*` flag independently opts that aspect IN, reflecting its authored animation/value;
        // off freezes it to a neutral default instead, so distracting aspects can be isolated away one at a
        // time. A dedicated, simplified path (not a count-1 call into the main loop) — reuses RasterShape/
        // RasterSprite and the modifier-stack machinery, but skips spawn timing, scatter placement and the
        // multi-instance loop entirely, none of which a single centred preview shape has any use for.
        public static Color32[] RenderShapePreview(Layer layer, Pyre spec, int li, float t, int frameIndex, int W, int H,
                                                    bool showGradientFill, bool showCrescent, bool showHollow, bool showSize,
                                                    bool showSpin, bool showAlpha, bool showModifiers)
        {
            var buf = new Color32[W * H];
            for (int i = 0; i < buf.Length; i++) buf[i] = Transparent;
            if (layer == null || spec == null) return buf;
            bool previewable = layer.shape == LayerShape.Disc || layer.shape == LayerShape.Crescent ||
                               layer.shape == LayerShape.SparkleField || layer.shape == LayerShape.Sprite;
            if (!previewable) return buf;

            float cx = W * 0.5f, cy = H * 0.5f;
            float maxRadius = Mathf.Min(W, H) * 0.5f * 0.9f;   // safe margin off the very edge
            int shapeSeed = ShapeSeed(spec.seed, li, 0);

            // Size: ON reflects the authored curve's own relative animation, rescaled so its own peak fills
            // the preview; OFF pins to the preview's max size always, ignoring the curve/animation entirely.
            float radius;
            if (showSize)
            {
                float natural = Eval(layer.size, t, spec.seed, li, 0, F_Size);
                float peak = PeakValue(layer.size);
                radius = peak > 0.001f ? natural * (maxRadius / peak) : maxRadius;
            }
            else radius = maxRadius;
            if (radius < 0.25f) return buf;

            float spinDeg = showSpin ? Eval(layer.spinDegrees, t, spec.seed, li, 0, F_SpinDegrees) : 0f;
            Color baseCol = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(t) : Color.white;

            if (layer.shape == LayerShape.Sprite)
            {
                if (layer.particleSprite != null)
                {
                    float pixelAlphaS = showAlpha ? Mathf.Clamp01(Eval(layer.alpha, t, spec.seed, li, 0, F_Alpha)) : 1f;
                    ModStack spriteStack = showModifiers ? BuildLayerOnlyStack(layer, spec, li, t, frameIndex) : EmptyStack;
                    RasterSprite(buf, W, H, cx, cy, layer.particleSprite, Vector2.zero, radius, spinDeg,
                                baseCol, pixelAlphaS, spriteStack, frameIndex, t, shapeSeed);
                }
                return buf;
            }

            float pixelAlpha = showAlpha ? Mathf.Clamp01(Eval(layer.alpha, t, spec.seed, li, 0, F_Alpha)) : 1f;
            if (pixelAlpha <= 0.001f) return buf;

            bool discLike = layer.shape == LayerShape.Disc || layer.shape == LayerShape.SparkleField;
            bool crescentShape = layer.shape == LayerShape.Crescent;
            bool hollowOn = showHollow && layer.hollow;
            float holeSize = discLike && hollowOn ? Mathf.Clamp01(Eval(layer.holeSize, t, spec.seed, li, 0, F_HoleSize)) : 0f;
            float outerSoft = (discLike || crescentShape) ? Mathf.Clamp01(Eval(layer.outerSoftness, t, spec.seed, li, 0, F_OuterSoft)) : 0f;
            float innerSoft = (discLike && hollowOn) || crescentShape ? Mathf.Clamp01(Eval(layer.innerSoftness, t, spec.seed, li, 0, F_InnerSoft)) : 0f;
            float crescX = crescentShape && showCrescent ? Eval(layer.crescentOffsetX, t, spec.seed, li, 0, F_CrescentX) * radius : 0f;
            float crescY = crescentShape && showCrescent ? Eval(layer.crescentOffsetY, t, spec.seed, li, 0, F_CrescentY) * radius : 0f;
            float holeOffX = 0f, holeOffY = 0f;
            if (discLike && hollowOn)
            {
                holeOffX = Eval(layer.holeOffsetX, t, spec.seed, li, 0, F_HoleOffX) * radius;
                holeOffY = Eval(layer.holeOffsetY, t, spec.seed, li, 0, F_HoleOffY) * radius;
            }

            float flowPos = 0f, flowZoom = 1f, gradX = 0f, gradY = 0f;
            float noiseZoomV = 20f, noiseRotV = 0f, noiseDriftXV = 0f, noiseDriftYV = 0f, noiseGradPosV = 0f, noiseGradZoomV = 1f, noiseWarpV = 0.6f;
            if (showGradientFill && layer.colorMode == ColorMode.NoiseFill)
            {
                noiseZoomV = Mathf.Max(1f, Eval(layer.noiseZoom, t, spec.seed, li, 0, F_NoiseZoom));
                noiseRotV = Eval(layer.noiseRotation, t, spec.seed, li, 0, F_NoiseRot) * Mathf.Deg2Rad;
                noiseDriftXV = Eval(layer.noiseDriftX, t, spec.seed, li, 0, F_NoiseDriftX);
                noiseDriftYV = Eval(layer.noiseDriftY, t, spec.seed, li, 0, F_NoiseDriftY);
                noiseGradPosV = Eval(layer.noiseGradientPosition, t, spec.seed, li, 0, F_NoiseGradPos);
                noiseGradZoomV = Eval(layer.noiseGradientZoom, t, spec.seed, li, 0, F_NoiseGradZoom);
                noiseWarpV = Eval(layer.noiseWarp, t, spec.seed, li, 0, F_NoiseWarp);
            }
            else if (showGradientFill && layer.colorMode != ColorMode.OverLife)
            {
                flowPos = Eval(layer.colorFlow, t, spec.seed, li, 0, F_ColorFlow);
                flowZoom = Eval(layer.colorFlowZoom, t, spec.seed, li, 0, F_ColorZoom);
                gradX = Eval(layer.gradientOffsetX, t, spec.seed, li, 0, F_GradX) * radius;
                gradY = Eval(layer.gradientOffsetY, t, spec.seed, li, 0, F_GradY) * radius;
            }

            float sparkleD = layer.shape == LayerShape.SparkleField
                ? Mathf.Clamp01(Eval(layer.sparkleDensity, t, spec.seed, li, 0, F_SparkleDensity)) : 1f;
            int sparkleSub = layer.shape == LayerShape.SparkleField
                ? Mathf.RoundToInt(Eval(layer.sparkleSeed, t, spec.seed, li, frameIndex, F_SparkleSeed) * 100000f) : 0;
            float sparkleBlobRadiusV = 2f, sparkleBlobLifeV = 8f, sparkleBlobSoftV = 0.6f;
            if (layer.shape == LayerShape.SparkleField && layer.sparkleBlobs)
            {
                sparkleBlobRadiusV = Mathf.Max(0.5f, Eval(layer.sparkleBlobRadius, t, spec.seed, li, 0, F_SparkleBlobRadius));
                sparkleBlobLifeV = Mathf.Max(1f, Eval(layer.sparkleBlobLife, t, spec.seed, li, 0, F_SparkleBlobLife));
                sparkleBlobSoftV = Mathf.Clamp01(Eval(layer.sparkleBlobSoftness, t, spec.seed, li, 0, F_SparkleBlobSoft));
            }

            ModStack stack = showModifiers ? BuildLayerOnlyStack(layer, spec, li, t, frameIndex) : EmptyStack;

            RasterShape(buf, W, H, cx, cy, 0f, layer, Vector2.zero, radius, baseCol, pixelAlpha,
                        t, shapeSeed, crescX, crescY, stack, frameIndex, sparkleD, sparkleSub, holeSize, outerSoft, innerSoft,
                        flowPos, flowZoom, gradX, gradY, holeOffX, holeOffY, spinDeg * Mathf.Deg2Rad,
                        noiseZoomV, noiseRotV, noiseDriftXV, noiseDriftYV, noiseGradPosV, noiseGradZoomV, noiseWarpV,
                        sparkleBlobRadiusV, sparkleBlobLifeV, sparkleBlobSoftV, showGradientFill);
            return buf;
        }

        // The largest value a ZUIValue could realistically produce, for rescaling a "preview at max size"
        // render. Static/MinMax: their own value/max. Curve: the highest authored point (points already store
        // real units, not normalized — see ZUIValueControl's ClampToRange).
        static float PeakValue(ZUIValue v)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax: return Mathf.Max(v.min, v.max);
                case ZUIValue.Mode.Curve:
                {
                    float m = 0f;
                    if (v.points != null) foreach (var p in v.points) if (p.value > m) m = p.value;
                    return m;
                }
                default: return v.staticValue;
            }
        }

        // Same as BuildStack but LAYER modifiers only — no global modifiers — for the isolated shape preview,
        // which is dialing in this one layer's own look, not the whole composited blast's post pass.
        static ModStack BuildLayerOnlyStack(Layer layer, Pyre spec, int li, float lp, int frameIndex)
        {
            var geo = new System.Collections.Generic.List<GeoEntry>();
            var pix = new System.Collections.Generic.List<PixelModifier>();
            var edge = new System.Collections.Generic.List<EdgeModifier>();
            CollectMods(layer != null ? layer.modifiers : null, spec, li, lp, frameIndex, geo, pix, edge, 0, 0);
            if (geo.Count == 0 && pix.Count == 0 && edge.Count == 0) return EmptyStack;
            if (geo.Count > 1) SortGeoStable(geo);
            var arr = new GeometryModifier[geo.Count];
            for (int i = 0; i < geo.Count; i++) arr[i] = geo[i].mod;
            return new ModStack(arr, pix.ToArray(), edge.ToArray());
        }

        static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ── Rosing scatter: an authored list of rings, each with its own count — flattens to one linear shape
        // index (0..RoseTotalCount-1), same as Area/Ring's `si`, so the rest of the per-shape pipeline (seed,
        // modifiers, rendering) doesn't need to know rings exist at all.
        static int RoseTotalCount(System.Collections.Generic.List<RoseRing> rings)
        {
            int total = 0;
            if (rings != null) for (int i = 0; i < rings.Count; i++) if (rings[i] != null) total += Mathf.Max(0, rings[i].count);
            return total;
        }

        // Maps a flat shape index back to its ring + its own slot within that ring (for angle placement) + that
        // ring's total count (for evenly spacing its slots around the shared arc). ring == null if si is out of
        // range (shouldn't happen since the si-loop is bounded by RoseTotalCount, but callers must check).
        // reverseDraw only changes which ring CLAIMS the low (drawn-first/behind) vs high (drawn-last/in-front)
        // si range — walking the rings list back-to-front for that assignment — so it flips compositing depth
        // without touching localIndex/ringCount (a ring's own internal angle placement is unaffected).
        static void RoseRingLookup(System.Collections.Generic.List<RoseRing> rings, int si, bool reverseDraw,
                                   out RoseRing ring, out int localIndex, out int ringCount)
        {
            int acc = 0;
            if (rings != null)
            {
                int n = rings.Count;
                for (int k = 0; k < n; k++)
                {
                    var r = rings[reverseDraw ? n - 1 - k : k];
                    if (r == null) continue;
                    int c = Mathf.Max(0, r.count);
                    if (si < acc + c) { ring = r; localIndex = si - acc; ringCount = c; return; }
                    acc += c;
                }
            }
            ring = null; localIndex = 0; ringCount = 0;
        }

        // ── Bars mode: rows of forward-growing bars. Star off = one arm off the back edge; Star on = `spread`
        // arms sharing the centre and radiating outward (an asterisk). Each bar reaches FORWARD, its length scaled
        // by Taper (centre-longest → triangle/flame). Arms are drawn INTERLEAVED by bar index — the centre bar of
        // every arm, then the next bar out of every arm, … — so overlapping arms layer consistently instead of
        // each whole arm stacking over the previous one. Geometry + pixel modifiers apply to bars (in RasterBar).
        static void RenderBarsLayerStar(Color32[] buf, int W, int H, float cx, float cy,
                                        Layer layer, int li, Pyre spec, float lp, int frameIndex, float framePhase,
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
            // Both bands scale off the bar's WIDTH (hw), not its length — a bar grows over its own lifetime, so
            // sizing the tip band off `span` (as this used to) made the fade zone grow right along with it: once
            // a bar had grown past ~2x its width at any real softness, the tip band exceeded the bar's own length
            // and the "fade near the tip" gradient stretched back through the ENTIRE bar, reading as a uniformly
            // semi-transparent body instead of a solid bar with soft ends. Width-relative sizing (matching
            // sideBand) keeps the softness a constant, proportional "how rounded are the caps" amount regardless
            // of how long the bar currently is — a capsule-style rounded end, not a growing gradient.
            float sideBand = soft > 0.001f ? Mathf.Max(0.5f, soft * hw) : 0f;
            float tipBand = soft > 0.001f ? Mathf.Min(span, Mathf.Max(0.5f, soft * hw)) : 0f;
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
                        if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, wx, wy, frameIndex, crossFrac, life, hash, W, H))
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
        // ── MetaBlob: sum a compact metaball field from the placed orbs (each with a grow-in→hold→melt-out weight
        // over its own life), threshold it, and shade by the field value through the layer's gradient (surface→core).
        // radScale multiplies every orb's radius, expand scales the orb centres about the origin — both shared,
        // animated layer-wide, so the whole blob can breathe / burst / gather. Geometry modifiers warp the sampling
        // (the blob presents ONE aggregate shape frame so Profile/Ground/Jagg mold it as a single shape); pixel
        // modifiers + the global post passes (Bloom/Outline) still apply.
        static void RenderMetaBlob(Color32[] buf, int W, int H, float cx, float cy, float framePhase,
                                   Layer layer, float lp, float alpha, float radScale, float expand, Vector2 originOff,
                                   ColorMode colorMode, float flowPos, float flowZoom,
                                   float noiseZoomV, float noiseRotRad, float noiseDriftXV, float noiseDriftYV, float noiseGradPosV, float noiseGradZoomV, float noiseWarpV,
                                   in ModStack stack, int frameIndex, int hash)
        {
            float nrCos = Mathf.Cos(noiseRotRad), nrSin = Mathf.Sin(noiseRotRad);
            var orbs = layer.metaOrbs;
            if (orbs == null || orbs.Count == 0 || alpha <= 0.001f) return;
            float threshold = Mathf.Max(0.02f, layer.metaThreshold);
            float range = Mathf.Max(0.05f, layer.metaShadeRange);
            // Capped at threshold: the AA band below reads as `field > threshold - band`, so once band exceeds
            // threshold that lower bound goes negative and EVERY pixel (including empty field=0 ones, far from
            // any orb) satisfies it — the whole frame washes out with a uniform faint fill instead of just the
            // blob's own edge softening. Capping keeps threshold - band >= 0 always.
            float band = Mathf.Clamp(layer.metaSoftness, 0.01f, threshold);
            // Over life = one flat colour for the whole blob, sampled at the layer's own life — constant across
            // the field, so it's resolved once here rather than per pixel (matches Disc's Over life exactly).
            Color overLifeColor = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(lp) : Color.white;

            // Resolve each orb's on-screen position + radius ONCE (apply the shared expand about the origin and the
            // radius scale), and its current life weight. Also grow a bounding box over ALL valid orbs so the whole
            // blob can present a single aggregate shape frame (centre + enclosing radius) to shape-frame geometry
            // modifiers (Profile/Ground/Jagg) — they treat the fused field as one shape instead of no-oping on it.
            int n = orbs.Count;
            var px = new float[n]; var py = new float[n]; var pr2 = new float[n]; var pw = new float[n];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var o = orbs[i];
                if (o == null) { pw[i] = 0f; continue; }

                // The orb's own life progress drives BOTH its fade weight and (since 2026-07-23) its
                // animatable radius — so a Curve-mode radius reads as "over THIS orb's life", not the
                // layer's. Computed before the radius for exactly that reason.
                float t = (lp - o.birth) / Mathf.Max(0.02f, o.life);   // 0..1 across this orb's own life
                float r = Eval(o.Radius, t, hash, i, 0, frameIndex) * radScale;
                if (r < 0.5f) { pw[i] = 0f; continue; }
                Vector2 p = originOff + (o.pos - originOff) * expand;
                px[i] = p.x; py[i] = p.y; pr2[i] = r * r;

                pw[i] = (t <= 0f || t >= 1f) ? 0f : MetaEnv(t);

                if (p.x - r < minX) minX = p.x - r; if (p.x + r > maxX) maxX = p.x + r;
                if (p.y - r < minY) minY = p.y - r; if (p.y + r > maxY) maxY = p.y + r;
                any = true;
            }
            if (!any) return;

            Vector2 fieldCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float fieldRadius = 0.5f * Mathf.Max(maxX - minX, maxY - minY);
            var ctx = new GeoCtx(W * 0.5f, H * 0.5f, fieldCenter, fieldRadius);

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                    if (stack.AnyGeo) off = ApplyGeo(stack, off, framePhase, ctx);

                    float field = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float w = pw[i];
                        if (w <= 0.001f) continue;
                        float dx = off.x - px[i], dy = off.y - py[i];
                        float r2 = pr2[i];
                        float d2 = dx * dx + dy * dy;
                        if (d2 >= r2) continue;
                        float k = 1f - d2 / r2; k *= k;    // compact polynomial kernel: 1 at centre → 0 at radius
                        field += w * k;
                    }

                    if (field <= threshold - band) continue;
                    float a = Mathf.Clamp01((field - (threshold - band)) / band) * alpha;   // AA across the iso-surface
                    if (a <= 0.003f) continue;

                    // 0 = surface, 1 = deep core — always resolved (used as the pixel modifiers' crossFrac too,
                    // matching RasterShape's nd, regardless of which colour mode is shading this pixel).
                    float rawFrac = Mathf.Clamp01((field - threshold) / range);
                    Color fc;
                    if (colorMode == ColorMode.OverLife) fc = overLifeColor;
                    else if (colorMode == ColorMode.NoiseFill)
                    {
                        // Sample the noise field centred on the fused blob's own aggregate centre, independent of
                        // the Fill/Flow fill's field-depth-based frac above.
                        float rx = off.x - fieldCenter.x, ry = off.y - fieldCenter.y;
                        float nrx = rx * nrCos - ry * nrSin, nry = rx * nrSin + ry * nrCos;
                        float noiseN = PyreNoise.Sample((nrx + noiseDriftXV) / noiseZoomV, (nry + noiseDriftYV) / noiseZoomV, hash, noiseWarpV);
                        int bands = Mathf.Max(1, layer.noiseBands);
                        float nfrac = NoiseBandFrac(noiseN, bands, layer.noiseBandSoftness);
                        // Mirror (0→1→0), not a plain wrap, same reasoning as FlowingFill below — a gradient's own
                        // start/end colours are rarely identical, so wrapping this straight into another 0 would
                        // jump between them every cycle. Mirroring instead plays the gradient forward then
                        // backward, so it's smooth at every period boundary regardless of Position/Zoom.
                        float noiseGradF = Mathf.Repeat(nfrac * noiseGradZoomV + noiseGradPosV, 1f);
                        float noiseGradFrac = 1f - Mathf.Abs(2f * noiseGradF - 1f);
                        fc = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(noiseGradFrac) : Color.white;
                    }
                    else
                    {
                        float frac;
                        if (colorMode == ColorMode.FlowingFill)
                        {
                            // Scroll the (mirrored) gradient through the field depth: position sweeps it, zoom sets
                            // how much spans surface→core. Mirror (0→1→0) so an animated position loops with no seam.
                            float f = Mathf.Repeat(rawFrac * flowZoom + flowPos, 1f);
                            frac = 1f - Mathf.Abs(2f * f - 1f);
                        }
                        else frac = Mathf.Clamp01(rawFrac * flowZoom + flowPos);   // Fill: position + zoom apply here too
                        fc = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(frac) : Color.white;
                    }
                    float outA = a * fc.a;
                    if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, cx + off.x, cy + off.y, frameIndex, rawFrac, lp, hash, W, H)) continue;
                    Over(buf, y * W + x, fc.r, fc.g, fc.b, outA);
                }
        }

        // Orb weight over its own life t∈[0,1]: ease in, hold, melt out (smoothstep both ends). Melting the weight
        // sinks the orb back out of the fused field, so the merged shape reshapes as orbs come and go.
        static float MetaEnv(float t)
        {
            const float grow = 0.22f;
            float w = Mathf.Min(Mathf.Clamp01(t / grow), Mathf.Clamp01((1f - t) / grow));
            return w * w * (3f - 2f * w);
        }

        // ── Height balls ───────────────────────────────────────────────────────────────────────────────────────
        // A churning cloud of soft balls, each carrying a DENSITY (mass) and a HEAT (energy). The balls are fused
        // into three scalar fields with a smooth max — density, heat, and a height field weighting heat above
        // density — so overlapping balls melt into one continuous mass instead of stacking as separate discs. The
        // height field's local slope lights the cloud (a normal dotted with a light direction), which is what gives
        // the chunky pixel-art 3D read; density+heat then indexes ONE gradient whose low end is smoke and whose
        // high end is fire. Adding energy to a ball therefore literally walks it UP that gradient, and losing
        // energy walks it back down.
        //
        // A layer holds any number of GROUPS. Every group's balls land in ONE shared list and are fused in ONE
        // pass, so a low, cool group and a hot one on top melt together — which is precisely what two separate
        // Pyre layers could never do, since those composite independently.
        //
        // Every ball's entire state at a frame — where it rests, how it churns, when its wave was born, how much
        // energy it currently carries, how far out it has travelled, how far it has withered — is a CLOSED-FORM
        // function of (layer hash, group, ball index, layer life progress). Nothing accumulates between frames, so
        // this scrubs, bakes and plays back identically like every other Pyre shape.
        readonly struct HeightBall
        {
            public readonly float x, y, r, density, heat;
            /// The owning group's own opacity at this frame — blended per pixel across whichever groups cover it.
            public readonly float alpha;
            // Per-ball shape: `rx`/`ry` are the ellipse's semi-axes and `cs`/`sn` its rotation. A ball is only a
            // circle when Squash is 0 — otherwise every ball is its own seeded ellipse at its own angle, which is
            // what stops a fused cloud from reading as a bag of marbles no matter how well it melts.
            public readonly float rx, ry, cs, sn;
            public HeightBall(float x, float y, float r, float density, float heat, float aspect, float rot, float alpha)
            {
                this.x = x; this.y = y; this.r = r; this.density = density; this.heat = heat; this.alpha = alpha;
                rx = r * aspect;
                ry = r / aspect;
                cs = Mathf.Cos(rot);
                sn = Mathf.Sin(rot);
            }
            /// The largest distance this ball reaches from its own centre (used for bounds + confinement).
            public float Extent => Mathf.Max(rx, ry);
        }

        // One group's slice of the shared ball list, plus the surface-noise field that slice is deformed by.
        // Noise is sampled PER GROUP rather than once for the whole layer: the coherence that matters is
        // "neighbouring balls in the same stratum bulge together", and a smoke base and a flame burst have no
        // reason to share a roughness, a feature size or a drift rate.
        readonly struct HbGroupSlice
        {
            public readonly int start, end, surfSeed;
            public readonly float surfAmp, surfZoom, surfDriftX, surfDriftY;
            public HbGroupSlice(int start, int end, int surfSeed, float surfAmp, float surfZoom,
                                float surfDriftX, float surfDriftY)
            {
                this.start = start; this.end = end; this.surfSeed = surfSeed;
                this.surfAmp = surfAmp; this.surfZoom = surfZoom;
                this.surfDriftX = surfDriftX; this.surfDriftY = surfDriftY;
            }
        }

        // Each group gets its own disjoint block of ball indices, chosen by its OWN stable seedSalt rather than
        // its position in the list — so two groups never share a stream (which would place them identically),
        // and reordering the list is purely cosmetic. Salt 0's block starts at 0, so an upgraded single-group
        // layer keeps the exact placement/churn/wave stream it had before groups existed.
        const int HbGroupIndexBlock = 4096;

        // Blends toward max(a,b) with a soft knee of width k — the "fusion" that melts neighbouring balls into one
        // mass rather than letting the brighter one simply win.
        static float SmoothMax(float a, float b, float k)
        {
            if (k <= 0.0001f) return Mathf.Max(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(a, b, h) + k * h * (1f - h);
        }

        static float SmoothStep01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// A ball's seeded long/short axis ratio. 1 at Squash 0 (a circle), fanning out to roughly 1.9:1 either
        /// way at Squash 1 — either way, so the cloud gets both wide and tall balls rather than all leaning
        /// the same direction.
        static float BallAspect(float squash, int hash, int i, int salt)
        {
            if (squash <= 0.001f) return 1f;
            float t = Hash01(hash, i, salt) * 2f - 1f;          // -1..1
            return Mathf.Pow(1.9f, t * squash);
        }

        // Places one ball. The caller says where the group ASKED for it (`placedR`, in pixels from the cloud
        // centre) and how far a wave is carrying it beyond that (`push`); everything about staying inside the
        // frame happens here.
        //
        // The split between the two matters, and getting it wrong is what left the top of the Cloud-size dial
        // doing nothing. A ball placed inside the confinement circle is not under any pressure — it goes
        // exactly where it was put, untouched. Only travel BEYOND that has to be fought:
        //   • the overshoot passes through a u/(1+u) bend that only ever APPROACHES the limit, so a ball pushed
        //     twice as hard barely gets further out — it piles up against the crowd ahead of it;
        //   • and the harder the confinement had to pull it back, the more it FOLDS: it loses mass and heat,
        //     shrinks, and is tucked inward, so pressure at the rim converts into withering and churn instead
        //     of escape velocity. That is the layer's Fold dial, now measured against how much a ball was
        //     actually compressed rather than against its absolute distance out — which used to wither the
        //     whole outer third of the cloud whether anything had pushed it there or not.
        // `limit` is measured so the ball's whole EXTENT (centre + longest semi-axis, swollen by however much
        // this group's Surface noise can inflate its rim) stays inside the confinement circle, with half a
        // pixel to spare — so the silhouette approaches that circle without ever landing on it.
        static void AddHeightBall(System.Collections.Generic.List<HeightBall> balls, Vector2 center,
                                  float ang, float placedR, float push, Vector2 churnOffset,
                                  float r, float density, float heat,
                                  float confineR, float fold, float rimSwell, float aspect, float rot, float alpha)
        {
            if (r < 0.35f || (density <= 0.0005f && heat <= 0.0005f)) return;
            // Confine by the ball's LONGEST axis, not its mean radius — otherwise a strongly squashed ball could
            // poke its long end past the confinement circle that the whole feature exists to guarantee.
            float extent = r * Mathf.Max(aspect, 1f / Mathf.Max(0.0001f, aspect)) * rimSwell;
            float limit = Mathf.Max(confineR * 0.15f, confineR - extent - 0.5f);
            float free = Mathf.Clamp(placedR, 0f, limit);

            float x = center.x + Mathf.Cos(ang) * (free + Mathf.Max(0f, push)) + churnOffset.x;
            float y = center.y + Mathf.Sin(ang) * (free + Mathf.Max(0f, push)) + churnOffset.y;
            float dx = x - center.x, dy = y - center.y;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > free && d > 0.0001f)
            {
                float u = (d - free) / Mathf.Max(0.0001f, limit - free);
                float soft = free + (limit - free) * (u / (1f + u));
                float press = fold <= 0.0001f ? 0f
                            : Mathf.Clamp01((d - soft) / Mathf.Max(0.0001f, fold * limit));
                if (press > 0f)
                {
                    density *= Mathf.Lerp(1f, 0.12f, press);
                    heat *= Mathf.Lerp(1f, 0.06f, press);
                    r *= Mathf.Lerp(1f, 0.55f, press);
                    soft -= press * press * limit * 0.18f;      // folded in under the cloud
                    if (r < 0.35f) return;
                }
                float k = soft / d;
                x = center.x + dx * k;
                y = center.y + dy * k;
            }
            balls.Add(new HeightBall(x, y, r, density, heat, aspect, rot, alpha));
        }

        /// Where one ball of a wave lands radially, as 0..1 of the cloud's radius, from a uniform sample and
        /// the group's Spread. All three anchors are EXACT, not approximate — that is the whole point of the
        /// dial: 0 puts every ball at the centre (one clump, nothing left straggling near the rim), 0.5 is the
        /// evenly filled disc (the square root of a uniform sample — equal area per unit radius, and bit-exact
        /// with what the placement did before Spread existed), 1 puts every ball on the rim as a ring. Between
        /// those it slides linearly through whichever pair it sits inside, so the slider reads as one
        /// continuous gather-in/push-out gesture rather than a curve that only bites at its ends.
        static float SpreadRadius(float u, float spread)
        {
            float even = Mathf.Sqrt(Mathf.Clamp01(u));
            float s = Mathf.Clamp01(spread);
            return s <= 0.5f ? even * (s * 2f) : Mathf.Lerp(even, 1f, (s - 0.5f) * 2f);
        }

        // Resolves the whole layer for one frame: every enabled GROUP's currently-alive waves, all appended into
        // ONE shared list so the shading pass fuses them together. `slices` records where each group's balls sit
        // in that list, plus the surface-noise field that group is deformed by.
        static void BuildHeightBalls(System.Collections.Generic.List<HeightBall> balls,
                                     System.Collections.Generic.List<HbGroupSlice> slices,
                                     Layer layer, Pyre spec, int li, float lp, Vector2 center, float half, int hash)
        {
            var groups = layer.HeightBallGroups;
            if (groups == null) return;
            float confineR = Mathf.Clamp01(layer.hbConfine) * half;
            float fold = Mathf.Clamp01(layer.hbFold);

            // Two groups sharing an identity would draw from the same number stream and place their balls on
            // top of each other. The editor hands out unique salts, but a hand-built or hand-edited list might
            // not — so collisions are resolved here, locally, for this render only. Nothing is written back.
            var usedSalts = new System.Collections.Generic.List<int>();

            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                if (g == null || !g.enabled) continue;
                int gs = Mathf.Max(0, g.seedSalt);
                while (usedSalts.Contains(gs)) gs++;
                usedSalts.Add(gs);

                int start = balls.Count;
                BuildHeightBallGroup(balls, g, gs, spec, li, lp, center, half, hash, confineR, fold);
                if (balls.Count == start) continue;

                // Drift the noise field over the layer's life so the surface roils instead of sitting still.
                // Still a pure function of `lp`, so scrubbing/baking stay identical.
                slices.Add(new HbGroupSlice(start, balls.Count,
                    Hash(spec.seed, li, gs, 0x5B1F),
                    Mathf.Clamp01(g.surfaceNoise), Mathf.Max(1f, g.surfaceZoom),
                    lp * g.surfaceDrift * 8f, lp * g.surfaceDrift * -5f));
            }
        }

        // Index stride between two waves of the same group, so no ball ever shares another's random identity.
        // Must be >= HeightBallGroup.MaxWaveBalls, and MaxWaves * this (plus one slot per wave for the wave's
        // own starting angle) must stay inside HbGroupIndexBlock.
        const int HbWaveIndexBlock = 128;

        // One group: every wave currently alive. There is no second, wave-less ball population — a group IS
        // its waves, which is what guarantees every ball has a birth and a death to be animated across.
        //
        // Two clocks are in play and the split is deliberate:
        //   • `lp` — the LAYER's life. Cloud size, Rotation, Churn and Push describe the group as a whole,
        //     so they move on the same clock as everything else in the layer.
        //   • `a` — one WAVE's own 0..1 age, which is the age of every ball in it. Alpha, Height, Mass,
        //     Ball size and Spread describe a single ball, so they are read here. That is what makes an
        //     Alpha curve rising from and falling back to 0 a genuine fade in and out, with no separate
        //     fade dials, and what lets a Spread curve blow a clump out into a ring as the wave ages.
        static void BuildHeightBallGroup(System.Collections.Generic.List<HeightBall> balls, HeightBallGroup g, int gs,
                                         Pyre spec, int li, float lp, Vector2 center, float half, int hash,
                                         float confineR, float fold)
        {
            const float Tau = Mathf.PI * 2f;
            int seed = spec.seed;
            int gb = gs * HbGroupIndexBlock;

            float cloudR = Mathf.Clamp01(Eval(g.cloudSize, lp, seed, li, gs, F_SpawnRadius)) * half;
            float churn = Mathf.Max(0f, Eval(g.churn, lp, seed, li, gs, F_HbChurn));
            // One angle added to every placement in the group, so the whole arrangement turns about the
            // cloud's centre as a rigid body rather than each wave drifting independently.
            float spin = Eval(g.rotation, lp, seed, li, gs, F_HbRotation) * Mathf.Deg2Rad;
            float push = Eval(g.wavePush, lp, seed, li, gs, F_HbWavePush);
            float churnPhase = lp * Mathf.Max(0.1f, g.churnSpeed) * Tau;
            // Squash 0 = every ball a circle (the original look); 1 = each ball a strongly seeded ellipse at its
            // own angle. Balls also slowly TURN as the cloud churns, so a squashed cloud rolls instead of looking
            // like a frozen arrangement of ovals.
            float squash = Mathf.Clamp01(g.squash);
            // How far Surface noise can push a ball's rim past its nominal radius — the confinement has to
            // budget for it, or a noisy cloud pokes out of the circle that is supposed to contain it.
            float rimSwell = 1f + Mathf.Clamp01(g.surfaceNoise);

            // Waves are born evenly across the part of the layer's life that still leaves room for the last
            // one to finish, so nothing is cut off mid-fade at the final frame. At least one, always.
            int waves = Mathf.Clamp(g.waves, 1, HeightBallGroup.MaxWaves);
            int per = Mathf.Clamp(g.waveBalls, 1, HeightBallGroup.MaxWaveBalls);
            float waveLife = Mathf.Clamp(g.waveLife, 0.05f, 1f);
            // A single wave IS the layer: with one wave there is nothing to leave room for, so it spans the whole
            // life regardless of the Wave-life dial (which only exists to fit several waves into one window).
            float span = waves == 1 ? 1f : waveLife;
            float symmetry = Mathf.Clamp01(g.symmetry);

            for (int w = 0; w < waves; w++)
            {
                float birth = waves > 1 ? (w / (float)(waves - 1)) * (1f - waveLife) : 0f;
                float a = (lp - birth) / span;                      // this wave's own 0..1 age
                if (a < 0f || a > 1f) continue;

                float gAlpha = Mathf.Clamp01(Eval(g.Alpha, a, seed, li, gs, F_HbGroupAlpha));
                // An invisible ball must not exist at all: it would still add mass to the fused field and
                // still drag the blended opacity of whatever it overlaps down toward nothing.
                if (gAlpha <= 0.0005f) continue;
                float dens = Mathf.Max(0f, Eval(g.mass, a, seed, li, gs, F_HbDensity));
                float baseHeat = Mathf.Max(0f, Eval(g.height, a, seed, li, gs, F_HbBaseHeat));
                float spread = Eval(g.spread, a, seed, li, gs, F_HbSpread);

                // Outward travel eases to a halt well before the wave ends: a push, then churn — not a launch.
                float reach = push * SmoothStep01(a / 0.7f);
                // Past every wave's ball block, so a wave's own starting angle can never collide with a ball's
                // random identity however high Balls per wave goes.
                float rot = Hash01(hash, gb + HbWaveIndexBlock * HeightBallGroup.MaxWaves + w, 11) * Tau + spin;

                for (int j = 0; j < per; j++)
                {
                    int bi = gb + w * HbWaveIndexBlock + j;
                    // Evenly-spaced angles scattered by Symmetry, and a Spread-shaped radius inside the
                    // cloud: at Spread 0 every ball of the wave lands in the middle, at 0.5 they fill the
                    // disc evenly, at 1 they sit on the rim as a ring. Push then carries the whole wave out
                    // from wherever Spread placed it.
                    float jitter = (1f - symmetry) * (Hash01(hash, bi, 12) * 2f - 1f) * (Mathf.PI / per) * 1.7f;
                    float ang = rot + (j / (float)per) * Tau + jitter;
                    float placedR = SpreadRadius(Hash01(hash, bi, 2), spread) * cloudR;
                    float ph = Hash01(hash, bi, 14) * Tau;
                    float fr = Mathf.Lerp(0.7f, 1.8f, Hash01(hash, bi, 15));
                    var churnOff = new Vector2(Mathf.Sin(churnPhase * fr + ph) * churn,
                                               Mathf.Cos(churnPhase * fr * 0.71f + ph) * churn * 0.8f);
                    float r = Eval(g.ballSize, a, seed, li, bi, F_Size) * Mathf.Lerp(0.8f, 1.2f, Hash01(hash, bi, 16));
                    AddHeightBall(balls, center, ang, placedR,
                                  reach * Mathf.Lerp(0.75f, 1.15f, Hash01(hash, bi, 13)), churnOff, r,
                                  dens * Mathf.Lerp(0.85f, 1.15f, Hash01(hash, bi, 17)),
                                  baseHeat * Mathf.Lerp(0.35f, 1.25f, Hash01(hash, bi, 18)),
                                  confineR, fold, rimSwell,
                                  BallAspect(squash, hash, bi, 19),
                                  Hash01(hash, bi, 10) * Tau + churnPhase * 0.35f,
                                  gAlpha);
                }
            }
        }

        static void RenderHeightBalls(Color32[] buf, int W, int H, float cx, float cy, float framePhase,
                                      Layer layer, Pyre spec, int li, float lp, float alpha, Vector2 originOff,
                                      in ModStack stack, int frameIndex, int hash)
        {
            if (alpha <= 0.001f) return;
            float half = Mathf.Min(W, H) * 0.5f;
            Vector2 center = originOff + new Vector2(Eval(layer.positionX, lp, spec.seed, li, 0, F_PosX),
                                                     Eval(layer.positionY, lp, spec.seed, li, 0, F_PosY));

            var balls = new System.Collections.Generic.List<HeightBall>();
            var slices = new System.Collections.Generic.List<HbGroupSlice>();
            BuildHeightBalls(balls, slices, layer, spec, li, lp, center, half, hash);
            int n = balls.Count;
            if (n == 0) return;

            // Geometry modifiers mold the cloud as ONE aggregate shape (same treatment MetaBlob's fused field
            // gets), so Profile/Ground/Jagg see a single silhouette rather than dozens of tiny balls.
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var b = balls[i];
                float e = b.Extent;
                if (b.x - e < minX) minX = b.x - e; if (b.x + e > maxX) maxX = b.x + e;
                if (b.y - e < minY) minY = b.y - e; if (b.y + e > maxY) maxY = b.y + e;
            }
            var ctx = new GeoCtx(W * 0.5f, H * 0.5f, new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f),
                                 0.5f * Mathf.Max(maxX - minX, maxY - minY));

            // The melt knee is sized RELATIVE to the strongest ball in each channel, not an absolute number:
            // energy runs several times larger than mass, so one shared constant melted mass nicely while
            // leaving energy an almost-hard max — hot balls stayed visibly separate orbs. Scaling each knee to
            // its own channel makes the Fusion dial mean the same thing no matter how hot the cloud is set.
            float fusion = Mathf.Max(0f, layer.hbFusion);
            float maxD = 0f, maxHe = 0f, maxHi = 0f;
            for (int i = 0; i < n; i++)
            {
                var b = balls[i];
                if (b.density > maxD) maxD = b.density;
                if (b.heat > maxHe) maxHe = b.heat;
                float hi = b.density * 2.65f + b.heat * 0.72f;
                if (hi > maxHi) maxHi = hi;
            }
            float kD = fusion * maxD, kHe = fusion * maxHe, kHi = fusion * maxHi;

            var density = new float[W * H];
            var heat = new float[W * H];
            var height = new float[W * H];
            var groupAlpha = new float[W * H];

            // Pass 1 — fuse every group's balls into the three shared fields. Height weights heat well above
            // density, so an energised ball genuinely stands TALLER than a cold one and catches more of the
            // relief light.
            //
            // Surface noise is sampled once per pixel PER GROUP — not per ball. That is the whole point: because
            // every ball of a group at a given pixel is stretched or pinched by the SAME value, neighbouring
            // balls bulge and dent together and their rims interlock, so they read as one lumpy mass reshaping
            // itself rather than as separate wobbly circles. (A per-ball noise would just give each ball its own
            // independent wobble, which still reads as a pile of blobs.) Groups deform independently because
            // they are separate strata — a cool smoke base and a flame burst have no reason to share roughness.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                    if (stack.AnyGeo) off = ApplyGeo(stack, off, framePhase, ctx);

                    float d = 0f, he = 0f, hi = 0f;
                    float aWeight = 0f, aSum = 0f;
                    bool anyGroup = false;

                    for (int gi = 0; gi < slices.Count; gi++)
                    {
                        var sl = slices[gi];
                        float surf = 0f;
                        if (sl.surfAmp > 0.001f)
                            surf = PyreNoise.Sample(off.x / sl.surfZoom + sl.surfDriftX,
                                                    off.y / sl.surfZoom + sl.surfDriftY, sl.surfSeed, 0.6f) * 2f - 1f;
                        float rimScale = 1f + surf * sl.surfAmp;
                        float invRim2 = 1f / Mathf.Max(0.05f, rimScale * rimScale);

                        float gd = 0f, ghe = 0f, ghi = 0f;
                        bool gHit = false;
                        for (int i = sl.start; i < sl.end; i++)
                        {
                            var b = balls[i];
                            float dx = off.x - b.x, dy = off.y - b.y;
                            // into the ball's own rotated frame, then an ELLIPSE test rather than a circle
                            float lx = dx * b.cs + dy * b.sn;
                            float ly = -dx * b.sn + dy * b.cs;
                            float q = ((lx * lx) / (b.rx * b.rx) + (ly * ly) / (b.ry * b.ry)) * invRim2;
                            if (q >= 1f) continue;
                            gHit = true;
                            float s = Mathf.Sqrt(1f - q);          // a soft dome, 1 at the centre → 0 at the rim
                            gd = SmoothMax(gd, s * b.density, kD);
                            ghe = SmoothMax(ghe, s * b.heat, kHe);
                            ghi = SmoothMax(ghi, s * (b.density * 2.65f + b.heat * 0.72f), kHi);
                            // Opacity blends whichever balls reach this pixel, weighted by coverage AND by
                            // their own opacity — so a ball on its way out cannot drag a solid one it happens
                            // to overlap down with it. A plain coverage mean did exactly that, which now
                            // matters constantly: every ball fades through its own Alpha curve, so a dying
                            // wave overlapping a fresh one is the normal case, not an edge case.
                            float w = s * (b.density + b.heat) * b.alpha;
                            aWeight += w;
                            aSum += w * b.alpha;
                        }
                        // A group that reaches nowhere near this pixel must contribute NOTHING — a smooth max is
                        // not the identity on two zeroes (SmoothMax(0,0,k) == 0.25k), so folding an absent group
                        // in anyway would raise a faint uniform haze across the entire frame.
                        if (!gHit) continue;

                        // Height also takes the noise directly, scaled by how much cloud is actually here, so the
                        // lit surface gains roughness in the interior too — not just a wobbly outline.
                        if (sl.surfAmp > 0.001f && ghi > 0f) ghi *= 1f + surf * sl.surfAmp * 0.55f;

                        // The first group present SEEDS each field, later ones melt into it with the same soft
                        // knee — so a single group is byte-identical to the pre-groups renderer, and several
                        // genuinely fuse rather than compositing.
                        if (!anyGroup) { d = gd; he = ghe; hi = ghi; anyGroup = true; }
                        else { d = SmoothMax(d, gd, kD); he = SmoothMax(he, ghe, kHe); hi = SmoothMax(hi, ghi, kHi); }
                    }

                    int idx = y * W + x;
                    density[idx] = Mathf.Clamp01(d);
                    heat[idx] = Mathf.Clamp01(he);
                    height[idx] = Mathf.Clamp01(hi);
                    // No weight at all means nothing opaque reached here — either no cloud (culled by the
                    // occupancy test below) or only fully faded balls, which must read as fully faded.
                    groupAlpha[idx] = aWeight > 1e-6f ? aSum / aWeight : 0f;
                }

            // Pass 2 — relief lighting from the height field's local slope.
            float[] light = null;
            if (layer.hbLighting)
            {
                light = new float[W * H];
                float ang = Eval(layer.hbLightAngle, lp, spec.seed, li, 0, F_HbLightAngle) * Mathf.Deg2Rad;
                float lx = Mathf.Cos(ang), ly = Mathf.Sin(ang), lz = 0.72f;
                float ll = Mathf.Sqrt(lx * lx + ly * ly + lz * lz);
                lx /= ll; ly /= ll; lz /= ll;
                float relief = Mathf.Max(0.01f, layer.hbRelief);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (height[i] <= 0f) continue;
                        float hl = height[y * W + Mathf.Max(0, x - 1)], hr = height[y * W + Mathf.Min(W - 1, x + 1)];
                        float hd = height[Mathf.Max(0, y - 1) * W + x], hu = height[Mathf.Min(H - 1, y + 1) * W + x];
                        float nx = -(hr - hl) * relief, ny = -(hu - hd) * relief, nz = 1f;
                        float nl = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                        light[i] = 0.18f + Mathf.Max(0f, (nx * lx + ny * ly + nz * lz) / nl) * 0.82f;
                    }
            }

            // Pass 3 — shade. The pixel's combined density+heat, lit, is its position on the ONE gradient;
            // how MUCH of the cloud is there (whichever field is stronger) is its opacity.
            float coverage = Mathf.Max(0.1f, layer.hbCoverage);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float occupied = Mathf.Max(density[i], heat[i]);
                    if (occupied < 0.004f) continue;

                    float value = Mathf.Clamp01(density[i] + heat[i]);
                    // The lit range is deliberately wide (roughly 0.4x on a face turned away, 1.5x on one turned
                    // into the light): the resting cloud sits low on the ramp where a narrow modulation would be
                    // swallowed by the ramp's own dark end, and the carved look is the whole point of the shape.
                    if (light != null) value = Mathf.Clamp01(value * (0.35f + light[i] * 1.15f));
                    Color fc = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(value) : Color.white;
                    float outA = Mathf.Clamp01(occupied * coverage) * alpha * groupAlpha[i] * fc.a;
                    if (outA <= 0.003f) continue;

                    if (stack.AnyPix)
                    {
                        Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                        if (stack.AnyGeo) off = ApplyGeo(stack, off, framePhase, ctx);
                        if (!ApplyPix(stack, ref fc, ref outA, x, y, cx + off.x, cy + off.y, frameIndex, value, lp, hash, W, H))
                            continue;
                    }
                    Over(buf, i, fc.r, fc.g, fc.b, outA);
                }
        }

        // ── Fuse: melts a Ring/Rosing-scattered Disc layer's own shapes into ONE gradient-shaded metaball field,
        // via the same SDF-sum → threshold → shade approach RenderMetaBlob uses for hand-placed orbs (a "twin"
        // pattern reuse — MetaBlob's per-orb birth/life envelope and Fuse's per-shape scatter/timing differ enough
        // that forcing both through one shared function would obscure each). `circles` are the shapes' already-
        // computed (position, radius, current alpha-as-weight) — how they got placed (Ring/Rosing math, per-shape
        // jitter/timing) is entirely the caller's concern in RenderFrame.
        readonly struct FusionCircle
        {
            public readonly float x, y, r, weight;
            public FusionCircle(float x, float y, float r, float weight) { this.x = x; this.y = y; this.r = r; this.weight = weight; }
        }

        static void RenderFusedField(Color32[] buf, int W, int H, float framePhase, Layer layer, float life,
                                     System.Collections.Generic.List<FusionCircle> circles,
                                     ColorMode colorMode, float flowPos, float flowZoom,
                                     float noiseZoomV, float noiseRotRad, float noiseDriftXV, float noiseDriftYV, float noiseGradPosV, float noiseGradZoomV, float noiseWarpV,
                                     in ModStack stack, int frameIndex, int hash)
        {
            int n = circles.Count;
            if (n == 0) return;
            float threshold = Mathf.Max(0.02f, layer.metaThreshold);
            float range = Mathf.Max(0.05f, layer.metaShadeRange);
            // Capped at threshold — see RenderMetaBlob's identical band clamp for why: past that point the AA
            // band's lower bound (threshold - band) goes negative and every pixel in the WHOLE frame (not just
            // near the fused shape) starts contributing a faint uniform alpha.
            float band = Mathf.Clamp(layer.metaSoftness, 0.01f, threshold);
            Color overLifeColor = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(life) : Color.white;
            float nrCos = Mathf.Cos(noiseRotRad), nrSin = Mathf.Sin(noiseRotRad);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var c = circles[i];
                if (c.x - c.r < minX) minX = c.x - c.r; if (c.x + c.r > maxX) maxX = c.x + c.r;
                if (c.y - c.r < minY) minY = c.y - c.r; if (c.y + c.r > maxY) maxY = c.y + c.r;
            }
            Vector2 fieldCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float fieldRadius = 0.5f * Mathf.Max(maxX - minX, maxY - minY);
            var ctx = new GeoCtx(W * 0.5f, H * 0.5f, fieldCenter, fieldRadius);
            float cx = W * 0.5f, cy = H * 0.5f;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                    if (stack.AnyGeo) off = ApplyGeo(stack, off, framePhase, ctx);

                    float field = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var c = circles[i];
                        if (c.weight <= 0.001f) continue;
                        float dx = off.x - c.x, dy = off.y - c.y;
                        float r2 = c.r * c.r;
                        float d2 = dx * dx + dy * dy;
                        if (d2 >= r2) continue;
                        float k = 1f - d2 / r2; k *= k;   // compact polynomial kernel: 1 at centre → 0 at radius
                        field += c.weight * k;
                    }

                    if (field <= threshold - band) continue;
                    float a = Mathf.Clamp01((field - (threshold - band)) / band);   // AA across the iso-surface
                    if (a <= 0.003f) continue;

                    float rawFrac = Mathf.Clamp01((field - threshold) / range);
                    Color fc;
                    if (colorMode == ColorMode.OverLife) fc = overLifeColor;
                    else if (colorMode == ColorMode.NoiseFill)
                    {
                        float rx = off.x - fieldCenter.x, ry = off.y - fieldCenter.y;
                        float nrx = rx * nrCos - ry * nrSin, nry = rx * nrSin + ry * nrCos;
                        float noiseN = PyreNoise.Sample((nrx + noiseDriftXV) / noiseZoomV, (nry + noiseDriftYV) / noiseZoomV, hash, noiseWarpV);
                        int bands = Mathf.Max(1, layer.noiseBands);
                        float nfrac = NoiseBandFrac(noiseN, bands, layer.noiseBandSoftness);
                        // Mirror (0→1→0), not a plain wrap, same reasoning as FlowingFill below — a gradient's own
                        // start/end colours are rarely identical, so wrapping this straight into another 0 would
                        // jump between them every cycle. Mirroring instead plays the gradient forward then
                        // backward, so it's smooth at every period boundary regardless of Position/Zoom.
                        float noiseGradF = Mathf.Repeat(nfrac * noiseGradZoomV + noiseGradPosV, 1f);
                        float noiseGradFrac = 1f - Mathf.Abs(2f * noiseGradF - 1f);
                        fc = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(noiseGradFrac) : Color.white;
                    }
                    else
                    {
                        float frac;
                        if (colorMode == ColorMode.FlowingFill)
                        {
                            float f = Mathf.Repeat(rawFrac * flowZoom + flowPos, 1f);
                            frac = 1f - Mathf.Abs(2f * f - 1f);
                        }
                        else frac = Mathf.Clamp01(rawFrac * flowZoom + flowPos);
                        fc = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(frac) : Color.white;
                    }
                    float outA = a * fc.a;
                    if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, cx + off.x, cy + off.y, frameIndex, rawFrac, life, hash, W, H)) continue;
                    Over(buf, y * W + x, fc.r, fc.g, fc.b, outA);
                }
        }

        static void RasterShape(Color32[] buf, int W, int H, float cx, float cy,
                                float framePhase, Layer layer, Vector2 c, float radius, Color baseCol, float alpha,
                                float t, int shapeSeed, float crescX, float crescY,
                                ModStack stack, int frameIndex, float sparkleDensity, int sparkleSub, float holeSize,
                                float outerSoft, float innerSoft, float flowPos, float flowZoom, float gradX, float gradY,
                                float holeOffX, float holeOffY, float shapeRotRad,
                                float noiseZoomV, float noiseRotRad, float noiseDriftXV, float noiseDriftYV, float noiseGradPosV, float noiseGradZoomV, float noiseWarpV,
                                float sparkleBlobRadius, float sparkleBlobLife, float sparkleBlobSoft,
                                bool useGradientFill = true)
        {
            var ctx = new GeoCtx(W * 0.5f, H * 0.5f, c, radius);
            float nrCos = Mathf.Cos(noiseRotRad), nrSin = Mathf.Sin(noiseRotRad);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    // Undo the opt-in geometry modifiers (outermost first) to reach undeformed blast space.
                    Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                    if (stack.AnyGeo) off = ApplyGeo(stack, off, framePhase, ctx);
                    // Ring + Align rotation: spin the shape's own frame about its centre (Crescent bite, hollow
                    // hole offset, gradient core all follow) so it faces its ring angle instead of a fixed one.
                    if (shapeRotRad != 0f)
                    {
                        float dxr = off.x - c.x, dyr = off.y - c.y;
                        float cr = Mathf.Cos(-shapeRotRad), sr = Mathf.Sin(-shapeRotRad);
                        off = new Vector2(c.x + dxr * cr - dyr * sr, c.y + dxr * sr + dyr * cr);
                    }
                    float ux = off.x, uy = off.y;

                    // Edge modifiers perturb ONLY the outer boundary test (by angle around the shape centre),
                    // leaving ux/uy — and so the fill/gradient sampling below — untouched: a jagged/wavy silhouette
                    // with a clean interior instead of a warped one. edgeSoftPx (if any modifier sets it) widens
                    // the hit-test radius so pixels in the fade band still register as a hit below, then fades
                    // their alpha back out afterwards — a soft edge that correctly tracks the PERTURBED boundary,
                    // unlike the layer's own Outer softness (which fades from the shape's true, unwarped radius).
                    float outerRadius = radius;
                    float edgeSoftPx = 0f;
                    if (stack.AnyEdge)
                    {
                        float edgeAng = Mathf.Atan2(uy - c.y, ux - c.x);
                        for (int ei = 0; ei < stack.edge.Length; ei++)
                        {
                            outerRadius += stack.edge[ei].EdgeOffset(edgeAng, shapeSeed, ctx);
                            edgeSoftPx = Mathf.Max(edgeSoftPx, stack.edge[ei].EdgeSoftness(edgeAng, shapeSeed, ctx));
                        }
                        outerRadius = Mathf.Max(0.1f, outerRadius);
                    }

                    if (!ShapeHit(layer, ux, uy, c, radius, outerRadius + edgeSoftPx, t, shapeSeed, x, y, frameIndex, baseCol, crescX, crescY,
                                 sparkleDensity, sparkleSub, holeSize, holeOffX, holeOffY,
                                 sparkleBlobRadius, sparkleBlobLife, sparkleBlobSoft, out Color col))
                        continue;

                    // Normalised distance from the shape centre (0 = centre, 1 = edge) — drives the outer softness AND
                    // the Tint modifier's cross gradient / fill (crossFrac). Divides by outerRadius (== radius when
                    // no Edge modifier is active, so this is a no-op then), NOT the shape's true unperturbed radius —
                    // using `radius` here was the actual bug behind "Edge warp doesn't seem to do anything": the
                    // outer-softness fade below (pixelAlpha *= (1-nd)/outerSoft) zeroed out anything past the TRUE
                    // round radius regardless of how far the edge warp had bulged outerRadius outward, so any
                    // outward bulge got silently erased back to fully transparent by the very next fade — only a
                    // faint trace of inward dents (already low-alpha that close to the edge) ever showed through.
                    float distFromCenter = Mathf.Sqrt((ux - c.x) * (ux - c.x) + (uy - c.y) * (uy - c.y));
                    float nd = outerRadius > 0.001f ? Mathf.Clamp01(distFromCenter / outerRadius) : 0f;

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
                    else if (layer.shape == LayerShape.Crescent)
                    {
                        // Same two fades as Disc/Hollow, just renamed to Crescent's own geometry: the OUTER
                        // boundary (nd, identical to Disc) and the BITE edge (measured from the mask disc's
                        // centre) — the bite's "hole size" is implicitly always 1 (full radius), since the mask
                        // disc is always the same size as the main one, so the Hollow-disc hole-size scaling term
                        // simply drops out of the same formula.
                        if (outerSoft > 0.001f) pixelAlpha *= Mathf.Clamp01((1f - nd) / outerSoft);
                        if (innerSoft > 0.001f)
                        {
                            Vector2 mc = c + new Vector2(crescX, crescY);
                            float mnd = radius > 0.001f
                                ? Mathf.Sqrt((ux - mc.x) * (ux - mc.x) + (uy - mc.y) * (uy - mc.y)) / radius : 0f;
                            pixelAlpha *= Mathf.Clamp01((mnd - 1f) / innerSoft);
                        }
                    }
                    if (edgeSoftPx > 0.001f)
                        pixelAlpha *= Mathf.Clamp01((outerRadius + edgeSoftPx - distFromCenter) / edgeSoftPx);
                    if (pixelAlpha <= 0.001f) continue;

                    // Colour mode: Over life = the per-shape colour; Fill = the gradient across the shape (centre→
                    // edge); Flow fill = that spatial fill scrolled through the gradient over life. (Sparkle keeps
                    // its own per-pixel shimmer.)
                    Color fc = col;
                    if (useGradientFill && layer.colorMode != ColorMode.OverLife && layer.shape != LayerShape.SparkleField && layer.colorOverLife != null)
                    {
                        if (layer.colorMode == ColorMode.NoiseFill)
                        {
                            // Sample the domain-warped noise field in the shape's own (rotated) frame, centred on
                            // the shape — independent of the Fill/Flow fill gradient-position/zoom/core fields above.
                            float rx = ux - c.x, ry = uy - c.y;
                            float nrx = rx * nrCos - ry * nrSin, nry = rx * nrSin + ry * nrCos;
                            float n = PyreNoise.Sample((nrx + noiseDriftXV) / noiseZoomV, (nry + noiseDriftYV) / noiseZoomV, shapeSeed, noiseWarpV);
                            int bands = Mathf.Max(1, layer.noiseBands);
                            float nfrac = NoiseBandFrac(n, bands, layer.noiseBandSoftness);
                            // Mirror (0→1→0), not a plain wrap — see the other Noise fill paths' identical comment.
                            float noiseGradF = Mathf.Repeat(nfrac * noiseGradZoomV + noiseGradPosV, 1f);
                            float noiseGradFrac = 1f - Mathf.Abs(2f * noiseGradF - 1f);
                            fc = layer.colorOverLife.Evaluate(noiseGradFrac);
                        }
                        else
                        {
                            // Colour distance is measured from the (optionally offset) gradient core, not the shape
                            // centre — so the highlight can sit off-centre for a 3D orb. Edge softness still uses `nd`.
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
                    }
                    float outA = pixelAlpha * fc.a;
                    if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, ux + cx, uy + cy, frameIndex, nd, t, shapeSeed, W, H))
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
                    // No geometry-warp stack applies to Sprite particles (see the class doc above), so the
                    // "warped" position is just the plain pixel centre.
                    if (stack.AnyPix && !ApplyPix(stack, ref fc, ref outA, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, life, hash, W, H))
                        continue;
                    Over(buf, y * W + x, fc.r, fc.g, fc.b, outA);
                }
        }

        // Returns whether this undeformed pixel is inside the shape, and the colour to lay down.
        static bool ShapeHit(Layer layer, float ux, float uy, Vector2 c, float radius, float outerRadius, float t,
                             int shapeSeed, int x, int y, int frameIndex, Color baseCol, float crescX, float crescY,
                             float sparkleDensity, int sparkleSub, float holeSize, float holeOffX, float holeOffY,
                             float sparkleBlobRadius, float sparkleBlobLife, float sparkleBlobSoft, out Color col)
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
                    // outerRadius (not radius) gates the boundary so Rough-edge modifiers can jag/wave the rim
                    // without disturbing the hole/fill, which still read the shape's TRUE, un-perturbed radius.
                    if (dist > outerRadius) return false;
                    // holeSize = inner radius fraction (0 = full disc; a Hollow disc carves a — possibly offset — hole).
                    return holeDist >= holeSize * radius;
                }

                case LayerShape.SparkleField:
                {
                    if (dist > outerRadius || holeDist < holeSize * radius) return false;   // disc, with an optional hole

                    if (!layer.sparkleBlobs)
                    {
                        // Original single-pixel-per-frame mode: which pixels light up, hashed with the sub-seed so
                        // it re-rolls (twinkles) when animated.
                        float h = Hash01(shapeSeed ^ sparkleSub, x, y);
                        if (h >= sparkleDensity) return false;
                        // Shimmer: shift each lit pixel along the gradient by its own hash and by life.
                        if (layer.colorOverLife != null)
                            col = layer.colorOverLife.Evaluate(Mathf.Repeat(t + h, 1f));
                        return true;
                    }

                    // Blob mode: quantize to a cell grid so each cell can host one persistent, multi-frame sparkle
                    // (grow in, hold, fade out via MetaEnv — the same envelope MetaBlob's orbs use) instead of a
                    // single flickering pixel. Cell presence/phase/jitter are hashed off shapeSeed alone (NOT
                    // sparkleSub, which bakes in frameIndex for the twinkle-every-frame mode) so a sparkle's
                    // identity stays stable across its own multi-frame lifetime; the "twinkle" quality instead
                    // comes from each cell's own repeating on/off cycle.
                    float cellSize = Mathf.Max(1f, sparkleBlobRadius * 2.2f);
                    int gx = Mathf.FloorToInt(ux / cellSize), gy = Mathf.FloorToInt(uy / cellSize);
                    float presence = Hash01(shapeSeed, gx * 4, gy * 4);
                    if (presence >= sparkleDensity) return false;   // this cell never gets a sparkle at all

                    float phase = Hash01(shapeSeed, gx * 4 + 1, gy * 4 + 1);         // desyncs cells' cycles
                    float jx = Hash01(shapeSeed, gx * 4 + 2, gy * 4 + 2) - 0.5f;      // jittered centre within
                    float jy = Hash01(shapeSeed, gx * 4 + 3, gy * 4 + 3) - 0.5f;      // the cell, for an organic scatter
                    Vector2 cellCentre = new Vector2((gx + 0.5f + jx * 0.6f) * cellSize, (gy + 0.5f + jy * 0.6f) * cellSize);

                    float cyclePos = Mathf.Repeat(frameIndex / sparkleBlobLife + phase, 1f);
                    float env = MetaEnv(cyclePos);   // 0..1 grow-hold-fade, repeating
                    if (env <= 0.01f) return false;

                    float curRadius = sparkleBlobRadius * env;   // area of effect fades in/out with brightness
                    float bd = Vector2.Distance(new Vector2(ux, uy), cellCentre);
                    if (bd > curRadius) return false;
                    float edgeFade = sparkleBlobSoft > 0.001f ? Mathf.Clamp01((curRadius - bd) / (curRadius * sparkleBlobSoft)) : 1f;

                    col = layer.colorOverLife != null ? layer.colorOverLife.Evaluate(Mathf.Repeat(t + phase, 1f)) : Color.white;
                    col.a *= env * edgeFade;
                    return col.a > 0.003f;
                }

                case LayerShape.Crescent:
                {
                    if (dist > outerRadius) return false;
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

        // Noise fill's shading-band quantization, shared by all three NoiseFill sampling sites below. At
        // softness=0 this is byte-identical to the original hard `Floor(noiseN*bands)/(bands-1)` step; at
        // softness=1 it's the fully continuous `noiseN` (same look as Bands=1) — a plain crossfade between the
        // two, so any softness in between blends a recognizable band with its neighbour instead of popping.
        static float NoiseBandFrac(float noiseN, int bands, float softness)
        {
            if (bands <= 1) return noiseN;
            float n = Mathf.Clamp01(noiseN);
            float hardFrac = Mathf.Floor(n * bands) / (bands - 1);
            if (softness <= 0.0001f) return hardFrac;
            return Mathf.Lerp(hardFrac, n, softness);
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
        public static Texture2D RenderFrameTexture(Pyre spec, int frameIndex)
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

        /// Texture2D wrapper around RenderShapePreview, same pattern as RenderFrameTexture — for PyreWindow's
        /// isolated single-shape preview box.
        public static Texture2D RenderShapePreviewTexture(Layer layer, Pyre spec, int li, float t, int frameIndex,
                                                           int W, int H, bool showGradientFill, bool showCrescent,
                                                           bool showHollow, bool showSize, bool showSpin, bool showAlpha,
                                                           bool showModifiers)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Pyre_ShapePreview"
            };
            tex.SetPixels32(RenderShapePreview(layer, spec, li, t, frameIndex, W, H, showGradientFill, showCrescent,
                                               showHollow, showSize, showSpin, showAlpha, showModifiers));
            tex.Apply();
            return tex;
        }

        /// Pack every frame into a grid sheet (cols left→right, rows top→bottom) for baking / preview.
        public static Texture2D RenderSheet(Pyre spec, out int cols, out int rows, int maxCols = 8)
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

// PyreRenderer.Layers — a frame taken apart into layers, so a caller can render ONE layer of ONE frame on its
// own and put a frame back together from layer buffers it already has. RenderFrame itself runs through exactly
// these pieces (PlanFrame → RenderLayerBody → FrameComposer.Apply → Finish), so there is one per-layer code path
// for the bake and for the editor's per-layer preview cache (PyreWindow.FrameCache.cs).
//
// The one thing a layer buffer cannot reproduce exactly is the renderer's FAST PATH: a plain Draw layer (no post
// over earlier content, no clip, no luma matte, no border, no sim, an enum form) paints its particles straight into
// the accumulating frame. `Over` is straight-alpha with a byte quantisation per step, so "particles onto the frame
// one by one" and "particles onto a transparent buffer, then that buffer onto the frame" differ by rounding
// wherever the frame beneath is not transparent (even Over onto a transparent pixel rounds 255·a·(1/a) down for
// ~4% of value/alpha pairs). Two rules keep the composite honest: where the frame beneath a fast-path layer is
// still transparent its pixels are COPIED verbatim (the same bytes the straight draw produced), and a fast-path
// layer that is the first thing drawn over a VISIBLE background is rendered FUSED with the background
// (LayerPlan.fuseBackground) so a post modifier on it sees bg + layer exactly as the bake does. What remains, where
// a fast-path layer overlaps earlier content, is the quantisation of its straight-alpha colour to bytes before it
// composites: premultiplied colour and alpha move by at most a step or two (the straight colour of a near-
// transparent pixel can read further off, because dividing by a tiny alpha amplifies a 1/255 step) — invisible,
// and pinned by the tests.
using System;
using System.Collections.Generic;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre
{
    public static partial class PyreRenderer
    {
        /// Everything RenderFrame decides about one layer of one frame BEFORE drawing it — a pure function of the
        /// spec and the frame index (PlanFrame), so the cache can key and schedule layers without rendering.
        public struct LayerPlan
        {
            public bool active;            // enabled and inside its start/end window this frame
            public float layerLife;        // the layer's own 0..1 clock across its window
            public bool isMatte;           // WriteMatte role: invisible, writes its coverage into a channel
            public bool isLuma;            // LumaMatte role: invisible, arms the luma matte for the Draw layers above
            public bool hasClip;           // Draw: alpha × a numbered channel an active writer below has written
            public bool isHeightConsumer;  // Draw: renders the accumulated channel as a heightmap instead of its shape
            public bool matteActive;       // Draw: an armed luma matte is applied to this layer before it composites
            public bool fastPath;          // Draw: painted straight into the frame (no isolation) by RenderFrame
            public bool fuseBackground;    // fastPath, nothing drawn yet, visible background: cache it WITH the background
            public bool hasBorder, hasPost, hasLayerSim;
            public bool IsDraw => active && !isMatte && !isLuma;
        }

        /// A visible background is anything the layers composite OVER: a clear colour with alpha, or a background
        /// fill. A fully transparent clear (the default) leaves nothing beneath the first layer.
        public static bool BackgroundVisible(Pyre spec) =>
            spec != null && (spec.backgroundUseFill ? spec.backgroundFill != null : ((Color32)spec.background).a != 0);

        /// The per-layer plan of one frame, indexed like spec.layers (inactive layers have `active == false`).
        public static LayerPlan[] PlanFrame(Pyre spec, int frameIndex)
        {
            if (spec == null || spec.layers == null) return Array.Empty<LayerPlan>();
            var layers = spec.layers;
            var plans = new LayerPlan[layers.Count];
            int frames = Mathf.Max(1, spec.frameCount);

            // The four matte channels exist for the frame when any ENABLED layer (window or not) uses them — the
            // same test RenderFrame has always made, which is what `hasClip` / `isHeightConsumer` hinge on.
            bool anyMatte = false;
            for (int li = 0; li < layers.Count; li++)
            {
                var l0 = layers[li];
                if (l0 == null || !l0.enabled) continue;
                if (l0.matteRole == MatteRole.WriteMatte
                    || (l0.matteRole == MatteRole.Draw && l0.clipByChannel >= 0)
                    || (l0.matteRole == MatteRole.Draw && l0.heightFromChannel >= 0)) { anyMatte = true; break; }
            }
            bool[] written = anyMatte ? new bool[4] : null;
            bool bufDirty = false;          // something has been drawn into the frame
            bool matteArmed = false;        // a LumaMatte layer below is waiting to shape the next Draw layer(s)
            bool matteOneShot = false;
            bool bgVisible = BackgroundVisible(spec);

            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                if (layer == null || !layer.enabled) continue;
                // Lifetime window (#55): endFrame < 0 is the "last frame" sentinel; outside the window the layer is
                // inactive; inside, its life is lerped across [start, end] exactly as Pyre1 does.
                int winStart = Mathf.Clamp(layer.startFrame, 0, frames - 1);
                int winEnd = layer.endFrame < 0 ? (frames - 1) : Mathf.Clamp(layer.endFrame, 0, frames - 1);
                if (winEnd < winStart) winEnd = winStart;
                if (frameIndex < winStart || frameIndex > winEnd) continue;

                ref var p = ref plans[li];
                p.active = true;
                p.layerLife = Mathf.Clamp01((frameIndex - winStart) / (float)Mathf.Max(1, winEnd - winStart));
                p.hasPost = HasEnabledPost(layer.modifiers);
                p.hasLayerSim = layer.simulationModifier != null && layer.simulationModifier.enabled;
                // The border rim belongs to the six flat enum forms only; a plug-in form owns its whole look.
                p.hasBorder = layer.form == null && layer.borderEnabled && IsFlat2DBorderForm(layer.shapeForm);

                bool matteOn = layer.matteEnabled;
                p.isMatte = matteOn && layer.matteRole == MatteRole.WriteMatte;
                p.isLuma = matteOn && layer.matteRole == MatteRole.LumaMatte;
                // Clip only by a channel an earlier writer actually WROTE (#58 fix 3) — an unwritten channel must not
                // blank the layer.
                p.hasClip = matteOn && !p.isMatte && !p.isLuma && written != null
                            && layer.clipByChannel >= 0 && layer.clipByChannel < 4 && written[layer.clipByChannel];

                if (p.isMatte) { if (written != null) written[Mathf.Clamp(layer.matteChannel, 0, 3)] = true; continue; }
                if (p.isLuma) { matteArmed = true; matteOneShot = layer.matteScope == MatteScope.NextLayer; continue; }

                p.isHeightConsumer = matteOn && written != null && layer.heightFromChannel >= 0 && layer.heightFromChannel < 4;
                p.matteActive = matteArmed;
                bool isFire = layer.shapeForm == ShapeForm.Fire;
                bool isFireball = layer.shapeForm == ShapeForm.Fireball;
                bool isForm = layer.form != null;
                // Isolation is needed when something must happen to this layer's pixels BEFORE they meet the frame
                // (clip, matte, border, a post that must not re-touch the layers beneath) or when the form SETS
                // pixels rather than Over-drawing them (forms, sims). Otherwise the particles paint straight in.
                bool needScratch = p.hasClip || (p.hasPost && bufDirty) || p.matteActive || isFire || isFireball
                                   || isForm || p.hasLayerSim || p.hasBorder;
                p.fastPath = !needScratch;
                p.fuseBackground = p.fastPath && !bufDirty && bgVisible;
                if (p.matteActive && matteOneShot) matteArmed = false;   // NextLayer scope: consumed by this layer
                bufDirty = true;
            }
            return plans;
        }

        // Paint the frame's background: the per-pixel fill when enabled, else the flat clear.
        static void PaintBackground(Color32[] buf, Pyre spec, int W, int H, float life)
        {
            if (spec != null && spec.backgroundUseFill && spec.backgroundFill != null)
            {
                for (int y = 0; y < H; y++)
                {
                    float v = H > 1 ? (y + 0.5f) / H * 2f - 1f : 0f;
                    int rowBase = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        float u = W > 1 ? (x + 0.5f) / W * 2f - 1f : 0f;
                        buf[rowBase + x] = (Color32)spec.backgroundFill.Evaluate(life, u, v);
                    }
                }
            }
            else
            {
                Color32 bg = spec != null ? (Color32)spec.background : Transparent;
                for (int i = 0; i < buf.Length; i++) buf[i] = bg;
            }
        }

        /// Render layer `li` of frame `frameIndex` into `target` — the ONE per-layer body: the form/particles (or the
        /// heightmap consumer), the border rim (folded in, or handed back to draw on top of the whole frame), the
        /// layer's own post modifiers, its simulation slot. `target` is the frame itself on the fast path, otherwise
        /// a fresh buffer. `heightField` is the accumulated matte channel a heightmap consumer reads (null otherwise).
        /// Returns the over-matte border to composite on top of the finished frame, or null.
        static Color32[] RenderLayerBody(Color32[] target, Pyre spec, int li, int frameIndex, in LayerPlan plan, float[] heightField)
        {
            var layer = spec.layers[li];
            int W = spec.Width, H = spec.Height;
            int frames = Mathf.Max(1, spec.frameCount);
            float life = frames > 1 ? frameIndex / (float)(frames - 1) : 0f;
            // Per-frame wobble phase for every GeometryModifier.InverseWarp — divides by `frames`, not frames-1,
            // deliberately mirroring BlastRenderer.framePhase.
            float phase = frames > 1 ? (frameIndex / (float)frames) * Mathf.PI * 2f : 0f;
            // The layer's decorrelation salt is its index: every seeded draw below reads it.
            _layerSalt = li;
            // This frame's effective geometry/pixel stack: the layer's own modifiers wrapped by the spec's globals
            // (layer mods Eval at layerLife, globals at the blast life).
            ModSet mods = BuildMods(layer.modifiers, spec.globalModifiers, spec.seed, plan.layerLife, life);
            // Text: bake + snapshot the SDF atlas for this layer's string/font; false ⇒ Disc fallback per glyph.
            _textReady = layer.shapeForm == ShapeForm.Text && EnsureTextGlyphs(layer);

            if (plan.isHeightConsumer) RenderHeightConsumer(target, W, H, layer, heightField);
            else RenderLayer(target, W, H, plan.layerLife, spec, layer, mods, phase, frameIndex);
            // Border (#60/#65): built from the FILL silhouette (pre-post). borderOverMatte OFF folds the rim into the
            // layer; ON keeps the layer fill-only and defers the rim to draw on top of the finished frame.
            Color32[] borderBuf = plan.hasBorder ? BuildBorderBuffer(target, W, H, plan.layerLife, spec, layer) : null;
            if (borderBuf != null && !layer.borderOverMatte) CompositeLayer(target, borderBuf, null, false);
            if (plan.hasPost) ApplyLayerPost(layer.modifiers, spec.seed, target, W, H, plan.layerLife, frameIndex);
            // Simulation slot (slice 7): after the stateless posts, before the matte — vanilla Pyre's sim position.
            if (plan.hasLayerSim) ApplyLayerSim(layer.simulationModifier, spec.seed, target, W, H, frameIndex, frames);
            return borderBuf != null && layer.borderOverMatte ? borderBuf : null;
        }

        /// One layer of one frame rendered on its own, exactly as RenderFrame would have rendered it into its
        /// isolated buffer: transparent beneath, or the background when `plan.fuseBackground` (the fast-path layer
        /// that opens a frame over a visible background). `heightField` = AccumulateChannel(...) for a heightmap
        /// consumer, null otherwise. `deferredBorder` is the over-matte rim to draw on top of the frame, or null.
        public static Color32[] RenderLayerFrame(Pyre spec, int li, int frameIndex, in LayerPlan plan, float[] heightField, out Color32[] deferredBorder)
        {
            int W = spec.Width, H = spec.Height;
            var target = new Color32[W * H];
            if (plan.fuseBackground)
            {
                int frames = Mathf.Max(1, spec.frameCount);
                PaintBackground(target, spec, W, H, frames > 1 ? frameIndex / (float)(frames - 1) : 0f);
            }
            deferredBorder = RenderLayerBody(target, spec, li, frameIndex, plan, heightField);
            return target;
        }

        /// The numbered matte channel `channel` as it stands just before layer `uptoLi` renders: every active
        /// WriteMatte layer below it on that channel, combined in paint order from their cached buffers
        /// (`layerPixels[li]`, indexed like spec.layers). What a heightmap consumer reads while rendering.
        public static float[] AccumulateChannel(Pyre spec, LayerPlan[] plans, Color32[][] layerPixels, int uptoLi, int channel)
        {
            var plane = new float[spec.Width * spec.Height];
            for (int li = 0; li < uptoLi && li < plans.Length; li++)
            {
                if (!plans[li].active || !plans[li].isMatte) continue;
                var layer = spec.layers[li];
                if (Mathf.Clamp(layer.matteChannel, 0, 3) != channel) continue;
                WriteMatteCoverage(plane, layerPixels[li], layer.matteCombine, layer.matteWriteLuma);
            }
            return plane;
        }

        /// Put a frame together from layer buffers: `layerPixels[li]` / `layerBorders[li]` (indexed like
        /// spec.layers) for every active layer of `plans`, then the deferred borders and the spec-level post
        /// modifiers. The cached buffers are never written to.
        public static Color32[] ComposeFrame(Pyre spec, int frameIndex, LayerPlan[] plans, Color32[][] layerPixels, Color32[][] layerBorders)
        {
            var fc = new FrameComposer(spec, frameIndex, inputsAreScratch: false);
            for (int li = 0; li < plans.Length; li++)
                if (plans[li].active) fc.Apply(li, plans[li], layerPixels[li], layerBorders != null ? layerBorders[li] : null);
            return fc.Finish();
        }

        /// The state a frame carries through its layer loop: the frame buffer (background painted), the matte
        /// channels, the armed luma matte, the deferred borders. RenderFrame and ComposeFrame both drive one.
        public sealed class FrameComposer
        {
            public readonly Color32[] buf;
            readonly Pyre spec;
            readonly int W, H, frameIndex;
            readonly float life;
            readonly bool inputsAreScratch;   // true: Apply may write into the buffers it is handed (RenderFrame's own scratch)
            float[][] channels;
            MatteState matteState;
            List<Color32[]> deferredBorders;

            public FrameComposer(Pyre spec, int frameIndex, bool inputsAreScratch)
            {
                this.spec = spec;
                this.frameIndex = frameIndex;
                this.inputsAreScratch = inputsAreScratch;
                W = spec != null ? spec.Width : 1;
                H = spec != null ? spec.Height : 1;
                int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
                life = frames > 1 ? frameIndex / (float)(frames - 1) : 0f;
                buf = new Color32[W * H];
                PaintBackground(buf, spec, W, H, life);
                if (spec == null || spec.layers == null) return;
                for (int li = 0; li < spec.layers.Count; li++)
                {
                    var l0 = spec.layers[li];
                    if (l0 == null || !l0.enabled) continue;
                    if (l0.matteRole == MatteRole.WriteMatte
                        || (l0.matteRole == MatteRole.Draw && l0.clipByChannel >= 0)
                        || (l0.matteRole == MatteRole.Draw && l0.heightFromChannel >= 0))
                    {
                        channels = new float[4][];
                        for (int c = 0; c < 4; c++) channels[c] = new float[W * H];
                        break;
                    }
                }
            }

            /// The accumulated numbered channel (what a heightmap consumer renders from), or null when the frame
            /// has no matte channels.
            public float[] Channel(int c) => channels?[c];

            /// Fold one finished layer into the frame: a WriteMatte writes its coverage, a LumaMatte arms the matte,
            /// a Draw layer gets the armed matte and its clip applied and is composited. A fast-path Draw layer's
            /// buffer (the cache path) is copied verbatim over transparent pixels and Over-drawn elsewhere; a
            /// background-fused buffer replaces the frame outright.
            public void Apply(int li, in LayerPlan p, Color32[] pixels, Color32[] deferredBorder)
            {
                var layer = spec.layers[li];
                if (p.isMatte)
                {
                    if (channels != null)
                    {
                        int wch = Mathf.Clamp(layer.matteChannel, 0, 3);
                        WriteMatteCoverage(channels[wch], pixels, layer.matteCombine, layer.matteWriteLuma);
                    }
                    AddBorder(deferredBorder);
                    return;
                }
                if (p.isLuma)
                {
                    // The matte's strength/amounts Eval over THIS layer's own life with its own salt.
                    _layerSalt = li;
                    float strength = Mathf.Clamp01(Eval(layer.matteStrength, p.layerLife, spec.seed, ModParticleIndex, FldMatteStrength));
                    matteState.mask = BuildMatteMask(pixels, layer.matteInvert, strength);
                    matteState.flags = layer.matteFlags;
                    matteState.blurAmt = Eval(layer.matteBlurAmount, p.layerLife, spec.seed, ModParticleIndex, FldMatteBlur);
                    matteState.dispAmt = Eval(layer.matteDisplaceAmount, p.layerLife, spec.seed, ModParticleIndex, FldMatteDisplace);
                    matteState.hueDeg = Eval(layer.matteHueDegrees, p.layerLife, spec.seed, ModParticleIndex, FldMatteHue);
                    matteState.oneShot = layer.matteScope == MatteScope.NextLayer;
                    matteState.alphaSource = layer.matteAlphaSource;
                    AddBorder(deferredBorder);
                    return;
                }
                if (p.fastPath)
                {
                    if (p.fuseBackground) Array.Copy(pixels, buf, buf.Length);
                    else OverOrCopy(buf, pixels);
                    return;
                }
                if (p.matteActive)
                {
                    if (!inputsAreScratch) pixels = (Color32[])pixels.Clone();   // ApplyMatte rewrites the layer's pixels
                    ApplyMatte(pixels, matteState.mask, matteState.flags, matteState.blurAmt,
                               matteState.dispAmt, matteState.hueDeg, matteState.alphaSource, W, H);
                    if (matteState.oneShot) matteState = default;
                }
                float[] clip = p.hasClip && channels != null ? channels[layer.clipByChannel] : null;
                CompositeLayer(buf, pixels, clip, layer.clipInvert);
                AddBorder(deferredBorder);
            }

            void AddBorder(Color32[] border) { if (border != null) (deferredBorders ??= new List<Color32[]>()).Add(border); }

            // A fast-path layer's buffer onto the frame. Where the frame is still transparent the bytes are exactly
            // what the straight draw produced, so they are copied; elsewhere the layer is Over-drawn (the
            // quantisation case described at the top of this file).
            static void OverOrCopy(Color32[] buf, Color32[] layer)
            {
                for (int i = 0; i < buf.Length; i++)
                {
                    var s = layer[i];
                    // An all-zero pixel is one the layer never touched (or wrote as Transparent — the same bytes).
                    // A pixel with alpha 0 but colour is one Over quantised to alpha 0 (outA < 1/255 yet > 0.0001):
                    // the straight draw left exactly those bytes in the frame, so it is copied like any other.
                    if (s.a == 0 && s.r == 0 && s.g == 0 && s.b == 0) continue;
                    if (buf[i].a == 0) { buf[i] = s; continue; }
                    if (s.a == 0) continue;
                    Over(buf, i, s.r * (1f / 255f), s.g * (1f / 255f), s.b * (1f / 255f), s.a * (1f / 255f));
                }
            }

            /// The deferred over-matte borders on top of the stack, then the spec-level post modifiers, in list
            /// order, at the blast life with the global salt pinned. Returns the finished frame.
            public Color32[] Finish()
            {
                if (deferredBorders != null)
                    for (int i = 0; i < deferredBorders.Count; i++)
                        CompositeLayer(buf, deferredBorders[i], null, false);
                if (spec != null && spec.globalModifiers != null)
                {
                    int savedSalt = _layerSalt;
                    _layerSalt = GlobalLayerSalt;
                    for (int i = 0; i < spec.globalModifiers.Count; i++)
                    {
                        var m = spec.globalModifiers[i];
                        if (m == null || !m.enabled) continue;
                        if (m is PostModifier post)
                        {
                            SetPostContext(post, life, spec.seed, frameIndex);
                            int idx = i;
                            m.Prepare((v, fid) => Eval(v, life, spec.seed, ModParticleIndex, FldModifier + (GlobalPostBase + idx) * 8 + fid));
                            post.Apply(buf, W, H);
                        }
                    }
                    _layerSalt = savedSalt;
                }
                return buf;
            }
        }
    }
}

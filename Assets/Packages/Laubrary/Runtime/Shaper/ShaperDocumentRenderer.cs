// ShaperDocumentRenderer — the document → pixels driver.
//
// T-0153. Until this file existed, Shaper had every piece of a renderer and nothing joining them, so a
// multi-layer document had NO DEFINED FINAL IMAGE anywhere in the engine. That is not a reading of the code;
// two independent agents reached it separately and T-0115's own spec says it outright: "There is no
// document/window/canvas-grid-renderer layer in Laubrary's own Shaper yet" (T-0115/SPEC.md:11).
//
// What already existed, and why none of it was a renderer:
//   • ShaperFrameCache.ComputeFrame returns a ShaperFieldBuffer — a canvas-sized float DISTANCE field for ONE
//     node tree (ShaperNodeCache.cs:10-20). Not colour, not a document.
//   • ShaperResolve.Query is a ray query against an ordered layer list, not a raster pass.
//   • ShaperFillResolver.PaintTile (:899) + Encode (:1516) are the colour path, but they paint ONE layer into
//     one ShaperFillBuffers and nothing walked ShaperDocument.layers to drive them and combine the results.
//
// The working sequence this file is built from was written twice before it was written here: first as
// ShaperLightAudit.cs:2851-2868 (CompileDocument → per-layer Resolve → BindLayer → PaintTile), and then again
// inside the T-0148 baker, which had to add the missing layer composite to produce a sheet at all. Both were
// downstream copies of a step that belongs in the engine. This is now the ONE implementation: ShaperBaker
// calls it and owns no pixel path of its own, so the baker's "the bake is what the preview showed" guarantee
// holds because there is a single renderer rather than because two implementations happen to agree. Phase C's
// preview window must call this too, for exactly the same reason.
using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// Renders a whole <see cref="ShaperDocument"/> — every enabled layer, composited — at a frame or an
    /// explicit phase.
    ///
    /// <b>Two output forms, both deliberate.</b> The float form (<see cref="RenderPhaseInto"/>) is the
    /// accumulated PREMULTIPLIED LINEAR destination, which is the form <see cref="ShaperFillResolver.Encode"/>'s
    /// own documentation names as the one a light stage or a further composite should read
    /// (<c>ShaperFillResolver.cs:1507-1509</c>) — it carries an additive glow over transparency exactly, which
    /// no 8-bit encoding can. The <see cref="Color32"/> form is that buffer encoded ONCE at the end, for a
    /// consumer that genuinely wants final pixels (a sheet, a preview blit). Exposing only the encoded form
    /// would force any compositing caller to un-premultiply and re-encode per step, which is precisely the
    /// mistake <c>Encode</c>'s comment warns about.
    /// </summary>
    public static class ShaperDocumentRenderer
    {
        /// <summary>Floats per sample in the accumulated destination: premultiplied linear R,G,B and alpha.</summary>
        public const int FloatsPerSample = 4;

        /// <summary>
        /// T-0171 — the depth band, in CANVAS PIXELS, over which a layer that is BEHIND the accumulated
        /// surface fades from painting under it to painting over it.
        ///
        /// One canvas pixel is chosen because Z is measured in canvas pixels (<see cref="ShaperDocument.pixelSize"/>
        /// is what relates a canvas pixel to a sample, LR-1.5) and because the band's job is to be invisible at
        /// the resolution the picture is actually sampled at: a surface crossing another within a single pixel
        /// of depth is a surface the raster cannot separate anyway, so blending there is honest rather than
        /// arbitrary. A band of zero would z-test hard and produce a one-sample stair along every intersection
        /// curve; a band of many pixels would smear a genuine occlusion into a haze.
        /// </summary>
        public const float DepthBlendBand = 1f;

        /// <summary>Samples in <paramref name="doc"/>'s canvas. 0 when the document is null.</summary>
        public static int SampleCount(ShaperDocument doc)
            => doc == null ? 0 : Mathf.Max(1, doc.canvasWidth) * Mathf.Max(1, doc.canvasHeight);

        /// <summary>
        /// Render one frame index to straight-alpha sRGB pixels. The index wraps through
        /// <see cref="ShaperClock.WrapFrame"/> and converts through the document's own
        /// <see cref="ShaperDocument.PhaseOfFrame"/>, so this file introduces no second frame→phase rule
        /// (T-0144 consolidated that into <see cref="ShaperClock"/> precisely so it could not drift).
        /// </summary>
        public static Color32[] RenderFrame(ShaperDocument doc, int frameIndex,
                                            IShaperEffectApplier effects = null,
                                            ShaperRenderBufferPool pool = null,
                                            ShaperLayerBufferCache layerCache = null)
        {
            if (doc == null) return Array.Empty<Color32>();
            int wrapped = ShaperClock.WrapFrame(frameIndex, Mathf.Max(1, doc.frameCount));
            return RenderPhase(doc, doc.PhaseOfFrame(wrapped), effects, wrapped, pool, layerCache);
        }

        /// <summary>
        /// Render at an explicit phase to straight-alpha sRGB pixels. Row 0 of the returned array is the BOTTOM
        /// row, matching both <see cref="ShaperSampleGrid"/>'s +Y-up sampling and <c>Texture2D.SetPixels32</c>,
        /// so nothing between the evaluator and a PNG needs to flip.
        /// </summary>
        /// <param name="frameIndex">(T-0166) The RAW, already-wrapped frame this phase corresponds to, used only
        /// to test each layer's <see cref="ShaperLayer.startFrame"/>/<see cref="ShaperLayer.endFrame"/> window.
        /// -1 (the default) means "unknown" — the phase is inverted back to a frame index via
        /// <c>phase01 × (frameCount − 1)</c>, ShaperClock's own mapping run in reverse, which is exact for every
        /// phase this file itself ever produces (it always comes from <see cref="ShaperClock.PhaseOfFrame"/> on
        /// an integer frame) and only approximates for a caller-supplied arbitrary phase — which is also the
        /// case a lifetime window has no exact frame to test against anyway.</param>
        public static Color32[] RenderPhase(ShaperDocument doc, float phase01,
                                            IShaperEffectApplier effects = null, int frameIndex = -1,
                                            ShaperRenderBufferPool pool = null,
                                            ShaperLayerBufferCache layerCache = null)
        {
            int n = SampleCount(doc);
            if (n == 0) return Array.Empty<Color32>();
            var px = new Color32[n];
            RenderPhase(doc, phase01, px, effects, frameIndex, pool, layerCache);
            return px;
        }

        /// <summary>
        /// Allocation-light overload: render at a phase into a caller-owned pixel array of at least
        /// <see cref="SampleCount"/> entries. Still allocates the float destination internally; a caller
        /// scrubbing frames should prefer <see cref="RenderPhaseInto"/> plus one <see cref="Encode"/> so it
        /// owns both buffers.
        ///
        /// <paramref name="effects"/> (T-0156) runs <see cref="ShaperDocument.effects"/> over the finished
        /// picture. It is applied HERE, after <see cref="Encode"/>, and that placement is the whole reason the
        /// parameter is on this overload rather than on <see cref="RenderPhaseInto"/>: every effect in the
        /// catalog is a pixel kernel over 8-bit colour, while the float destination is premultiplied linear.
        /// Running them before the encode would mean inventing a float form of every kernel; running them
        /// after means they see exactly the picture a viewer sees. Passing null is a first-class case — the
        /// render is then bit-identical to one from before effects existed.
        /// </summary>
        public static void RenderPhase(ShaperDocument doc, float phase01, Color32[] outPixels,
                                       IShaperEffectApplier effects = null, int frameIndex = -1,
                                       ShaperRenderBufferPool pool = null,
                                       ShaperLayerBufferCache layerCache = null)
        {
            int n = SampleCount(doc);
            if (n == 0 || outPixels == null || outPixels.Length < n) return;
            var acc = new float[n * FloatsPerSample];
            // The SAME applier is handed to the layer walk, so a layer's own PRE-composite list runs on that
            // layer's buffer inside the walk, and the document's POST list runs below on the folded picture.
            // One applier, one render path — which is what keeps the bake and the preview identical (they both
            // arrive here, and neither owns a pixel path of its own).
            RenderPhaseInto(doc, phase01, acc, frameIndex, pool, effects, layerCache);
            Encode(acc, outPixels, n);

            if (effects == null || doc.effects == null || doc.effects.Count == 0) return;
            effects.Apply(doc.effects, ShaperEffectStage.PostComposite, outPixels,
                          Mathf.Max(1, doc.canvasWidth), Mathf.Max(1, doc.canvasHeight), phase01, doc.seed);
        }

        /// <summary>
        /// Encode an accumulated destination to straight-alpha sRGB. A thin, named pass-through to
        /// <see cref="ShaperFillResolver.Encode"/> so a caller that composited its own float buffer does not
        /// have to reach across to the fill resolver to finish, and so there remains exactly one encoder.
        /// </summary>
        public static void Encode(float[] dst, Color32[] outPixels, int sampleCount)
            => ShaperFillResolver.Encode(dst, outPixels, sampleCount);

        /// <summary>
        /// <b>The layer walk.</b> Composites every enabled layer of <paramref name="doc"/> at
        /// <paramref name="phase01"/> into <paramref name="dst"/>, which must hold at least
        /// <see cref="SampleCount"/> × <see cref="FloatsPerSample"/> floats. The buffer is cleared first, so a
        /// caller may reuse one array across frames.
        ///
        /// <b>The document is moved to the phase, not just the layers.</b> LR-1.8 says the rig is sampled on
        /// the DOCUMENT's clock, so <see cref="ShaperDocument.phase01"/> is set for the duration of the compile
        /// and restored in a <c>finally</c>. Driving only the per-layer resolve would light every frame as
        /// though it were whichever frame the document happened to be parked on. Rendering is a READ: the
        /// document is left exactly as it was found, including when a layer throws.
        ///
        /// <b>Disabled layers are skipped entirely</b>, not painted and discarded — a disabled layer must not
        /// contribute shadowing either. Skipping is safe because <see cref="ShaperLightCompiler.BindLayer"/> is
        /// index-addressed (the layer's own index is passed explicitly), so omitting one cannot shift another
        /// layer's binding.
        ///
        /// <b>Cross-layer masking (T-0170).</b> A layer naming another in its <see cref="ShaperLayer.mask"/>
        /// has that source resolved separately (<see cref="BuildMaskField"/>) and its own buffer cut by the
        /// result before it composites; a layer with <see cref="ShaperLayer.contributesToPicture"/> off is
        /// skipped here entirely and exists only to be read as such a source. An unset mask costs nothing —
        /// no extra resolve, no extra buffer — so an unmasked document is bit-identical.
        ///
        /// <b>Per-layer PRE-composite effects (T-0163), and what they cost.</b> When
        /// <paramref name="effects"/> is supplied and a layer's <see cref="ShaperLayer.effects"/> holds an
        /// enabled entry, that layer's own picture is encoded to straight-alpha sRGB
        /// <see cref="Color32"/>, run through the effect list, and decoded back to premultiplied linear before
        /// it composites — because every catalogued effect is a SpriteFx kernel over 8-bit colour and there is
        /// no float form of any of them. The round trip is lossy in exactly one way worth naming: a
        /// premultiplied colour ABOVE its own alpha (an additive glow over transparency, which
        /// <c>ShaperFillResolver.Encode</c>'s own comment names as the thing 8-bit cannot carry) is clamped by
        /// the encode and does not come back. A layer with no enabled effect never enters that path — it takes
        /// the float composite untouched, so every existing document is bit-identical.
        ///
        /// <b>Layers are composited by DEPTH, not by list order alone (T-0171).</b> A layer's surface Z at a
        /// sample is <c>LayerBase(i) + height(i)</c> — HS-7.2's base plane
        /// (<c>ShaperHeightCompiler.cs:273-280</c>) plus that layer's own accumulated height field
        /// (<c>ShaperFillResolver.cs:243-244</c>, seeded with <c>height_shape</c> at <c>:1001</c> and summed
        /// with every fill's <c>heightDelta</c> at <c>:1238</c>/<c>:1371</c>) — and the walk carries a running
        /// depth buffer so a layer whose surface sits BEHIND what is already painted goes under it instead of
        /// over it. Two domes with different <see cref="ShaperLayer.zOffset"/>s therefore INTERSECT along a
        /// curve instead of one hiding the other. See <see cref="CompositeDepth"/> for the rule, the boundary
        /// blend, and why a document that never contradicts its own list order is untouched.
        /// </summary>
        /// <param name="layerCache">T-0194 — the per-layer resolved-buffer cache. When supplied, a layer whose
        /// CONTENT key (<see cref="ShaperLayerKey.PaintKey"/>) is already resident skips Resolve, BindLayer,
        /// PaintTile, its mask and its own effects entirely and composites the stored buffers instead. That is
        /// what makes hiding a layer, nudging a Z offset or recolouring the background a re-COMPOSITE rather
        /// than a full re-resolve: none of those changes any other layer's key. Passing null keeps every
        /// existing caller's behaviour byte-for-byte, and a hit is byte-identical to a miss by construction —
        /// the stored arrays are the very ones the miss path would have produced.</param>
        public static void RenderPhaseInto(ShaperDocument doc, float phase01, float[] dst, int frameIndex = -1,
                                           ShaperRenderBufferPool pool = null,
                                           IShaperEffectApplier effects = null,
                                           ShaperLayerBufferCache layerCache = null)
        {
            int n = SampleCount(doc);
            if (n == 0 || dst == null || dst.Length < n * FloatsPerSample) return;
            Array.Clear(dst, 0, n * FloatsPerSample);

            // T-0166 — the document background, composited FIRST so every layer paints over it. Uniform across
            // every sample, so writing it directly is exactly what CompositeOver would do against a
            // freshly-cleared (all-zero) destination — no need to build a whole-canvas source buffer for it.
            if (doc.background.a > 0f)
            {
                ShaperSrgb.Decode(doc.background, out float br, out float bg, out float bb);
                float ba = doc.background.a;
                float pr = br * ba, pg = bg * ba, pb = bb * ba;
                for (int i = 0; i < n; i++)
                {
                    int k = i * FloatsPerSample;
                    dst[k + 0] = pr; dst[k + 1] = pg; dst[k + 2] = pb; dst[k + 3] = ba;
                }
            }

            if (doc.layers == null || doc.layers.Count == 0) return;

            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);

            // T-0166 — the frame this phase corresponds to, for each layer's lifetime window test below. See
            // this method's own public overload doc for why an unknown (-1) index is inverted back from phase.
            int fc = Mathf.Max(1, doc.frameCount);
            int fi = frameIndex >= 0 ? frameIndex : Mathf.RoundToInt(phase01 * Mathf.Max(0, fc - 1));

            float savedPhase = doc.phase01;
            try
            {
                doc.phase01 = phase01;
                var grid = doc.Grid();
                // T-0194 — the light program is compiled LAZILY: a frame every one of whose layers hits the
                // buffer cache needs no light scene at all, and compiling one anyway would put a document-wide
                // cost back on the composite-only path this cache exists to make cheap.
                ShaperLightProgram prog = null;
                float halfW = 0.5f * (w - 1) * doc.pixelSize;
                float halfH = 0.5f * (h - 1) * doc.pixelSize;

                // The 8-bit scratch for the per-layer effect stage below, allocated LAZILY on the first layer
                // that actually needs it and then shared by every later one — a document with no per-layer
                // effects allocates neither buffer, which is what keeps its render cost unchanged.
                Color32[] layerPx = null;
                float[] layerAcc = null;

                // T-0170 — the mask scratch, allocated only for a document that actually masks something and
                // then shared by every masked layer in the pass. It is deliberately NOT rented from `pool`:
                // the pool is keyed by LAYER INDEX, and a mask source is very often a layer that also renders
                // itself, so renting its slot here would hand the same arrays to two live uses in one pass.
                ShaperFillBuffers maskBuf = null;
                float[] maskField = null;

                // T-0171 — the cross-layer depth buffer. `accZ` is the Z of the surface currently showing at
                // each sample and `accZw` is how much of that sample is actually covered by it, which is what
                // lets a partly-transparent edge hand its depth over gradually instead of asserting a hard
                // plane. They are separate from `dst`'s alpha on purpose: the document background fills alpha
                // everywhere while occupying no depth at all, and reusing alpha as the depth weight would put
                // every layer with a negative Z behind the backdrop.
                var accZ = new float[n];
                var accZw = new float[n];

                // T-0194 — re-key the document's layers on EVERY render, never across one. A memo carried into
                // a later render can only be wrong in the one direction that matters: a layer edited since it
                // was built keys as unchanged and composites a stale buffer. Measured cost of the rebuild is
                // 0.22 ms for four layers against a resolve of ~20 ms per layer, so this is not a trade-off.
                layerCache?.Keys.Rebuild(doc);

                for (int li = 0; li < doc.layers.Count; li++)
                {
                    var lay = doc.layers[li];
                    if (lay == null || !lay.enabled || lay.root == null) continue;

                    // T-0170 — a layer that does not contribute to the picture is resolved only when some
                    // other layer asks for it as a mask, so it is skipped here exactly as a disabled one is.
                    if (!lay.contributesToPicture) continue;

                    // T-0166 — the layer's lifetime window. endFrame == -1 means "the document's last frame",
                    // matching Pyre's own convention, so a window never needs updating when frameCount changes.
                    int end = lay.endFrame < 0 ? fc - 1 : lay.endFrame;
                    if (fi < lay.startFrame || fi > end) continue;

                    // T-0191 — a composite's bake box is DERIVED, not authored. The owner's report: on
                    // Pyre > Disc the Half extent and Bake dials "scale the disc and make no sense to a human
                    // next to Pyre's Size", because they are two ways of saying the same thing and only one of
                    // them is the generator's own. So the box is fitted to the canvas here, at the one place
                    // that knows the canvas, for every composite in the tree. The window no longer shows them.
                    // T-0198 — pixelSize goes in too: the box is in CANVAS UNITS and the canvas is
                    // (w−1)·pixelSize wide, so fitting it in samples alone shrank every composite by 1/pixelSize.
                    ShaperCompositeDef.FitTree(lay.root, w, h, doc.pixelSize);

                    // T-0171 — this layer's base plane, the same HS-7.2 value BindLayer below compiles its
                    // height stage against (ShaperLightCompiler.cs:492), read from the one function that
                    // computes it so the composite cannot drift from the shading. Hoisted above the resolve
                    // (T-0194) because it is also part of the layer's cache key: the height and light stages
                    // both compile against it, so two different base planes are two different pictures.
                    float baseZ = ShaperHeightCompiler.LayerBase(doc, li, phase01, doc.seed);

                    // T-0194 — the cache probe. The key is taken AFTER FitTree, because FitTree writes the
                    // derived bake box into the composite nodes it walks: keying the unfitted tree would key
                    // a state the document only ever holds for the first render after a load.
                    ShaperCacheKey layerKey = default;
                    if (layerCache != null)
                    {
                        layerKey = layerCache.Keys.PaintKey(doc, li, phase01, baseZ, fi, effects != null);
                        if (layerCache.TryGet(layerKey, n, out var cachedDst, out var cachedHeight))
                        {
                            CompositeDepth(dst, accZ, accZw, cachedDst, cachedHeight, baseZ, n);
                            continue;
                        }
                    }

                    prog ??= ShaperLightCompiler.CompileDocument(doc);

                    var fdoc = ShaperFillResolver.Resolve(lay.root, phase01, doc.seed, halfW, halfH,
                                                          ShaperQuantitySet.ShippedShapeEngine);
                    // T-0165 (T-0146 T21): reuse this layer's own ShaperFillBuffers across calls when a pool
                    // is supplied and the capacity is unchanged from last time -- see ShaperRenderBufferPool's
                    // header for why an exact-match-only policy was chosen. `pool == null` keeps every
                    // existing caller's behaviour byte-for-byte (a fresh buffer every call, as before).
                    var buf = pool != null
                        ? pool.Rent(li, n, Mathf.Max(1, fdoc.owners.Count))
                        : new ShaperFillBuffers(n, Mathf.Max(1, fdoc.owners.Count));
                    var scene = ShaperLightCompiler.BindLayer(doc, li, prog, buf.sampleCapacity,
                                                              buf.ownerCapacity, RootProgram(fdoc), doc.pixelSize);
                    BindSolids(fdoc, scene, phase01, doc.seed, prog);
                    ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, w, h, buf,
                                                 new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                                 scene);

                    // T-0170 — the cross-layer mask, applied to this layer's own finished buffer and BEFORE
                    // its pre-composite effects, so a blur or a glow may bleed past the cut the way an author
                    // expects an effect on a masked layer to behave.
                    if (lay.mask != null && lay.mask.IsSet
                        && BuildMaskField(doc, lay, li, prog, grid, phase01, w, h, n, halfW, halfH, fi, fc,
                                          ref maskBuf, ref maskField))
                        ApplyMask(buf.dst, maskField, n, lay.mask.mode);

                    // T-0163 — the layer's own PRE-composite list, on the layer's own picture, before the fold.
                    // The effect stage rewrites the layer's COLOUR, never its height, so the depth it composites
                    // at is still the layer's own surface.
                    float[] contribution = buf.dst;
                    if (effects != null && HasEnabled(lay.effects))
                    {
                        if (layerPx == null || layerPx.Length < n) layerPx = new Color32[n];
                        if (layerAcc == null || layerAcc.Length < n * FloatsPerSample)
                            layerAcc = new float[n * FloatsPerSample];
                        Encode(buf.dst, layerPx, n);
                        effects.Apply(lay.effects, ShaperEffectStage.PreComposite, layerPx, w, h, phase01, doc.seed);
                        DecodeToPremultiplied(layerPx, layerAcc, n);
                        contribution = layerAcc;
                    }

                    // T-0194 — store AFTER the mask and the layer's own effects, because that pair is what the
                    // composite actually reads; caching the raw paint would re-run both on every later frame.
                    layerCache?.Store(layerKey, contribution, buf.height, n);

                    CompositeDepth(dst, accZ, accZw, contribution, buf.height, baseZ, n);
                }
            }
            finally { doc.phase01 = savedPhase; }
        }

        /// <summary>
        /// The layer root's compiled shape program — the owner with no binding ancestor
        /// (<c>ShaperFillResolver.cs:16-17</c>), which is the node HS-1.1 defines the layer's solid from.
        ///
        /// <b>Why the layer walk has to hand this to <see cref="ShaperLightCompiler.BindLayer"/> (T-0171).</b>
        /// <c>BindLayer</c> takes the program and the sample spacing as OPTIONAL arguments and falls back to
        /// <c>span = pixelSize</c> and an identity local frame when they are absent
        /// (<c>ShaperLightCompiler.cs:468-475</c>, <c>ShaperHeightCompiler.cs:96-105</c>). The renderer passed
        /// neither, so HS-1.2's span was one canvas pixel for every layer in every document: <c>t</c> saturates
        /// a single pixel in from the silhouette, so EVERY extrusion profile flattened to a full-depth plateau
        /// and <c>Linear</c> — the one technique that reads the node-local frame (HS-2.3) — read
        /// <c>nx = ny = 0</c> and became a constant slab. Measured on a Dome of depth 26 over a 34-pixel disc:
        /// the height field was 26 everywhere inside and 0 outside.
        ///
        /// It surfaced here because a depth composite makes the height field VISIBLE for the first time — a
        /// plateau can only re-order whole layers, never let two surfaces cross — but the flattening was never
        /// specific to depth: it was already wrong for lighting, whose <c>pointZ</c> reads the same sheet.
        /// </summary>
        static ShaperProgram RootProgram(ShaperFillDocument fdoc)
        {
            if (fdoc == null || fdoc.owners == null) return null;
            for (int o = 0; o < fdoc.owners.Count; o++)
                if (fdoc.owners[o] != null && fdoc.owners[o].ancestorOwner < 0) return fdoc.owners[o].shape;
            return null;
        }

        /// <summary>True when <paramref name="list"/> holds at least one entry that would actually run — the
        /// test that decides whether a layer pays for the 8-bit round trip at all.</summary>
        static bool HasEnabled(System.Collections.Generic.List<ShaperEffectRef> list)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].enabled) return true;
            return false;
        }

        /// <summary>
        /// <b>T-0170 — resolve the mask field a layer's <see cref="ShaperLayer.mask"/> names.</b> Returns false
        /// when there is nothing to mask WITH, in which case the layer renders unmasked; every one of those
        /// cases is an ordinary authoring state rather than an error, so none of them throws or logs:
        ///
        /// <list type="bullet">
        /// <item>the named layer has been deleted (<see cref="ShaperDocument.LayerIndexById"/> gives -1) — the
        /// reference is KEPT so Undo can restore both halves, and the window shows the missing state;</item>
        /// <item>the source is the masked layer itself, which would be a mask of a shape by itself;</item>
        /// <item>the source is disabled, has no shape, or is outside its own lifetime window — a layer that is
        /// off is off, including as a mask source.</item>
        /// </list>
        ///
        /// <b>The source is resolved UNMASKED</b> (its own <see cref="ShaperLayer.mask"/> is not applied
        /// here), which is what makes cycles unrepresentable rather than merely unlikely: a mask is one level
        /// deep by construction, so no depth guard, no visited-set and no recursion exists to get wrong.
        /// <see cref="ShaperLayerMask.SourceIsReadUnmasked"/> is the sentence the UI shows for it.
        ///
        /// It resolves and paints the source through exactly the same three calls the layer walk above uses —
        /// Resolve → BindLayer (+ <see cref="BindSolids"/>) → PaintTile, at the source's OWN layer index so its
        /// height base and lighting are the ones it would have had — because a mask that disagreed with the
        /// picture about where the source's shape is would be worse than no mask at all.
        /// </summary>
        static bool BuildMaskField(ShaperDocument doc, ShaperLayer target, int targetIndex,
                                   ShaperLightProgram prog, in ShaperSampleGrid grid, float phase01,
                                   int w, int h, int n, float halfW, float halfH, int fi, int fc,
                                   ref ShaperFillBuffers scratch, ref float[] field)
        {
            var m = target.mask;
            int si = doc.LayerIndexById(m.sourceLayerId);
            if (si < 0 || si == targetIndex) return false;

            var src = doc.layers[si];
            if (src == null || !src.enabled || src.root == null) return false;

            int srcEnd = src.endFrame < 0 ? fc - 1 : src.endFrame;
            if (fi < src.startFrame || fi > srcEnd) return false;

            var sdoc = ShaperFillResolver.Resolve(src.root, phase01, doc.seed, halfW, halfH,
                                                  ShaperQuantitySet.ShippedShapeEngine);
            int owners = Mathf.Max(1, sdoc.owners.Count);
            if (scratch == null || scratch.sampleCapacity < n || scratch.ownerCapacity < owners)
                scratch = new ShaperFillBuffers(n, owners);

            var sscene = ShaperLightCompiler.BindLayer(doc, si, prog, scratch.sampleCapacity,
                                                       scratch.ownerCapacity, RootProgram(sdoc), doc.pixelSize);
            BindSolids(sdoc, sscene, phase01, doc.seed, prog);
            ShaperFillResolver.PaintTile(sdoc, grid, 0, 0, w, h, scratch,
                                         new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                         sscene);

            if (field == null || field.Length < n) field = new float[n];

            // HS-1.4: a layer with no height stage publishes ShippedShapeEngine and has no Height sheet, so a
            // mask asking for one falls back to coverage rather than reading an all-zero slab and cutting the
            // whole layer away. ShaperLayerMask.QuantityNotPublished is the sentence the UI shows for it.
            var q = m.quantity;
            if (q == ShaperMaskQuantity.Height && src.height == null) q = ShaperMaskQuantity.Coverage;

            float fullAt = ShaperValue.Sample(m.fullAt, phase01, doc.seed, 1f);

            // Owner 0 is the source layer's ROOT: ShaperFillDocument.owners is paint order and "a node appears
            // before its own members" (ShaperFillResolver.cs:123), so slab 0 is the whole layer's silhouette,
            // which is what "mask by that layer" means.
            for (int i = 0; i < n; i++)
            {
                float raw;
                switch (q)
                {
                    case ShaperMaskQuantity.Height: raw = scratch.ownHeight[i]; break;
                    // Negated: the engine's edgeDistance is a SIGNED distance, negative inside the shape, and
                    // a mask ramps INWARD from the source's edge.
                    case ShaperMaskQuantity.EdgeDistance: raw = -scratch.ownDistance[i]; break;
                    case ShaperMaskQuantity.Luma:
                        {
                            int k = i * FloatsPerSample;
                            // Rec.709 luminance of the PREMULTIPLIED linear sample, so a transparent pixel is
                            // dark by construction and a bright opaque one is light — which is what "mask by
                            // how bright that layer is" means to someone looking at the picture.
                            raw = 0.2126f * scratch.dst[k + 0] + 0.7152f * scratch.dst[k + 1]
                                  + 0.0722f * scratch.dst[k + 2];
                            break;
                        }
                    default: raw = scratch.ownCoverage[i]; break;
                }
                field[i] = ShaperMaskOps.MaskValue(raw, q, fullAt, m.invert);
            }
            return true;
        }

        /// <summary>
        /// T-0170 — scale a layer's premultiplied buffer by its mask. All four floats take the SAME factor,
        /// which is what keeps the buffer premultiplied: it cuts the sample's alpha and leaves the colour it
        /// would un-premultiply to untouched, so a masked edge fades out rather than fading to black.
        ///
        /// <c>ShaperFillBuffers.height</c> is deliberately not scaled, and T-0171's depth composite is the
        /// reason that is still right rather than merely harmless: the mask cuts the sample's ALPHA to zero,
        /// and <see cref="CompositeDepth"/> weights a layer's claim on the depth buffer by exactly that alpha,
        /// so a masked-away region contributes no depth however tall its height field says it is. Scaling the
        /// height instead would move the masked surface DOWNWARD in Z, which is a different picture — a shape
        /// sinking into the one behind it rather than being cut out of it.
        /// </summary>
        static void ApplyMask(float[] dst, float[] field, int sampleCount, ShaperMaskMode mode)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                int k = i * FloatsPerSample;
                float f = ShaperMaskOps.Factor(mode, dst[k + 3], field[i]);
                if (f >= 1f) continue;
                if (f <= 0f)
                {
                    dst[k + 0] = 0f; dst[k + 1] = 0f; dst[k + 2] = 0f; dst[k + 3] = 0f;
                    continue;
                }
                dst[k + 0] *= f; dst[k + 1] *= f; dst[k + 2] *= f; dst[k + 3] *= f;
            }
        }

        /// <summary>
        /// The exact inverse of <see cref="Encode"/>: straight-alpha sRGB <see cref="Color32"/> back to the
        /// premultiplied LINEAR destination the layer composite reads.
        ///
        /// Written as the mirror of <c>ShaperFillResolver.Encode</c> (<c>:1516-1529</c>) rather than as a new
        /// colour rule — it decodes each channel through <see cref="ShaperSrgb.DecodeChannel"/>, the same
        /// transfer function Encode's <c>EncodeToByte</c> inverts, and re-premultiplies by alpha, which is the
        /// un-premultiply Encode performs run backwards. Round-tripping an untouched buffer therefore returns
        /// the original values to within 8-bit quantisation, and the ONE thing it cannot return is a colour
        /// that was above 1 after un-premultiplying (an additive glow) — clamped by the encode, gone by here.
        /// </summary>
        static void DecodeToPremultiplied(Color32[] src, float[] dst, int sampleCount)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                var c = src[i];
                float a = c.a / 255f;
                int k = i * FloatsPerSample;
                dst[k + 0] = ShaperSrgb.DecodeChannel(c.r / 255f) * a;
                dst[k + 1] = ShaperSrgb.DecodeChannel(c.g / 255f) * a;
                dst[k + 2] = ShaperSrgb.DecodeChannel(c.b / 255f) * a;
                dst[k + 3] = a;
            }
        }

        /// <summary>
        /// T-0155 — bind every <see cref="ShaperNodeKind.Solid"/> owner's compiled generator onto the layer's
        /// light scene, which is the step that made Solids reachable at all.
        ///
        /// The seam already existed and nothing in the shipped runtime ever used it:
        /// <see cref="ShaperFillResolver.PaintTile"/> reads <c>scene.solid[owner]</c> and, when it is non-null,
        /// hands that owner's slab to the Solids generator instead of the shape stage (LR-6.1). But the ONLY
        /// caller of <see cref="ShaperLightScene.SetSolid"/> was an editor audit, so in a real document the
        /// array was always null and a Solids node could not exist to fill it. This connects the two using
        /// <see cref="ShaperFillOwner.node"/>, rather than inventing a second path into the fill resolver.
        ///
        /// <b>Existing documents are untouched by construction.</b> The loop only ever calls
        /// <c>SetSolid</c> for a node whose <c>kind</c> is <c>Solid</c>, and <c>Solid</c> is a value no
        /// previously-serialized node can hold — the enum is append-only and <c>kind</c> defaults to
        /// <c>Primitive</c>. Every other document leaves <c>scene.solid</c> exactly as
        /// <see cref="ShaperLightCompiler.BindLayer"/> left it, so <c>PaintTile</c>'s null check takes the same
        /// branch it always did and the render is bit-identical.
        ///
        /// <paramref name="prog"/> is forwarded so the generator can raise LR-7.3's inert-dial diagnostic at
        /// compile time — without it the whole <see cref="ShaperSolids.InertReason"/> declaration table would be
        /// code nothing ever runs.
        /// </summary>
        static void BindSolids(ShaperFillDocument fdoc, ShaperLightScene scene, float phase01, uint seed,
                               ShaperLightProgram prog)
        {
            if (fdoc == null || scene == null) return;
            for (int o = 0; o < fdoc.owners.Count && o < scene.ownerCapacity; o++)
            {
                var node = fdoc.owners[o]?.node;
                if (node == null || node.kind != ShaperNodeKind.Solid || node.solid == null) continue;
                scene.SetSolid(o, ShaperSolids.Compile(node.solid, phase01, seed, prog));
            }
        }

        /// <summary>Render one frame index into a caller-owned float destination. See <see cref="RenderPhaseInto"/>.</summary>
        public static void RenderFrameInto(ShaperDocument doc, int frameIndex, float[] dst,
                                           ShaperRenderBufferPool pool = null,
                                           ShaperLayerBufferCache layerCache = null)
        {
            if (doc == null) return;
            int wrapped = ShaperClock.WrapFrame(frameIndex, Mathf.Max(1, doc.frameCount));
            RenderPhaseInto(doc, doc.PhaseOfFrame(wrapped), dst, wrapped, pool, null, layerCache);
        }

        /// <summary>
        /// Premultiplied source-over: <paramref name="src"/> laid on top of everything already in
        /// <paramref name="acc"/>.
        ///
        /// Done in the float destination, which is linear and PREMULTIPLIED
        /// (<c>ShaperFillResolver.cs:241-242</c>), with the encode to straight-alpha sRGB happening ONCE after
        /// every layer has landed. Compositing encoded bytes instead would mean un-premultiplying and
        /// re-encoding per layer, losing an additive glow's colour at every step — the exact failure
        /// <c>Encode</c>'s own comment describes, which is also why it names the float destination as where a
        /// further composite belongs (<c>:1507-1509</c>).
        ///
        /// <b>This is the unconditional composite — the layer walk uses <see cref="CompositeDepth"/> instead
        /// (T-0171).</b> It remains the primitive that one names when the question is "what does laying this
        /// on top mean", and it is what the depth composite degenerates to for the first layer, for a tie, and
        /// for anything in front.
        /// </summary>
        public static void CompositeOver(float[] acc, float[] src, int sampleCount)
        {
            if (acc == null || src == null) return;
            for (int i = 0; i < sampleCount; i++)
            {
                int k = i * FloatsPerSample;
                float sa = src[k + 3];
                float inv = 1f - sa;
                acc[k + 0] = src[k + 0] + acc[k + 0] * inv;
                acc[k + 1] = src[k + 1] + acc[k + 1] * inv;
                acc[k + 2] = src[k + 2] + acc[k + 2] * inv;
                acc[k + 3] = sa + acc[k + 3] * inv;
            }
        }

        /// <summary>
        /// <b>T-0171 — the depth-resolved layer composite.</b> Lays <paramref name="src"/> into
        /// <paramref name="acc"/> at the surface Z <c>baseZ + srcHeight[i]</c>, choosing per sample whether it
        /// goes OVER or UNDER what is already there, and carries the running depth in
        /// <paramref name="accZ"/>/<paramref name="accZw"/>.
        ///
        /// <b>The rule, and why it is this one.</b> Both orderings are exact premultiplied composites of the
        /// same two samples — <c>over = src + acc·(1−src.a)</c> and <c>under = acc + src·(1−acc.a)</c> — and
        /// they produce IDENTICAL alpha (<c>a+b−ab</c> either way). That is what makes blending between them
        /// legitimate rather than a fudge: the mix parameter moves colour only, so a soft intersection can
        /// never punch a hole in, or double up, the coverage. The mix is
        /// <list type="number">
        /// <item><b>Ties and anything in front go OVER, exactly.</b> <c>srcZ ≥ accZ</c> gives <c>t = 1</c>,
        /// which is <see cref="CompositeOver"/> byte for byte. A document whose layers never contradict their
        /// own list order — every default one, since <c>base(i) = i × layerSpacing</c> rises with the index,
        /// and equally one with <c>layerSpacing = 0</c> where every base ties — therefore renders exactly as
        /// it did before this existed. Ordering ties by list order is not a convenience: list order IS the
        /// document's authored answer to "which of these is on top" and there is no better one to invent.</item>
        /// <item><b>Behind by more than <see cref="DepthBlendBand"/> goes UNDER, exactly.</b></item>
        /// <item><b>In between, a smoothstep across that band.</b> Continuous at both ends (the ramp reaches 1
        /// at the crossing), so an intersection curve is a soft seam a pixel wide rather than the stair a hard
        /// z-test leaves along it, and no sample flips between two orderings frame to frame.</item>
        /// </list>
        ///
        /// <b>Coverage-weighting, which is the other half of "no hard seam".</b> A depth read from a sample the
        /// accumulator barely covers is barely a surface — a shape's antialiased rim covers a tenth of its edge
        /// pixels, and letting that tenth assert a full occluding plane is what makes naive z-testing bite
        /// visibly along every silhouette. So the test's outcome is itself lerped toward "in front" by
        /// <paramref name="accZw"/>, the coverage the standing depth was written with, and a layer writes its
        /// own depth in weighted by its own alpha. An empty sample (<c>accZw = 0</c>) hands the incoming layer
        /// the depth outright.
        /// </summary>
        /// <param name="srcHeight">The layer's accumulated height field, one float per sample, measured above
        /// its own base plane (<c>ShaperFillResolver.cs:243-244</c>). Null means a flat layer at
        /// <paramref name="baseZ"/>.</param>
        /// <param name="baseZ">HS-7.2's <c>i × layerSpacing + zOffset(i)</c> for this layer, from
        /// <see cref="ShaperHeightCompiler.LayerBase"/> — the same value its height and lighting compiled
        /// against.</param>
        public static void CompositeDepth(float[] acc, float[] accZ, float[] accZw,
                                          float[] src, float[] srcHeight, float baseZ, int sampleCount)
        {
            if (acc == null || src == null) return;
            if (accZ == null || accZw == null) { CompositeOver(acc, src, sampleCount); return; }

            for (int i = 0; i < sampleCount; i++)
            {
                int k = i * FloatsPerSample;
                float sa = src[k + 3];
                float sz = baseZ + (srcHeight != null ? srcHeight[i] : 0f);

                float zw = accZw[i];
                float t = 1f;
                if (zw > 0f)
                {
                    float d = sz - accZ[i];
                    float raw = 1f;
                    if (d < 0f)
                    {
                        float x = 1f + d / DepthBlendBand;          // 1 at the crossing, 0 a full band behind
                        raw = x <= 0f ? 0f : x * x * (3f - 2f * x);
                    }
                    t = 1f + (raw - 1f) * zw;                        // lerp(1, raw, zw)
                }

                float aa = acc[k + 3];
                float invS = 1f - sa, invA = 1f - aa;
                for (int c = 0; c < 3; c++)
                {
                    float over = src[k + c] + acc[k + c] * invS;
                    float under = acc[k + c] + src[k + c] * invA;
                    acc[k + c] = under + (over - under) * t;
                }
                acc[k + 3] = sa + aa * invS;

                if (sa <= 0f) continue;
                float front = zw <= 0f || sz > accZ[i] ? sz : accZ[i];
                accZ[i] = zw <= 0f ? sz : accZ[i] + (front - accZ[i]) * sa;
                accZw[i] = zw + sa * (1f - zw);
            }
        }
    }
}

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
                                            ShaperRenderBufferPool pool = null)
        {
            if (doc == null) return Array.Empty<Color32>();
            int wrapped = ShaperClock.WrapFrame(frameIndex, Mathf.Max(1, doc.frameCount));
            return RenderPhase(doc, doc.PhaseOfFrame(wrapped), effects, wrapped, pool);
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
                                            ShaperRenderBufferPool pool = null)
        {
            int n = SampleCount(doc);
            if (n == 0) return Array.Empty<Color32>();
            var px = new Color32[n];
            RenderPhase(doc, phase01, px, effects, frameIndex, pool);
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
                                       ShaperRenderBufferPool pool = null)
        {
            int n = SampleCount(doc);
            if (n == 0 || outPixels == null || outPixels.Length < n) return;
            var acc = new float[n * FloatsPerSample];
            // The SAME applier is handed to the layer walk, so a layer's own PRE-composite list runs on that
            // layer's buffer inside the walk, and the document's POST list runs below on the folded picture.
            // One applier, one render path — which is what keeps the bake and the preview identical (they both
            // arrive here, and neither owns a pixel path of its own).
            RenderPhaseInto(doc, phase01, acc, frameIndex, pool, effects);
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
        /// </summary>
        public static void RenderPhaseInto(ShaperDocument doc, float phase01, float[] dst, int frameIndex = -1,
                                           ShaperRenderBufferPool pool = null,
                                           IShaperEffectApplier effects = null)
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
                var prog = ShaperLightCompiler.CompileDocument(doc);
                float halfW = 0.5f * (w - 1) * doc.pixelSize;
                float halfH = 0.5f * (h - 1) * doc.pixelSize;

                // The 8-bit scratch for the per-layer effect stage below, allocated LAZILY on the first layer
                // that actually needs it and then shared by every later one — a document with no per-layer
                // effects allocates neither buffer, which is what keeps its render cost unchanged.
                Color32[] layerPx = null;
                float[] layerAcc = null;

                for (int li = 0; li < doc.layers.Count; li++)
                {
                    var lay = doc.layers[li];
                    if (lay == null || !lay.enabled || lay.root == null) continue;

                    // T-0166 — the layer's lifetime window. endFrame == -1 means "the document's last frame",
                    // matching Pyre's own convention, so a window never needs updating when frameCount changes.
                    int end = lay.endFrame < 0 ? fc - 1 : lay.endFrame;
                    if (fi < lay.startFrame || fi > end) continue;

                    var fdoc = ShaperFillResolver.Resolve(lay.root, phase01, doc.seed, halfW, halfH,
                                                          ShaperQuantitySet.ShippedShapeEngine);
                    // T-0165 (T-0146 T21): reuse this layer's own ShaperFillBuffers across calls when a pool
                    // is supplied and the capacity is unchanged from last time -- see ShaperRenderBufferPool's
                    // header for why an exact-match-only policy was chosen. `pool == null` keeps every
                    // existing caller's behaviour byte-for-byte (a fresh buffer every call, as before).
                    var buf = pool != null
                        ? pool.Rent(li, n, Mathf.Max(1, fdoc.owners.Count))
                        : new ShaperFillBuffers(n, Mathf.Max(1, fdoc.owners.Count));
                    var scene = ShaperLightCompiler.BindLayer(doc, li, prog, buf.sampleCapacity, buf.ownerCapacity);
                    BindSolids(fdoc, scene, phase01, doc.seed, prog);
                    ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, w, h, buf,
                                                 new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                                 scene);

                    // T-0163 — the layer's own PRE-composite list, on the layer's own picture, before the fold.
                    if (effects != null && HasEnabled(lay.effects))
                    {
                        if (layerPx == null || layerPx.Length < n) layerPx = new Color32[n];
                        if (layerAcc == null || layerAcc.Length < n * FloatsPerSample)
                            layerAcc = new float[n * FloatsPerSample];
                        Encode(buf.dst, layerPx, n);
                        effects.Apply(lay.effects, ShaperEffectStage.PreComposite, layerPx, w, h, phase01, doc.seed);
                        DecodeToPremultiplied(layerPx, layerAcc, n);
                        CompositeOver(dst, layerAcc, n);
                        continue;
                    }

                    CompositeOver(dst, buf.dst, n);
                }
            }
            finally { doc.phase01 = savedPhase; }
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
                                           ShaperRenderBufferPool pool = null)
        {
            if (doc == null) return;
            int wrapped = ShaperClock.WrapFrame(frameIndex, Mathf.Max(1, doc.frameCount));
            RenderPhaseInto(doc, doc.PhaseOfFrame(wrapped), dst, wrapped, pool);
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
        /// <b>Stacking is LIST ORDER, not Z order, and that is a finding rather than a shortcut — see the
        /// class-level note in the file header of this method's caller.</b> <see cref="ShaperDocument.layers"/>
        /// is ordered bottom-most first and no stage may reorder it (<c>ShaperResolve.cs:120</c>,
        /// <c>ShaperHeightCompiler.cs:248-249</c>), so walking the list in order and compositing each layer OVER
        /// the accumulator IS the document's authored stacking order by definition.
        ///
        /// What that does NOT do: HS-7.2 gives every layer a base plane
        /// <c>base(i) = i × layerSpacing + zOffset(i)</c> with a SIGNED, deliberately unrestricted
        /// <c>zOffset</c> (<c>ShaperHeightCompiler.cs:247-279</c>). That Z feeds the height and light compile
        /// per layer; nothing in the engine depth-TESTS one layer against another, and no cross-layer depth
        /// buffer exists. So pushing a later layer far back in Z changes its shading and its height, but it
        /// still paints in front of earlier layers. A depth-resolved composite is buildable — every layer's
        /// <c>ShaperFillBuffers.height</c> (<c>:243-244</c>) already carries the accumulated height a z-test
        /// would need — but it does not exist today, was not silently invented here, and is recorded as an open
        /// question rather than decided by a renderer.
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
    }
}

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
        public static Color32[] RenderFrame(ShaperDocument doc, int frameIndex)
            => doc == null ? Array.Empty<Color32>()
             : RenderPhase(doc, doc.PhaseOfFrame(ShaperClock.WrapFrame(frameIndex, Mathf.Max(1, doc.frameCount))));

        /// <summary>
        /// Render at an explicit phase to straight-alpha sRGB pixels. Row 0 of the returned array is the BOTTOM
        /// row, matching both <see cref="ShaperSampleGrid"/>'s +Y-up sampling and <c>Texture2D.SetPixels32</c>,
        /// so nothing between the evaluator and a PNG needs to flip.
        /// </summary>
        public static Color32[] RenderPhase(ShaperDocument doc, float phase01)
        {
            int n = SampleCount(doc);
            if (n == 0) return Array.Empty<Color32>();
            var px = new Color32[n];
            RenderPhase(doc, phase01, px);
            return px;
        }

        /// <summary>
        /// Allocation-light overload: render at a phase into a caller-owned pixel array of at least
        /// <see cref="SampleCount"/> entries. Still allocates the float destination internally; a caller
        /// scrubbing frames should prefer <see cref="RenderPhaseInto"/> plus one <see cref="Encode"/> so it
        /// owns both buffers.
        /// </summary>
        public static void RenderPhase(ShaperDocument doc, float phase01, Color32[] outPixels)
        {
            int n = SampleCount(doc);
            if (n == 0 || outPixels == null || outPixels.Length < n) return;
            var acc = new float[n * FloatsPerSample];
            RenderPhaseInto(doc, phase01, acc);
            Encode(acc, outPixels, n);
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
        /// </summary>
        public static void RenderPhaseInto(ShaperDocument doc, float phase01, float[] dst)
        {
            int n = SampleCount(doc);
            if (n == 0 || dst == null || dst.Length < n * FloatsPerSample) return;
            Array.Clear(dst, 0, n * FloatsPerSample);
            if (doc.layers == null || doc.layers.Count == 0) return;

            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);
            float savedPhase = doc.phase01;
            try
            {
                doc.phase01 = phase01;
                var grid = doc.Grid();
                var prog = ShaperLightCompiler.CompileDocument(doc);
                float halfW = 0.5f * (w - 1) * doc.pixelSize;
                float halfH = 0.5f * (h - 1) * doc.pixelSize;

                for (int li = 0; li < doc.layers.Count; li++)
                {
                    var lay = doc.layers[li];
                    if (lay == null || !lay.enabled || lay.root == null) continue;

                    var fdoc = ShaperFillResolver.Resolve(lay.root, phase01, doc.seed, halfW, halfH,
                                                          ShaperQuantitySet.ShippedShapeEngine);
                    var buf = new ShaperFillBuffers(n, Mathf.Max(1, fdoc.owners.Count));
                    var scene = ShaperLightCompiler.BindLayer(doc, li, prog, buf.sampleCapacity, buf.ownerCapacity);
                    ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, w, h, buf,
                                                 new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                                 scene);
                    CompositeOver(dst, buf.dst, n);
                }
            }
            finally { doc.phase01 = savedPhase; }
        }

        /// <summary>Render one frame index into a caller-owned float destination. See <see cref="RenderPhaseInto"/>.</summary>
        public static void RenderFrameInto(ShaperDocument doc, int frameIndex, float[] dst)
        {
            if (doc == null) return;
            RenderPhaseInto(doc, doc.PhaseOfFrame(ShaperClock.WrapFrame(frameIndex, Mathf.Max(1, doc.frameCount))), dst);
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

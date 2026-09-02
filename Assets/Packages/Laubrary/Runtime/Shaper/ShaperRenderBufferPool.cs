// ShaperRenderBufferPool — an OPT-IN buffer reuse for ShaperDocumentRenderer.RenderPhaseInto.
//
// T-0146 T21 found the gap this closes: RenderPhaseInto `new`s a ShaperFillBuffers PER LAYER, PER CALL
// (ShaperDocumentRenderer.cs, the layer loop), even though ShaperFillBuffers' own class doc states the
// contract explicitly -- "the resolver never allocates, replaces, resizes or frees one of these inside a
// paint call" (ShaperFillResolver.cs:196). A caller that renders the SAME document repeatedly with an
// unchanged layer/owner shape -- exactly what frame-scrubbing and background pre-baking do (T-0165) -- was
// paying that allocation on every single frame for no reason: ShaperFillBuffers.ClearDestination already
// exists specifically so a host can clear and reuse one instead of replacing it.
//
// Scope, deliberately conservative: a pooled slot is reused ONLY when its capacity is an EXACT match for
// what the current call needs (Rent below), never grown-and-reused-larger. Growing a shared buffer and
// letting downstream code run against a bigger scene.ownerCapacity than doc.owners.Count would rely on
// every consumer in ShaperFillResolver.PaintTile correctly bounding its own loops to the SMALLER of the
// two -- true in the spots this file's author could confirm by reading, not confirmed for the whole
// ~1500-line resolver. An exact-match-only pool sidesteps that question entirely: whenever a layer's own
// owner count changes, Rent falls back to a fresh allocation (today's exact behaviour), and only the
// steady-state case -- the same document rendered many times with nothing structural changed, which is the
// overwhelming majority of a scrub/playback/pre-bake session -- gets the reuse win.
//
// Passing a pool is opt-in on every RenderPhaseInto/RenderFrame overload (default null): every existing
// caller (ShaperBaker, the asset browser's thumbnails, RenderDocumentInto) keeps allocating exactly as it
// always has, so this cannot change what they produce. Only a caller that explicitly wants steady-state
// speed (T-0165's preview frame cache) opts in.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// Reuses one <see cref="ShaperFillBuffers"/> per LAYER INDEX across repeated
    /// <see cref="ShaperDocumentRenderer.RenderPhaseInto"/> calls against the same document, when that
    /// layer's own (sampleCapacity, ownerCapacity) is unchanged from the previous call. NOT thread-safe --
    /// this pool is meant to be owned by one caller (the editor preview's frame cache) that only ever
    /// calls into the renderer from the main thread.
    /// </summary>
    public sealed class ShaperRenderBufferPool
    {
        readonly List<ShaperFillBuffers> perLayer = new List<ShaperFillBuffers>();

        /// <summary>Return the pooled buffer for <paramref name="layerIndex"/> if its capacity matches
        /// exactly, otherwise allocate one, store it, and return that -- see this file's header for why an
        /// exact-match-only policy was chosen over grow-and-reuse.</summary>
        public ShaperFillBuffers Rent(int layerIndex, int sampleCapacity, int ownerCapacity)
        {
            sampleCapacity = Mathf.Max(1, sampleCapacity);
            ownerCapacity = Mathf.Max(1, ownerCapacity);

            while (perLayer.Count <= layerIndex) perLayer.Add(null);
            var buf = perLayer[layerIndex];
            if (buf == null || buf.sampleCapacity != sampleCapacity || buf.ownerCapacity != ownerCapacity)
            {
                buf = new ShaperFillBuffers(sampleCapacity, ownerCapacity);
                perLayer[layerIndex] = buf;
            }
            return buf;
        }

        /// <summary>Drop every pooled buffer. Call when the document/canvas identity a caller was pooling
        /// for has changed, so a stale layer-index slot cannot be mistaken for a different document's own
        /// layer with a matching capacity by coincidence.</summary>
        public void Clear() => perLayer.Clear();
    }
}

// ShaperLayerMask — the CONSUMER-SIDE cross-layer mask reference (T-0170, design R5).
//
// R5 dropped Pyre's `MatteRole` — a layer declaring "I am a matte for whoever is next" — on the grounds that
// every node already publishes coverage, coverage IS a mask, and the reference therefore belongs on the layer
// being masked rather than on the layer doing the masking. That half was never built: nothing in
// Runtime/Shaper/ referred to another layer at all, so Shaper had no cross-layer masking of any kind while
// Pyre has three separate matte facilities (write/clip channels, heightmap-from-channel, a six-channel luma
// matte).
//
// Why consumer-side is the better shape, restated so it is not re-litigated: a producer-side role is a
// one-to-one claim on the NEXT layer in list order, so masking two layers with one shape needs two copies of
// that shape, and reordering the list silently re-points the matte. A reference held by the consumer is
// many-to-one, survives reordering, and is visible on the layer whose picture it changes — which is the layer
// an author is looking at when they wonder why it has a hole in it.
using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>APPEND-ONLY: serialized as an int.</summary>
    public enum ShaperMaskMode
    {
        /// <summary>Keep what the source covers: <c>a' = a · m</c>.</summary>
        Clip = 0,
        /// <summary>Cut away what the source covers: <c>a' = a · (1 − m)</c>.</summary>
        Subtract = 1,
        /// <summary>Keep the lesser of the two: <c>a' = min(a, m)</c>.</summary>
        Intersect = 2,
    }

    /// <summary>
    /// Which quantity of the source layer is read as the mask. APPEND-ONLY: serialized as an int.
    ///
    /// The first three are the fill vocabulary's own names (<see cref="ShaperQuantity"/>) and are gated on
    /// what the source actually PUBLISHES — a source with no height stage publishes
    /// <see cref="ShaperQuantitySet.ShippedShapeEngine"/> and has no Height to give (HS-1.4).
    /// <see cref="Luma"/> is not a shape quantity at all: it is the source's finished BRIGHTNESS, which is
    /// what Pyre's own luma matte reads, and it is available from every layer because every layer paints.
    /// </summary>
    public enum ShaperMaskQuantity
    {
        Coverage = 0,
        Height = 1,
        EdgeDistance = 2,
        Luma = 3,
    }

    /// <summary>
    /// One layer's mask: WHICH other layer of the same document cuts it, and how.
    ///
    /// <b>The source is a stable id, never a name.</b> <see cref="ShaperLayer.id"/> exists for exactly this
    /// reference. A name would be typed twice, would break the moment a layer is renamed, and would silently
    /// match the wrong layer as soon as two layers shared a name — which is the failure the project's
    /// "never type a reference string" rule names. A list INDEX would be no better: layer order is authored
    /// data that the user drags around, so an index re-points itself on every reorder.
    ///
    /// Flat and <c>[Serializable]</c> with no <c>SerializeReference</c>, matching
    /// <see cref="ShaperLightResponse"/> and <c>ShaperBorderDef</c> for the reason
    /// <c>ShaperBorderDef.cs:6-11</c> gives.
    /// </summary>
    [Serializable]
    public class ShaperLayerMask
    {
        /// <summary>The masking layer's <see cref="ShaperLayer.id"/>. <b>0 means no mask</b>, which is also
        /// the id no layer is ever assigned (<see cref="ShaperDocument.IdOf"/> allocates from 1).</summary>
        public int sourceLayerId = 0;

        /// <summary>Read the source's quantity backwards: <c>m → 1 − m</c>, before the mode is applied.</summary>
        public bool invert = false;

        public ShaperMaskMode mode = ShaperMaskMode.Clip;

        public ShaperMaskQuantity quantity = ShaperMaskQuantity.Coverage;

        /// <summary>
        /// The source-quantity value that reads as a fully solid mask; everything at or above it is 1 and the
        /// scale between 0 and it is linear.
        ///
        /// ONE meaning, not two: it is the quantity's own unit — canvas pixels for
        /// <see cref="ShaperMaskQuantity.Height"/> and <see cref="ShaperMaskQuantity.EdgeDistance"/> (measured
        /// INWARD from the source's edge), and linear luminance for <see cref="ShaperMaskQuantity.Luma"/>. It
        /// is ignored by <see cref="ShaperMaskQuantity.Coverage"/>, which is already 0..1 and needs no scale;
        /// giving coverage a second amplitude dial would be the "two dials for one quantity" defect
        /// <c>ShaperLight.range</c> refuses by name.
        ///
        /// At 0 the ramp degenerates to a hard test (anything above zero is fully masked), which is the useful
        /// answer for "just the source's silhouette, hard-edged" and is why 0 is allowed rather than clamped.
        /// </summary>
        public ZUIValue fullAt = new ZUIValue(1f);

        /// <summary>True when this mask names a source at all. A named-but-missing source is NOT this
        /// method's business — see <see cref="ShaperDocument.LayerById"/>, which returns null for it.</summary>
        public bool IsSet => sourceLayerId != 0;

        // ── the sentences a UI shows, as const strings (the LR-7.1 pattern) ──────────────────────────────

        /// <summary>Shown on the source picker when the named layer is gone.</summary>
        public const string MissingSource =
            "The layer this mask pointed at no longer exists, so nothing is being masked. Pick another layer " +
            "or clear the mask — the reference is kept rather than silently dropped, so a layer deleted by " +
            "accident can be restored by Undo without also losing every mask that named it.";

        /// <summary>Shown on the Quantity control when the source cannot publish the chosen quantity.</summary>
        public const string QuantityNotPublished =
            "The source layer does not publish this quantity, so the mask falls back to its coverage. Height " +
            "needs the source to have a height stage; Coverage, Edge Distance and Luma are published by every " +
            "layer.";

        /// <summary>Shown on the mask card whenever a source is set. Names the one nesting limit up front.</summary>
        public const string SourceIsReadUnmasked =
            "The source layer is read as it resolves on its own: its OWN mask, if it has one, is not applied " +
            "while it is being used as a mask. Masks therefore never chain, and a document can never build a " +
            "cycle that would not resolve.";
    }

    /// <summary>
    /// The mask arithmetic, in one place so the renderer states it once and a probe can check it without
    /// re-deriving it. Pure functions over already-resolved buffers — nothing here resolves, paints or
    /// allocates.
    /// </summary>
    public static class ShaperMaskOps
    {
        /// <summary>Below this alpha a premultiplied sample carries no recoverable colour ratio; matches
        /// <c>ShaperFillResolver.Encode</c>'s own degenerate-alpha guard.</summary>
        public const float MinAlpha = 1e-6f;

        /// <summary>
        /// One sample of the source quantity, mapped to 0..1 and inverted if asked. <paramref name="raw"/> is
        /// the quantity in its own unit — coverage 0..1, height in canvas pixels, INWARD distance in canvas
        /// pixels (so a positive value is inside the source), linear luminance.
        /// </summary>
        public static float MaskValue(float raw, ShaperMaskQuantity quantity, float fullAt, bool invert)
        {
            float m;
            if (quantity == ShaperMaskQuantity.Coverage) m = Mathf.Clamp01(raw);
            else if (fullAt > 0f) m = Mathf.Clamp01(raw / fullAt);
            else m = raw > 0f ? 1f : 0f;
            return invert ? 1f - m : m;
        }

        /// <summary>
        /// The factor the masked layer's PREMULTIPLIED sample is scaled by. Scaling all four floats by one
        /// factor is what keeps a premultiplied buffer premultiplied — it is the same operation as cutting the
        /// sample's alpha and leaving its un-premultiplied colour alone.
        /// </summary>
        public static float Factor(ShaperMaskMode mode, float alpha, float m)
        {
            switch (mode)
            {
                case ShaperMaskMode.Subtract: return 1f - m;
                case ShaperMaskMode.Intersect: return alpha > MinAlpha ? Mathf.Min(alpha, m) / alpha : 0f;
                default: return m;   // Clip
            }
        }
    }
}

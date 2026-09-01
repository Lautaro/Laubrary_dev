// ShaperEffects — the authored effect list, and the seam that lets something actually run it.
//
// T-0156. T-0114 built the whole universal-effects system — a 41-entry catalog, the sheets/padding
// portability split, and an explicit pre/post-composite stage distinction proven with a real runner. What it
// never built was a way for a DOCUMENT to say "apply this one". No node and no layer held an effect list, so
// ShaperEffectCatalog was static classification and nothing else: finished engine work that no user could
// reach, exactly like Solids before T-0155.
//
// ── Why the list lives on the DOCUMENT, and why there is only one list ──────────────────────────────────
// The stage enum below decides this, and it is worth stating rather than asserting. PostComposite "runs once
// on the FINISHED, folded picture"; PreComposite "runs once per instance, on that instance's OWN buffer,
// before instances fold into one picture". The only fold that exists in the shipped renderer is
// ShaperDocumentRenderer's layer composite, so the finished picture is the DOCUMENT's picture — which makes
// the document the honest owner of a post-composite list.
//
// A per-layer PRE-composite list is deliberately NOT added here, and not because it was forgotten. A layer's
// own buffer inside the renderer is a PREMULTIPLIED LINEAR float destination, and every effect in the catalog
// is a SpriteFx PixelModifier operating on Color32. Running one per layer would mean encoding that layer to
// 8-bit, applying, and decoding back before the float composite — a silent precision regression on every
// layer of every document, introduced to serve a feature nobody asked for yet. That is a real design
// decision about where the picture becomes 8-bit, and it belongs to whoever wants per-layer effects, not to
// the task that made effects authorable at all. Until then there is one list, it runs at one stage, and
// everything a user can author does something — no inert authoring, no on-screen apology for it.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0114 — WHERE in the pipeline an effect runs, relative to the point several pictures fold into one.
    /// This is a real, user-visible property, not an implementation detail: for a single un-folded picture the
    /// two stages produce an IDENTICAL result, but once pictures overlap they do not, because a stage that can
    /// drop a pixel (or any non-linear kernel — posterise, threshold, dither) sees a different input on each
    /// side of the fold.
    ///
    /// MOVED here from <c>Laubrary.PyreShaper</c> by T-0156. It had to move: the authored
    /// <see cref="ShaperEffectRef"/> below lives on <see cref="ShaperDocument"/> in this assembly, and
    /// <c>Laubrary.PyreShaper</c> REFERENCES this assembly — so naming its enum from here would have been a
    /// cycle. The enum itself has no dependencies of its own, so moving it down is free, and the bridge
    /// assembly still sees it through the <c>using Laubrary.Shaper</c> it already had.
    ///
    /// APPEND-ONLY: serialized as an int.
    /// </summary>
    public enum ShaperEffectStage
    {
        /// <summary>Runs on one picture's OWN buffer, before pictures fold into one.</summary>
        PreComposite = 0,

        /// <summary>Runs once on the FINISHED, folded picture — the only stage that can read a pixel's
        /// NEIGHBOURS, since a pre-composite pass only ever sees one picture's own buffer.</summary>
        PostComposite = 1,
    }

    /// <summary>
    /// One authored entry in a document's effect list.
    ///
    /// The effect is named by TYPE NAME rather than held as an instance, and that is deliberate: the concrete
    /// modifier types live in <c>Laubrary.SpriteFx</c> and the catalog that classifies them lives in
    /// <c>Laubrary.PyreShaper</c>, both of which sit ABOVE this assembly. A string keeps the authored document
    /// free of a dependency it must not have, and it is the same key
    /// <c>ShaperEffectCatalogEntry.typeName</c> already uses, so the catalog lookup is an exact match rather
    /// than a mapping table that could drift.
    /// </summary>
    [Serializable]
    public class ShaperEffectRef
    {
        /// <summary>The modifier's type name, e.g. <c>BloomModifier</c> — <c>ShaperEffectCatalogEntry.typeName</c>.</summary>
        public string typeName;

        /// <summary>Which stage this entry runs at. Only <see cref="ShaperEffectStage.PostComposite"/> is run
        /// by the shipped renderer today; see this file's header for why.</summary>
        public ShaperEffectStage stage = ShaperEffectStage.PostComposite;

        public bool enabled = true;

        public ShaperEffectRef() { }
        public ShaperEffectRef(string typeName, ShaperEffectStage stage)
        {
            this.typeName = typeName;
            this.stage = stage;
        }
    }

    /// <summary>
    /// The seam that lets an effect actually run without this assembly knowing what an effect IS.
    ///
    /// A Shaper document can name an effect (a string) but cannot execute one: every concrete modifier is a
    /// <c>Laubrary.SpriteFx.PixelModifier</c>, and this assembly deliberately references neither SpriteFx nor
    /// the PyreShaper catalog — its whole dependency list is ZuiRuntime and Pooling, and the bridge assembly
    /// depends on IT. Rather than invert that (or reach for a mutable static hook, which is global state by
    /// another name), the renderer takes an applier and the BRIDGE supplies one.
    ///
    /// Passing none is a first-class case, not a degraded one: a caller that wants raw geometry gets exactly
    /// the picture it would have got before effects existed.
    /// </summary>
    public interface IShaperEffectApplier
    {
        /// <summary>
        /// Apply every enabled entry of <paramref name="effects"/> whose stage is <paramref name="stage"/>, in
        /// list order, in place over <paramref name="pixels"/>. An entry naming a type that cannot be resolved,
        /// or one the catalog reports unavailable, must be SKIPPED rather than throwing — a document must not
        /// fail to render because one effect is missing.
        /// </summary>
        void Apply(IReadOnlyList<ShaperEffectRef> effects, ShaperEffectStage stage,
                   Color32[] pixels, int width, int height, float phase01, uint seed);
    }
}

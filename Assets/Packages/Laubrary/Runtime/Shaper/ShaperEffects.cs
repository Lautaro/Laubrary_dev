// ShaperEffects — the authored effect list, and the seam that lets something actually run it.
//
// T-0156. T-0114 built the whole universal-effects system — a 41-entry catalog, the sheets/padding
// portability split, and an explicit pre/post-composite stage distinction proven with a real runner. What it
// never built was a way for a DOCUMENT to say "apply this one". No node and no layer held an effect list, so
// ShaperEffectCatalog was static classification and nothing else: finished engine work that no user could
// reach, exactly like Solids before T-0155.
//
// ── Two lists, and the stage is WHERE THE LIST LIVES ────────────────────────────────────────────────────
// T-0163. PostComposite "runs once on the FINISHED, folded picture"; PreComposite "runs once per instance,
// on that instance's OWN buffer, before instances fold into one picture". The only fold in the shipped
// renderer is ShaperDocumentRenderer's layer composite, so the finished picture is the DOCUMENT's picture and
// one layer's buffer is the pre-fold picture. That makes the stage a property of WHICH LIST an entry sits in,
// never a per-entry dropdown: ShaperLayer.effects is pre-composite by construction, ShaperDocument.effects is
// post-composite by construction, and no authored value can disagree with where the list is.
//
// The 8-bit boundary T-0156 refused to cross is now crossed DELIBERATELY and only where it is paid for. A
// layer's own buffer is a PREMULTIPLIED LINEAR float destination and every catalogued effect is a SpriteFx
// kernel over Color32, so a layer that carries effects is encoded to 8-bit, run, and decoded back to
// premultiplied linear before the composite. That round trip cannot carry an additive glow whose premultiplied
// colour exceeds its alpha — the one thing the float destination expresses and 8-bit straight alpha does not.
// A layer with an EMPTY effect list never takes that path at all, so every existing document, and every layer
// the author did not put an effect on, still composites bit-identically in float.
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
    /// T-0163 — the authored SETTINGS of one effect, held polymorphically so a document carries real dial
    /// values instead of a type name the renderer has to construct at defaults.
    ///
    /// It is an abstract base declared HERE, with its one concrete subclass declared in the bridge assembly
    /// (<c>Laubrary.PyreShaper.ShaperModifierEffect</c>), because that is the only shape that gives a document
    /// a serialized effect instance without this assembly learning what an effect IS. The concrete modifier
    /// types are <c>Laubrary.SpriteFx.PyreModifier</c> subclasses and the catalog that classifies them is
    /// <c>Laubrary.PyreShaper</c>'s, both ABOVE this assembly; a <c>[SerializeReference]</c> field typed to a
    /// base declared below its subclasses is exactly how Unity's managed references are meant to cross that
    /// line (the field records the concrete type's assembly, so the subclass need not be visible from here).
    /// It is the same seam <see cref="IShaperEffectApplier"/> already uses for execution, extended to data.
    /// </summary>
    [Serializable]
    public abstract class ShaperEffectInstance
    {
        /// <summary>The catalog key this instance answers to — <c>ShaperEffectCatalogEntry.typeName</c>. Used
        /// for classification and cache identity, so it must not be a display string.</summary>
        public abstract string TypeName { get; }

        /// <summary>What a human calls it in the effect list.</summary>
        public abstract string DisplayName { get; }
    }

    /// <summary>
    /// One authored entry in an effect list — the settings, plus whether the entry runs.
    ///
    /// <b>The entry does not name its own stage.</b> Stage is derived from which list holds it (see this
    /// file's header): <see cref="ShaperLayer.effects"/> is pre-composite, <see cref="ShaperDocument.effects"/>
    /// is post-composite. <see cref="stage"/> survives only so that documents authored before T-0163 keep
    /// deserializing; nothing reads it.
    /// </summary>
    [Serializable]
    public class ShaperEffectRef
    {
        /// <summary>The modifier's type name, e.g. <c>BloomModifier</c> — <c>ShaperEffectCatalogEntry.typeName</c>.
        /// Kept alongside <see cref="instance"/> so an entry still identifies itself in the UI when its
        /// instance fails to deserialize (an effect class removed or renamed), rather than becoming a blank row
        /// with nothing to say about what it was.</summary>
        public string typeName;

        /// <summary>
        /// This entry's authored settings. Null on an entry written before T-0163 — such an entry had no
        /// settings to lose (every effect ran at class defaults, and none of them ran at all), so the editor
        /// upgrades it in place by constructing the modifier <see cref="typeName"/> names.
        /// </summary>
        [SerializeReference] public ShaperEffectInstance instance;

        /// <summary>LEGACY (pre-T-0163). Read by nothing: stage is where the list lives.</summary>
        public ShaperEffectStage stage = ShaperEffectStage.PostComposite;

        public bool enabled = true;

        public ShaperEffectRef() { }
        public ShaperEffectRef(string typeName, ShaperEffectInstance instance)
        {
            this.typeName = typeName;
            this.instance = instance;
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
        /// Apply every enabled entry of <paramref name="effects"/>, in list order, in place over
        /// <paramref name="pixels"/>. <paramref name="stage"/> is the stage THIS LIST runs at — derived by the
        /// caller from where the list lives, never from an entry — and an entry the catalog reports unavailable
        /// at that stage must be SKIPPED rather than throwing, as must one whose instance cannot be resolved. A
        /// document must not fail to render because one effect is missing.
        /// </summary>
        void Apply(IReadOnlyList<ShaperEffectRef> effects, ShaperEffectStage stage,
                   Color32[] pixels, int width, int height, float phase01, uint seed);
    }
}

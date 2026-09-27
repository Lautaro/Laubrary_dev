using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// Locates (or creates) the two chain modifiers that drive a Klip's volume and pitch over its play
    /// length — an Envelope bound to a Gain node for volume, and an Envelope bound to the source stage's
    /// pitch — the same way the waveform overlay's curves are really just ordinary chain modifiers under
    /// the hood.
    ///
    /// The Klip editor's waveform overlay (<see cref="KlipEditorWindow"/> + <see cref="AudioSpectrumView"/>)
    /// reads and writes curves exclusively through this class. That matters because real-time playback
    /// (<see cref="ZoundDspPlayback"/> / <c>ZoundSapPlayback.ResolveChainForPlayback</c>) only converts a
    /// Klip's legacy <c>volumeEnvelope</c>/<c>pitchEnvelope</c> fields into a chain when the Klip has no
    /// chain of its own — the moment any effect is added through the chain editor, those legacy fields stop
    /// being consulted. Editing through this class instead of the legacy fields means a curve affects
    /// playback whether or not the Klip already has other effects.
    ///
    /// The legacy fields themselves are left alone: a one-time project migration still converts them for
    /// Klips nobody has opened in this editor yet, and a handful of older editor-only paths (importing with
    /// a bake, Zequence-entry mixing) still use them directly for their own, unrelated purposes.
    /// </summary>
    internal static class KlipChainEnvelopes {

        /// <summary>A permanently disabled envelope for an overlay that has no modifier yet.</summary>
        public static readonly Envelope Disabled = new Envelope(0f, 1f);

        private static ZoundEffectChain Chain(Zound zound) => ZoundDspPlayback.ResolveChain(zound, out _);

        private static int FindEnvelopeModifier(ZoundEffectChain chain, System.Predicate<ZoundModifierBinding> target) {
            for (int b = 0; b < chain.bindings.Count; b++) {
                var bind = chain.bindings[b];
                if (bind.modifierIndex < 0 || bind.modifierIndex >= chain.modifiers.Count) continue;
                if (chain.modifiers[bind.modifierIndex].type != ZoundModifierType.Envelope) continue;
                if (target(bind)) return bind.modifierIndex;
            }
            return -1;
        }

        private static int VolumeModifier(ZoundEffectChain chain) =>
            FindEnvelopeModifier(chain, b => b.nodeIndex >= 0 && b.nodeIndex < chain.nodes.Count && chain.nodes[b.nodeIndex].type == ZoundEffectType.Gain && b.paramIndex == 0);

        private static int PitchModifier(ZoundEffectChain chain) =>
            FindEnvelopeModifier(chain, b => b.nodeIndex == -1 && b.paramIndex == SourceStageParam.Pitch);

        public static Envelope VolumeCurve(Zound zound, bool create) {
            var chain = Chain(zound);
            int m = VolumeModifier(chain);
            if (m < 0) {
                if (!create) return null;
                chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));
                // Seed the new modifier from the Klip's legacy curve, if it drew one, so switching a Klip
                // over to the chain (by giving it its first modifier) doesn't discard existing curve work.
                Envelope seed = zound is Klip legacyKlip && legacyKlip.volumeEnvelope != null
                    ? legacyKlip.volumeEnvelope.DeepCopy()
                    : new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume", curve = seed };
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding {
                    modifierIndex = chain.modifiers.Count - 1, nodeIndex = chain.nodes.Count - 1, paramIndex = 0,
                    // The drawn curve owns the level outright, which is what a volume curve has always meant.
                    combine = Dsp.ModulationCombine.Set, depth = 1f, schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA });
                chain.Touch();
                m = chain.modifiers.Count - 1;
            }
            if (chain.modifiers[m].curve == null) chain.modifiers[m].curve = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
            return chain.modifiers[m].curve;
        }

        public static Envelope PitchCurve(Zound zound, bool create) {
            var chain = Chain(zound);
            int m = PitchModifier(chain);
            if (m < 0) {
                if (!create) return null;
                // Same carry-over as VolumeCurve: keep the shape of any existing legacy pitch curve.
                Envelope seed = zound is Klip legacyKlip && legacyKlip.pitchEnvelope != null
                    ? legacyKlip.pitchEnvelope.DeepCopy()
                    : new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch", curve = seed };
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding {
                    modifierIndex = chain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Pitch,
                    combine = Dsp.ModulationCombine.Set, depth = 1f, schema = Dsp.ChainModulationCompat.CURRENT_SCHEMA });
                chain.Touch();
                m = chain.modifiers.Count - 1;
            }
            if (chain.modifiers[m].curve == null) chain.modifiers[m].curve = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);
            return chain.modifiers[m].curve;
        }

        public static void SetVolumeEnabled(Zound zound, bool enabled) {
            var chain = Chain(zound);
            var curve = VolumeCurve(zound, enabled);
            if (curve == null) return;
            int m = VolumeModifier(chain);
            if (m >= 0) chain.modifiers[m].enabled = enabled;
            curve.enabled = enabled;
            chain.Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
        }

        public static void SetPitchEnabled(Zound zound, bool enabled) {
            var chain = Chain(zound);
            var curve = PitchCurve(zound, enabled);
            if (curve == null) return;
            int m = PitchModifier(chain);
            if (m >= 0) chain.modifiers[m].enabled = enabled;
            curve.enabled = enabled;
            chain.Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
        }

        /// <summary>After an overlay drag mutated a curve in place: the next play rebuilds the layout.</summary>
        public static void Touch(Zound zound) {
            Chain(zound).Touch();
            ZoundDspPlayback.InvalidateLayout(zound);
            UnityEditor.EditorUtility.SetDirty(ZoundsProject.Instance);
        }
    }

}

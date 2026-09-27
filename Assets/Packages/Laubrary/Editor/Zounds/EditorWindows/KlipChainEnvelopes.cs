using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// Ported utility: locates (or creates) the two chain modifiers that would drive a Klip's volume and
    /// pitch over its play length — an Envelope bound to a Gain node for volume, and an Envelope bound to
    /// the source stage's pitch — the same way the source project's waveform overlay curves are really
    /// just ordinary chain modifiers under the hood.
    ///
    /// This project's Klip editor does not use that wiring: a Klip's volume/pitch-over-time curves are
    /// its own <c>volumeEnvelope</c>/<c>pitchEnvelope</c> fields, baked into the rendered audio by
    /// <see cref="KlipEditorWindow.RenderToAudioClip"/> rather than applied live by a per-voice modifier.
    /// Rewiring the waveform overlay to use chain modifiers instead would be an architecture change, not
    /// a compile fix, so this class is kept as a standalone utility against the (already-present) chain
    /// data model: it compiles and works correctly against any zound's chain, it is simply not called
    /// from the Klip editor's spectrum-view events the way it was in the source project.
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
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume", curve = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange) };
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding { modifierIndex = chain.modifiers.Count - 1, nodeIndex = chain.nodes.Count - 1, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f });
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
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch", curve = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange) };
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding { modifierIndex = chain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Pitch, op = ModifierOp.Multiply, depth = 1f });
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

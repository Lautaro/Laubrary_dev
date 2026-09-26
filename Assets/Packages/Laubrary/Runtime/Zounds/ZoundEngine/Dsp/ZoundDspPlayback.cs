using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Resolves which effect chain a Zound plays with and turns it into the flat parameter/state layout
    /// any engine consumes (<see cref="GetLayout"/>), and rolls the trigger-time-only source duration math.
    /// Also owns the per-Zound layout cache.
    /// </summary>
    public static class ZoundDspPlayback {

        private sealed class LayoutEntry {
            public ChainLayout layout;
            public int chainVersion;
            public int sampleRate;
            public ZoundEffectChain chain;
            public int presetId;
        }

        private static readonly Dictionary<Zound, LayoutEntry> layouts = new Dictionary<Zound, LayoutEntry>();

        public static void InvalidateLayouts() {
            layouts.Clear();
        }

        public static void InvalidateLayout(Zound zound) {
            if (zound != null) layouts.Remove(zound);
        }

        /// <summary>The chain a Zound plays with: its preset by live reference, else its inline chain.</summary>
        public static ZoundEffectChain ResolveChain(Zound zound, out ZoundChainPreset preset) {
            preset = null;
            if (zound.chainPresetId != 0) {
                preset = ZoundsProject.Instance.FindChainPreset(zound.chainPresetId);
                if (preset != null) return preset.chain;
            }
            return zound.effectChain;
        }

        public static ChainLayout GetLayout(Zound zound, int sampleRate) {
            var chain = ResolveChain(zound, out var preset);
            if (chain == null || chain.IsEmpty) return ChainLayout.Empty;
            if (layouts.TryGetValue(zound, out var entry) && entry.chain == chain && entry.chainVersion == chain.version
                && entry.sampleRate == sampleRate && entry.presetId == zound.chainPresetId) {
                return entry.layout;
            }
            var layout = ChainLayout.Build(chain, sampleRate, zound.chainPresetId != 0 ? zound.chainOverrides : null);
            if (layout.error != null) Debug.LogWarning("[Zounds] " + zound.name + ": " + layout.error);
            layouts[zound] = new LayoutEntry { layout = layout, chain = chain, chainVersion = chain.version, sampleRate = sampleRate, presetId = zound.chainPresetId };
            return layout;
        }

        /// <summary>
        /// Play length of a source of <paramref name="sourceSeconds"/> under the chain's pitch modulation:
        /// the integral of 1/pitch over the source for every Envelope modifier bound to the source pitch
        /// (waveform time base). Other modulators cannot be predicted here; the hold-after-end rule covers them.
        /// </summary>
        public static float DurationUnderPitchModulation(Zound zound, float sourceSeconds) {
            var chain = ResolveChain(zound, out _);
            if (chain == null || chain.IsEmpty) return sourceSeconds;
            const int steps = 400;
            double total = 0;
            for (int i = 0; i < steps; i++) {
                float t = (i + 0.5f) / steps;
                float pitch = 1f;
                for (int b = 0; b < chain.bindings.Count; b++) {
                    var bind = chain.bindings[b];
                    if (bind.nodeIndex != -1 || bind.paramIndex != SourceStageParam.Pitch) continue;
                    if (bind.modifierIndex < 0 || bind.modifierIndex >= chain.modifiers.Count) continue;
                    var m = chain.modifiers[bind.modifierIndex];
                    if (!m.enabled || m.type != ZoundModifierType.Envelope || m.curve == null) continue;
                    // The curve spans the source plus its extra time (see DspVoice.EvaluateModifiers).
                    float extra = Mathf.Max(m.Param(0), 0f);
                    float tn = sourceSeconds + extra > 0f ? t * sourceSeconds / (sourceSeconds + extra) : t;
                    float v = m.curve.Evaluate(tn) * bind.depth;
                    switch (bind.op) {
                        case ModifierOp.Multiply: pitch *= v; break;
                        case ModifierOp.Add: pitch += v; break;
                        default: pitch = v; break;
                    }
                }
                pitch = Mathf.Clamp(pitch, ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Pitch].min, ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Pitch].max);
                total += (sourceSeconds / steps) / pitch;
            }
            return (float)total;
        }

        // Round-robin without repeats across the cycle seam: when every step has been used the set is
        // cleared, and the first pick of the new cycle excludes the last-played index (for that pick only,
        // so every step is still played exactly once per cycle).
        internal static int NextStepIndex(int last, int count, bool roundRobin, ref int used) {
            if (count <= 1) return 0;
            if (!roundRobin) return (last + 1) % count;
            int all = count >= 31 ? int.MaxValue : (1 << count) - 1;
            used |= 1 << last;
            int exclude = 0;
            if ((used & all) == all) { used = 0; exclude = 1 << last; }
            int free = 0;
            for (int i = 0; i < count && i < 31; i++) if (((used | exclude) & (1 << i)) == 0) free++;
            int pick = Random.Range(0, free);
            for (int i = 0; i < count && i < 31; i++) {
                if (((used | exclude) & (1 << i)) != 0) continue;
                if (pick == 0) { used |= 1 << i; return i; }
                pick--;
            }
            return (last + 1) % count;
        }
    }

    /// <summary>
    /// Builds the chain equivalent of a Klip's legacy named-field effects (EQ, gain, compression,
    /// normalization, fade, volume and pitch envelopes) in the fixed order the old bake applied them.
    /// Used in memory until the persisted migration lands in the data-model phase.
    /// </summary>
    public static class ChainMigration {

        /// <summary>
        /// One-time, persisted migration: every Klip whose named-field effects are set and whose chain is
        /// empty gets the equivalent chain (same fixed order the bake applied), then the legacy flags are
        /// cleared so the old and the new path can never both apply. Returns how many Klips changed.
        /// </summary>
        public static int MigrateProject(ZoundLibrary library) {
            int migrated = 0;
            library.ForEachZound(z => {
                if (!(z is Klip k)) return;
                if (!HasLegacyEdits(k)) return;
                if (k.effectChain != null && !k.effectChain.IsEmpty) { ClearLegacyFlags(k); return; }
                if (k.chainPresetId != 0) { ClearLegacyFlags(k); return; }
                k.effectChain = SynthesizeFromLegacy(k);
                ClearLegacyFlags(k);
                migrated++;
            });
            return migrated;
        }

        public static void ClearLegacyFlags(Klip k) {
            k.gainEnabled = false;
            k.eqEnabled = false;
            k.compressionEnabled = false;
            k.normalizationEnabled = false;
            k.fadeEnabled = false;
            if (k.volumeEnvelope != null) k.volumeEnvelope.enabled = false;
            if (k.pitchEnvelope != null) k.pitchEnvelope.enabled = false;
        }

        public static bool HasLegacyEdits(Klip k) {
            if (k.volumeEnvelope != null && k.volumeEnvelope.enabled) return true;
            if (k.pitchEnvelope != null && k.pitchEnvelope.enabled) return true;
            if (k.gainEnabled && k.gain > 0.0001f && !Mathf.Approximately(k.gain, 1f)) return true;
            if (k.eqEnabled) return true;
            if (k.compressionEnabled) return true;
            if (k.normalizationEnabled) return true;
            if (k.fadeEnabled && (k.fadeInDuration > 0.001f || k.fadeOutDuration > 0.001f)) return true;
            return false;
        }

        public static ZoundEffectChain SynthesizeFromLegacy(Klip k) {
            var chain = new ZoundEffectChain();
            if (k.eqEnabled) {
                var eq = new ZoundEffectNode(ZoundEffectType.EQ);
                eq.p[0] = k.subGain; eq.p[1] = k.lowGain; eq.p[2] = k.lowMidGain; eq.p[3] = k.midGain;
                eq.p[4] = k.highMidGain; eq.p[5] = k.highGain; eq.p[6] = k.airGain; eq.p[7] = k.hpFrequency; eq.p[8] = k.lpFrequency;
                chain.nodes.Add(eq);
            }
            if (k.gainEnabled && k.gain > 0.0001f && !Mathf.Approximately(k.gain, 1f)) {
                var g = new ZoundEffectNode(ZoundEffectType.Gain);
                g.p[0] = k.gain;
                chain.nodes.Add(g);
            }
            if (k.compressionEnabled) {
                var c = new ZoundEffectNode(ZoundEffectType.Compressor);
                c.p[0] = k.compThreshold; c.p[1] = k.compRatio; c.p[2] = k.compAttack; c.p[3] = k.compRelease; c.p[4] = k.compMakeupGain;
                chain.nodes.Add(c);
            }
            if (k.normalizationEnabled) {
                var n = new ZoundEffectNode(ZoundEffectType.Normalize);
                n.p[0] = k.normalizeTargetDB;
                chain.nodes.Add(n);
            }
            if (k.fadeEnabled && (k.fadeInDuration > 0.001f || k.fadeOutDuration > 0.001f)) {
                var f = new ZoundEffectNode(ZoundEffectType.Fade);
                f.p[0] = k.fadeInDuration; f.p[1] = k.fadeOutDuration; f.p[2] = k.fadeUseSCurve ? 1f : 0f;
                chain.nodes.Add(f);
            }
            if (k.volumeEnvelope != null && k.volumeEnvelope.enabled) {
                var gain = new ZoundEffectNode(ZoundEffectType.Gain);
                chain.nodes.Add(gain);
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume envelope", curve = k.volumeEnvelope.DeepCopy() };
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding { modifierIndex = chain.modifiers.Count - 1, nodeIndex = chain.nodes.Count - 1, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f });
            }
            if (k.pitchEnvelope != null && k.pitchEnvelope.enabled) {
                var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch envelope", curve = k.pitchEnvelope.DeepCopy() };
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding { modifierIndex = chain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Pitch, op = ModifierOp.Multiply, depth = 1f });
            }
            return chain;
        }

        /// <summary>Output duration of a source of the given length under a pitch envelope (the integral of 1/pitch).</summary>
        public static float DurationUnderPitchEnvelope(float sourceDuration, Envelope pitchEnvelope) {
            const int steps = 1000;
            float stepSize = 1f / steps;
            float total = 0f;
            for (int i = 0; i < steps; i++) {
                float pitch = Mathf.Max(pitchEnvelope.Evaluate(i * stepSize), 0.01f);
                total += stepSize * sourceDuration / pitch;
            }
            return total;
        }
    }

}

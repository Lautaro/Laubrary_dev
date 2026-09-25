using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using Laubrary.Zounds.Dsp.Native;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Main-thread side of starting a voice: resolves PCM, chain layout and bus, allocates a voice,
    /// resolves trigger-time modifiers, publishes it and registers the owning token.
    /// Also owns the per-Zound layout cache and the per-Zound persistent modifier state.
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

        // Per-Zound persistent trigger-time state (Step cursors and round-robin masks), keyed by zound
        // and modifier index — the same place CompositeZound.playedEntries lives for the same reason.
        private struct StepState { public int index; public int usedMask; public bool started; }
        private static readonly Dictionary<Zound, StepState[]> stepStates = new Dictionary<Zound, StepState[]>();

        public static void InvalidateLayouts() {
            layouts.Clear();
        }

        public static void InvalidateLayout(Zound zound) {
            if (zound != null) layouts.Remove(zound);
        }

        /// <summary>
        /// Pushes a parameter edit into every voice or group currently playing this zound's chain, so a
        /// slider drag is heard on the playing sound. Only parameter VALUES travel this way (single float
        /// writes, atomic); a structural edit (nodes, modifiers, bindings) is heard on the next play.
        /// </summary>
        public static void PushLiveParam(Zound zound, ZoundEffectChain chain, int nodeIndex, int paramIndex, float value) {
            var g = ZoundEngine.DspIfAny;
            if (g == null) return;
            for (int i = 0; i < g.voices.Length; i++) {
                if (g.voiceZounds[i] == zound) PushInto(g.voices[i], chain, nodeIndex, paramIndex, value);
            }
            for (int i = 0; i < g.groups.Length; i++) {
                if (g.groupZounds[i] == zound) PushInto(g.groups[i], chain, nodeIndex, paramIndex, value);
            }
            // Zounds sharing the preset by live reference hear it too.
            if (zound.chainPresetId != 0) {
                for (int i = 0; i < g.voices.Length; i++) {
                    var z = g.voiceZounds[i];
                    if (z != null && z != zound && z.chainPresetId == zound.chainPresetId) PushInto(g.voices[i], chain, nodeIndex, paramIndex, value);
                }
                for (int i = 0; i < g.groups.Length; i++) {
                    var z = g.groupZounds[i];
                    if (z != null && z != zound && z.chainPresetId == zound.chainPresetId) PushInto(g.groups[i], chain, nodeIndex, paramIndex, value);
                }
            }
        }

        private static void PushInto(NativeNode v, ZoundEffectChain chain, int nodeIndex, int paramIndex, float value) {
            if (v.State == VoiceState.Free) return;
            var L = v.layout;
            if (L == null) return;
            if (nodeIndex >= 0) {
                if (nodeIndex >= L.nodeCount || nodeIndex >= chain.nodes.Count) return;
                if (L.nodeType[nodeIndex] != chain.nodes[nodeIndex].type || paramIndex >= L.paramCountOf[nodeIndex]) return;
            }
            else if (paramIndex >= SourceStageParam.Count) return;
            // The clamp and the "is it modulated" test live on the native side too; the
            // value is handed over as-is and refused there if a modifier owns it.
            v.PushLiveParam(L.FlatIndex(nodeIndex, paramIndex), value);
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

        /// <summary>
        /// Starts a voice for a token. Returns null (and plays nothing) when the clip cannot be read or the
        /// pool is exhausted; the reason is logged once per clip through the PCM cache's problem field.
        /// </summary>
        public static NativeNode StartVoice(ZoundToken token, Zound zound, AudioClip clip, AudioMixerGroup mixerGroup,
                                          float startSeconds, float endSeconds, float basePitch, float outGain, float sourceDuration, bool loop,
                                          int parentGroup = -1, RepeatPlan repeat = default, PcmClip pcmOverride = null) {
            var graph = ZoundEngine.Dsp;
            var pcm = pcmOverride ?? ZoundPcmCache.Get(clip);
            if (pcm == null || !pcm.valid) {
                if (pcm != null && !pcm.problemLogged) {
                    pcm.problemLogged = true;
                    Debug.LogWarning("[Zounds] Cannot play '" + (clip != null ? clip.name : "null") + "' through the DSP engine: " + pcm.problem);
                }
                return null;
            }
            var layout = GetLayout(zound, graph.sampleRate);
            int pcmId = NativePcm.IdFor(pcm);
            int layoutId = NativeChainBlob.IdFor(layout);
            if (pcmId == 0 || layoutId == 0) return null;
            var voice = graph.AllocateVoice(layout.heavy);
            if (voice == null) return null;
            int bus = graph.GetOrCreateBus(mixerGroup);
            if (bus < 0) return null;
            double startFrame = Mathf.Clamp(startSeconds, 0f, pcm.LengthSeconds) * pcm.frequency;
            double endFrame = endSeconds > startSeconds ? Mathf.Min(endSeconds, pcm.LengthSeconds) * pcm.frequency : pcm.frames;
            if (endFrame > pcm.frames) endFrame = pcm.frames;
            if (endFrame <= startFrame + 1) { endFrame = pcm.frames; startFrame = 0; }
            if (parentGroup >= 0 && parentGroup < graph.groups.Length && graph.groups[parentGroup].State != VoiceState.Free) {
                bus = graph.groups[parentGroup].BusIndex; // a subtree renders on its root's bus
            }
            else parentGroup = -1;
            // A chain whose state does not fit the node's arena is refused by the native
            // side rather than indexing past it on the audio thread; that is the same last
            // line of defence the managed ArenaFits check used to be.
            if (!voice.Prepare(graph.NewTokenId(), bus, pcmId, layout, layoutId,
                               startFrame, endFrame, basePitch, outGain, sourceDuration, loop, parentGroup)) {
                Debug.LogError("[Zounds] '" + zound.name + "': the native engine refused the voice (chain state "
                    + layout.stateFloats + " floats, " + (voice.heavyTier ? "heavy" : "light") + " node); played nothing.");
                return null;
            }
            if (repeat.enabled) voice.SetRepeat(in repeat);
            ResolveTriggerTimeModifiers(zound, layout, voice);
            graph.RegisterVoiceToken(voice, token, zound);
            voice.Publish();
            return voice;
        }

        /// <summary>
        /// Starts a group node for a Zequence that carries a chain. Returns null when the Zequence has no
        /// chain (children then sum straight into the parent) or no group slot is free.
        /// </summary>
        public static NativeNode StartGroup(ZoundToken token, Zound zound, AudioMixerGroup mixerGroup, float duration, int parentGroup) {
            var graph = ZoundEngine.Dsp;
            var layout = GetLayout(zound, graph.sampleRate);
            if (layout.IsEmptyChain) return null;
            int layoutId = NativeChainBlob.IdFor(layout);
            if (layoutId == 0) return null;
            var group = graph.AllocateGroup(layout.heavy);
            if (group == null) return null;
            int bus = graph.GetOrCreateBus(mixerGroup);
            if (bus < 0) return null;
            int depth = 0;
            if (parentGroup >= 0 && parentGroup < graph.groups.Length && graph.groups[parentGroup].State != VoiceState.Free) {
                bus = graph.groups[parentGroup].BusIndex;
                depth = graph.groups[parentGroup].Depth + 1;
                if (depth > ZoundDspGraph.MAX_GROUP_DEPTH) { parentGroup = graph.groups[parentGroup].GroupIndex; depth = ZoundDspGraph.MAX_GROUP_DEPTH; }
            }
            else parentGroup = -1;
            if (!group.PrepareGroup(graph.NewTokenId(), bus, parentGroup, depth, layout, layoutId, duration)) {
                Debug.LogError("[Zounds] '" + zound.name + "': the native engine refused the group node (chain state "
                    + layout.stateFloats + " floats); the Zequence plays without its chain.");
                return null;
            }
            ResolveTriggerTimeModifiers(zound, layout, group);
            graph.RegisterGroupToken(group, token, zound);
            group.Publish();
            return group;
        }

        /// <summary>Random values, per-trigger Step advances and free-running LFO phases are settled here, on the main thread.</summary>
        private static void ResolveTriggerTimeModifiers(Zound zound, ChainLayout layout, NativeNode voice) {
            for (int m = 0; m < layout.modCount; m++) {
                var mp = layout.modParams[m];
                switch (layout.modType[m]) {
                    case ZoundModifierType.Random: {
                        float min = mp[0], max = mp[1], bias = mp.Length > 2 ? mp[2] : 1f;
                        float r = Random.value;
                        if (bias > 0f && !Mathf.Approximately(bias, 1f)) r = Mathf.Pow(r, bias);
                        voice.SeedModifierState(m, 0, Mathf.Lerp(min, max, r));
                        break;
                    }
                    case ZoundModifierType.Lfo: {
                        bool resetPhase = mp[3] >= 0.5f;
                        if (!resetPhase) voice.SeedModifierState(m, 0, (float)((AudioSettings.dspTime * mp[1]) % 1.0));
                        break;
                    }
                    case ZoundModifierType.Step: {
                        var steps = layout.modSteps[m];
                        bool roundRobin = (int)mp[2] == (int)StepOrder.RoundRobinNoRepeat;
                        bool startRandom = mp[3] >= 0.5f;
                        bool resetOnTrigger = mp[4] >= 0.5f;
                        bool perTrigger = (int)mp[0] == (int)StepTiming.PerTrigger;
                        if (!stepStates.TryGetValue(zound, out var states) || states.Length < layout.modCount) {
                            states = new StepState[ZoundDspConstants.MAX_MODIFIERS];
                            stepStates[zound] = states;
                        }
                        ref var st = ref states[m];
                        int count = steps.Length;
                        if (!st.started || resetOnTrigger) {
                            int last = st.started ? st.index : -1;
                            st.index = startRandom ? Random.Range(0, count) : 0;
                            st.usedMask = 0;
                            if (roundRobin && last >= 0 && count > 1) {
                                while (st.index == last) st.index = Random.Range(0, count);
                            }
                            st.usedMask |= 1 << st.index;
                            st.started = true;
                        }
                        else if (perTrigger) {
                            st.index = NextStepIndex(st.index, count, roundRobin, ref st.usedMask);
                        }
                        voice.SeedModifierState(m, 0, st.index);
                        voice.SeedModifierState(m, 2, st.usedMask);
                        break;
                    }
                }
            }
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

using System.Text;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Public diagnostics surface of the DSP graph: the Monitor tab and editor probes read the graph
    /// through this instead of reaching into engine internals.
    /// </summary>
    public static class ZoundDspDebug {

        public static bool IsGraphAlive => ZoundEngine.DspIfAny != null && ZoundEngine.DspIfAny.running;

        /// <summary>Creates the engine and its master bus (no mixer group) if they do not exist yet.</summary>
        public static void EnsureMasterBus() {
            ZoundEngine.Dsp.GetOrCreateBus(null);
        }

        public static void SetTestTone(bool on, float frequency = 440f, float amplitude = 0.1f) {
            var g = ZoundEngine.Dsp;
            g.GetOrCreateBus(null);
            g.testToneFrequency = frequency;
            g.testToneAmplitude = amplitude;
            g.testTone = on;
        }

        public static bool TestTone => ZoundEngine.DspIfAny != null && ZoundEngine.DspIfAny.testTone;

        public static void ResetStats() {
            ZoundEngine.DspIfAny?.ResetStats();
        }

        public static int BusCount => ZoundEngine.DspIfAny != null ? ZoundEngine.DspIfAny.BusCount : 0;

        public struct BusStats {
            public string name;
            public long callbacks;
            public float lastMs, maxMs, deadlineMs, lastGapMs, maxGapMs;
            public long lateCallbacks;
            public float lastPeak;
            public int frames, channels;
            public bool carrierPlaying;
        }

        public static unsafe BusStats GetBusStats(int index) {
            var g = ZoundEngine.DspIfAny;
            // Straight out of the plugin's shared control block: these are the audio
            // thread's own numbers, not a managed mirror of them.
            var b = Native.ZoundsNative.Bus(index);
            return new BusStats {
                name = "Zounds Bus " + index,
                callbacks = b->callbackCount,
                lastMs = g.NativeTicksToMs(b->lastCallbackTicks),
                maxMs = g.NativeTicksToMs(b->maxCallbackTicks),
                deadlineMs = g.DeadlineMs,
                lastGapMs = 0f,
                maxGapMs = g.NativeTicksToMs(b->maxGapTicks),
                lateCallbacks = b->lateCallbacks,
                lastPeak = b->lastPeak,
                frames = b->lastFrames,
                channels = b->lastChannels,
                carrierPlaying = b->bound != 0
            };
        }

        public struct VoiceInfo {
            public int index; public VoiceState state; public string zound; public long tokenId; public bool heavy;
            public float peak; public int bus; public int nodes; public int modifiers; public float tailSeconds;
        }

        public static int VoiceCount => ZoundDspConstants.MAX_VOICES;

        public static VoiceInfo GetVoiceInfo(int i) {
            var g = ZoundEngine.DspIfAny;
            var v = g.voices[i];
            var z = g.voiceZounds[i];
            var L = v.layout;
            return new VoiceInfo {
                index = i, state = v.State, zound = z != null ? z.name : "", tokenId = v.tokenId, heavy = v.heavyTier,
                peak = v.LastPeak, bus = v.BusIndex, nodes = L != null ? L.nodeCount : 0, modifiers = L != null ? L.modCount : 0,
                tailSeconds = L != null ? L.tailSeconds : 0f
            };
        }

        /// <summary>A modifier's per-voice state slot (index/value/phase) for probes.</summary>
        public static float GetVoiceModifierState(int voiceIndex, int modifierIndex, int slot) {
            var v = ZoundEngine.DspIfAny.voices[voiceIndex];
            if (v.layout == null) return 0f;
            return v.ReadState(v.layout.modStateOffset[modifierIndex] + slot);
        }

        /// <summary>Index of the group node owned by a token, or -1.</summary>
        public static int FindGroupOfToken(ZoundToken token) {
            var g = ZoundEngine.DspIfAny;
            if (g == null) return -1;
            for (int i = 0; i < g.groups.Length; i++) if (g.groupTokens[i] == token) return i;
            return -1;
        }

        public static float GetGroupPeak(int groupIndex) => ZoundEngine.DspIfAny.groups[groupIndex].LastPeak;
        public static string GetGroupDebug(int groupIndex) => ZoundEngine.DspIfAny.groups[groupIndex].DebugCompletionState();
        public static int RapidOnsetReports() => ZoundEngine.DspIfAny != null ? ZoundEngine.DspIfAny.rapidOnsetReports : 0;
        /// <summary>Onsets this voice produced since it was prepared (−1 when the graph does not exist).</summary>
        public static int GetVoiceOnsetTotal(int voiceIndex) => ZoundEngine.DspIfAny != null ? ZoundEngine.DspIfAny.voices[voiceIndex].OnsetTotal : -1;
        public static string GetVoiceDebug(int voiceIndex) => ZoundEngine.DspIfAny.voices[voiceIndex].DebugCompletionState();
        public static float GetVoicePeak(int voiceIndex) => ZoundEngine.DspIfAny.voices[voiceIndex].LastPeak;

        /// <summary>Index of the newest voice owned by a token, or -1.</summary>
        public static int FindVoiceOfToken(ZoundToken token) {
            var g = ZoundEngine.DspIfAny;
            if (g == null) return -1;
            int best = -1; long bestId = -1;
            for (int i = 0; i < g.voices.Length; i++) {
                if (g.voiceTokens[i] == token && g.voiceTokenIds[i] > bestId) { best = i; bestId = g.voiceTokenIds[i]; }
            }
            return best;
        }

        public static string VoiceReport() {
            var g = ZoundEngine.DspIfAny;
            if (g == null) return "DSP graph: none";
            var sb = new StringBuilder();
            sb.Append("voices: active=").Append(g.CountVoices(VoiceState.Active)).Append(" tailing=").Append(g.CountVoices(VoiceState.Tailing))
              .Append(" stopping=").Append(g.CountVoices(VoiceState.Stopping)).Append(" free=").Append(g.CountVoices(VoiceState.Free))
              .Append(" dropped=").Append(g.playsDropped).Append(" stolen=").Append(g.voicesStolen).Append(" eventsDropped=").Append(g.EventsDropped)
              .Append(" pcmClips=").Append(ZoundPcmCache.Count).Append(" pcmMB=").Append((ZoundPcmCache.TotalBytes / (1024f * 1024f)).ToString("F1")).Append('\n');
            for (int i = 0; i < g.voices.Length; i++) {
                var v = g.voices[i];
                if (v.State == VoiceState.Free) continue;
                var info = GetVoiceInfo(i);
                sb.Append("  v").Append(i).Append(' ').Append(info.state).Append(' ').Append(info.zound).Append(" tok=").Append(info.tokenId)
                  .Append(" peak=").Append(info.peak.ToString("F3")).Append(" nodes=").Append(info.nodes).Append(" mods=").Append(info.modifiers)
                  .Append(" tail=").Append(info.tailSeconds.ToString("F2")).Append(v.GroupIndex >= 0 ? " group=" + v.GroupIndex : "")
                  .Append(v.RepeatEnabled ? " repeat=" + v.RepeatsDone + "/" + (v.RepeatsTotal == int.MaxValue ? "∞" : v.RepeatsTotal.ToString()) + (v.SlotsStolen > 0 ? " slotsStolen=" + v.SlotsStolen : "") : "").Append('\n');
            }
            sb.Append("groups: active=").Append(g.CountGroups(VoiceState.Active)).Append(" tailing=").Append(g.CountGroups(VoiceState.Tailing))
              .Append(" free=").Append(g.CountGroups(VoiceState.Free)).Append(" dropped=").Append(g.groupsDropped).Append('\n');
            for (int i = 0; i < g.groups.Length; i++) {
                var v = g.groups[i];
                if (v.State == VoiceState.Free) continue;
                var z = g.groupZounds[i];
                sb.Append("  g").Append(i).Append(' ').Append(v.State).Append(' ').Append(z != null ? z.name : "").Append(" tok=").Append(v.tokenId)
                  .Append(" peak=").Append(v.LastPeak.ToString("F3")).Append(" nodes=").Append(v.layout != null ? v.layout.nodeCount : 0)
                  .Append(" depth=").Append(v.Depth).Append(" children=").Append(v.LiveChildren).Append(v.GroupIndex >= 0 ? " parent=" + v.GroupIndex : "").Append('\n');
            }
            return sb.ToString();
        }

        public static string Report() {
            var g = ZoundEngine.DspIfAny;
            if (g == null) return "DSP graph: none";
            var sb = new StringBuilder();
            sb.Append("DSP graph: running=").Append(g.running).Append(" sr=").Append(g.sampleRate)
              .Append(" buf=").Append(g.dspBufferSize).Append(" deadline=").Append(g.DeadlineMs.ToString("F2")).Append("ms")
              .Append(" tone=").Append(g.testTone).Append('\n');
            for (int i = 0; i < g.BusCount; i++) {
                var s = GetBusStats(i);
                sb.Append("  bus").Append(i).Append(' ').Append(s.name)
                  .Append(" cb=").Append(s.callbacks)
                  .Append(" last=").Append(s.lastMs.ToString("F3")).Append("ms")
                  .Append(" max=").Append(s.maxMs.ToString("F3")).Append("ms")
                  .Append(" (").Append((100f * s.maxMs / s.deadlineMs).ToString("F1")).Append("% of deadline)")
                  .Append(" gapMax=").Append(s.maxGapMs.ToString("F1")).Append("ms")
                  .Append(" late=").Append(s.lateCallbacks)
                  .Append(" peak=").Append(s.lastPeak.ToString("F3"))
                  .Append(" frames=").Append(s.frames).Append('x').Append(s.channels)
                  .Append(" carrier=").Append(s.carrierPlaying ? "playing" : "STOPPED")
                  .Append('\n');
            }
            return sb.ToString();
        }
    }

}

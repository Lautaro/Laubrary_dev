using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Audio;
using Laubrary.Zounds.Dsp.Native;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// The per-voice DSP graph: output buses, the voice pool, group nodes and the
    /// audio-thread to main-thread event ring.
    ///
    /// The rendering itself is no longer here. It lives in the native plugin
    /// (NativeAudio/ZoundsNative), and this class is the main-thread half: it creates
    /// buses, decides which node a play gets, publishes it, drains the events the audio
    /// thread pushes back, and reports what happened. There is deliberately no managed
    /// audio callback anywhere in the process — a single one permanently attaches
    /// Unity's mixer thread to the garbage collector, including for audio that renders
    /// natively, which is the whole reason for the port.
    ///
    /// Threading contract, unchanged in shape: the main thread fills a Free node and
    /// publishes it with a release store; the audio thread only renders and frees.
    /// </summary>
    public sealed class ZoundDspGraph {

        private readonly ZoundNativeBuses buses;
        internal int sampleRate;
        internal int dspBufferSize;
        internal static readonly double ticksPerSecond = Stopwatch.Frequency;

        // ── nodes ──
        internal readonly NativeNode[] voices = new NativeNode[ZoundDspConstants.MAX_VOICES];
        internal readonly ZoundToken[] voiceTokens = new ZoundToken[ZoundDspConstants.MAX_VOICES];
        internal readonly long[] voiceTokenIds = new long[ZoundDspConstants.MAX_VOICES];
        internal readonly Zound[] voiceZounds = new Zound[ZoundDspConstants.MAX_VOICES];
        internal readonly NativeNode[] groups = new NativeNode[ZoundDspConstants.MAX_GROUPS];
        internal readonly ZoundToken[] groupTokens = new ZoundToken[ZoundDspConstants.MAX_GROUPS];
        internal readonly long[] groupTokenIds = new long[ZoundDspConstants.MAX_GROUPS];
        internal readonly Zound[] groupZounds = new Zound[ZoundDspConstants.MAX_GROUPS];
        internal const int MAX_GROUP_DEPTH = 8;

        private long nextTokenId = 1;
        internal long groupsDropped;
        internal long playsDropped;
        internal long voicesStolen;
        internal int rapidOnsetReports;

        /// <summary>False when the native engine could not be brought up; nothing plays and the reason is logged once.</summary>
        internal bool running;
        internal string startupError;

        internal int BusCount => buses != null ? buses.Count : 0;

        public ZoundDspGraph(Transform parent, HideFlags hideFlags) {
            sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out dspBufferSize, out _);

            if (!ZoundsNative.Initialise(sampleRate, dspBufferSize)) {
                startupError = ZoundsNative.LoadError;
                running = false;
                buses = null;
                return;
            }
            buses = new ZoundNativeBuses(parent, hideFlags, sampleRate);
            for (int i = 0; i < voices.Length; i++)
                voices[i] = new NativeNode(ZoundsNative.VoiceNodeId(i), i, false, i < ZoundDspConstants.HEAVY_VOICES);
            for (int g = 0; g < groups.Length; g++)
                groups[g] = new NativeNode(ZoundsNative.GroupNodeId(g), g, true, g < ZoundDspConstants.HEAVY_GROUPS);
            // Anything the previous graph left playing belongs to a torn-down session.
            FlushAllVoices();
            running = true;
        }

        // ─────────────────────────── buses (main thread) ───────────────────────────

        /// <summary>Returns the bus index for a mixer group, creating the bus on first use.</summary>
        internal int GetOrCreateBus(AudioMixerGroup group) {
            if (!running) return -1;
            return buses.GetOrCreate(group);
        }

        /// <summary>Re-arms every bus carrier (after an AudioSettings reset stops them).</summary>
        internal void EnsureBusesPlaying() {
            if (running) buses.EnsurePlaying();
        }

        // ─────────────────────────── nodes (main thread) ───────────────────────────

        internal long NewTokenId() => nextTokenId++;

        /// <summary>
        /// Finds a voice for a new play: a Free one in the fitting tier, else the least
        /// audible Tailing or oldest Active one is stopped and a reserve voice is used.
        /// Returns null when the pool is exhausted (counted and reported).
        /// </summary>
        internal NativeNode AllocateVoice(bool heavy) {
            if (!running) return null;
            int free = CountFree(heavy, out NativeNode firstFree);
            if (free > ZoundDspConstants.RESERVE_VOICES) return firstFree;
            // Steal: prefer a tailing voice, quietest first; then the oldest unprotected active voice.
            NativeNode victim = null;
            float bestPeak = float.MaxValue;
            double oldest = double.MaxValue;
            for (int i = 0; i < voices.Length; i++) {
                // The offline renderer owns one heavy node permanently, so that an
                // equivalence render or a waveform preview can never be interrupted by,
                // or interrupt, something the game is playing.
                if (i == ZoundNativeOffline.OFFLINE_NODE) continue;
                var v = voices[i];
                if (heavy && !v.heavyTier) continue;
                var st = v.State;
                if (st == VoiceState.Tailing) {
                    if (victim == null || victim.State != VoiceState.Tailing || v.LastPeak < bestPeak) { victim = v; bestPeak = v.LastPeak; }
                }
                else if (st == VoiceState.Active && (victim == null || victim.State != VoiceState.Tailing)) {
                    if (v.protectedFromSteal) continue;
                    if (v.allocatedAtDsp < oldest) { oldest = v.allocatedAtDsp; victim = v; }
                }
            }
            if (victim != null && firstFree != null) {
                victim.RequestKill();
                voicesStolen++;
                return firstFree;
            }
            if (firstFree != null) return firstFree;
            playsDropped++;
            return null;
        }

        // A heavy chain must only ever land in a heavy voice: its state does not fit a
        // light arena, and the native side refuses such a prepare outright, so a play
        // would silently be dropped rather than merely sounding wrong.
        private int CountFree(bool heavy, out NativeNode first) {
            first = null;
            int count = 0;
            int begin = heavy ? 0 : ZoundDspConstants.HEAVY_VOICES;
            int end = heavy ? ZoundDspConstants.HEAVY_VOICES : voices.Length;
            for (int i = begin; i < end; i++) {
                if (i == ZoundNativeOffline.OFFLINE_NODE) continue;
                if (voices[i].State == VoiceState.Free) { count++; if (first == null) first = voices[i]; }
            }
            if (!heavy && count <= ZoundDspConstants.RESERVE_VOICES) {
                // Light chains may borrow a heavy voice when the light tier is exhausted.
                for (int i = 0; i < ZoundDspConstants.HEAVY_VOICES; i++) {
                    if (i == ZoundNativeOffline.OFFLINE_NODE) continue;
                    if (voices[i].State == VoiceState.Free) { count++; if (first == null) first = voices[i]; }
                }
            }
            return count;
        }

        internal void RegisterVoiceToken(NativeNode voice, ZoundToken token, Zound zound) {
            voiceTokens[voice.index] = token;
            voiceTokenIds[voice.index] = voice.tokenId;
            voiceZounds[voice.index] = zound;
        }

        /// <summary>
        /// A Free group node of the fitting tier, or null (counted as a dropped group; the
        /// Zequence then plays without its chain). When the heavy tier is exhausted the
        /// quietest Tailing heavy group is told to stop so the slot returns soon.
        /// </summary>
        internal NativeNode AllocateGroup(bool heavy) {
            if (!running) return null;
            int begin = heavy ? 0 : ZoundDspConstants.HEAVY_GROUPS;
            int end = heavy ? ZoundDspConstants.HEAVY_GROUPS : groups.Length;
            for (int i = begin; i < end; i++) if (groups[i].State == VoiceState.Free) return groups[i];
            if (!heavy) for (int i = 0; i < ZoundDspConstants.HEAVY_GROUPS; i++) if (groups[i].State == VoiceState.Free) return groups[i];
            if (heavy) {
                NativeNode victim = null;
                for (int i = 0; i < ZoundDspConstants.HEAVY_GROUPS; i++) {
                    var g = groups[i];
                    if (g.State == VoiceState.Tailing && (victim == null || g.LastPeak < victim.LastPeak)) victim = g;
                }
                victim?.RequestKill();
            }
            groupsDropped++;
            return null;
        }

        internal void RegisterGroupToken(NativeNode group, ZoundToken token, Zound zound) {
            groupTokens[group.index] = token;
            groupTokenIds[group.index] = group.tokenId;
            groupZounds[group.index] = zound;
        }

        /// <summary>Requests a hard stop on every voice and group (StopAllZounds, teardown).</summary>
        internal void FlushAllVoices() {
            if (voices[0] == null) return;
            for (int i = 0; i < voices.Length; i++) if (voices[i].State != VoiceState.Free) voices[i].RequestKill();
            for (int i = 0; i < groups.Length; i++) if (groups[i].State != VoiceState.Free) groups[i].RequestKill();
        }

        /// <summary>
        /// Safety net against leaked slots: a node can only sit in Tailing for its tail
        /// budget, so one seen Tailing for longer than that is a node the audio thread
        /// stopped visiting (a stalled bus carrier, a bus that never ticks). It gets a kill
        /// request, and if that too goes unanswered the slot is freed from here and the
        /// token told Audio End, so the pool never bleeds dry.
        /// </summary>
        private void SweepStaleTails() {
            float now = Time.realtimeSinceStartup;
            const float killAfter = ZoundDspConstants.MAX_TAIL_SEC + 1f;
            const float freeAfter = ZoundDspConstants.MAX_TAIL_SEC + 3f;
            for (int i = 0; i < voices.Length; i++) SweepNode(voices[i], now, killAfter, freeAfter, i, false);
            for (int g = 0; g < groups.Length; g++) SweepNode(groups[g], now, killAfter, freeAfter, g, true);
        }

        private void SweepNode(NativeNode v, float now, float killAfter, float freeAfter, int index, bool isGroup) {
            if (v.State != VoiceState.Tailing) { v.tailingSeenAt = 0f; return; }
            if (v.tailingSeenAt <= 0f) { v.tailingSeenAt = now; return; }
            float age = now - v.tailingSeenAt;
            if (age > freeAfter) {
                string why = v.DebugCompletionState();
                v.ForceFree();
                UnityEngine.Debug.LogWarning("[Zounds] DSP " + (isGroup ? "group " : "voice ") + index + " stayed in Tailing for "
                    + age.ToString("F0") + " s and ignored a kill request: freed from the main thread (" + why + ")");
                if (isGroup) {
                    var gtoken = groupTokens[index];
                    groupTokens[index] = null; groupZounds[index] = null; groupTokenIds[index] = 0;
                    gtoken?.RaiseAudioEnd();
                }
                else {
                    var token = voiceTokens[index];
                    voiceTokens[index] = null; voiceZounds[index] = null; voiceTokenIds[index] = 0;
                    token?.RaiseAudioEnd();
                }
                v.tailingSeenAt = 0f;
            }
            else if (age > killAfter) v.RequestKill();
        }

        /// <summary>
        /// Audio-level "machine gun" detector: one node producing <see cref="RapidOnsetCount"/>
        /// or more onsets inside a second is reported once per token, with the chain that made
        /// it. The onset counting happens in the native engine; this reads the running total and
        /// derives a rate from it, which catches the pattern in the rendered audio itself,
        /// independent of how many plays were issued.
        /// </summary>
        public const int RapidOnsetCount = 5;

        private void SweepRapidOnsets() {
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < voices.Length; i++) SweepOnsets(voices[i], voiceZounds[i], voiceTokens[i], i, false, now);
            for (int g = 0; g < groups.Length; g++) SweepOnsets(groups[g], groupZounds[g], groupTokens[g], g, true, now);
        }

        private void SweepOnsets(NativeNode v, Zound zound, ZoundToken token, int index, bool isGroup, float now) {
            if (v.State == VoiceState.Free) { v.lastOnsetTotal = 0; v.lastOnsetSampleAt = now; return; }
            int total = v.OnsetTotal;
            if (v.lastOnsetSampleAt <= 0f) { v.lastOnsetSampleAt = now; v.lastOnsetTotal = total; return; }
            float window = now - v.lastOnsetSampleAt;
            if (window < 1f) return;
            int onsets = total - v.lastOnsetTotal;
            v.lastOnsetSampleAt = now;
            v.lastOnsetTotal = total;
            if (onsets < RapidOnsetCount || v.onsetReportedForToken == v.tokenId) return;
            v.onsetReportedForToken = v.tokenId;
            rapidOnsetReports++;
            var sb = new System.Text.StringBuilder();
            sb.Append("[Zounds] RAPID ONSETS: ").Append(isGroup ? "group " : "voice ").Append(index).Append(" ('")
              .Append(zound != null ? zound.name : "?").Append("') produced ").Append(onsets)
              .Append(" onsets in the last second of its own output (").Append(total)
              .Append(" since it started); one play, many hits.\n");
            var chain = zound != null ? ZoundDspPlayback.ResolveChain(zound, out _) : null;
            if (chain != null && !chain.IsEmpty) {
                sb.Append("chain of '").Append(zound.name).Append("':\n");
                for (int n = 0; n < chain.nodes.Count; n++) {
                    var node = chain.nodes[n];
                    sb.Append("  node ").Append(n).Append(' ').Append(node.type).Append(node.enabled ? "" : " (bypassed)").Append(" p=[");
                    for (int k = 0; k < node.p.Length; k++) { if (k > 0) sb.Append(", "); sb.Append(node.p[k].ToString("G4")); }
                    sb.Append("]\n");
                }
                for (int m = 0; m < chain.modifiers.Count; m++)
                    sb.Append("  modifier ").Append(m).Append(' ').Append(chain.modifiers[m].type).Append(chain.modifiers[m].enabled ? "" : " (off)").Append('\n');
            }
            else sb.Append("chain: none (the hits come from the source or the repeat plan)\n");
            if (v.RepeatsTotal > 1) sb.Append("repeat plan: ").Append(v.RepeatsDone).Append('/').Append(v.RepeatsTotal).Append('\n');
            sb.Append("node: ").Append(v.DebugCompletionState()).Append('\n');
            if (token != null) sb.Append("token: state=").Append(token.state).Append(" time=").Append(token.time.ToString("F3"))
                                 .Append(" duration=").Append(token.duration.ToString("F3")).Append('\n');
            ZoundTriggerWatch.AppendMainThreadTimeline(sb, now);
            sb.Append(ZoundDspDebug.Report()).Append(ZoundDspDebug.VoiceReport());
            UnityEngine.Debug.LogWarning(sb.ToString());
        }

        /// <summary>Main-thread drain: routes AudioEnd to the token that owns the node, by tokenId.</summary>
        internal void DrainEvents(int maxEvents = 512) {
            if (!running) return;
            EnsureBusesPlaying();
            SweepStaleTails();
            SweepRapidOnsets();
            LogAudioStalls();
            int handled = 0;
            var e = new ZoundsNative.NativeEvent();
            while (handled < maxEvents && ZoundsNative.Zounds_PopEvent(ref e) != 0) {
                handled++;
                if (e.type != ZoundsNative.EV_AUDIO_END) continue;
                int id = e.nodeId;
                // The offline renderer shares the native pool and pushes its own Audio End
                // for a node nobody owns; it is discarded here rather than drained by the
                // offline path, which would also swallow live voices' events.
                if (id == ZoundNativeOffline.OFFLINE_NODE) continue;
                if (ZoundsNative.IsGroupNode(id)) {
                    int g = id - ZoundDspConstants.MAX_VOICES;
                    if (g < 0 || g >= groups.Length) continue;
                    if (groupTokenIds[g] != e.tokenId) continue;
                    var gtoken = groupTokens[g];
                    groupTokens[g] = null; groupZounds[g] = null; groupTokenIds[g] = 0;
                    gtoken?.RaiseAudioEnd();
                    continue;
                }
                if (id < 0 || id >= voices.Length) continue;
                if (voiceTokenIds[id] != e.tokenId) continue;   // stale: the slot was reused
                var token = voiceTokens[id];
                voiceTokens[id] = null;
                voiceZounds[id] = null;
                voiceTokenIds[id] = 0;
                token?.RaiseAudioEnd();
            }
        }

        /// <summary>Main thread: is any voice or group not Free (Active, Tailing or Stopping)?</summary>
        internal bool AnyNodeLive() {
            if (!running) return false;
            for (int i = 0; i < voices.Length; i++) if (voices[i].State != VoiceState.Free) return true;
            for (int g = 0; g < groups.Length; g++) if (groups[g].State != VoiceState.Free) return true;
            return false;
        }

        internal int CountVoices(VoiceState state) {
            if (!running) return 0;
            int c = 0;
            for (int i = 0; i < voices.Length; i++) if (voices[i].State == state) c++;
            return c;
        }

        internal int CountGroups(VoiceState state) {
            if (!running) return 0;
            int c = 0;
            for (int i = 0; i < groups.Length; i++) if (groups[i].State == state) c++;
            return c;
        }

        // ─────────────────────────── stalls (reported, not detected, here) ───────────────────────────
        //
        // The native engine counts the times its own callback was not called for more than
        // 100 ms. A gap like that means the output device repeated its last buffer, which
        // is heard as a stutter. On the managed engine this was the normal consequence of
        // any full GC; the point of the port is that it should now stay at zero, so it is
        // still watched and still reported.

        private long stallsLogged;
        private int lastGc2AtStallLog;

        /// <summary>Audio End events the native ring could not take. The stale-tail sweep covers them.</summary>
        internal unsafe long EventsDropped => running ? ZoundsNative.Header()->eventsDropped : 0;

        internal unsafe long StallCount {
            get {
                if (!running) return 0;
                long total = 0;
                for (int b = 0; b < ZoundDspConstants.MAX_BUSES; b++) total += ZoundsNative.Bus(b)->stallCount;
                return total;
            }
        }

        private void LogAudioStalls() {
            long count = StallCount;
            if (count == stallsLogged) return;
            long pending = count - stallsLogged;
            stallsLogged = count;
            int gc2 = System.GC.CollectionCount(2);
            bool live = AnyNodeLive();
            var sb = new System.Text.StringBuilder();
            sb.Append(live ? "[Zounds] AUDIO STALL (audible): " : "[Zounds] audio stall (nothing was playing): ")
              .Append(pending).Append(" callback gap(s) over ").Append(ZoundDspConstants.MAX_TAIL_SEC > 0 ? 100 : 100)
              .Append(" ms in the native engine");
            if (live) sb.Append("; nodes were live, so the output device repeated its last buffer for that long");
            sb.Append(".\n  gen2 GC collections since the previous stall report: +").Append(gc2 - lastGc2AtStallLog)
              .Append(" (a full collection no longer stops this callback — if these numbers track each other, something managed is back on the audio thread)\n");
            ZoundTriggerWatch.AppendMainThreadTimeline(sb, Time.realtimeSinceStartup);
            lastGc2AtStallLog = gc2;
            if (live) UnityEngine.Debug.LogWarning(sb.ToString()); else UnityEngine.Debug.Log(sb.ToString());
        }

        // ─────────────────────────── teardown (main thread) ───────────────────────────

        /// <summary>
        /// Stops every node, tears the buses down and shuts the native engine. Safe to call
        /// more than once.
        /// </summary>
        internal void Teardown() {
            if (!running) return;
            running = false;
            FlushAllVoices();
            for (int i = 0; i < voices.Length; i++) voices[i].ForceFree();
            for (int g = 0; g < groups.Length; g++) groups[g].ForceFree();
            buses.Teardown();
            for (int i = 0; i < voices.Length; i++) { voiceTokens[i] = null; voiceZounds[i] = null; voiceTokenIds[i] = 0; }
            for (int i = 0; i < groups.Length; i++) { groupTokens[i] = null; groupZounds[i] = null; groupTokenIds[i] = 0; }
            NativeChainBlob.Clear();
            ZoundsNative.Shutdown();
        }

        // ─────────────────────────── monitor (main thread) ───────────────────────────

        internal unsafe void ResetStats() {
            playsDropped = 0; voicesStolen = 0; groupsDropped = 0;
            if (!running) return;
            for (int b = 0; b < ZoundDspConstants.MAX_BUSES; b++) {
                var s = ZoundsNative.Bus(b);
                s->maxCallbackTicks = 0; s->maxGapTicks = 0; s->lateCallbacks = 0; s->callbackCount = 0;
            }
        }

        /// <summary>Test tone straight out of the native engine, for proving a bus is alive.</summary>
        internal unsafe bool testTone {
            get { return running && ZoundsNative.Header()->testTone != 0; }
            set { if (running) ZoundsNative.Header()->testTone = value ? 1 : 0; }
        }

        internal unsafe float testToneFrequency {
            get { return running ? ZoundsNative.Header()->testToneFrequency : 440f; }
            set { if (running) ZoundsNative.Header()->testToneFrequency = value; }
        }

        internal unsafe float testToneAmplitude {
            get { return running ? ZoundsNative.Header()->testToneAmplitude : 0.1f; }
            set { if (running) ZoundsNative.Header()->testToneAmplitude = value; }
        }

        internal static float TicksToMs(long ticks) => (float)(ticks * 1000.0 / ticksPerSecond);

        /// <summary>Native QPC ticks to ms (the audio-side numbers use the plugin's own clock).</summary>
        internal unsafe float NativeTicksToMs(long ticks) {
            if (!running) return 0f;
            long freq = ZoundsNative.Header()->qpcFrequency;
            return freq > 0 ? (float)(ticks * 1000.0 / freq) : 0f;
        }

        /// <summary>Wall-clock deadline of one callback at the current buffer size, in ms.</summary>
        internal float DeadlineMs => dspBufferSize * 1000f / sampleRate;
    }

}

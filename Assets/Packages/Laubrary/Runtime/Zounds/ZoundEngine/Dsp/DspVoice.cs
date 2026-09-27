using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    public enum VoiceState { Free = 0, Active = 1, Tailing = 2, Stopping = 3 }

    /// <summary>Per-block values an effect may need beyond its parameters.</summary>
    public struct VoiceContext {
        public int sampleRate;
        public float elapsedSeconds;     // since the source started, at block start
        public float sourceDuration;     // resolved play length of the source material
        public float sourcePeak;         // peak of the source PCM (for Normalize)
        public bool sourceExhausted;
    }

    /// <summary>How a repeating track re-arms its source: resolved on the main thread, executed by the voice.</summary>
    public struct RepeatPlan {
        public bool enabled;
        public int count;                  // plays in total including the first; int.MaxValue for FixedDuration
        public long intervalSamples;       // output samples between repeats (from start, or from the previous end)
        public bool spaceFromEnd;
        public bool retrigger;             // re-roll pitch/volume per repeat within the multipliers below
        public float pitchMulMin, pitchMulMax, gainMulMin, gainMulMax;
        public long durationLimitSamples;  // FixedDuration: a repeat must end by here; 0 = no limit
        public long nominalLengthSamples;  // one play at the base pitch, for scheduling from the end
    }

    /// <summary>One read cursor over the voice's PCM. Repeater arms several; a plain play uses slot 0.</summary>
    public struct SourceSlot {
        public bool active;
        public int startAt;          // samples into the current block before this slot begins (sample-accurate arming)
        public double cursor;        // in source frames
        public double startFrame;
        public double endFrame;
        public float gain;
        public float pitchMul;       // per-slot pitch (retriggered repeats differ)
        public bool loop;
        public int fadeSamplesLeft;  // > 0: fading out to a stop
        public int fadeSamplesTotal;
    }

    /// <summary>
    /// One playing source with its chain, modifier stack and output routing. The main thread fills a Free
    /// voice and publishes it by writing its state, the audio thread renders it and frees it. Nothing in
    /// Render allocates.
    ///
    /// Its buffers are native memory, so a voice OWNS them and must be disposed — unlike the managed
    /// arrays it used to hold, which the collector reclaimed on its own. Whoever constructs a voice is
    /// responsible for disposing it; today that is only the offline renderer, which does so in a finally
    /// block, but the generator path will need the same discipline designed in rather than added later.
    /// </summary>
    public sealed class DspVoice {

        public readonly int index;
        public readonly bool heavyTier;
        /// <summary>A group node: children sum into bufL/bufR instead of a source stage reading PCM.</summary>
        public readonly bool isGroup;

        // Buffers plus every per-block render scalar now live in one struct (see SapVoiceState) so the
        // same render code can run over either this class's owned instance or a future value-type
        // generator's own instance. The public/internal accessors below forward into it unchanged, so
        // nothing outside this class needed to change.
        private SapVoiceState sap;

        public NativeArray<float> arena => sap.arena;
        public NativeArray<float> bufL => sap.bufL;
        public NativeArray<float> bufR => sap.bufR;
        public NativeArray<SourceSlot> slots => sap.slots;

        // Flat parameter block: live value, block-start value and per-sample step.
        public NativeArray<float> pLive => sap.pLive;
        public NativeArray<float> pStart => sap.pStart;
        public NativeArray<float> pStep => sap.pStep;

        // ── published on allocation (main thread), read-only afterwards ──
        internal int state;              // VoiceState, Volatile
        internal long tokenId;
        internal int busIndex;
        /// <summary>Group this node sums into, or -1 for the bus directly.</summary>
        internal int groupIndex = -1;
        /// <summary>Groups only: nesting depth (0 = directly on the bus); parents render after children.</summary>
        internal int depth;
        /// <summary>Groups only: children still rendering, counted by the graph during each callback.</summary>
        internal int liveChildren;
        internal PcmClip pcm;
        internal ChainLayout layout;
        /// <summary>Burst-readable copy of <see cref="layout"/> for this play; the audio thread reads only this. Built in Prepare/PrepareGroup, disposed in Dispose (and before a new one replaces it — a voice can be set up more than once).</summary>
        internal SapChainLayout sapLayout;
        internal float sourceDuration;   // seconds, resolved on the main thread
        internal double clipRate;        // pcm.frequency / sampleRate
        internal bool protectedFromSteal;
        internal double allocatedAtDsp;

        // ── main-thread control (written any time, read at control rate) ──
        internal float basePitchTarget = 1f;
        internal float outGainTarget = 1f;
        internal int pauseRequest;
        internal int killRequest;        // 1 = hard stop (declick, flush)
        internal int releaseRequest;     // 1 = stop feeding, let the tail ring

        // ── audio-thread state ── now lives in `sap` (SapVoiceState); these forward to it unchanged so
        // any internal reader elsewhere in this class (or a future Monitor) still sees the same names.
        internal float lastPeak => sap.lastPeak;

        // Repeater: the schedule lives on the voice so Release can cancel it and the Monitor can show 3/8.
        internal RepeatPlan repeat => sap.repeat;
        internal int repeatsPending => sap.repeatsPending;
        internal int repeatsDone => sap.repeatsDone;
        internal int repeatsTotal => sap.repeatsTotal;
        /// <summary>Output sample (voice time) at which the last armed repeat ends; the main thread grows the duration from it.</summary>
        internal long trainEndSample => Volatile.Read(ref sap.trainEndSample);
        internal int slotsStolen => sap.slotsStolen;

        public DspVoice(int index, bool heavy, bool isGroup = false) {
            this.index = index;
            heavyTier = heavy;
            this.isGroup = isGroup;
            int arenaFloats = heavy ? ZoundDspConstants.HEAVY_ARENA_FLOATS : ZoundDspConstants.LIGHT_ARENA_FLOATS;
            sap = SapVoiceState.Create(arenaFloats, ChainLayout.MAX_PARAMS, ZoundDspConstants.MAX_SOURCE_SLOTS,
                                        ZoundDspConstants.MAX_MODIFIERS, ZoundDspConstants.MAX_DSP_BUFFER, Allocator.Persistent);
        }

        /// <summary>Releases every native buffer this voice owns. Safe to call more than once.</summary>
        public void Dispose() {
            sap.Dispose();
            if (sapLayout.IsCreated) sapLayout.Dispose();
        }

        public VoiceState State => (VoiceState)Volatile.Read(ref state);

        // ───────────────────────────── main thread ─────────────────────────────

        /// <summary>Prepares a Free voice for a play. Publish with <see cref="Publish"/> afterwards.</summary>
        internal void Prepare(long tokenId, int busIndex, PcmClip pcm, ChainLayout layout, int sampleRate,
                              double startFrame, double endFrame, float basePitch, float outGain, float sourceDuration, bool loop) {
            this.tokenId = tokenId;
            this.busIndex = busIndex;
            groupIndex = -1;
            this.pcm = pcm;
            this.layout = layout;
            if (sapLayout.IsCreated) sapLayout.Dispose();
            sapLayout = SapChainLayout.Create(layout, Allocator.Persistent);
            this.sourceDuration = sourceDuration;
            clipRate = (double)pcm.frequency / sampleRate;
            basePitchTarget = basePitch; sap.basePitchLive = basePitch;
            outGainTarget = outGain; sap.outGainLive = outGain;
            pauseRequest = 0; killRequest = 0; releaseRequest = 0;
            sap.stopping = false; sap.released = false;
            sap.sourceExhausted = false;
            sap.elapsedSamples = 0; sap.samplesSinceSourceEnd = 0; sap.silentSamples = 0;
            sap.lastPeak = 0f;
            protectedFromSteal = false;
            sap.repeat = default; sap.repeatsPending = 0; sap.repeatsDone = 1; sap.repeatsTotal = 1; sap.nextRepeatSample = 0; sap.lastRepeatEndSample = 0; sap.trainEndSample = 0; sap.slotsStolen = 0;
            allocatedAtDsp = AudioSettings.dspTime;
            sap.rng = 2463534242u ^ (uint)tokenId;
            ResetOnsets();

            for (int i = 0; i < sap.slots.Length; i++) sap.slots[i] = default;
            sap.slots[0] = new SourceSlot { active = true, cursor = startFrame, startFrame = startFrame, endFrame = endFrame, gain = 1f, pitchMul = 1f, loop = loop };

            { int clearLen = Mathf.Min(sap.arena.Length, Mathf.Max(layout.stateFloats, 1)); for (int ci = 0; ci < clearLen; ci++) sap.arena[ci] = 0f; }
            for (int i = 0; i < layout.paramCount; i++) {
                sap.pLive[i] = layout.pBase[i]; sap.pStart[i] = layout.pBase[i]; sap.pStep[i] = 0f; sap.pTarget[i] = layout.pBase[i];
            }
            sap.tailBudgetSamples = (long)(layout.tailSeconds * sampleRate);
            sap.hangoverSamples = ZoundDspConstants.HANGOVER_MS * sampleRate / 1000;
            ZoundEffects.ResetChain(sapLayout, sap.arena, sampleRate);
        }

        /// <summary>Arms the repeat schedule (call after Prepare, before Publish).</summary>
        internal void SetRepeat(in RepeatPlan plan) {
            sap.repeat = plan;
            if (!plan.enabled) return;
            sap.repeatsTotal = plan.count;
            sap.repeatsDone = 1;
            sap.repeatsPending = plan.count == int.MaxValue ? int.MaxValue : plan.count - 1;
            sap.lastRepeatEndSample = plan.nominalLengthSamples;
            sap.nextRepeatSample = plan.spaceFromEnd ? sap.lastRepeatEndSample + plan.intervalSamples : plan.intervalSamples;
            protectedFromSteal = sap.repeatsPending > 0;
            SapVoiceRender.ProjectTrainEnd(ref sap);
        }

        /// <summary>Prepares a Free group node: no source stage, children sum into its buffers.</summary>
        internal void PrepareGroup(long tokenId, int busIndex, int parentGroup, int depth, ChainLayout layout, int sampleRate, float duration) {
            this.tokenId = tokenId;
            this.busIndex = busIndex;
            groupIndex = parentGroup;
            this.depth = depth;
            pcm = null;
            this.layout = layout;
            if (sapLayout.IsCreated) sapLayout.Dispose();
            sapLayout = SapChainLayout.Create(layout, Allocator.Persistent);
            sourceDuration = duration;
            clipRate = 1.0;
            basePitchTarget = 1f; sap.basePitchLive = 1f;
            outGainTarget = 1f; sap.outGainLive = 1f;
            pauseRequest = 0; killRequest = 0; releaseRequest = 0;
            sap.stopping = false; sap.released = false;
            sap.sourceExhausted = false;
            sap.elapsedSamples = 0; sap.samplesSinceSourceEnd = 0; sap.silentSamples = 0;
            sap.lastPeak = 0f;
            liveChildren = 0;
            protectedFromSteal = true;
            sap.repeatsPending = 0; sap.repeatsDone = 0; sap.repeatsTotal = 1;
            allocatedAtDsp = AudioSettings.dspTime;
            sap.rng = 2463534242u ^ (uint)tokenId;
            ResetOnsets();
            for (int i = 0; i < sap.slots.Length; i++) sap.slots[i] = default;
            { int clearLen = Mathf.Min(sap.arena.Length, Mathf.Max(layout.stateFloats, 1)); for (int ci = 0; ci < clearLen; ci++) sap.arena[ci] = 0f; }
            for (int i = 0; i < layout.paramCount; i++) {
                sap.pLive[i] = layout.pBase[i]; sap.pStart[i] = layout.pBase[i]; sap.pStep[i] = 0f; sap.pTarget[i] = layout.pBase[i];
            }
            sap.tailBudgetSamples = (long)(layout.tailSeconds * sampleRate);
            sap.hangoverSamples = ZoundDspConstants.HANGOVER_MS * sampleRate / 1000;
            ZoundEffects.ResetChain(sapLayout, sap.arena, sampleRate);
        }

        /// <summary>Main-thread bookkeeping: realtime when this node was first seen Tailing (0 = not tailing). Read by whichever main-thread sweep reclaims voices whose tail never finished.</summary>
        internal float tailingSeenAt;

        /// <summary>
        /// Main-thread emergency free for a node the audio thread stopped visiting (its bus no longer
        /// renders). Only called after a kill request went unanswered for seconds, so no callback is
        /// touching this node.
        /// </summary>
        internal void ForceFree() {
            pcm = null;
            Volatile.Write(ref state, (int)VoiceState.Free);
        }

        // ── onset tracking (audio thread): a "machine gun" is many onsets from ONE node in a short time,
        // whatever produced them (a delay feeding back, repeat slots, a gated modifier). An onset is a
        // block whose peak jumps ≥ 12 dB above the node's own decaying envelope. Read on the main thread by
        // whichever sweep watches for runaway retriggering. ──
        private const int ONSET_RING = 16;
        private readonly long[] onsetAt = new long[ONSET_RING];
        private int onsetHead;
        private float onsetPrevPeak, onsetLastPeak;
        private bool onsetArmed = true;
        internal int onsetTotal;
        internal long onsetReportedForToken;

        // An onset = a block whose peak at least doubles the previous block's, once the level has fallen
        // to half of the last onset's peak (hysteresis, so one hit's own ripple is not several). A
        // decaying echo train (each echo quieter than the last) still counts every echo, because between
        // echoes the level drops. A steady tone or a single hit counts once.
        private void TrackOnsets(float peak, int frames, int sampleRate) {
            if (peak < onsetLastPeak * 0.5f) onsetArmed = true;
            if (onsetArmed && peak > 0.005f && peak > onsetPrevPeak * 2f) {
                onsetAt[onsetHead] = sap.elapsedSamples;
                onsetHead = (onsetHead + 1) % ONSET_RING;
                onsetTotal++;
                onsetLastPeak = peak;
                onsetArmed = false;
            }
            onsetPrevPeak = peak;
        }

        /// <summary>Main thread: onsets inside the last <paramref name="windowSamples"/> of this node's own clock.</summary>
        internal int OnsetsInLast(long windowSamples) {
            long now = Volatile.Read(ref sap.elapsedSamples);
            int c = 0;
            for (int i = 0; i < ONSET_RING; i++) if (onsetAt[i] > 0 && now - onsetAt[i] <= windowSamples) c++;
            return c;
        }

        private void ResetOnsets() {
            for (int i = 0; i < ONSET_RING; i++) onsetAt[i] = 0;
            onsetHead = 0; onsetPrevPeak = 0f; onsetLastPeak = 0f; onsetArmed = true; onsetTotal = 0; onsetReportedForToken = 0;
        }

        /// <summary>Audio-thread emergency stop after a render fault: silence this block, report Audio End, free the slot.</summary>
        internal void FaultFree(ZoundDspEventRing events, long dspSample) {
            for (int ci = 0; ci < sap.bufL.Length; ci++) sap.bufL[ci] = 0f;
            for (int ci = 0; ci < sap.bufR.Length; ci++) sap.bufR[ci] = 0f;
            sap.lastPeak = 0f;
            Finish(events, dspSample);
        }

        /// <summary>Main-thread diagnostic snapshot of the completion state (values may be a block stale).</summary>
        internal string DebugCompletionState() {
            return "state=" + State + " bus=" + busIndex + " parent=" + groupIndex + " released=" + sap.released + " stopping=" + sap.stopping
                + " sourceExhausted=" + sap.sourceExhausted + " elapsed=" + sap.elapsedSamples + " sinceSourceEnd=" + sap.samplesSinceSourceEnd
                + " tailBudget=" + sap.tailBudgetSamples + " silent=" + sap.silentSamples + "/" + sap.hangoverSamples + " pause=" + pauseRequest
                + " release=" + releaseRequest + " kill=" + killRequest + " liveChildren=" + liveChildren + " lastPeak=" + sap.lastPeak.ToString("F4");
        }

        /// <summary>Seeds a modifier's per-voice state (trigger-time values resolved on the main thread).</summary>
        internal void SeedModifierState(int modifierIndex, int slot, float value) {
            sap.arena[layout.modStateOffset[modifierIndex] + slot] = value;
        }

        internal void Publish() {
            Volatile.Write(ref state, (int)VoiceState.Active);
        }

        /// <summary>Main thread: asks this voice to stop hard (declick, flush) at the next render.</summary>
        internal void RequestKill() {
            Volatile.Write(ref killRequest, 1);
        }

        /// <summary>Main thread: asks this voice to stop feeding new source material and let its tail ring out.</summary>
        internal void RequestRelease() {
            Volatile.Write(ref releaseRequest, 1);
        }

        /// <summary>
        /// Main thread: pushes one live parameter edit (a slider drag) into this voice only — the shared
        /// layout is never touched, since other voices may be playing the same chain. A parameter a
        /// modifier binding owns is refused: the next control block would just overwrite it anyway.
        /// </summary>
        internal void PushLiveParam(int flatIndex, float value) {
            if (flatIndex < 0 || flatIndex >= layout.paramCount) return;
            for (int r = 0; r < layout.rampedCount; r++) if (layout.ramped[r] == flatIndex) return;
            float clamped = value < layout.pMin[flatIndex] ? layout.pMin[flatIndex] : value > layout.pMax[flatIndex] ? layout.pMax[flatIndex] : value;
            sap.pLive[flatIndex] = clamped;
            sap.pStart[flatIndex] = clamped;
            sap.pStep[flatIndex] = 0f;
        }

        // ───────────────────────────── audio thread ─────────────────────────────

        /// <summary>
        /// Renders this voice's output for one callback into bufL/bufR (frames valid). Returns false when
        /// the voice produced nothing (paused). Frees itself and reports through the ring when finished.
        ///
        /// Thin wrapper: the actual per-block render is <see cref="SapVoiceRender.Render"/>, a static
        /// function over <see cref="SapVoiceState"/> shared with the future value-type generator. What
        /// stays here is exactly what cannot move — the cross-thread request flags and targets (read once,
        /// their VALUES passed in, per the port plan) and the two things that touch managed objects: the
        /// onset-tracking bookkeeping (a plain array, not native) and pushing the AudioEnd event once the
        /// static call reports the voice finished.
        /// </summary>
        internal bool Render(int frames, int sampleRate, ZoundDspEventRing events, long dspSample) {
            if (Volatile.Read(ref pauseRequest) != 0) return false;

            bool killRequested = Volatile.Read(ref killRequest) != 0;
            bool releaseRequested = Volatile.Read(ref releaseRequest) != 0;
            float basePitchTargetNow = Volatile.Read(ref basePitchTarget);
            float outGainTargetRaw = Volatile.Read(ref outGainTarget);

            bool voiceFinished = SapVoiceRender.Render(ref sap, in sapLayout, frames, sampleRate,
                pcm, isGroup, clipRate, sourceDuration, liveChildren,
                killRequested, releaseRequested, basePitchTargetNow, outGainTargetRaw,
                ref state, ref protectedFromSteal);

            TrackOnsets(sap.lastPeak, frames, sampleRate);

            if (voiceFinished) Finish(events, dspSample);
            return true;
        }

        private void Finish(ZoundDspEventRing events, long dspSample) {
            events.TryPush(new ZoundDspEvent { type = ZoundDspEventType.AudioEnd, nodeId = isGroup ? index + ZoundDspConstants.MAX_VOICES : index, tokenId = tokenId, dspSample = dspSample });
            pcm = null;
            Volatile.Write(ref state, (int)VoiceState.Free);
        }
    }

}

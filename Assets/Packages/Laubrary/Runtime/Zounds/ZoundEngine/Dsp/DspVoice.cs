using System.Threading;
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
    /// One playing source with its chain, modifier stack and output routing. Preallocated by the graph
    /// and reused by index; the main thread fills a Free voice and publishes it by writing its state,
    /// the audio thread renders it and frees it. Nothing in Render allocates.
    /// </summary>
    public sealed class DspVoice {

        public readonly int index;
        public readonly bool heavyTier;
        /// <summary>A group node: children sum into bufL/bufR instead of a source stage reading PCM.</summary>
        public readonly bool isGroup;
        public readonly float[] arena;
        public readonly float[] bufL = new float[ZoundDspConstants.MAX_DSP_BUFFER];
        public readonly float[] bufR = new float[ZoundDspConstants.MAX_DSP_BUFFER];
        public readonly SourceSlot[] slots = new SourceSlot[ZoundDspConstants.MAX_SOURCE_SLOTS];

        // Flat parameter block: live value, block-start value and per-sample step.
        public readonly float[] pLive = new float[ChainLayout.MAX_PARAMS];
        public readonly float[] pStart = new float[ChainLayout.MAX_PARAMS];
        public readonly float[] pStep = new float[ChainLayout.MAX_PARAMS];
        private readonly float[] pTarget = new float[ChainLayout.MAX_PARAMS];

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

        // ── audio-thread state ──
        private float basePitchLive = 1f;
        private float outGainLive;
        private bool sourceExhausted;
        private long elapsedSamples;
        private long samplesSinceSourceEnd;
        private int silentSamples;
        private long tailBudgetSamples;
        private int hangoverSamples;
        internal float lastPeak;
        private bool stopping;
        private bool released;
        private uint rng = 2463534242u;
        // Modifier scratch
        private readonly float[] modValue = new float[ZoundDspConstants.MAX_MODIFIERS];

        // Repeater: the schedule lives on the voice so Release can cancel it and the Monitor can show 3/8.
        internal RepeatPlan repeat;
        internal int repeatsPending;
        internal int repeatsDone;
        internal int repeatsTotal;
        private long nextRepeatSample;
        private long lastRepeatEndSample;
        /// <summary>Output sample (voice time) at which the last armed repeat ends; the main thread grows the duration from it.</summary>
        internal long trainEndSample;
        internal int slotsStolen;

        public DspVoice(int index, bool heavy, bool isGroup = false) {
            this.index = index;
            heavyTier = heavy;
            this.isGroup = isGroup;
            arena = new float[heavy ? ZoundDspConstants.HEAVY_ARENA_FLOATS : ZoundDspConstants.LIGHT_ARENA_FLOATS];
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
            this.sourceDuration = sourceDuration;
            clipRate = (double)pcm.frequency / sampleRate;
            basePitchTarget = basePitch; basePitchLive = basePitch;
            outGainTarget = outGain; outGainLive = outGain;
            pauseRequest = 0; killRequest = 0; releaseRequest = 0;
            stopping = false; released = false;
            sourceExhausted = false;
            elapsedSamples = 0; samplesSinceSourceEnd = 0; silentSamples = 0;
            lastPeak = 0f;
            protectedFromSteal = false;
            repeat = default; repeatsPending = 0; repeatsDone = 1; repeatsTotal = 1; nextRepeatSample = 0; lastRepeatEndSample = 0; trainEndSample = 0; slotsStolen = 0;
            allocatedAtDsp = AudioSettings.dspTime;
            rng = 2463534242u ^ (uint)tokenId;
            ResetOnsets();

            for (int i = 0; i < slots.Length; i++) slots[i] = default;
            slots[0] = new SourceSlot { active = true, cursor = startFrame, startFrame = startFrame, endFrame = endFrame, gain = 1f, pitchMul = 1f, loop = loop };

            System.Array.Clear(arena, 0, Mathf.Min(arena.Length, Mathf.Max(layout.stateFloats, 1)));
            for (int i = 0; i < layout.paramCount; i++) {
                pLive[i] = layout.pBase[i]; pStart[i] = layout.pBase[i]; pStep[i] = 0f; pTarget[i] = layout.pBase[i];
            }
            tailBudgetSamples = (long)(layout.tailSeconds * sampleRate);
            hangoverSamples = ZoundDspConstants.HANGOVER_MS * sampleRate / 1000;
            ZoundEffects.ResetChain(layout, arena, sampleRate);
        }

        /// <summary>Arms the repeat schedule (call after Prepare, before Publish).</summary>
        internal void SetRepeat(in RepeatPlan plan) {
            repeat = plan;
            if (!plan.enabled) return;
            repeatsTotal = plan.count;
            repeatsDone = 1;
            repeatsPending = plan.count == int.MaxValue ? int.MaxValue : plan.count - 1;
            lastRepeatEndSample = plan.nominalLengthSamples;
            nextRepeatSample = plan.spaceFromEnd ? lastRepeatEndSample + plan.intervalSamples : plan.intervalSamples;
            protectedFromSteal = repeatsPending > 0;
            ProjectTrainEnd();
        }

        // Where the train ends if every repeat still pending plays at the nominal length; the main thread
        // grows the token's duration from this (never shrinks it), so Zound End waits for the last repeat.
        private void ProjectTrainEnd() {
            long end = lastRepeatEndSample;
            if (repeatsPending > 0 && repeatsPending != int.MaxValue) {
                long nominal = repeat.nominalLengthSamples;
                if (repeat.spaceFromEnd) end = lastRepeatEndSample + repeatsPending * (repeat.intervalSamples + nominal);
                else end = nextRepeatSample + (repeatsPending - 1) * repeat.intervalSamples + nominal;
                if (end < lastRepeatEndSample) end = lastRepeatEndSample;
            }
            Volatile.Write(ref trainEndSample, end);
        }

        /// <summary>Prepares a Free group node: no source stage, children sum into its buffers.</summary>
        internal void PrepareGroup(long tokenId, int busIndex, int parentGroup, int depth, ChainLayout layout, int sampleRate, float duration) {
            this.tokenId = tokenId;
            this.busIndex = busIndex;
            groupIndex = parentGroup;
            this.depth = depth;
            pcm = null;
            this.layout = layout;
            sourceDuration = duration;
            clipRate = 1.0;
            basePitchTarget = 1f; basePitchLive = 1f;
            outGainTarget = 1f; outGainLive = 1f;
            pauseRequest = 0; killRequest = 0; releaseRequest = 0;
            stopping = false; released = false;
            sourceExhausted = false;
            elapsedSamples = 0; samplesSinceSourceEnd = 0; silentSamples = 0;
            lastPeak = 0f;
            liveChildren = 0;
            protectedFromSteal = true;
            repeatsPending = 0; repeatsDone = 0; repeatsTotal = 1;
            allocatedAtDsp = AudioSettings.dspTime;
            rng = 2463534242u ^ (uint)tokenId;
            ResetOnsets();
            for (int i = 0; i < slots.Length; i++) slots[i] = default;
            System.Array.Clear(arena, 0, Mathf.Min(arena.Length, Mathf.Max(layout.stateFloats, 1)));
            for (int i = 0; i < layout.paramCount; i++) {
                pLive[i] = layout.pBase[i]; pStart[i] = layout.pBase[i]; pStep[i] = 0f; pTarget[i] = layout.pBase[i];
            }
            tailBudgetSamples = (long)(layout.tailSeconds * sampleRate);
            hangoverSamples = ZoundDspConstants.HANGOVER_MS * sampleRate / 1000;
            ZoundEffects.ResetChain(layout, arena, sampleRate);
        }

        /// <summary>Main-thread bookkeeping: realtime when this node was first seen Tailing (0 = not tailing). See ZoundDspGraph.SweepStaleTails.</summary>
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
        // block whose peak jumps ≥ 12 dB above the node's own decaying envelope. Read by
        // ZoundDspGraph.SweepRapidOnsets on the main thread. ──
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
                onsetAt[onsetHead] = elapsedSamples;
                onsetHead = (onsetHead + 1) % ONSET_RING;
                onsetTotal++;
                onsetLastPeak = peak;
                onsetArmed = false;
            }
            onsetPrevPeak = peak;
        }

        /// <summary>Main thread: onsets inside the last <paramref name="windowSamples"/> of this node's own clock.</summary>
        internal int OnsetsInLast(long windowSamples) {
            long now = Volatile.Read(ref elapsedSamples);
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
            System.Array.Clear(bufL, 0, bufL.Length);
            System.Array.Clear(bufR, 0, bufR.Length);
            lastPeak = 0f;
            Finish(events, dspSample);
        }

        /// <summary>Main-thread diagnostic snapshot of the completion state (values may be a block stale).</summary>
        internal string DebugCompletionState() {
            return "state=" + State + " bus=" + busIndex + " parent=" + groupIndex + " released=" + released + " stopping=" + stopping
                + " sourceExhausted=" + sourceExhausted + " elapsed=" + elapsedSamples + " sinceSourceEnd=" + samplesSinceSourceEnd
                + " tailBudget=" + tailBudgetSamples + " silent=" + silentSamples + "/" + hangoverSamples + " pause=" + pauseRequest
                + " release=" + releaseRequest + " kill=" + killRequest + " liveChildren=" + liveChildren + " lastPeak=" + lastPeak.ToString("F4");
        }

        /// <summary>Seeds a modifier's per-voice state (trigger-time values resolved on the main thread).</summary>
        internal void SeedModifierState(int modifierIndex, int slot, float value) {
            arena[layout.modStateOffset[modifierIndex] + slot] = value;
        }

        internal void Publish() {
            Volatile.Write(ref state, (int)VoiceState.Active);
        }

        // ───────────────────────────── audio thread ─────────────────────────────

        /// <summary>
        /// Renders this voice's output for one callback into bufL/bufR (frames valid). Returns false when
        /// the voice produced nothing (paused). Frees itself and reports through the ring when finished.
        /// </summary>
        internal bool Render(int frames, int sampleRate, ZoundDspEventRing events, long dspSample) {
            if (Volatile.Read(ref pauseRequest) != 0) return false;

            if (Volatile.Read(ref killRequest) != 0 && !stopping) {
                stopping = true;
                Volatile.Write(ref state, (int)VoiceState.Stopping);
            }
            if (Volatile.Read(ref releaseRequest) != 0 && !released) {
                released = true;
                Release(sampleRate);
            }

            var L = layout;
            float invN;
            int off = 0;
            float basePitchTargetNow = Volatile.Read(ref basePitchTarget);
            float outGainTargetNow = stopping ? 0f : Volatile.Read(ref outGainTarget);
            var ctx = new VoiceContext { sampleRate = sampleRate, sourceDuration = sourceDuration, sourcePeak = pcm != null ? pcm.peak : 1f };
            if (isGroup && released && liveChildren == 0 && !sourceExhausted) { sourceExhausted = true; samplesSinceSourceEnd = 0; silentSamples = 0; }

            while (off < frames) {
                int n = frames - off;
                if (n > ZoundDspConstants.CONTROL_BLOCK) n = ZoundDspConstants.CONTROL_BLOCK;
                invN = 1f / n;

                // ── control block: targets, clamps, per-sample slopes ──
                float basePitchStart = basePitchLive;
                float basePitchStep = (basePitchTargetNow - basePitchLive) * invN;
                float outGainStart = outGainLive;
                float outGainStep = (outGainTargetNow - outGainLive) * invN;
                ctx.elapsedSeconds = (float)elapsedSamples / sampleRate;
                ctx.sourceExhausted = sourceExhausted;

                if (L.bindCount > 0) {
                    for (int r = 0; r < L.rampedCount; r++) pTarget[L.ramped[r]] = L.pBase[L.ramped[r]];
                    // Targets are evaluated for the END of this block: the per-sample ramp then lands on the
                    // right value exactly when the block ends, so the reconstruction is a true piecewise-linear
                    // interpolation of the modulator rather than one lagging by a block.
                    EvaluateModifiers(L, sampleRate, n, ctx.elapsedSeconds + (float)n / sampleRate, n);
                    for (int b = 0; b < L.bindCount; b++) {
                        int t = L.bindTarget[b];
                        float m = modValue[L.bindModifier[b]] * L.bindDepth[b];
                        switch (L.bindOp[b]) {
                            case ModifierOp.Multiply: pTarget[t] *= m; break;
                            case ModifierOp.Add: pTarget[t] += m; break;
                            default: pTarget[t] = m; break;
                        }
                    }
                    for (int r = 0; r < L.rampedCount; r++) {
                        int t = L.ramped[r];
                        float target = pTarget[t];
                        if (target < L.pMin[t]) target = L.pMin[t]; else if (target > L.pMax[t]) target = L.pMax[t];
                        pStart[t] = pLive[t];
                        pStep[t] = (target - pLive[t]) * invN;
                    }
                }

                // ── source stage (a group's input is the children's sum, already in bufL/bufR) ──
                if (!isGroup) {
                    if (repeatsPending > 0) ArmRepeats(n, basePitchStart);
                    System.Array.Clear(bufL, off, n);
                    System.Array.Clear(bufR, off, n);
                    ReadSource(off, n, basePitchStart, basePitchStep);
                }

                // ── chain ──
                if (L.nodeCount > 0) ZoundEffects.ProcessChain(L, arena, pStart, pStep, bufL, bufR, off, n, in ctx);

                // ── output gain ──
                float g = outGainStart;
                for (int i = 0; i < n; i++) {
                    bufL[off + i] *= g; bufR[off + i] *= g; g += outGainStep;
                }

                // ── advance live values ──
                basePitchLive = basePitchStart + basePitchStep * n;
                outGainLive = outGainStart + outGainStep * n;
                for (int r = 0; r < L.rampedCount; r++) { int t = L.ramped[r]; pLive[t] = pStart[t] + pStep[t] * n; }
                elapsedSamples += n;
                if (sourceExhausted) samplesSinceSourceEnd += n;
                off += n;
            }

            // ── completion ──
            float peak = 0f;
            for (int i = 0; i < frames; i++) {
                float a = bufL[i] < 0f ? -bufL[i] : bufL[i]; if (a > peak) peak = a;
                float b = bufR[i] < 0f ? -bufR[i] : bufR[i]; if (b > peak) peak = b;
            }
            lastPeak = peak;
            TrackOnsets(peak, frames, sampleRate);

            if (stopping) {
                Finish(events, dspSample);
                return true;
            }
            if (sourceExhausted) {
                if (Volatile.Read(ref state) == (int)VoiceState.Active) Volatile.Write(ref state, (int)VoiceState.Tailing);
                bool done = false;
                if (tailBudgetSamples <= 0) done = true;
                else {
                    if (peak < ZoundDspConstants.SILENCE_LINEAR) {
                        silentSamples += frames;
                        if (silentSamples >= hangoverSamples) done = true;
                    }
                    else silentSamples = 0;
                    if (samplesSinceSourceEnd >= tailBudgetSamples) done = true;
                }
                if (done) Finish(events, dspSample);
            }
            return true;
        }

        private void Finish(ZoundDspEventRing events, long dspSample) {
            events.TryPush(new ZoundDspEvent { type = ZoundDspEventType.AudioEnd, nodeId = isGroup ? index + ZoundDspConstants.MAX_VOICES : index, tokenId = tokenId, dspSample = dspSample });
            pcm = null;
            Volatile.Write(ref state, (int)VoiceState.Free);
        }

        // Arms every repeat whose start falls inside the next n samples, sample-accurately, into a free slot
        // (the oldest slot is taken over when all four are busy).
        private void ArmRepeats(int n, float basePitch) {
            while (repeatsPending > 0 && nextRepeatSample < elapsedSamples + n) {
                if (repeat.durationLimitSamples > 0 && nextRepeatSample + repeat.nominalLengthSamples > repeat.durationLimitSamples) { repeatsPending = 0; break; }
                int slot = -1;
                double oldest = double.MaxValue;
                for (int s = 0; s < slots.Length; s++) {
                    if (!slots[s].active) { slot = s; break; }
                    if (slots[s].cursor < oldest) { oldest = slots[s].cursor; }
                }
                if (slot < 0) {
                    // Steal the slot that has been playing longest (furthest along its region).
                    double furthest = -1;
                    for (int s = 0; s < slots.Length; s++) {
                        double progress = slots[s].cursor - slots[s].startFrame;
                        if (progress > furthest) { furthest = progress; slot = s; }
                    }
                    slotsStolen++;
                }
                var first = slots[0];
                float pitchMul = 1f, gainMul = 1f;
                if (repeat.retrigger) {
                    pitchMul = repeat.pitchMulMin + (repeat.pitchMulMax - repeat.pitchMulMin) * NextRandom01();
                    gainMul = repeat.gainMulMin + (repeat.gainMulMax - repeat.gainMulMin) * NextRandom01();
                }
                slots[slot] = new SourceSlot {
                    active = true, startAt = (int)(nextRepeatSample - elapsedSamples), cursor = first.startFrame,
                    startFrame = first.startFrame, endFrame = first.endFrame, gain = gainMul, pitchMul = pitchMul, loop = false
                };
                repeatsDone++;
                if (repeatsPending != int.MaxValue) repeatsPending--;
                double rate = clipRate * Mathf.Max(basePitch, 0.01f) * pitchMul;
                long lengthSamples = (long)((first.endFrame - first.startFrame) / rate);
                lastRepeatEndSample = nextRepeatSample + lengthSamples;
                nextRepeatSample = repeat.spaceFromEnd ? lastRepeatEndSample + repeat.intervalSamples : nextRepeatSample + repeat.intervalSamples;
                if (repeatsPending == 0) protectedFromSteal = false;
                ProjectTrainEnd();
            }
        }

        private void Release(int sampleRate) {
            repeatsPending = 0;
            protectedFromSteal = false;
            if (isGroup) return;
            int graceFrames = sampleRate / 10;
            int fadeSamples = (int)(ZoundDspConstants.STOP_FADE_MS * 0.001f * sampleRate);
            for (int s = 0; s < slots.Length; s++) {
                if (!slots[s].active) continue;
                slots[s].loop = false;
                double remaining = (slots[s].endFrame - slots[s].cursor) / (clipRate * Mathf.Max(basePitchLive * slots[s].pitchMul, 0.01f));
                if (remaining > graceFrames && slots[s].fadeSamplesLeft == 0) {
                    slots[s].fadeSamplesLeft = fadeSamples;
                    slots[s].fadeSamplesTotal = fadeSamples;
                }
            }
        }

        // ── source stage: Catmull-Rom cubic Hermite, per-sample pitch ──

        private void ReadSource(int off, int n, float basePitchStart, float basePitchStep) {
            var samples = pcm.samples;
            int ch = pcm.channels;
            bool anyActive = false;
            float srcGainStart = pStart[SourceStageParam.Gain], srcGainStep = pStep[SourceStageParam.Gain];
            float pitchParamStart = pStart[SourceStageParam.Pitch], pitchParamStep = pStep[SourceStageParam.Pitch];
            double rateBase = clipRate;

            for (int s = 0; s < slots.Length; s++) {
                if (!slots[s].active) continue;
                anyActive = true;
                int i0Start = slots[s].startAt;
                if (i0Start >= n) { slots[s].startAt -= n; continue; }
                slots[s].startAt = 0;
                double cur = slots[s].cursor;
                double start = slots[s].startFrame;
                double end = slots[s].endFrame;
                int lastFrame = (int)end - 1;
                int firstFrame = (int)start;
                float slotGain = slots[s].gain;
                float pitchMul = slots[s].pitchMul;
                int fadeLeft = slots[s].fadeSamplesLeft;
                int fadeTotal = slots[s].fadeSamplesTotal;
                float basePitch = basePitchStart + basePitchStep * i0Start;
                float pitchParam = pitchParamStart + pitchParamStep * i0Start;
                float srcGain = srcGainStart + srcGainStep * i0Start;

                for (int i = i0Start; i < n; i++) {
                    if (cur >= end) {
                        if (slots[s].loop && end > start + 1) { cur = start + (cur - end); }
                        else { slots[s].active = false; break; }
                    }
                    int i1 = (int)cur;
                    float t = (float)(cur - i1);
                    int i0 = i1 - 1; if (i0 < firstFrame) i0 = firstFrame;
                    int i2 = i1 + 1; if (i2 > lastFrame) i2 = lastFrame;
                    int i3 = i1 + 2; if (i3 > lastFrame) i3 = lastFrame;
                    float gain = slotGain * srcGain;
                    if (fadeLeft > 0) {
                        gain *= (float)fadeLeft / fadeTotal;
                        fadeLeft--;
                        if (fadeLeft == 0) { slots[s].active = false; }
                    }
                    float t2 = t * t, t3 = t2 * t;
                    if (ch == 1) {
                        float p0 = samples[i0], p1 = samples[i1], p2 = samples[i2], p3 = samples[i3];
                        float v = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                        v *= gain;
                        bufL[off + i] += v; bufR[off + i] += v;
                    }
                    else {
                        int b0 = i0 * ch, b1 = i1 * ch, b2 = i2 * ch, b3 = i3 * ch;
                        float p0 = samples[b0], p1 = samples[b1], p2 = samples[b2], p3 = samples[b3];
                        bufL[off + i] += gain * 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                        p0 = samples[b0 + 1]; p1 = samples[b1 + 1]; p2 = samples[b2 + 1]; p3 = samples[b3 + 1];
                        bufR[off + i] += gain * 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    }
                    if (!slots[s].active) break;
                    cur += rateBase * basePitch * pitchParam * pitchMul;
                    basePitch += basePitchStep;
                    pitchParam += pitchParamStep;
                    srcGain += srcGainStep;
                }
                slots[s].cursor = cur;
                slots[s].fadeSamplesLeft = fadeLeft;
            }

            if (!anyActive || !AnySlotActive()) {
                if (repeatsPending > 0) return; // Repeater: the next repeat is armed by the schedule (Phase 6)
                if (!sourceExhausted) { sourceExhausted = true; samplesSinceSourceEnd = 0; silentSamples = 0; }
            }
        }

        /// <summary>
        /// Normalized position (0..1) of the most recently armed active read slot over its region,
        /// predicted <paramref name="aheadSamples"/> output samples from now at the current rate.
        /// </summary>
        private float SourceProgress(int aheadSamples) {
            for (int s = slots.Length - 1; s >= 0; s--) {
                if (!slots[s].active) continue;
                double len = slots[s].endFrame - slots[s].startFrame;
                if (len <= 0) return 1f;
                double ahead = aheadSamples * clipRate * basePitchLive * pLive[SourceStageParam.Pitch] * slots[s].pitchMul;
                float p = (float)((slots[s].cursor + ahead - slots[s].startFrame) / len);
                return p < 0f ? 0f : (p > 1f ? 1f : p);
            }
            return 1f;
        }

        private bool AnySlotActive() {
            for (int s = 0; s < slots.Length; s++) if (slots[s].active) return true;
            return false;
        }

        // ── modifiers (control rate) ──

        private float NextRandom01() {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }

        private void EvaluateModifiers(ChainLayout L, int sampleRate, int blockSamples, float elapsed, int lookaheadSamples) {
            float blockSeconds = (float)blockSamples / sampleRate;
            for (int m = 0; m < L.modCount; m++) {
                int so = L.modStateOffset[m];
                var mp = L.modParams[m];
                switch (L.modType[m]) {
                    case ZoundModifierType.Envelope: {
                        float total = sourceDuration + L.modExtraSeconds[m];
                        float tn;
                        bool sourceBase = !isGroup && !repeat.enabled && (mp.Length < 2 || mp[1] < 0.5f);
                        if (sourceBase && !sourceExhausted && total > 0f) {
                            // Follow the waveform: normalized position of the read cursor over the trimmed region
                            // at the end of this block, scaled so the extra-time band still sits past the source end.
                            tn = SourceProgress(lookaheadSamples) * (sourceDuration / total);
                        }
                        else {
                            tn = total > 0f ? elapsed / total : 1f;
                        }
                        if (tn > 1f) tn = 1f;
                        int seg = (int)arena[so];
                        modValue[m] = ChainLayout.EvaluateEnvelope(L.modCurve[m], tn, ref seg);
                        arena[so] = seg;
                        break;
                    }
                    case ZoundModifierType.Lfo: {
                        float amount = mp[0], rate = mp[1];
                        int shape = (int)mp[2];
                        int mode = (int)mp[4];
                        float offset = mp.Length > 6 ? mp[6] : 0f;
                        float ramp = 1f;
                        if (L.modCurve[m] != null && L.modCurve[m].Length > 0) {
                            float total = sourceDuration;
                            float tn = total > 0f ? elapsed / total : 1f; if (tn > 1f) tn = 1f;
                            int seg = (int)arena[so + 6];
                            ramp = ChainLayout.EvaluateEnvelope(L.modCurve[m], tn, ref seg);
                            arena[so + 6] = seg;
                        }
                        if (mode == (int)LfoMode.Oscillate) {
                            // Phase is advanced first so the value is the one at the end of the block (see Render).
                            float phase = arena[so] + rate * blockSeconds;
                            if (phase >= 1f) phase -= (int)phase;
                            float w;
                            switch (shape) {
                                case (int)LfoShape.Triangle: w = phase < 0.5f ? (phase * 4f - 1f) : (3f - phase * 4f); break;
                                case (int)LfoShape.Saw: w = phase * 2f - 1f; break;
                                case (int)LfoShape.Square: w = phase < 0.5f ? 1f : -1f; break;
                                default: w = Mathf.Sin(phase * 6.2831853f); break;
                            }
                            modValue[m] = offset + amount * w * ramp;
                            arena[so] = phase;
                        }
                        else {
                            // Random: glide from current toward a target; pick a new target on a timer.
                            float every = mp.Length > 5 ? mp[5] : 0.5f;
                            if (arena[so + 7] == 0f) {
                                arena[so + 7] = 1f;
                                arena[so + 1] = NextRandom01() * 2f - 1f;
                                arena[so + 2] = NextRandom01() * 2f - 1f;
                                arena[so + 4] = arena[so + 1];
                                arena[so + 3] = every;
                                arena[so + 5] = 0f;
                            }
                            arena[so + 3] -= blockSeconds;
                            if (arena[so + 3] <= 0f) {
                                arena[so + 3] += every;
                                arena[so + 4] = arena[so + 1];
                                arena[so + 2] = NextRandom01() * 2f - 1f;
                                arena[so + 5] = 0f;
                            }
                            if (rate <= 0f) arena[so + 1] = arena[so + 2];
                            else {
                                arena[so + 5] += rate * blockSeconds;
                                float gt = arena[so + 5] > 1f ? 1f : arena[so + 5];
                                arena[so + 1] = arena[so + 4] + (arena[so + 2] - arena[so + 4]) * gt;
                            }
                            modValue[m] = offset + amount * arena[so + 1] * ramp;
                        }
                        break;
                    }
                    case ZoundModifierType.Random:
                        modValue[m] = arena[so];
                        break;
                    case ZoundModifierType.Step: {
                        var steps = L.modSteps[m];
                        int timing = (int)mp[0];
                        if (timing == (int)StepTiming.PerInterval) {
                            float interval = mp[1] * 0.001f;
                            arena[so + 1] += blockSeconds;
                            if (arena[so + 1] >= interval) {
                                arena[so + 1] -= interval;
                                AdvanceStep(steps, (int)mp[2] == (int)StepOrder.RoundRobinNoRepeat, so);
                            }
                        }
                        int idx = (int)arena[so];
                        if (idx < 0 || idx >= steps.Length) idx = 0;
                        modValue[m] = steps[idx];
                        break;
                    }
                    default: modValue[m] = 0f; break;
                }
            }
        }

        // Round-robin over the step list with a used-mask in state[2] (up to 24 steps). When every step has
        // been used the mask clears and the first pick of the new cycle excludes the last-played index, so
        // the seam can never repeat and every step still plays once per cycle.
        private void AdvanceStep(float[] steps, bool roundRobin, int so) {
            int count = steps.Length;
            if (count <= 1) { arena[so] = 0f; return; }
            int last = (int)arena[so];
            if (!roundRobin) { arena[so] = (last + 1) % count; return; }
            int used = (int)arena[so + 2];
            int all = count >= 24 ? 0xFFFFFF : (1 << count) - 1;
            used |= 1 << last;
            int exclude = 0;
            if ((used & all) == all) { used = 0; exclude = 1 << last; }
            int free = 0;
            for (int i = 0; i < count && i < 24; i++) if (((used | exclude) & (1 << i)) == 0) free++;
            int pick = (int)(NextRandom01() * free);
            for (int i = 0; i < count && i < 24; i++) {
                if (((used | exclude) & (1 << i)) != 0) continue;
                if (pick == 0) { arena[so] = i; arena[so + 2] = used | (1 << i); return; }
                pick--;
            }
            arena[so] = (last + 1) % count;
        }
    }

}

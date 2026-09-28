using Unity.Collections;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// What state transition a <see cref="SapVoiceRender.Render"/> call wants published. Render itself
    /// cannot perform the publication (the write needs the volatile-write helper, which Burst cannot call),
    /// so it reports the transition here and the caller — which still owns the plain <c>state</c> field —
    /// performs the actual write, in the same place and under the same condition the render used to.
    /// </summary>
    internal enum VoiceStateTransition {
        /// <summary>No transition to publish this block.</summary>
        None,
        /// <summary>Publish VoiceState.Stopping unconditionally (mirrors the old unconditional write).</summary>
        Stopping,
        /// <summary>Publish VoiceState.Tailing, but only if the currently-published state is still Active
        /// (mirrors the old read-then-conditional-write).</summary>
        BecameTailing,
    }

    /// <summary>
    /// The per-block voice render, lifted out of <see cref="DspVoice"/> into static functions over
    /// <see cref="SapVoiceState"/> so both the class and (later) a Burst-side generator can call the same
    /// code. This is a relocation, not a rewrite: every body below is the code that used to live as a
    /// private instance method on <see cref="DspVoice"/>, with field accesses rewritten to go through the
    /// <c>sap</c> parameter (or, for the handful of fields that stay on the class, through the parameters
    /// listed for exactly that purpose).
    ///
    /// What is deliberately NOT here: pushing the AudioEnd event into the managed event ring, and freeing
    /// the voice (<c>pcm = null</c>, publishing <c>VoiceState.Free</c>). Both touch managed objects the
    /// class still owns, so <see cref="Render"/> only returns a <c>finished</c> flag; <see cref="DspVoice.Render"/>
    /// does that small piece of work itself, immediately after this call returns, in the same spot in the
    /// control flow the old inline code did it.
    /// </summary>
    internal static class SapVoiceRender {

        /// <summary>
        /// Applies one live parameter change to a playing voice — a slider being dragged, or gameplay driving
        /// a filter. Works entirely on native data, so it can be applied on whichever thread the change
        /// arrives on, including from inside compiled code.
        ///
        /// Two rules, both carried over unchanged from the long-lived voice's version of this:
        ///
        /// A parameter that a modifier is driving is REFUSED rather than written. Accepting it would look like
        /// it worked for a fraction of a second and then be overwritten by the modifier at the next control
        /// block, which is worse than visibly doing nothing.
        ///
        /// The value is clamped to the parameter's own declared range before it lands anywhere, so a caller
        /// cannot push an effect into a state the layout never sized for.
        ///
        /// It writes the live value, the block-start value and a zero slope together, so the change takes
        /// effect immediately rather than being smoothed towards from wherever the last block left off.
        /// Anything wanting a smooth change should send a stream of values rather than one jump.
        /// </summary>
        public static void ApplyLiveParam(ref SapVoiceState sap, in SapChainLayout L, int flatIndex, float value) {
            if (flatIndex < 0 || flatIndex >= L.paramCount) return;
            for (int r = 0; r < L.rampedCount; r++) if (L.ramped[r] == flatIndex) return;

            float min = L.pMin[flatIndex];
            float max = L.pMax[flatIndex];
            float clamped = value < min ? min : value > max ? max : value;

            sap.pLive[flatIndex] = clamped;
            sap.pStart[flatIndex] = clamped;
            sap.pStep[flatIndex] = 0f;
        }

        /// <summary>
        /// Renders one callback's worth of frames into sap.bufL/sap.bufR. Mirrors the old
        /// <c>DspVoice.Render</c> body exactly, minus the pause short-circuit (the caller already handled
        /// it) and minus the Finish()/TrackOnsets() calls (the caller does those after this returns).
        /// Returns true when this block finished the voice (the caller must then push AudioEnd and free it).
        /// </summary>
        public static bool Render(ref SapVoiceState sap, in SapChainLayout L, int frames, int sampleRate,
                                   in SapPcm pcm, bool isGroup, double clipRate, float sourceDuration, int liveChildren,
                                   bool killRequested, bool releaseRequested,
                                   float basePitchTargetNow, float outGainTargetRaw,
                                   ref bool protectedFromSteal, out VoiceStateTransition transition,
                                   float baseSpeedTargetNow = 1f) {

            transition = VoiceStateTransition.None;

            if (killRequested && !sap.stopping) {
                sap.stopping = true;
                transition = VoiceStateTransition.Stopping;
            }
            if (releaseRequested && !sap.released) {
                sap.released = true;
                Release(ref sap, sampleRate, isGroup, ref protectedFromSteal, clipRate);
            }

            float invN;
            int off = 0;
            float outGainTargetNow = sap.stopping ? 0f : outGainTargetRaw;
            var ctx = new VoiceContext { sampleRate = sampleRate, sourceDuration = sourceDuration, sourcePeak = pcm.IsCreated ? pcm.peak : 1f };
            if (isGroup && sap.released && liveChildren == 0 && !sap.sourceExhausted) { sap.sourceExhausted = true; sap.samplesSinceSourceEnd = 0; sap.silentSamples = 0; }

            while (off < frames) {
                int n = frames - off;
                // Control blocks sit on a grid counted from the voice's own start, not from the start of this call, so the
                // moments at which modulators are evaluated -- and therefore the samples -- do not depend on how the host
                // happens to slice its calls (T-0479: a pitch curve rendered in blocks of 333 differed from blocks of 256).
                // The mixer's calls are whole multiples of the grid, so what it hears is exactly what it heard before.
                int phase = (int)(sap.elapsedSamples % ZoundDspConstants.CONTROL_BLOCK);
                int toGrid = ZoundDspConstants.CONTROL_BLOCK - phase;
                if (n > toGrid) n = toGrid;
                // Targets and slopes are worked out once per grid block, over the whole block, and a block that a call
                // boundary splits in two carries on with the same slopes -- evaluating again for the second piece would
                // put a second evaluation, and differently sloped ramps, wherever a call happened to end.
                bool gridStart = phase == 0;
                invN = 1f / ZoundDspConstants.CONTROL_BLOCK;

                // ── control block: targets, clamps, per-sample slopes ──
                float basePitchStart = sap.basePitchLive;
                float outGainStart = sap.outGainLive;
                float baseSpeedStart = sap.baseSpeedLive;
                if (gridStart) {
                    sap.ctlBasePitchStep = (basePitchTargetNow - sap.basePitchLive) * invN;
                    sap.ctlOutGainStep = (outGainTargetNow - sap.outGainLive) * invN;
                    sap.ctlBaseSpeedStep = (baseSpeedTargetNow - sap.baseSpeedLive) * invN;
                }
                float basePitchStep = sap.ctlBasePitchStep;
                float outGainStep = sap.ctlOutGainStep;
                float baseSpeedStep = sap.ctlBaseSpeedStep;
                ctx.elapsedSeconds = (float)sap.elapsedSamples / sampleRate;
                ctx.sourceExhausted = sap.sourceExhausted;

                if (L.bindCount > 0 && !gridStart) {
                    // The rest of a block a call boundary split: same slopes, from where the ramps have got to.
                    for (int r = 0; r < L.rampedCount; r++) { int t = L.ramped[r]; sap.pStart[t] = sap.pLive[t]; }
                }
                else if (L.bindCount > 0) {
                    for (int r = 0; r < L.rampedCount; r++) sap.pTarget[L.ramped[r]] = L.pBase[L.ramped[r]];
                    // Targets are evaluated for the END of this block: the per-sample ramp then lands on the
                    // right value exactly when the block ends, so the reconstruction is a true piecewise-linear
                    // interpolation of the modulator rather than one lagging by a block.
                    EvaluateModifiers(ref sap, L, sampleRate, ZoundDspConstants.CONTROL_BLOCK,
                                      ctx.elapsedSeconds + (float)ZoundDspConstants.CONTROL_BLOCK / sampleRate, ZoundDspConstants.CONTROL_BLOCK,
                                      isGroup, sourceDuration, clipRate);
                    // Each modulator moves its parameter along that parameter's OWN control, by a fraction of the control's
                    // travel, rather than by an amount in the parameter's units. That one change is what makes a depth mean
                    // the same thing on a cutoff measured in thousands of hertz and on a resonance measured from nought to
                    // one, makes a sweep of a frequency cover the same musical distance wherever it starts, and makes it
                    // impossible for a modulator to ask for a value outside the parameter's range — the position is clamped
                    // before it is turned back into a value, instead of a wild value being produced and then truncated.
                    //
                    // Several modulators may target the same parameter; each one moves it further from where the last left
                    // it, which is why this reads and writes the running target rather than the authored value.
                    for (int b = 0; b < L.bindCount; b++) {
                        int t = L.bindTarget[b];
                        sap.pTarget[t] = ModulationMath.Apply(L.bindCombine[b], sap.pTarget[t],
                                                              sap.modValue[L.bindModifier[b]], L.bindDepth[b],
                                                              L.pMin[t], L.pMax[t], L.pRatio[t]);
                    }
                    for (int r = 0; r < L.rampedCount; r++) {
                        int t = L.ramped[r];
                        float target = sap.pTarget[t];
                        if (target < L.pMin[t]) target = L.pMin[t]; else if (target > L.pMax[t]) target = L.pMax[t];
                        sap.pStart[t] = sap.pLive[t];
                        sap.pStep[t] = (target - sap.pLive[t]) * invN;
                    }
                }

                // ── source stage (a group's input is the children's sum, already in bufL/bufR) ──
                if (!isGroup) {
                    if (sap.repeatsPending > 0) ArmRepeats(ref sap, n, basePitchStart, clipRate, ref protectedFromSteal);
                    for (int ci = 0; ci < n; ci++) sap.bufL[off + ci] = 0f;
                    for (int ci = 0; ci < n; ci++) sap.bufR[off + ci] = 0f;
                    if (sap.stretch.enabled) ReadSourceStretched(ref sap, pcm, clipRate, off, n, basePitchStart, basePitchStep, baseSpeedStart, baseSpeedStep);
                    else if (sap.looping.enabled) ReadLoop(ref sap, pcm, clipRate, off, n, basePitchStart, basePitchStep);
                    else ReadSource(ref sap, pcm, clipRate, off, n, basePitchStart, basePitchStep);
                }

                // ── chain ──
                if (L.nodeCount > 0) ZoundEffects.ProcessChain(L, sap.arena, sap.pStart, sap.pStep, sap.bufL, sap.bufR, off, n, in ctx);

                // ── output gain ──
                float g = outGainStart;
                for (int i = 0; i < n; i++) {
                    sap.bufL[off + i] *= g; sap.bufR[off + i] *= g; g += outGainStep;
                }

                // ── advance live values ──
                sap.basePitchLive = basePitchStart + basePitchStep * n;
                sap.outGainLive = outGainStart + outGainStep * n;
                sap.baseSpeedLive = baseSpeedStart + baseSpeedStep * n;
                for (int r = 0; r < L.rampedCount; r++) { int t = L.ramped[r]; sap.pLive[t] = sap.pStart[t] + sap.pStep[t] * n; }
                sap.elapsedSamples += n;
                if (sap.sourceExhausted) sap.samplesSinceSourceEnd += n;
                off += n;
            }

            // ── completion ──
            float peak = 0f;
            for (int i = 0; i < frames; i++) {
                float a = sap.bufL[i] < 0f ? -sap.bufL[i] : sap.bufL[i]; if (a > peak) peak = a;
                float b = sap.bufR[i] < 0f ? -sap.bufR[i] : sap.bufR[i]; if (b > peak) peak = b;
            }
            sap.lastPeak = peak;

            if (sap.stopping) {
                return true;
            }
            if (sap.sourceExhausted) {
                transition = VoiceStateTransition.BecameTailing;
                bool done = false;
                if (sap.tailBudgetSamples <= 0) done = true;
                else {
                    if (peak < ZoundDspConstants.SILENCE_LINEAR) {
                        sap.silentSamples += frames;
                        if (sap.silentSamples >= sap.hangoverSamples) done = true;
                    }
                    else sap.silentSamples = 0;
                    if (sap.samplesSinceSourceEnd >= sap.tailBudgetSamples) done = true;
                }
                if (done) return true;
            }
            return false;
        }

        /// <summary>Records this voice's train-end projection. See the comment at the write below for why
        /// it is a plain write rather than Volatile.</summary>
        public static void ProjectTrainEnd(ref SapVoiceState sap) {
            long end = sap.lastRepeatEndSample;
            if (sap.repeatsPending > 0 && sap.repeatsPending != int.MaxValue) {
                long nominal = sap.repeat.nominalLengthSamples;
                if (sap.repeat.spaceFromEnd) end = sap.lastRepeatEndSample + sap.repeatsPending * (sap.repeat.intervalSamples + nominal);
                else end = sap.nextRepeatSample + (sap.repeatsPending - 1) * sap.repeat.intervalSamples + nominal;
                if (end < sap.lastRepeatEndSample) end = sap.lastRepeatEndSample;
            }
            // Plain write: the volatile-write helper is unavailable to Burst. This value is read by the
            // main thread through a volatile read (DspVoice.trainEndSample). Render currently has exactly
            // one caller on one thread, so there is no live race here today. The proper cross-thread
            // publication of this value — a real barrier or an explicit handshake — is owed to the step
            // that introduces the audio framework's own message and data channels; this plain write is
            // not a substitute for that and is not safe once a second thread is actually involved.
            sap.trainEndSample = end;
        }

        // Arms every repeat whose start falls inside the next n samples, sample-accurately, into a free slot
        // (the oldest slot is taken over when all four are busy).
        private static void ArmRepeats(ref SapVoiceState sap, int n, float basePitch, double clipRate, ref bool protectedFromSteal) {
            while (sap.repeatsPending > 0 && sap.nextRepeatSample < sap.elapsedSamples + n) {
                if (sap.repeat.durationLimitSamples > 0 && sap.nextRepeatSample + sap.repeat.nominalLengthSamples > sap.repeat.durationLimitSamples) { sap.repeatsPending = 0; break; }
                int slot = -1;
                double oldest = double.MaxValue;
                for (int s = 0; s < sap.slots.Length; s++) {
                    if (!sap.slots[s].active) { slot = s; break; }
                    if (sap.slots[s].cursor < oldest) { oldest = sap.slots[s].cursor; }
                }
                if (slot < 0) {
                    // Steal the slot that has been playing longest (furthest along its region).
                    double furthest = -1;
                    for (int s = 0; s < sap.slots.Length; s++) {
                        double progress = sap.slots[s].cursor - sap.slots[s].startFrame;
                        if (progress > furthest) { furthest = progress; slot = s; }
                    }
                    sap.slotsStolen++;
                }
                var first = sap.slots[0];
                float pitchMul = 1f, gainMul = 1f;
                if (sap.repeat.retrigger) {
                    pitchMul = sap.repeat.pitchMulMin + (sap.repeat.pitchMulMax - sap.repeat.pitchMulMin) * NextRandom01(ref sap);
                    gainMul = sap.repeat.gainMulMin + (sap.repeat.gainMulMax - sap.repeat.gainMulMin) * NextRandom01(ref sap);
                }
                sap.slots[slot] = new SourceSlot {
                    active = true, startAt = (int)(sap.nextRepeatSample - sap.elapsedSamples), cursor = first.startFrame,
                    startFrame = first.startFrame, endFrame = first.endFrame, gain = gainMul, pitchMul = pitchMul, loop = false
                };
                sap.repeatsDone++;
                if (sap.repeatsPending != int.MaxValue) sap.repeatsPending--;
                double rate = clipRate * Mathf.Max(basePitch, 0.01f) * pitchMul;
                // A repeat gets its own stretcher state, started at the top of the sound like the slot itself.
                if (sap.stretch.enabled) sap.stretch.ResetSlot(slot, first.startFrame, rate);
                long lengthSamples = (long)((first.endFrame - first.startFrame) / rate);
                sap.lastRepeatEndSample = sap.nextRepeatSample + lengthSamples;
                sap.nextRepeatSample = sap.repeat.spaceFromEnd ? sap.lastRepeatEndSample + sap.repeat.intervalSamples : sap.nextRepeatSample + sap.repeat.intervalSamples;
                if (sap.repeatsPending == 0) protectedFromSteal = false;
                ProjectTrainEnd(ref sap);
            }
        }

        private static void Release(ref SapVoiceState sap, int sampleRate, bool isGroup, ref bool protectedFromSteal, double clipRate) {
            sap.repeatsPending = 0;
            protectedFromSteal = false;
            if (isGroup) return;
            int graceFrames = sampleRate / 10;
            int fadeSamples = (int)(ZoundDspConstants.STOP_FADE_MS * 0.001f * sampleRate);
            // A native buffer's indexer returns a copy, so a slot is read into a local, mutated, and
            // written back. Same values in the same order as the previous direct element writes.
            for (int s = 0; s < sap.slots.Length; s++) {
                var sl = sap.slots[s];
                if (!sl.active) continue;
                sl.loop = false;
                double remaining = (sl.endFrame - sl.cursor) / (clipRate * Mathf.Max(sap.basePitchLive * sl.pitchMul, 0.01f));
                if (remaining > graceFrames && sl.fadeSamplesLeft == 0) {
                    sl.fadeSamplesLeft = fadeSamples;
                    sl.fadeSamplesTotal = fadeSamples;
                }
                sap.slots[s] = sl;
            }
        }

        // ── source stage: Catmull-Rom cubic Hermite, per-sample pitch ──

        private static void ReadSource(ref SapVoiceState sap, in SapPcm pcm, double clipRate, int off, int n, float basePitchStart, float basePitchStep) {
            var samples = pcm.samples;
            int ch = pcm.channels;
            bool anyActive = false;
            float srcGainStart = sap.pStart[SourceStageParam.Gain], srcGainStep = sap.pStep[SourceStageParam.Gain];
            float pitchParamStart = sap.pStart[SourceStageParam.Pitch], pitchParamStep = sap.pStep[SourceStageParam.Pitch];
            double rateBase = clipRate;

            // As in Release: a native buffer's indexer returns a copy, so the slot is held in a local for
            // the duration and written back once at the end. The early-out below writes back before it
            // continues, because it advances startAt.
            for (int s = 0; s < sap.slots.Length; s++) {
                var sl = sap.slots[s];
                if (!sl.active) continue;
                anyActive = true;
                int i0Start = sl.startAt;
                if (i0Start >= n) { sl.startAt -= n; sap.slots[s] = sl; continue; }
                sl.startAt = 0;
                double cur = sl.cursor;
                double start = sl.startFrame;
                double end = sl.endFrame;
                int lastFrame = (int)end - 1;
                int firstFrame = (int)start;
                float slotGain = sl.gain;
                float pitchMul = sl.pitchMul;
                int fadeLeft = sl.fadeSamplesLeft;
                int fadeTotal = sl.fadeSamplesTotal;
                float basePitch = basePitchStart + basePitchStep * i0Start;
                float pitchParam = pitchParamStart + pitchParamStep * i0Start;
                float srcGain = srcGainStart + srcGainStep * i0Start;

                for (int i = i0Start; i < n; i++) {
                    if (cur >= end) {
                        if (sl.loop && end > start + 1) { cur = start + (cur - end); }
                        else { sl.active = false; break; }
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
                        if (fadeLeft == 0) { sl.active = false; }
                    }
                    float t2 = t * t, t3 = t2 * t;
                    if (ch == 1) {
                        float p0 = samples[i0], p1 = samples[i1], p2 = samples[i2], p3 = samples[i3];
                        float v = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                        v *= gain;
                        sap.bufL[off + i] += v; sap.bufR[off + i] += v;
                    }
                    else {
                        int b0 = i0 * ch, b1 = i1 * ch, b2 = i2 * ch, b3 = i3 * ch;
                        float p0 = samples[b0], p1 = samples[b1], p2 = samples[b2], p3 = samples[b3];
                        sap.bufL[off + i] += gain * 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                        p0 = samples[b0 + 1]; p1 = samples[b1 + 1]; p2 = samples[b2 + 1]; p3 = samples[b3 + 1];
                        sap.bufR[off + i] += gain * 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    }
                    if (!sl.active) break;
                    cur += rateBase * basePitch * pitchParam * pitchMul;
                    basePitch += basePitchStep;
                    pitchParam += pitchParamStep;
                    srcGain += srcGainStep;
                }
                sl.cursor = cur;
                sl.fadeSamplesLeft = fadeLeft;
                sap.slots[s] = sl;
            }

            if (!anyActive || !AnySlotActive(in sap)) {
                if (sap.repeatsPending > 0) return; // Repeater: the next repeat is armed by the schedule (Phase 6)
                if (!sap.sourceExhausted) { sap.sourceExhausted = true; sap.samplesSinceSourceEnd = 0; sap.silentSamples = 0; }
            }
        }

        // ── the Looper (T-0474) ──

        /// <summary>
        /// One cycle's outgoing crossmix length, in source frames: a fixed value, or a draw within the range. The draw is
        /// an integer hash of the play's seed and the cycle number — never a managed random source — so it is the same
        /// compiled and managed, and every cycle gets a new length without any shared state. The top of the range is
        /// limited to half the loop so a cycle's fade-in (the previous draw) and its fade-out (this draw) cannot overlap.
        /// </summary>
        public static double LoopCrossmixLength(in SapLoopState lp, in SapPcm pcm, double start, double end, int cycle) {
            double limit = (end - start) * 0.5;
            if (limit < 0) limit = 0;
            double lo = lp.crossMin < limit ? lp.crossMin : limit;
            double hi = lp.crossMax < limit ? lp.crossMax : limit;
            if (hi < lo) hi = lo;
            double x = hi - lo < 0.5 ? lo : lo + (hi - lo) * LoopHash01(lp.seed, cycle);
            return AlignCrossmix(in pcm, start, end, x, limit);
        }

        /// <summary>
        /// Nudges a crossmix length by up to 5 ms either way to where the two copies line up best, the way sample-loop
        /// editors place a crossfade. The incoming copy always starts at the loop's start, so what is chosen here is how
        /// far before the end the outgoing copy is when it does; the score is the normalised correlation of the first
        /// few milliseconds of the two copies (at most 512 frames), and the nearest of equally good positions wins.
        ///
        /// **Why it is needed.** For material with a pitch, whether the copies add or cancel depends on the phase at which
        /// they meet, and a length drawn at random (or typed) lands anywhere: measured on a steady tone, un-nudged lengths
        /// dipped the level by up to 2.7 dB and broke the waveform across the fade. Nudged, the copies meet in phase and
        /// the fade is seamless; for material without a pitch it simply picks the least-cancelling point nearby.
        /// Costs one short search per loop cycle, when the next seam is decided — never per sample.
        /// </summary>
        public static double AlignCrossmix(in SapPcm pcm, double start, double end, double x, double limit) {
            if (x < 1 || !pcm.IsCreated || pcm.frequency <= 0) return x;
            int ch = pcm.channels < 1 ? 1 : pcm.channels;
            int total = pcm.samples.Length / ch;
            int radius = (int)(0.005 * pcm.frequency);
            int w = (int)(0.01 * pcm.frequency);
            if (w > 512) w = 512;
            if (w > (int)x) w = (int)x;
            if (radius < 1 || w < 8) return x;
            int b0 = (int)start;
            double best = x, bestScore = double.NegativeInfinity;
            for (int step = 0; step <= 2 * radius; step++) {
                // 0, +1, -1, +2, -2, ... so of equally good positions the nearest to the asked-for length wins
                int lag = (step & 1) == 0 ? step / 2 : -(step + 1) / 2;
                // Whole frames: the copies are then an exact number of frames apart, so aligned really means aligned
                // (a fractional length left them a fraction of a sample out, measured as a -58 dB residual on a tone).
                double xc = System.Math.Round(x) + lag;
                if (xc < 1 || xc > limit) continue;
                int a0 = (int)(end - xc);
                if (a0 < 0 || a0 + w > total || b0 + w > total) continue;
                double ab = 0, aa = 0, bb = 0;
                for (int k = 0; k < w; k++) {
                    for (int c = 0; c < ch && c < 2; c++) {
                        double p = pcm.samples[(a0 + k) * ch + c], q = pcm.samples[(b0 + k) * ch + c];
                        ab += p * q; aa += p * p; bb += q * q;
                    }
                }
                double score = aa > 1e-12 && bb > 1e-12 ? ab / System.Math.Sqrt(aa * bb) : 1.0;
                if (score > bestScore + 1e-9) { bestScore = score; best = xc; }
            }
            return best;
        }

        /// <summary>A number in [0, 1) from a seed and a cycle number; integer hashing only (see LfoWalkTarget).</summary>
        public static double LoopHash01(uint seed, int cycle) {
            uint h = (uint)cycle * 0x9E3779B1u ^ seed * 0x85EBCA77u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216.0;
        }

        /// <summary>
        /// The crossfade's two gains at progress <paramref name="p"/> (0..1), for regions whose normalised correlation is
        /// <paramref name="corr"/>: the outgoing copy gets <paramref name="gOut"/>, the incoming one <paramref name="gIn"/>.
        ///
        /// **Why the law depends on how alike the two regions are.** The two copies are the loop's start and the material
        /// X before its end — the same recording, but different parts of it. How loud their sum is mid-fade depends on how
        /// they line up: identical copies add as amplitudes (so an equal-gain fade keeps the level flat and equal-power
        /// bumps up to +3 dB), unrelated ones add as powers (so equal-power is flat and equal-gain dips 3 dB), and a steady
        /// tone lands anywhere between depending on the phase at which the copies meet — which a random crossmix changes
        /// every cycle. No fixed law is flat for all of these; both were measured failing on one or the other.
        ///
        /// So the linear fade is scaled to keep the summed POWER at one for the measured correlation:
        /// gOut = (1-p)k, gIn = pk, with k = 1/sqrt((1-p)^2 + p^2 + 2 corr p(1-p)). Correlation 1 gives exactly the
        /// equal-gain fade, 0 gives a flat-power fade, and a steady tone at any phase comes out flat. Only copies in
        /// opposite phase cannot be rescued (they cancel); the correlation is floored at -0.5 so the boost stays within
        /// +6 dB rather than reaching for an infinite gain.
        /// </summary>
        public static void LoopFadeGains(float p, float corr, out float gOut, out float gIn) {
            if (corr < -0.5f) corr = -0.5f; else if (corr > 1f) corr = 1f;
            float q = 1f - p;
            float d = q * q + p * p + 2f * corr * p * q;
            float k = d > 1e-6f ? 1f / Mathf.Sqrt(d) : 1f;
            gOut = q * k;
            gIn = p * k;
        }

        /// <summary>
        /// The normalised correlation of the two regions a crossfade will overlap: [outStart, outStart + len) and
        /// [inStart, inStart + len), in source frames, both channels. Measured when the fade starts, from at most about
        /// 8k sample pairs (a stride over longer regions) so its cost is bounded however long the crossmix. Silence gives 1.
        /// </summary>
        public static float LoopCorrelation(in SapPcm pcm, double outStart, double inStart, double len) {
            int ch = pcm.channels < 1 ? 1 : pcm.channels;
            int total = pcm.samples.Length / ch;
            int n = (int)len;
            if (n < 2) return 1f;
            int stride = n / 8192 + 1;
            if ((stride & 1) == 0) stride++;   // an odd stride, so it does not sit on the period of a simple tone
            int a0 = (int)outStart, b0 = (int)inStart;
            double ab = 0, aa = 0, bb = 0;
            for (int k = 0; k < n; k += stride) {
                int ia = a0 + k, ib = b0 + k;
                if (ia < 0 || ib < 0 || ia >= total || ib >= total) continue;
                for (int c = 0; c < ch && c < 2; c++) {
                    double x = pcm.samples[ia * ch + c], y = pcm.samples[ib * ch + c];
                    ab += x * y; aa += x * x; bb += y * y;
                }
            }
            if (aa < 1e-12 || bb < 1e-12) return 1f;
            return (float)(ab / System.Math.Sqrt(aa * bb));
        }

        /// <summary>
        /// The source stage for a looping voice (T-0474). Slots 0 and 1 take turns as the main copy. The main copy wraps
        /// at the region's end when there is no crossmix; with a crossmix of X source frames, when it reaches end − X the
        /// other slot starts at start + (how far past end − X the main copy already is), so the two are exactly X apart
        /// in the region and finish the fade together at any pitch. The fade's progress is the incoming copy's position
        /// over its first X frames, not elapsed time. When the outgoing copy reaches the end it stops and the incoming
        /// one becomes the main copy, drawing its own outgoing length for the next seam.
        ///
        /// Interpolation neighbours wrap around the region instead of being clamped at its edges, so a plain wrap on
        /// material that lines up at the loop points has no seam at all.
        ///
        /// After a release the slots no longer loop: no new copy starts and nothing wraps, the running fade (if any)
        /// completes, and each copy plays to the end or fades out as any released slot does.
        /// </summary>
        private static void ReadLoop(ref SapVoiceState sap, in SapPcm pcm, double clipRate, int off, int n, float basePitchStart, float basePitchStep) {
            int m = sap.looping.mainSlot, o = 1 - m;
            var a = sap.slots[m];
            var b = sap.slots[o];
            a.startAt = 0; b.startAt = 0;   // a looping voice has no repeat schedule, so nothing waits for a later sample
            double start = a.startFrame, end = a.endFrame;
            int first = (int)start;
            int len = (int)end - first;
            if (len < 2) len = 2;
            float srcGain = sap.pStart[SourceStageParam.Gain], srcGainStep = sap.pStep[SourceStageParam.Gain];
            float pitchParam = sap.pStart[SourceStageParam.Pitch], pitchParamStep = sap.pStep[SourceStageParam.Pitch];
            float basePitch = basePitchStart;

            for (int i = 0; i < n; i++) {
                if (!a.active) {
                    if (!b.active) break;
                    var t = a; a = b; b = t; int tm = m; m = o; o = tm;
                    sap.looping.inFade = false;
                }

                // The main copy reaches its seam: start the other copy (crossmix), or wrap (none).
                if (!sap.looping.inFade && a.loop) {
                    double outLen = sap.looping.outLen;
                    if (outLen >= 1.0) {
                        if (a.cursor >= end - outLen) {
                            double bc = start + (a.cursor - (end - outLen));
                            if (bc < start || bc >= end) bc = start;
                            b = a;
                            b.cursor = bc;
                            b.fadeSamplesLeft = 0; b.fadeSamplesTotal = 0;
                            b.active = true;
                            sap.looping.inFade = true;
                            sap.looping.fadeLen = outLen;
                            sap.looping.fadeCorr = LoopCorrelation(in pcm, end - outLen, start, outLen);
                            sap.looping.cycle++;
                            sap.looping.outLen = LoopCrossmixLength(in sap.looping, in pcm, start, end, sap.looping.cycle);
                        }
                    }
                    else if (a.cursor >= end) {
                        a.cursor = start + (a.cursor - end);
                        if (a.cursor >= end || a.cursor < start) a.cursor = start;
                        sap.looping.cycle++;
                        sap.looping.outLen = LoopCrossmixLength(in sap.looping, in pcm, start, end, sap.looping.cycle);
                    }
                }

                // The outgoing copy has reached the end: it stops, and the incoming one becomes the main copy.
                if (a.cursor >= end) {
                    a.active = false;
                    if (sap.looping.inFade && b.active) {
                        var t = a; a = b; b = t; int tm = m; m = o; o = tm;
                        sap.looping.inFade = false;
                    }
                    if (!a.active) break;
                }

                float gA = 1f, gB = 0f;
                if (sap.looping.inFade && b.active) {
                    double p = sap.looping.fadeLen > 0 ? (b.cursor - start) / sap.looping.fadeLen : 1.0;
                    if (p < 0) p = 0; else if (p > 1) p = 1;
                    LoopFadeGains((float)p, sap.looping.fadeCorr, out gA, out gB);
                }

                AccumulateLoopSample(ref sap, in pcm, ref a, first, len, off + i, a.gain * srcGain * gA);
                if (sap.looping.inFade && b.active) AccumulateLoopSample(ref sap, in pcm, ref b, first, len, off + i, b.gain * srcGain * gB);

                double step = clipRate * basePitch * pitchParam;
                a.cursor += step * a.pitchMul;
                if (b.active) b.cursor += step * b.pitchMul;
                basePitch += basePitchStep;
                pitchParam += pitchParamStep;
                srcGain += srcGainStep;
            }

            sap.slots[m] = a;
            sap.slots[o] = b;
            sap.looping.mainSlot = m;

            if (!a.active && !b.active) {
                if (!sap.sourceExhausted) { sap.sourceExhausted = true; sap.samplesSinceSourceEnd = 0; sap.silentSamples = 0; }
            }
        }

        /// <summary>
        /// Adds one interpolated sample of <paramref name="sl"/> into the output at <paramref name="at"/>, with the
        /// slot's own release fade on top of <paramref name="gain"/>. Catmull-Rom as in the direct read, with the
        /// neighbours wrapping around the loop region [first, first + len).
        /// </summary>
        private static void AccumulateLoopSample(ref SapVoiceState sap, in SapPcm pcm, ref SourceSlot sl, int first, int len, int at, float gain) {
            if (sl.fadeSamplesLeft > 0) {
                gain *= (float)sl.fadeSamplesLeft / sl.fadeSamplesTotal;
                sl.fadeSamplesLeft--;
                if (sl.fadeSamplesLeft == 0) sl.active = false;
            }
            double cur = sl.cursor;
            int i1 = (int)cur;
            float t = (float)(cur - i1);
            int r1 = i1 - first;
            if (r1 < 0) { r1 = 0; t = 0f; } else if (r1 >= len) { r1 = len - 1; t = 0f; }
            int r0 = r1 - 1; if (r0 < 0) r0 += len;
            int r2 = r1 + 1; if (r2 >= len) r2 -= len;
            int r3 = r1 + 2; if (r3 >= len) r3 -= len;
            int i0 = first + r0, j1 = first + r1, i2 = first + r2, i3 = first + r3;
            var samples = pcm.samples;
            int ch = pcm.channels;
            int total = samples.Length / (ch > 0 ? ch : 1);
            if (i0 >= total) i0 = total - 1; if (j1 >= total) j1 = total - 1; if (i2 >= total) i2 = total - 1; if (i3 >= total) i3 = total - 1;
            float t2 = t * t, t3 = t2 * t;
            if (ch == 1) {
                float p0 = samples[i0], p1 = samples[j1], p2 = samples[i2], p3 = samples[i3];
                float v = gain * 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                sap.bufL[at] += v; sap.bufR[at] += v;
            }
            else {
                int b0 = i0 * ch, b1 = j1 * ch, b2 = i2 * ch, b3 = i3 * ch;
                float p0 = samples[b0], p1 = samples[b1], p2 = samples[b2], p3 = samples[b3];
                sap.bufL[at] += gain * 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                p0 = samples[b0 + 1]; p1 = samples[b1 + 1]; p2 = samples[b2 + 1]; p3 = samples[b3 + 1];
                sap.bufR[at] += gain * 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
            }
        }

        /// <summary>
        /// The source stage for a voice with live speed (T-0409). Each read slot is run through its own stretcher,
        /// which reads the clip at clip-rate × pitch (so the sample rate is converted and Pitch works exactly as in the
        /// direct read) and advances through it at the live speed. Everything around the read — the sample-accurate start
        /// of a repeat, the slot's and the source's gain, the release fade, the slot ending — is the same as the direct
        /// read, so a voice behaves identically in every other respect.
        /// </summary>
        private static void ReadSourceStretched(ref SapVoiceState sap, in SapPcm pcm, double clipRate, int off, int n,
                                                float basePitchStart, float basePitchStep, float baseSpeedStart, float baseSpeedStep) {
            bool anyActive = false;
            float srcGainStart = sap.pStart[SourceStageParam.Gain], srcGainStep = sap.pStep[SourceStageParam.Gain];
            float pitchParamStart = sap.pStart[SourceStageParam.Pitch], pitchParamStep = sap.pStep[SourceStageParam.Pitch];
            float speedParamStart = sap.pStart[SourceStageParam.Speed], speedParamStep = sap.pStep[SourceStageParam.Speed];

            for (int s = 0; s < sap.slots.Length; s++) {
                var sl = sap.slots[s];
                if (!sl.active) continue;
                anyActive = true;
                int i0 = sl.startAt;
                if (i0 >= n) { sl.startAt -= n; sap.slots[s] = sl; continue; }
                sl.startAt = 0;
                int count = n - i0;

                float rate = (float)(clipRate * (basePitchStart + basePitchStep * i0) * (pitchParamStart + pitchParamStep * i0) * sl.pitchMul);
                float speedA = (baseSpeedStart + baseSpeedStep * i0) * (speedParamStart + speedParamStep * i0);
                float speedB = (baseSpeedStart + baseSpeedStep * n) * (speedParamStart + speedParamStep * n);
                if (sap.stretch.keepLength) {
                    // Keep length (T-0482): the pitch curve changes pitch only. The read rate already carries it (tape
                    // style), so the time-scale is divided by exactly the same value at exactly the same moments -- the
                    // compensation is derived from the pitch itself and cannot drift from it.
                    float pa = pitchParamStart + pitchParamStep * i0, pb = pitchParamStart + pitchParamStep * n;
                    speedA /= pa > 1e-3f ? pa : 1e-3f;
                    speedB /= pb > 1e-3f ? pb : 1e-3f;
                }
                int wrote = sap.stretch.Render(s, in pcm, sl.startFrame, sl.endFrame, sl.loop, rate, speedA, speedB,
                                               sap.stretchScratchL, sap.stretchScratchR, 0, count);

                float srcGain = srcGainStart + srcGainStep * i0;
                int fadeLeft = sl.fadeSamplesLeft, fadeTotal = sl.fadeSamplesTotal;
                for (int k = 0; k < wrote; k++) {
                    float gain = sl.gain * srcGain;
                    if (fadeLeft > 0) {
                        gain *= (float)fadeLeft / fadeTotal;
                        fadeLeft--;
                        if (fadeLeft == 0) { sl.active = false; }
                    }
                    sap.bufL[off + i0 + k] += gain * sap.stretchScratchL[k];
                    sap.bufR[off + i0 + k] += gain * sap.stretchScratchR[k];
                    if (!sl.active) break;
                    srcGain += srcGainStep;
                }
                if (wrote < count) sl.active = false;
                sl.cursor = sap.stretch.SourcePosition(s);
                sl.fadeSamplesLeft = fadeLeft;
                sap.slots[s] = sl;
            }

            if (!anyActive || !AnySlotActive(in sap)) {
                if (sap.repeatsPending > 0) return;
                if (!sap.sourceExhausted) { sap.sourceExhausted = true; sap.samplesSinceSourceEnd = 0; sap.silentSamples = 0; }
            }
        }

        /// <summary>
        /// Normalized position (0..1) of the most recently armed active read slot over its region,
        /// predicted <paramref name="aheadSamples"/> output samples from now at the current rate.
        /// </summary>
        private static float SourceProgress(in SapVoiceState sap, double clipRate, int aheadSamples) {
            for (int s = sap.slots.Length - 1; s >= 0; s--) {
                if (!sap.slots[s].active) continue;
                double len = sap.slots[s].endFrame - sap.slots[s].startFrame;
                if (len <= 0) return 1f;
                double ahead = aheadSamples * clipRate * sap.basePitchLive * sap.pLive[SourceStageParam.Pitch] * sap.slots[s].pitchMul;
                float p = (float)((sap.slots[s].cursor + ahead - sap.slots[s].startFrame) / len);
                return p < 0f ? 0f : (p > 1f ? 1f : p);
            }
            return 1f;
        }

        private static bool AnySlotActive(in SapVoiceState sap) {
            for (int s = 0; s < sap.slots.Length; s++) if (sap.slots[s].active) return true;
            return false;
        }

        /// <summary>
        /// Envelope.Evaluate's maths, over a slice [offset, offset+count) of a flat modulator-curve array,
        /// with a cached segment index (O(1) amortised). Takes offset/count instead of its own array so the
        /// audio thread never allocates or copies a slice — it indexes straight into the native snapshot's
        /// modCurveFlat (SapChainLayout). Relocated from ChainLayout (unchanged arithmetic) so it can be
        /// reached from Burst-compiled code: ChainLayout is a managed class and cannot be seen from a
        /// Burst job, while this method only ever touched a NativeArray&lt;EnvPoint&gt; and plain values.
        /// Its only callers are the two envelope-modifier evaluations in <see cref="EvaluateModifiers"/> below.
        /// </summary>
        private static float EvaluateEnvelope(NativeArray<EnvPoint> pts, int offset, int count, float time, ref int segment, uint seed, int mod) {
            if (count == 0) return 1f;
            // Random points (T-0483): a curve with any takes the drawn path; every other curve the exact old arithmetic.
            for (int i = 0; i < count; i++) {
                var rp = pts[offset + i];
                if (rp.randomX > 0f || rp.randomY > 0f) return EvaluateDrawnEnvelope(pts, offset, count, time, ref segment, seed, mod);
            }
            if (count == 1) return pts[offset].value;
            if (time <= pts[offset].time) { segment = 0; return pts[offset].value; }
            if (time >= pts[offset + count - 1].time) { segment = count - 2; return pts[offset + count - 1].value; }
            if (segment < 0 || segment >= count - 1) segment = 0;
            while (segment > 0 && pts[offset + segment].time > time) segment--;
            while (segment < count - 2 && pts[offset + segment + 1].time <= time) segment++;
            float x1 = pts[offset + segment].time, x2 = pts[offset + segment + 1].time;
            float t = x2 > x1 ? (time - x1) / (x2 - x1) : 1f;
            float exp = pts[offset + segment + 1].exponent;
            if (exp <= 0f) exp = 0.000001f;
            float a = pts[offset + segment].value, b = pts[offset + segment + 1].value;
            return a + (b - a) * Mathf.Pow(t, exp);
        }

        static float DrawnTime(NativeArray<EnvPoint> pts, int offset, int count, int i, uint seed, int mod) {
            var p = pts[offset + i];
            if (p.randomX <= 0f && p.randomY <= 0f) return p.time;
            EnvelopeRandom.Offset(seed, mod, i, p.randomX, p.randomY, p.randomBias, out float dx, out _);
            return EnvelopeRandom.DrawnTime(p.time, i > 0 ? pts[offset + i - 1].time : p.time, i < count - 1 ? pts[offset + i + 1].time : p.time,
                                           i == 0 || i == count - 1, dx);
        }

        static float DrawnValue(NativeArray<EnvPoint> pts, int offset, int i, uint seed, int mod) {
            var p = pts[offset + i];
            if (p.randomX <= 0f && p.randomY <= 0f) return p.value;
            EnvelopeRandom.Offset(seed, mod, i, p.randomX, p.randomY, p.randomBias, out _, out float dy);
            return EnvelopeRandom.DrawnValue(p.value, dy, p.yMin, p.yMax);
        }

        /// <summary>EvaluateEnvelope for a curve with random points: the same arithmetic over each point's drawn position
        /// (EnvelopeRandom; the same draw the play-length calculation uses).</summary>
        static float EvaluateDrawnEnvelope(NativeArray<EnvPoint> pts, int offset, int count, float time, ref int segment, uint seed, int mod) {
            if (count == 1) return DrawnValue(pts, offset, 0, seed, mod);
            if (time <= DrawnTime(pts, offset, count, 0, seed, mod)) { segment = 0; return DrawnValue(pts, offset, 0, seed, mod); }
            if (time >= DrawnTime(pts, offset, count, count - 1, seed, mod)) { segment = count - 2; return DrawnValue(pts, offset, count - 1, seed, mod); }
            if (segment < 0 || segment >= count - 1) segment = 0;
            while (segment > 0 && DrawnTime(pts, offset, count, segment, seed, mod) > time) segment--;
            while (segment < count - 2 && DrawnTime(pts, offset, count, segment + 1, seed, mod) <= time) segment++;
            float x1 = DrawnTime(pts, offset, count, segment, seed, mod), x2 = DrawnTime(pts, offset, count, segment + 1, seed, mod);
            float t = x2 > x1 ? (time - x1) / (x2 - x1) : 1f;
            float exp = pts[offset + segment + 1].exponent;
            if (exp <= 0f) exp = 0.000001f;
            float a = DrawnValue(pts, offset, segment, seed, mod), b = DrawnValue(pts, offset, segment + 1, seed, mod);
            return a + (b - a) * Mathf.Pow(t, exp);
        }

        // ── modifiers (control rate) ──

        /// <summary>
        /// An oscillator's output from its raw wave (-1 to 1), its Amount, its Offset and its strength at this moment.
        ///
        /// **Offset says which way the swing goes, not how far to shove it.** Minus one swings only below the set value,
        /// nought swings evenly either side, plus one swings only above; in between leans one way. The swing's reach is
        /// never more than Amount in either direction, whatever the Offset — so no Offset can push a parameter further
        /// than an evenly balanced swing already does.
        ///
        /// It used to be added straight onto the wave in Amount's own units, on a slider running to four either way. Four
        /// whole swings of shove, applied before a binding's depth, pushed the swing past the end of the parameter's
        /// range: measured on a cutoff set mid-slider, 27% of that slider's travel was usable at the default depth, 3%
        /// with the modulator in charge (Set), none at full depth — everything else pinned the cutoff against an end stop.
        /// It was also added after the strength curve, so a strength of "none" still shoved the parameter.
        ///
        /// Strength scales the whole result here, Offset included: "none" means the parameter is left where it was set.
        /// </summary>
        public static float LfoOutput(float wave, float amount, float bias, float strength) {
            float a = bias < 0f ? -bias : bias;
            return amount * strength * (wave + bias) / (1f + a);
        }

        /// <summary>Offset limited to its meaningful range. A value saved under the old meaning (up to four either way)
        /// becomes "entirely one way", which is the closest the new meaning has to what such a value did.</summary>
        public static float LfoBias(float offset) => offset < -1f ? -1f : offset > 1f ? 1f : offset;

        /// <summary>
        /// The shared random walk's target for one interval: a fixed function of the interval's number and the walk's
        /// seed, so every play of the same oscillator — whenever it started, on whichever thread — computes the same walk
        /// without sharing any state. Integer hashing only, so it gives identical answers compiled and managed.
        /// </summary>
        public static float LfoWalkTarget(int interval, int seed) {
            uint h = (uint)interval * 0x9E3779B1u ^ (uint)seed * 0x85EBCA77u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f * 2f - 1f;
        }

        /// <summary>
        /// Which step a free-running list (Per interval, Retrigger off) is on at step number <paramref name="k"/> of its
        /// own clock. A fixed function of the step number, so every play of the list agrees without sharing state.
        ///
        /// Sequential: simply k modulo the list length. Round robin: each pass through the list is its own shuffle, drawn
        /// from the pass number and the list's seed — ranking the steps by a hash, which needs no scratch memory — and the
        /// first two of a pass are swapped when the pass would otherwise open on the step the previous pass ended on, so a
        /// value never plays twice running, the same promise round robin makes when it restarts with every play.
        /// </summary>
        public static int FreeRunStep(int k, int count, bool roundRobin, int seed) {
            if (count <= 1) return 0;
            if (k < 0) k = 0;
            int pass = k / count, pos = k - pass * count;
            // With two steps, never-twice-running leaves exactly one order: alternate. (The seam swap below touches a
            // pass's first two entries, which for a two-step list is the whole pass — measured to repeat without this.)
            if (!roundRobin || count == 2) return pos;
            int e = StepAtRank(pass, pos, count, seed);
            if (pos <= 1 && pass > 0) {
                int prevLast = StepAtRank(pass - 1, count - 1, count, seed);
                if (StepAtRank(pass, 0, count, seed) == prevLast) e = StepAtRank(pass, pos == 0 ? 1 : 0, count, seed);
            }
            return e;
        }

        private static int StepAtRank(int pass, int rank, int count, int seed) {
            for (int i = 0; i < count; i++) {
                uint ki = StepKey(pass, i, seed);
                int below = 0;
                for (int j = 0; j < count; j++) {
                    uint kj = StepKey(pass, j, seed);
                    if (kj < ki || (kj == ki && j < i)) below++;
                }
                if (below == rank) return i;
            }
            return 0;
        }

        private static uint StepKey(int pass, int i, int seed) {
            uint h = (uint)pass * 0x9E3779B1u ^ (uint)i * 0x85EBCA77u ^ (uint)seed * 0xC2B2AE3Du;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return h;
        }

        /// <summary>
        /// The interval counter is kept in a float, which counts whole numbers exactly only up to about sixteen million,
        /// so it wraps well before that. At the shortest interval (a hundredth of a second) that is a wrap every
        /// twenty-three hours, invisible except as a new stretch of random walk, which is what the walk is anyway.
        /// </summary>
        public const float LfoWalkWrap = 8388608f;

        private static float NextRandom01(ref SapVoiceState sap) {
            sap.rng ^= sap.rng << 13; sap.rng ^= sap.rng >> 17; sap.rng ^= sap.rng << 5;
            return (sap.rng & 0xFFFFFF) / 16777216f;
        }

        private static void EvaluateModifiers(ref SapVoiceState sap, in SapChainLayout L, int sampleRate, int blockSamples, float elapsed, int lookaheadSamples,
                                               bool isGroup, float sourceDuration, double clipRate) {
            float blockSeconds = (float)blockSamples / sampleRate;
            for (int m = 0; m < L.modCount; m++) {
                int so = L.modStateOffset[m];
                int mpo = L.modParamOffset[m];
                int mpc = L.modParamCountOf[m];
                var mp = L.modParamFlat;
                int mco = L.modCurveOffset[m];
                int mcc = L.modCurveCountOf[m];
                switch (L.modType[m]) {
                    case ZoundModifierType.Envelope: {
                        float total = sourceDuration + L.modExtraSeconds[m];
                        float tn;
                        bool sourceBase = !isGroup && !sap.repeat.enabled && (mpc < 2 || mp[mpo + 1] < 0.5f);
                        if (sourceBase && !sap.sourceExhausted && total > 0f) {
                            // Follow the waveform: normalized position of the read cursor over the trimmed region
                            // at the end of this block, scaled so the extra-time band still sits past the source end.
                            tn = SourceProgress(in sap, clipRate, lookaheadSamples) * (sourceDuration / total);
                        }
                        else {
                            tn = total > 0f ? elapsed / total : 1f;
                        }
                        if (tn > 1f) tn = 1f;
                        int seg = (int)sap.arena[so];
                        sap.modValue[m] = EvaluateEnvelope(L.modCurveFlat, mco, mcc, tn, ref seg, sap.curveSeed, m);
                        sap.arena[so] = seg;
                        break;
                    }
                    case ZoundModifierType.Lfo: {
                        float amount = mp[mpo], rate = mp[mpo + 1];
                        int shape = (int)mp[mpo + 2];
                        int mode = (int)mp[mpo + 4];
                        float bias = LfoBias(mpc > 6 ? mp[mpo + 6] : 0f);
                        float ramp = 1f;
                        if (mcc > 0) {
                            float total = sourceDuration;
                            float tn = total > 0f ? elapsed / total : 1f; if (tn > 1f) tn = 1f;
                            int seg = (int)sap.arena[so + 6];
                            ramp = EvaluateEnvelope(L.modCurveFlat, mco, mcc, tn, ref seg, sap.curveSeed, m);
                            sap.arena[so + 6] = seg;
                        }
                        if (mode == (int)LfoMode.Oscillate) {
                            // Phase is advanced first so the value is the one at the end of the block (see Render).
                            float phase = sap.arena[so] + rate * blockSeconds;
                            if (phase >= 1f) phase -= (int)phase;
                            float w;
                            switch (shape) {
                                case (int)LfoShape.Triangle: w = phase < 0.5f ? (phase * 4f - 1f) : (3f - phase * 4f); break;
                                case (int)LfoShape.Saw: w = phase * 2f - 1f; break;
                                case (int)LfoShape.Square: w = phase < 0.5f ? 1f : -1f; break;
                                default: w = Mathf.Sin(phase * 6.2831853f); break;
                            }
                            sap.modValue[m] = LfoOutput(w, amount, bias, ramp);
                            sap.arena[so] = phase;
                        }
                        else {
                            // Random: glide from current toward a target; pick a new target on a timer.
                            float every = mpc > 5 ? mp[mpo + 5] : 0.5f;
                            if (sap.arena[so + 7] == 0f) {
                                sap.arena[so + 7] = 1f;
                                sap.arena[so + 1] = NextRandom01(ref sap) * 2f - 1f;
                                sap.arena[so + 2] = NextRandom01(ref sap) * 2f - 1f;
                                sap.arena[so + 4] = sap.arena[so + 1];
                                sap.arena[so + 3] = every;
                                sap.arena[so + 5] = 0f;
                            }
                            sap.arena[so + 3] -= blockSeconds;
                            if (sap.arena[so + 3] <= 0f) {
                                sap.arena[so + 3] += every;
                                sap.arena[so + 4] = sap.arena[so + 1];
                                // "Keeps running": the next target is the shared walk's next value, the same one every
                                // other play of this oscillator draws, so overlapping plays stay in step. "Per play":
                                // this play's own fresh draw.
                                bool keepsRunning = mpc > 3 && mp[mpo + 3] < 0.5f;
                                if (keepsRunning) {
                                    float next = sap.arena[so + 8] + 1f;
                                    if (next >= LfoWalkWrap) next -= LfoWalkWrap;
                                    sap.arena[so + 8] = next;
                                    sap.arena[so + 2] = LfoWalkTarget((int)next, (int)sap.arena[so + 9]);
                                }
                                else sap.arena[so + 2] = NextRandom01(ref sap) * 2f - 1f;
                                sap.arena[so + 5] = 0f;
                            }
                            if (rate <= 0f) sap.arena[so + 1] = sap.arena[so + 2];
                            else {
                                sap.arena[so + 5] += rate * blockSeconds;
                                float gt = sap.arena[so + 5] > 1f ? 1f : sap.arena[so + 5];
                                sap.arena[so + 1] = sap.arena[so + 4] + (sap.arena[so + 2] - sap.arena[so + 4]) * gt;
                            }
                            sap.modValue[m] = LfoOutput(sap.arena[so + 1], amount, bias, ramp);
                        }
                        break;
                    }
                    case ZoundModifierType.Random:
                        sap.modValue[m] = sap.arena[so];
                        break;
                    case ZoundModifierType.Step: {
                        int sso = L.modStepOffset[m];
                        int ssc = L.modStepCountOf[m];
                        int timing = (int)mp[mpo];
                        bool roundRobin = (int)mp[mpo + 2] == (int)StepOrder.RoundRobinNoRepeat;
                        if (timing == (int)StepTiming.PerInterval) {
                            float interval = mp[mpo + 1] * 0.001f;
                            if (interval < 0.0005f) interval = 0.0005f;
                            int cur = (int)sap.arena[so];
                            if (cur < 0 || cur >= ssc) cur = 0;
                            if (sap.arena[so + 7] == 0f) { sap.arena[so + 7] = 1f; sap.arena[so + 4] = L.modStepFlat[sso + cur]; }
                            sap.arena[so + 1] += blockSeconds;
                            while (sap.arena[so + 1] >= interval) {
                                sap.arena[so + 1] -= interval;
                                // The glide into the next step starts from wherever the output is now, so a step cut
                                // short mid-glide carries on smoothly rather than jumping to its own start.
                                sap.arena[so + 4] = sap.modValue[m];
                                if (sap.arena[so + 5] > 0.5f) {
                                    // Retrigger off: the list follows its own clock (see SeedModifiersAtTrigger).
                                    float k = sap.arena[so + 3] + 1f;
                                    if (k >= LfoWalkWrap) k -= LfoWalkWrap;
                                    sap.arena[so + 3] = k;
                                    sap.arena[so] = FreeRunStep((int)k, ssc, roundRobin, (int)sap.arena[so + 6]);
                                }
                                else AdvanceStep(ref sap, ssc, roundRobin, so);
                            }
                            int idx = (int)sap.arena[so];
                            if (idx < 0 || idx >= ssc) idx = 0;
                            float target = L.modStepFlat[sso + idx];
                            // Smooth: the share of each step spent gliding from the previous value to this one. A share
                            // rather than a time, so the pattern keeps its shape however short the steps are made.
                            float smooth = mpc > 5 ? mp[mpo + 5] : 0f;
                            float glide = smooth * interval;
                            float into = sap.arena[so + 1];
                            sap.modValue[m] = glide > 0f && into < glide
                                ? sap.arena[so + 4] + (target - sap.arena[so + 4]) * (into / glide)
                                : target;
                        }
                        else {
                            int idx = (int)sap.arena[so];
                            if (idx < 0 || idx >= ssc) idx = 0;
                            sap.modValue[m] = L.modStepFlat[sso + idx];
                        }
                        break;
                    }
                    default: sap.modValue[m] = 0f; break;
                }
            }
        }

        // Round-robin over the step list with a used-mask in state[2] (up to 24 steps). When every step has
        // been used the mask clears and the first pick of the new cycle excludes the last-played index, so
        // the seam can never repeat and every step still plays once per cycle.
        private static void AdvanceStep(ref SapVoiceState sap, int count, bool roundRobin, int so) {
            if (count <= 1) { sap.arena[so] = 0f; return; }
            int last = (int)sap.arena[so];
            if (!roundRobin) { sap.arena[so] = (last + 1) % count; return; }
            int used = (int)sap.arena[so + 2];
            int all = count >= 24 ? 0xFFFFFF : (1 << count) - 1;
            used |= 1 << last;
            int exclude = 0;
            if ((used & all) == all) { used = 0; exclude = 1 << last; }
            int free = 0;
            for (int i = 0; i < count && i < 24; i++) if (((used | exclude) & (1 << i)) == 0) free++;
            int pick = (int)(NextRandom01(ref sap) * free);
            for (int i = 0; i < count && i < 24; i++) {
                if (((used | exclude) & (1 << i)) != 0) continue;
                if (pick == 0) { sap.arena[so] = i; sap.arena[so + 2] = used | (1 << i); return; }
                pick--;
            }
            sap.arena[so] = (last + 1) % count;
        }
    }
}

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
        /// Renders one callback's worth of frames into sap.bufL/sap.bufR. Mirrors the old
        /// <c>DspVoice.Render</c> body exactly, minus the pause short-circuit (the caller already handled
        /// it) and minus the Finish()/TrackOnsets() calls (the caller does those after this returns).
        /// Returns true when this block finished the voice (the caller must then push AudioEnd and free it).
        /// </summary>
        public static bool Render(ref SapVoiceState sap, in SapChainLayout L, int frames, int sampleRate,
                                   in SapPcm pcm, bool isGroup, double clipRate, float sourceDuration, int liveChildren,
                                   bool killRequested, bool releaseRequested,
                                   float basePitchTargetNow, float outGainTargetRaw,
                                   ref bool protectedFromSteal, out VoiceStateTransition transition) {

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
                if (n > ZoundDspConstants.CONTROL_BLOCK) n = ZoundDspConstants.CONTROL_BLOCK;
                invN = 1f / n;

                // ── control block: targets, clamps, per-sample slopes ──
                float basePitchStart = sap.basePitchLive;
                float basePitchStep = (basePitchTargetNow - sap.basePitchLive) * invN;
                float outGainStart = sap.outGainLive;
                float outGainStep = (outGainTargetNow - sap.outGainLive) * invN;
                ctx.elapsedSeconds = (float)sap.elapsedSamples / sampleRate;
                ctx.sourceExhausted = sap.sourceExhausted;

                if (L.bindCount > 0) {
                    for (int r = 0; r < L.rampedCount; r++) sap.pTarget[L.ramped[r]] = L.pBase[L.ramped[r]];
                    // Targets are evaluated for the END of this block: the per-sample ramp then lands on the
                    // right value exactly when the block ends, so the reconstruction is a true piecewise-linear
                    // interpolation of the modulator rather than one lagging by a block.
                    EvaluateModifiers(ref sap, L, sampleRate, n, ctx.elapsedSeconds + (float)n / sampleRate, n, isGroup, sourceDuration, clipRate);
                    for (int b = 0; b < L.bindCount; b++) {
                        int t = L.bindTarget[b];
                        float m = sap.modValue[L.bindModifier[b]] * L.bindDepth[b];
                        switch (L.bindOp[b]) {
                            case ModifierOp.Multiply: sap.pTarget[t] *= m; break;
                            case ModifierOp.Add: sap.pTarget[t] += m; break;
                            default: sap.pTarget[t] = m; break;
                        }
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
                    ReadSource(ref sap, pcm, clipRate, off, n, basePitchStart, basePitchStep);
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
        private static float EvaluateEnvelope(NativeArray<EnvPoint> pts, int offset, int count, float time, ref int segment) {
            if (count == 0) return 1f;
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

        // ── modifiers (control rate) ──

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
                        sap.modValue[m] = EvaluateEnvelope(L.modCurveFlat, mco, mcc, tn, ref seg);
                        sap.arena[so] = seg;
                        break;
                    }
                    case ZoundModifierType.Lfo: {
                        float amount = mp[mpo], rate = mp[mpo + 1];
                        int shape = (int)mp[mpo + 2];
                        int mode = (int)mp[mpo + 4];
                        float offset = mpc > 6 ? mp[mpo + 6] : 0f;
                        float ramp = 1f;
                        if (mcc > 0) {
                            float total = sourceDuration;
                            float tn = total > 0f ? elapsed / total : 1f; if (tn > 1f) tn = 1f;
                            int seg = (int)sap.arena[so + 6];
                            ramp = EvaluateEnvelope(L.modCurveFlat, mco, mcc, tn, ref seg);
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
                            sap.modValue[m] = offset + amount * w * ramp;
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
                                sap.arena[so + 2] = NextRandom01(ref sap) * 2f - 1f;
                                sap.arena[so + 5] = 0f;
                            }
                            if (rate <= 0f) sap.arena[so + 1] = sap.arena[so + 2];
                            else {
                                sap.arena[so + 5] += rate * blockSeconds;
                                float gt = sap.arena[so + 5] > 1f ? 1f : sap.arena[so + 5];
                                sap.arena[so + 1] = sap.arena[so + 4] + (sap.arena[so + 2] - sap.arena[so + 4]) * gt;
                            }
                            sap.modValue[m] = offset + amount * sap.arena[so + 1] * ramp;
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
                        if (timing == (int)StepTiming.PerInterval) {
                            float interval = mp[mpo + 1] * 0.001f;
                            sap.arena[so + 1] += blockSeconds;
                            if (sap.arena[so + 1] >= interval) {
                                sap.arena[so + 1] -= interval;
                                AdvanceStep(ref sap, ssc, (int)mp[mpo + 2] == (int)StepOrder.RoundRobinNoRepeat, so);
                            }
                        }
                        int idx = (int)sap.arena[so];
                        if (idx < 0 || idx >= ssc) idx = 0;
                        sap.modValue[m] = L.modStepFlat[sso + idx];
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

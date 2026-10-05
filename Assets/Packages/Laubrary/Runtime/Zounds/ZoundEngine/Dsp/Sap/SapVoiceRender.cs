using Laubrary.Audio;
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
        /// A parameter that a modifier is driving takes the value as where the modifier starts from (this voice's
        /// own copy of the layout), so the modifier carries on moving it from there; its live value is left alone,
        /// since the modifier would overwrite it at the next control block anyway.
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

            float min = L.pMin[flatIndex];
            float max = L.pMax[flatIndex];
            float clamped = value < min ? min : value > max ? max : value;

            // A parameter a modifier drives: the edit moves where the modifier starts from (this voice's own copy of the
            // layout), and the modifier goes on moving it from there at the next control block. Writing the live value
            // instead would be overwritten a block later, which is why this used to refuse outright -- and a refusal meant a
            // modulated slider dragged while the sound played was not heard until the next play.
            for (int r = 0; r < L.rampedCount; r++) {
                if (L.ramped[r] != flatIndex) continue;
                var bases = L.pBase;
                bases[flatIndex] = clamped;
                return;
            }

            sap.pLive[flatIndex] = clamped;
            sap.pStart[flatIndex] = clamped;
            sap.pStep[flatIndex] = 0f;
        }

        // ─────────────── snapshot glide (T-0498) ───────────────

        static bool IsRamped(in SapChainLayout L, int flat) {
            for (int r = 0; r < L.rampedCount; r++) if (L.ramped[r] == flat) return true;
            return false;
        }

        /// <summary>
        /// One glide command (sent as Clear, targets, Begin). Begin records where every targeted value is RIGHT NOW as the
        /// glide's start, which is what makes a glide start "from wherever the sound is" -- including mid-way through an
        /// earlier glide, since those values are simply where that glide had got to.
        /// </summary>
        public static void ApplyGlideCommand(ref SapVoiceState sap, in SapChainLayout L, in SapVoiceCommand c) {
            int i = c.index;
            switch (c.kind) {
                case SapVoiceCommandKind.GlideClear:
                    for (int k = 0; k < sap.gpKind.Length; k++) sap.gpKind[k] = 0;
                    for (int k = 0; k < sap.gmKind.Length; k++) sap.gmKind[k] = 0;
                    for (int k = 0; k < sap.gbKind.Length; k++) sap.gbKind[k] = 0;
                    for (int k = 0; k < sap.gnKind.Length; k++) sap.gnKind[k] = 0;
                    break;
                case SapVoiceCommandKind.GlideParam:
                case SapVoiceCommandKind.GlideParamSwitch:
                    if (i < 0 || i >= L.paramCount) break;
                    float v = c.value < L.pMin[i] ? L.pMin[i] : c.value > L.pMax[i] ? L.pMax[i] : c.value;
                    sap.gpTo[i] = v;
                    sap.gpKind[i] = (byte)((c.kind == SapVoiceCommandKind.GlideParam ? 1 : 2) + (IsRamped(in L, i) ? 2 : 0));
                    break;
                case SapVoiceCommandKind.GlideModParam:
                case SapVoiceCommandKind.GlideModParamSwitch:
                    if (i < 0 || i >= sap.gmKind.Length || i >= L.modParamFlat.Length) break;
                    sap.gmTo[i] = c.value;
                    sap.gmKind[i] = (byte)(c.kind == SapVoiceCommandKind.GlideModParam ? 1 : 2);
                    break;
                case SapVoiceCommandKind.GlideDepth:
                    if (i < 0 || i >= L.bindCount || i >= sap.gbKind.Length) break;
                    sap.gbTo[i] = c.value; sap.gbKind[i] = 1;
                    break;
                case SapVoiceCommandKind.GlidePresence:
                    if (i < 0 || i >= L.nodeCount || i >= sap.gnKind.Length) break;
                    sap.gnTo[i] = c.value < 0.5f ? 0f : 1f; sap.gnKind[i] = 1;
                    break;
                case SapVoiceCommandKind.GlideBegin: {
                    for (int f = 0; f < L.paramCount && f < sap.gpKind.Length; f++) {
                        byte k = sap.gpKind[f];
                        if (k == 0) continue;
                        sap.gpFrom[f] = k >= 3 ? L.pBase[f] : sap.pLive[f];
                    }
                    for (int m = 0; m < sap.gmKind.Length && m < L.modParamFlat.Length; m++) if (sap.gmKind[m] != 0) sap.gmFrom[m] = L.modParamFlat[m];
                    for (int b = 0; b < L.bindCount && b < sap.gbKind.Length; b++) if (sap.gbKind[b] != 0) sap.gbFrom[b] = L.bindDepth[b];
                    for (int n = 0; n < L.nodeCount && n < sap.gnKind.Length; n++) if (sap.gnKind[n] != 0) sap.gnFrom[n] = sap.presence[n];
                    sap.glideTotal = i < 0 ? 0 : i;
                    sap.glideDone = 0;
                    sap.glideActive = true;
                    sap.glideSettled = false;
                    // Before the voice has rendered anything, a glide of any length starts where it ends: a play that begins
                    // on a snapshot is on it from its first sample.
                    if (sap.elapsedSamples == 0) sap.glideTotal = 0;
                    break;
                }
            }
        }

        /// <summary>
        /// Moves every gliding value one control block further (T-0498). Continuous values move along their own control
        /// (a frequency by ratio, a level by amount), choices switch at the midpoint, effects fade in or out. A value no
        /// modifier moves is ramped sample by sample across the block; one a modifier moves has its resting value moved,
        /// and the modifier goes on from there. One more block at the end settles every ramp exactly on its target.
        /// </summary>
        static void AdvanceGlide(ref SapVoiceState sap, in SapChainLayout L, float invN) {
            if (sap.glideSettled) {
                for (int f = 0; f < L.paramCount && f < sap.gpKind.Length; f++) {
                    byte k = sap.gpKind[f];
                    if (k == 1 || k == 2) { sap.pStart[f] = sap.pLive[f]; sap.pStep[f] = 0f; }
                }
                sap.glideActive = false;
                return;
            }
            sap.glideDone += ZoundDspConstants.CONTROL_BLOCK;
            float t = sap.glideTotal <= 0 ? 1f : (float)sap.glideDone / sap.glideTotal;
            if (t > 1f) t = 1f;
            bool past = t >= 0.5f;
            var bases = L.pBase;
            for (int f = 0; f < L.paramCount && f < sap.gpKind.Length; f++) {
                byte k = sap.gpKind[f];
                if (k == 0) continue;
                float v;
                if (t >= 1f) v = sap.gpTo[f];   // arrived: exactly the target (the round trip through the control is not exact)
                else if (k == 1 || k == 3) {
                    bool ratio = L.pRatio[f];
                    float a = ModulationMath.ToPosition(sap.gpFrom[f], L.pMin[f], L.pMax[f], ratio);
                    float b = ModulationMath.ToPosition(sap.gpTo[f], L.pMin[f], L.pMax[f], ratio);
                    v = ModulationMath.FromPosition(a + (b - a) * t, L.pMin[f], L.pMax[f], ratio);
                }
                else v = past ? sap.gpTo[f] : sap.gpFrom[f];
                if (k >= 3) bases[f] = v;
                else {
                    float prev = sap.pLive[f];
                    sap.pLive[f] = v; sap.pStart[f] = prev; sap.pStep[f] = (v - prev) * invN;
                }
            }
            var mp = L.modParamFlat;
            for (int m = 0; m < sap.gmKind.Length && m < mp.Length; m++) {
                byte k = sap.gmKind[m];
                if (k == 0) continue;
                mp[m] = k == 1 ? sap.gmFrom[m] + (sap.gmTo[m] - sap.gmFrom[m]) * t : (past ? sap.gmTo[m] : sap.gmFrom[m]);
            }
            var depths = L.bindDepth;
            for (int b = 0; b < L.bindCount && b < sap.gbKind.Length; b++)
                if (sap.gbKind[b] != 0) depths[b] = sap.gbFrom[b] + (sap.gbTo[b] - sap.gbFrom[b]) * t;
            for (int n = 0; n < L.nodeCount && n < sap.gnKind.Length; n++)
                if (sap.gnKind[n] != 0) sap.presence[n] = sap.gnFrom[n] + (sap.gnTo[n] - sap.gnFrom[n]) * t;
            if (t >= 1f) sap.glideSettled = true;
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
                float boostStart = sap.boostLive;
                if (gridStart) {
                    // Landed (within rounding): snap exactly, so a sound at 1 keeps skipping the multiply below.
                    float boostGap = sap.boostTarget - sap.boostLive;
                    if (boostGap < 1e-5f && boostGap > -1e-5f) { sap.boostLive = sap.boostTarget; boostStart = sap.boostLive; }
                    sap.ctlBoostStep = (sap.boostTarget - sap.boostLive) * invN;
                    sap.ctlBasePitchStep = (basePitchTargetNow - sap.basePitchLive) * invN;
                    sap.ctlOutGainStep = (outGainTargetNow - sap.outGainLive) * invN;
                    sap.ctlBaseSpeedStep = (baseSpeedTargetNow - sap.baseSpeedLive) * invN;
                }
                float basePitchStep = sap.ctlBasePitchStep;
                float outGainStep = sap.ctlOutGainStep;
                float baseSpeedStep = sap.ctlBaseSpeedStep;
                ctx.elapsedSeconds = (float)sap.elapsedSamples / sampleRate;
                ctx.sourceExhausted = sap.sourceExhausted;

                // A snapshot glide moves its values once per control block, before the modulators read them (T-0498).
                if (gridStart && sap.glideActive) AdvanceGlide(ref sap, in L, invN);

                if (L.bindCount > 0 && !gridStart) {
                    // The rest of a block a call boundary split: same slopes, from where the ramps have got to.
                    for (int r = 0; r < L.rampedCount; r++) { int t = L.ramped[r]; sap.pStart[t] = sap.pLive[t]; }
                }
                else if (L.bindCount > 0) {
                    EvaluateModifiers(ref sap, L, sampleRate, ZoundDspConstants.CONTROL_BLOCK,
                                      ctx.elapsedSeconds + (float)ZoundDspConstants.CONTROL_BLOCK / sampleRate, ZoundDspConstants.CONTROL_BLOCK,
                                      isGroup, sourceDuration, clipRate);
                    ChainModulation.PrepareTargets(in L, sap.modValue, sap.modCtlLive, sap.pLive, sap.pStart, sap.pStep, sap.pTarget);
                }

                // ── source stage (a group's input is the children's sum, already in bufL/bufR) ──
                if (!isGroup) {
                    if (sap.repeatsPending > 0) ArmRepeats(ref sap, n, basePitchStart, clipRate, ref protectedFromSteal);
                    for (int ci = 0; ci < n; ci++) sap.bufL[off + ci] = 0f;
                    for (int ci = 0; ci < n; ci++) sap.bufR[off + ci] = 0f;
                    if (sap.stretch.enabled) ReadSourceStretched(ref sap, pcm, clipRate, off, n, basePitchStart, basePitchStep, baseSpeedStart, baseSpeedStep);
                    else if (sap.looping.enabled) ReadLoop(ref sap, pcm, clipRate, off, n, basePitchStart, basePitchStep);
                    else ReadSource(ref sap, pcm, clipRate, off, n, basePitchStart, basePitchStep);
                    // The sound's fixed boost into its effects (T-0521): the same as multiplying Drive by it at every place
                    // the source is read. Skipped at rest (exactly one, not moving), which every sound without one is.
                    float bg = boostStart, bs = sap.ctlBoostStep;
                    if (bg != 1f || bs != 0f) {
                        for (int i = 0; i < n; i++) { sap.bufL[off + i] *= bg; sap.bufR[off + i] *= bg; bg += bs; }
                    }
                }

                // ── chain ──
                if (L.nodeCount > 0) {
                    ZoundEffects.ProcessChain(L, sap.arena, sap.pStart, sap.pStep, sap.bufL, sap.bufR, off, n, in ctx,
                                              sap.presence, sap.presencePrev, sap.dryL, sap.dryR);
                    for (int pi = 0; pi < L.nodeCount && pi < sap.presence.Length; pi++) sap.presencePrev[pi] = sap.presence[pi];
                }

                // ── the Zound's own volume (T-0493), after every effect ──
                // The same per-sample ramp an inserted Gain effect at the end of the chain applies, so a volume curve moved
                // here sounds identical. Skipped at rest (exactly one, not moving), which every sound without one is.
                {
                    float vg = sap.pStart[SourceStageParam.Volume], vs = sap.pStep[SourceStageParam.Volume];
                    if (vg != 1f || vs != 0f) {
                        for (int i = 0; i < n; i++) { sap.bufL[off + i] *= vg; sap.bufR[off + i] *= vg; vg += vs; }
                    }
                }

                // ── output gain ──
                float g = outGainStart;
                for (int i = 0; i < n; i++) {
                    sap.bufL[off + i] *= g; sap.bufR[off + i] *= g; g += outGainStep;
                }

                // ── advance live values ──
                sap.basePitchLive = basePitchStart + basePitchStep * n;
                sap.outGainLive = outGainStart + outGainStep * n;
                sap.boostLive = boostStart + sap.ctlBoostStep * n;
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
        public const float LfoWalkWrap = ChainModulation.LfoWalkWrap;
        private static float NextRandom01(ref SapVoiceState sap) => ChainModulation.NextRandom01(ref sap.rng);
        public static float LfoOutput(float wave, float amount, float bias, float strength) => ChainModulation.LfoOutput(wave, amount, bias, strength);
        public static float LfoBias(float offset) => ChainModulation.LfoBias(offset);
        public static float LfoWalkTarget(int interval, int seed) => ChainModulation.LfoWalkTarget(interval, seed);
        public static int FreeRunStep(int k, int count, bool roundRobin, int seed) => ChainModulation.FreeRunStep(k, count, roundRobin, seed);
        private static void EvaluateModifiers(ref SapVoiceState sap, in SapChainLayout L, int sampleRate, int blockSamples, float elapsed, int lookaheadSamples, bool isGroup, float sourceDuration, double clipRate) {
            var state = new ChainModulationState { arena = sap.arena, modValue = sap.modValue, modCtlLive = sap.modCtlLive, modCtlTarget = sap.modCtlTarget, rng = sap.rng, curveSeed = sap.curveSeed };
            var context = new ModulationContext { elapsedSeconds = elapsed, sourceDuration = sourceDuration, followSource = !isGroup && !sap.repeat.enabled, sourceExhausted = sap.sourceExhausted, sourceProgress = SourceProgress(in sap, clipRate, lookaheadSamples) };
            ChainModulation.EvaluateModifiers(ref state, in L, sampleRate, blockSamples, in context);
            sap.rng = state.rng;
        }
    }
}

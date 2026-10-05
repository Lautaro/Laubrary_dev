using Unity.Collections;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Mathf = Laubrary.Audio.AudioMath;

namespace Laubrary.Audio {
    public static class ChainModulation {
        public static void PrepareTargets(in SapChainLayout L, NativeArray<float> modValue, NativeArray<float> modCtlLive,
                                          NativeArray<float> pLive, NativeArray<float> pStart, NativeArray<float> pStep, NativeArray<float> pTarget) {
            for (int r = 0; r < L.rampedCount; r++) pTarget[L.ramped[r]] = L.pBase[L.ramped[r]];
            for (int b = 0; b < L.bindCount; b++) {
                int t = L.bindTarget[b], bm = L.bindModifier[b];
                float depth = L.modType[bm] == ZoundModifierType.Code ? L.bindDepth[b] : L.bindDepth[b] * modCtlLive[bm];
                pTarget[t] = ModulationMath.Apply(L.bindCombine[b], pTarget[t], modValue[bm], depth, L.pMin[t], L.pMax[t], L.pRatio[t]);
            }
            for (int r = 0; r < L.rampedCount; r++) {
                int t = L.ramped[r];
                float target = pTarget[t];
                if (target < L.pMin[t]) target = L.pMin[t]; else if (target > L.pMax[t]) target = L.pMax[t];
                pStart[t] = pLive[t];
                pStep[t] = (target - pLive[t]) * (1f / AudioControl.BlockFrames);
            }
        }
        public static float EvaluateEnvelope(NativeArray<EnvPoint> pts, int offset, int count, float time, ref int segment, uint seed, int mod) {
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
            EnvelopeRandomMath.Offset(seed, mod, i, p.randomX, p.randomY, p.randomBias, out float dx, out _);
            return EnvelopeRandomMath.DrawnTime(p.time, i > 0 ? pts[offset + i - 1].time : p.time, i < count - 1 ? pts[offset + i + 1].time : p.time,
                                           i == 0 || i == count - 1, dx);
        }

        static float DrawnValue(NativeArray<EnvPoint> pts, int offset, int i, uint seed, int mod) {
            var p = pts[offset + i];
            if (p.randomX <= 0f && p.randomY <= 0f) return p.value;
            EnvelopeRandomMath.Offset(seed, mod, i, p.randomX, p.randomY, p.randomBias, out _, out float dy);
            return EnvelopeRandomMath.DrawnValue(p.value, dy, p.yMin, p.yMax);
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

        private static float NextRandom01(ref ChainModulationState sap) {
            return NextRandom01(ref sap.rng);
        }

        public static float NextRandom01(ref uint rng) {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }

        public static float DrawRandom(float min, float max, float bias, float r01) {
            min = Mathf.Clamp(min, -1f, 1f); max = Mathf.Clamp(max, -1f, 1f);
            if (bias > 0f && System.Math.Abs(bias - 1f) > 1e-4f) r01 = Mathf.Pow(r01, 1f / bias);
            return min + (max - min) * r01;
        }

        public static void EvaluateModifiers(ref ChainModulationState sap, in SapChainLayout L, int sampleRate, int blockSamples, in ModulationContext context) {
            float elapsed = context.elapsedSeconds;
            float sourceDuration = context.sourceDuration;
            float blockSeconds = (float)blockSamples / sampleRate;
            for (int m = 0; m < L.modCount; m++) {
                // ZPOC: close part of the gap to the value game code last sent, once per control block. A pure function of
                // the block count and the values sent, so the result does not depend on how the host slices its calls.
                float ctlCoef = L.modCtlCoef[m];
                sap.modCtlLive[m] += (sap.modCtlTarget[m] - sap.modCtlLive[m]) * ctlCoef;
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
                        bool sourceBase = context.followSource && (mpc < 2 || mp[mpo + 1] < 0.5f);
                        if (sourceBase && !context.sourceExhausted && total > 0f) {
                            // Follow the waveform: normalized position of the read cursor over the trimmed region
                            // at the end of this block, scaled so the extra-time band still sits past the source end.
                            tn = context.sourceProgress * (sourceDuration / total);
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
                    case ZoundModifierType.Code:
                        sap.modValue[m] = sap.modCtlLive[m];
                        break;
                    default: sap.modValue[m] = 0f; break;
                }
            }
        }

        // Round-robin over the step list with a used-mask in state[2] (up to 24 steps). When every step has
        // been used the mask clears and the first pick of the new cycle excludes the last-played index, so
        // the seam can never repeat and every step still plays once per cycle.
        private static void AdvanceStep(ref ChainModulationState sap, int count, bool roundRobin, int so) {
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

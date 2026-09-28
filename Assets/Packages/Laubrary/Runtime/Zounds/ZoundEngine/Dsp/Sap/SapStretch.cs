using Unity.Collections;
using Unity.Mathematics;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// How a live-speed voice's stretcher is set up. Fixed for the life of the voice (it sizes the buffers); only the
    /// speed itself changes while the sound plays.
    /// </summary>
    public struct SapStretchConfig {
        public bool enabled;
        /// <summary><see cref="LiveStretchAlgorithm"/> as a number (the named type lives in the authoring assembly's namespace).</summary>
        public int algorithm;
        /// <summary>Window length in milliseconds. 20–30 suits speech and hits, 40–50 pads and chords.</summary>
        public float windowMs;
        /// <summary>Play each detected hit once, whole, at speed 1, and repay the time it borrowed afterwards.</summary>
        public bool keepHits;
        /// <summary>How long after each detected hit is protected, in milliseconds (the offline stretcher's default is 40).</summary>
        public float keepMs;

        public static SapStretchConfig Off => new SapStretchConfig { enabled = false };
    }

    /// <summary>What one read slot's stretcher remembers between blocks. Plain values only.</summary>
    public struct SapStretchSlot {
        /// <summary>Source frame (absolute, in the clip) where the next window nominally starts.</summary>
        public double inPos;
        /// <summary>Where the previous window actually came from (fractional).</summary>
        public double prevBest;
        /// <summary>Source frames the playhead is AHEAD of the requested timeline because a locked hit played at speed 1.</summary>
        public double debt;
        public int readyCount, readyPos, skip, flushFrames;
        public bool sourceDone, finished;
        public uint rng;
    }

    /// <summary>
    /// Real-time, pitch-independent time-stretch in the source stage (T-0409): WSOLA, or Granular (WSOLA with the
    /// alignment search switched off). Ported from the research prototype (D:\UNITY\ZoundsStretchTest, report in
    /// Report\TIME_STRETCH_REPORT.md) into this engine's shape: all state native and sized when the voice starts,
    /// nothing allocated while rendering, and one state per read slot so a repeat train stretches as well.
    ///
    /// **How it plugs into the existing source read.** Each window is read from the clip with the same cubic
    /// interpolation the direct read uses, stepping through the source at clip-rate × pitch — so the clip's sample rate
    /// is converted and Pitch keeps its existing meaning (pitch and length together, like tape). Speed then changes
    /// only how far each window advances through the source: that is what makes the length change while the pitch
    /// does not. At speed 1 a window continues the previous one exactly.
    ///
    /// **The three things the prototype proved necessary** are all here: the alignment is refined to a fraction of a
    /// sample (a tone whose period is not a whole number of samples otherwise gets a small error at every splice); a
    /// detected hit is laid down once, whole, at speed 1 (without it a click train at quarter speed doubled and
    /// quadrupled its hits); and the time a locked hit borrowed is repaid by the free material after it, at most half
    /// a window's worth per window, so the sound still lasts as long as asked.
    ///
    /// **The end is reported, not predicted.** A live speed makes the length unknowable in advance, so the stretcher
    /// says when it is done (the slot goes inactive) and the voice's existing tail and finish logic takes it from there.
    /// </summary>
    public struct SapStretch {
        public bool enabled;
        public int algorithm;
        public bool keepHits;
        /// <summary>Window and hop, in OUTPUT frames.</summary>
        public int N, H;
        /// <summary>How far either side of the nominal position the alignment search looks, in SOURCE frames.</summary>
        public int searchRadius;
        public int sourceFrequency;

        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<float> window;            // N, periodic Hann (sums to exactly 1 at 50 % overlap)
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<float> acc;               // slots x 2N: overlap-add accumulator, L then R
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<float> ready;             // slots x 2H: finished output waiting to be handed out, L then R
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<SapStretchSlot> slot;     // slots
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<byte> onsetMs;            // 1 in the millisecond a hit begins (whole clip)
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<byte> keepMap;            // 1 inside the protected span after a hit
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction] public NativeArray<float> mono;              // the clip as mono, the alignment search's guide (contiguous, so the search is a tight loop)

        public const float MinSpeed = 0.05f, MaxSpeed = 8f;
        public const int Wsola = 0, Granular = 1;

        public bool IsCreated => window.IsCreated;

        /// <summary>Main thread: sizes everything and finds the clip's hits (the offline stretcher's rule).</summary>
        public static SapStretch Create(SapStretchConfig cfg, PcmClip clip, int outputSampleRate, int slots, Allocator a) {
            var st = new SapStretch { enabled = cfg.enabled && clip != null && clip.frames > 0, algorithm = cfg.algorithm, keepHits = cfg.keepHits };
            if (!st.enabled) return st;
            float windowMs = cfg.windowMs > 5f ? cfg.windowMs : 30f;
            st.N = math.max(64, (int)(windowMs * 0.001f * outputSampleRate)) & ~1;
            st.H = st.N / 2;
            st.sourceFrequency = clip.frequency;
            st.searchRadius = math.max(0, (int)(0.010f * clip.frequency));   // ±10 ms, as the prototype
            st.window = new NativeArray<float>(st.N, a);
            for (int i = 0; i < st.N; i++) st.window[i] = 0.5f - 0.5f * math.cos(2f * math.PI * i / st.N);
            st.acc = new NativeArray<float>(slots * 2 * st.N, a, NativeArrayOptions.ClearMemory);
            st.ready = new NativeArray<float>(slots * 2 * st.H, a, NativeArrayOptions.ClearMemory);
            st.slot = new NativeArray<SapStretchSlot>(slots, a, NativeArrayOptions.ClearMemory);

            // Hits: 2 ms peaks jumping to 3x a decaying envelope and above a tenth of the clip's peak — the same rule as
            // the offline stretcher, at 1 ms resolution over the whole clip.
            int frames = clip.frames, ch = clip.channels, sr = clip.frequency;
            st.mono = new NativeArray<float>(frames, a, NativeArrayOptions.UninitializedMemory);
            for (int f = 0; f < frames; f++) {
                float m = 0f;
                for (int c = 0; c < ch; c++) m += clip.samples[f * ch + c];
                st.mono[f] = m / ch;
            }
            int ms = math.max(1, (int)((long)frames * 1000 / sr) + 1);
            st.onsetMs = new NativeArray<byte>(ms, a, NativeArrayOptions.ClearMemory);
            st.keepMap = new NativeArray<byte>(ms, a, NativeArrayOptions.ClearMemory);
            int blk = math.max(1, sr / 500), blocks = frames / blk;
            var env = new float[math.max(1, blocks)];
            float peak = 0f;
            var s = clip.samples;
            for (int b = 0; b < blocks; b++) {
                float mx = 0f;
                for (int f = b * blk; f < (b + 1) * blk; f++)
                    for (int c = 0; c < ch; c++) mx = math.max(mx, math.abs(s[f * ch + c]));
                env[b] = mx; peak = math.max(peak, mx);
            }
            float e = 0f; int keep = (int)math.max(0f, cfg.keepMs);
            for (int b = 0; b < blocks; b++) {
                float prev = e * 0.85f;
                if (env[b] > peak * 0.1f && env[b] > prev * 3f) {
                    int from = b * 2;
                    if (from < ms) st.onsetMs[from] = 1;
                    for (int i = from; i < math.min(ms, from + keep); i++) st.keepMap[i] = 1;
                }
                e = math.max(env[b], prev);
            }
            return st;
        }

        public void Dispose() {
            if (window.IsCreated) window.Dispose();
            if (acc.IsCreated) acc.Dispose();
            if (ready.IsCreated) ready.Dispose();
            if (slot.IsCreated) slot.Dispose();
            if (onsetMs.IsCreated) onsetMs.Dispose();
            if (keepMap.IsCreated) keepMap.Dispose();
            if (mono.IsCreated) mono.Dispose();
        }

        /// <summary>
        /// Starts slot <paramref name="s"/> at <paramref name="startFrame"/>. Two warm-up windows sit before the start and
        /// always advance at speed 1, so that output frame 0 lines up with the start of the sound.
        /// </summary>
        public void ResetSlot(int s, double startFrame, double rate) {
            if (!enabled) return;
            int twoN = 2 * N;
            for (int i = 0; i < twoN; i++) acc[s * twoN + i] = 0f;
            var st = new SapStretchSlot {
                inPos = startFrame - (N - H) * rate,
                skip = N - H,
                rng = 0x9E3779B9u ^ (uint)(s * 7919),
            };
            st.prevBest = st.inPos - H * rate;
            slot[s] = st;
        }

        private readonly int MsIndex(double frame) {
            long m = (long)(frame * 1000.0 / sourceFrequency);
            return m < 0 ? 0 : (m >= onsetMs.Length ? onsetMs.Length - 1 : (int)m);
        }

        private readonly bool KeepAt(double frame, double first, double end) =>
            frame >= first && frame < end && keepMap[MsIndex(frame)] != 0;

        private readonly bool OnsetIn(double from, double to, double first, double end) {
            if (to <= first || from >= end) return false;
            int a = MsIndex(math.max(from, first)), b = MsIndex(math.min(to, end) - 1);
            for (int m = a; m <= b; m++) if (onsetMs[m] != 0) return true;
            return false;
        }

        // ── reading the clip: zero outside the sound's region, as the prototype ──

        private static float At(in SapPcm pcm, int frame, int c, int first, int last) {
            if (frame < first || frame > last) return 0f;
            return pcm.samples[frame * pcm.channels + (pcm.channels > 1 ? c : 0)];
        }

        private static float Cubic(float p0, float p1, float p2, float p3, float t) {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static float ReadCubic(in SapPcm pcm, double pos, int c, int first, int last) {
            int i1 = (int)math.floor(pos);
            float t = (float)(pos - i1);
            float p1 = At(in pcm, i1, c, first, last);
            if (t < 1e-6f) return p1;
            return Cubic(At(in pcm, i1 - 1, c, first, last), p1, At(in pcm, i1 + 1, c, first, last), At(in pcm, i1 + 2, c, first, last), t);
        }

        /// <summary>
        /// Writes up to <paramref name="n"/> frames of slot <paramref name="s"/> into <paramref name="outL"/> and
        /// <paramref name="outR"/> from <paramref name="off"/>, and returns how many it wrote; fewer than n means the
        /// slot has finished. <paramref name="rate"/> is source frames per output frame at speed 1 (clip-rate × pitch),
        /// and <paramref name="speedStart"/>..<paramref name="speedEnd"/> ramp across the block, as pitch and gain do.
        /// </summary>
        public int Render(int s, in SapPcm pcm, double first, double end, bool loop, float rate,
                          float speedStart, float speedEnd, NativeArray<float> outL, NativeArray<float> outR, int off, int n) {
            var st = slot[s];
            int i = 0;
            float invN = n > 0 ? 1f / n : 0f;
            while (i < n) {
                if (st.readyPos >= st.readyCount) {
                    if (st.finished) break;
                    float sp = math.clamp(speedStart + (speedEnd - speedStart) * (i * invN), MinSpeed, MaxSpeed);
                    ProduceHop(s, ref st, in pcm, first, end, loop, rate, sp);
                    continue;
                }
                int avail = st.readyCount - st.readyPos;
                if (st.skip > 0) { int k = math.min(st.skip, avail); st.skip -= k; st.readyPos += k; continue; }
                int take = math.min(n - i, avail);
                int rb = s * 2 * H;
                for (int k = 0; k < take; k++) {
                    outL[off + i + k] = ready[rb + st.readyPos + k];
                    outR[off + i + k] = ready[rb + H + st.readyPos + k];
                }
                i += take; st.readyPos += take;
            }
            slot[s] = st;
            return i;
        }

        /// <summary>The slot's current source position, for whatever follows the read cursor (progress, stealing).</summary>
        public readonly double SourcePosition(int s) => slot[s].inPos;

        private void ProduceHop(int s, ref SapStretchSlot st, in SapPcm pcm, double first, double end, bool loop, float rate, float speed) {
            int ab = s * 2 * N, rb = s * 2 * H;
            if (st.sourceDone) {
                if (st.flushFrames <= 0) { st.finished = true; st.readyCount = st.readyPos = 0; return; }
                st.flushFrames--;
            } else {
                if (loop && end > first + 1 && st.inPos >= end) {
                    double len = end - first;
                    st.inPos -= len; st.prevBest -= len;
                }
                // Warm-up windows sit before the start; they always advance at speed 1 so output 0 lines up with it.
                if (st.inPos < first) speed = 1f;
                if (st.inPos >= end) { st.sourceDone = true; st.flushFrames = N / H - 1; }
                else Frame(ref st, in pcm, first, end, rate, speed, ab);
            }
            // The first H frames of the accumulator are now final: hand them out and slide the rest down.
            for (int k = 0; k < H; k++) { ready[rb + k] = acc[ab + k]; ready[rb + H + k] = acc[ab + N + k]; }
            for (int k = 0; k < N - H; k++) { acc[ab + k] = acc[ab + k + H]; acc[ab + N + k] = acc[ab + N + k + H]; }
            for (int k = N - H; k < N; k++) { acc[ab + k] = 0f; acc[ab + N + k] = 0f; }
            st.readyCount = H; st.readyPos = 0;
        }

        /// <summary>How far the next free window moves through the source: the requested amount, minus a repayment of
        /// any timeline debt of at most half of it.</summary>
        private static double Advance(ref SapStretchSlot st, double wanted, double first) {
            if (st.inPos < first) return wanted;
            double repay = math.clamp(st.debt, -0.5 * wanted, 0.5 * wanted);
            st.debt -= repay;
            return wanted - repay;
        }

        private void Frame(ref SapStretchSlot st, in SapPcm pcm, double first, double end, float rate, float speed, int ab) {
            double hopSrc = H * (double)rate;           // source frames one hop covers at speed 1
            double pos = st.inPos;
            double nominal = math.round(pos);
            double natural = st.prevBest + hopSrc;      // the window that would continue the previous one seamlessly
            int firstI = (int)first, lastI = (int)end - 1;
            double best;
            bool locked = false;
            if (nominal <= first) best = nominal;
            else if (keepHits && (KeepAt(natural, first, end) || OnsetIn(natural, natural + N * (double)rate, first, end))) {
                // A hit is coming into, or is inside, the window: continue exactly, so it is laid down once, whole, at speed 1.
                best = natural; locked = true;
            } else if (algorithm == Granular) {
                st.rng ^= st.rng << 13; st.rng ^= st.rng >> 17; st.rng ^= st.rng << 5;
                float r = (st.rng & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
                best = nominal + (int)(0.2f * N * rate * 0.5f * r);
            } else best = Search(in pcm, natural, (int)nominal, (int)math.max(1, math.round(hopSrc)), (int)math.ceil(N * (double)rate), firstI, lastI);

            // Read the window at clip-rate × pitch, through the same cubic interpolation as the direct read.
            for (int c = 0; c < 2; c++) {
                int cb = ab + c * N;
                if (rate == 1f && best == math.floor(best)) {
                    int b0 = (int)best;
                    for (int i = 0; i < N; i++) acc[cb + i] += window[i] * At(in pcm, b0 + i, c, firstI, lastI);
                } else if (rate == 1f) {
                    // The clip at its own rate (the usual case): the fraction is the same for every sample of the window,
                    // so it is worked out once, and the window reads the clip directly when it lies wholly inside the sound.
                    int b0 = (int)math.floor(best);
                    float t = (float)(best - b0);
                    float t2 = t * t, t3 = t2 * t;
                    float k0 = 0.5f * (-t + 2f * t2 - t3), k1 = 0.5f * (2f - 5f * t2 + 3f * t3), k2 = 0.5f * (t + 4f * t2 - 3f * t3), k3 = 0.5f * (-t2 + t3);
                    int ch = pcm.channels, cc = ch > 1 ? c : 0;
                    if (b0 - 1 >= firstI && b0 + N + 2 <= lastI) {
                        var smp = pcm.samples;
                        for (int i = 0; i < N; i++) {
                            int j = (b0 + i) * ch + cc;
                            acc[cb + i] += window[i] * (k0 * smp[j - ch] + k1 * smp[j] + k2 * smp[j + ch] + k3 * smp[j + 2 * ch]);
                        }
                    } else {
                        for (int i = 0; i < N; i++) {
                            int j = b0 + i;
                            acc[cb + i] += window[i] * (k0 * At(in pcm, j - 1, c, firstI, lastI) + k1 * At(in pcm, j, c, firstI, lastI)
                                                      + k2 * At(in pcm, j + 1, c, firstI, lastI) + k3 * At(in pcm, j + 2, c, firstI, lastI));
                        }
                    }
                } else {
                    for (int i = 0; i < N; i++) acc[cb + i] += window[i] * ReadCubic(in pcm, best + i * (double)rate, c, firstI, lastI);
                }
            }
            st.prevBest = best;
            st.inPos = locked ? best + hopSrc : pos + Advance(ref st, hopSrc * speed, first);
            if (locked) st.debt += (best + hopSrc - pos) - hopSrc * speed;
        }

        /// <summary>
        /// The candidate start within ±radius of nominal whose opening best matches the natural continuation, by
        /// normalised correlation (a raw product favours loud regions), coarse then fine, then refined to a fraction of a
        /// sample by fitting a parabola through the best score and its neighbours.
        /// </summary>
        private readonly double Search(in SapPcm pcm, double natural, int nominal, int len, int span, int first, int last) {
            int target = (int)math.round(natural);
            int lo = math.max(first + 1, nominal - searchRadius), hi = math.min(last - span - 2, nominal + searchRadius);
            if (hi < lo || target + len > last || target < first) return math.clamp(nominal, first, math.max(first, last));
            int best = Scan(target, lo, hi, 2, len, 2, out _);
            best = Scan(target, math.max(lo, best - 2), math.min(hi, best + 2), 1, len, 1, out float s0);
            double result = best + math.round((natural - target) * 4096.0) / 4096.0;
            // Refined even when the winner sits on the edge of the search range: the parabola only needs the neighbouring
            // candidates to exist in the clip, not to have been inside the range. The prototype skipped refinement at the
            // edge, and a live sweep (where the natural continuation drifts furthest from the nominal position) then
            // produced isolated whole-sample splices, measured at -52 dB against -84 dB elsewhere.
            if (best - 1 >= first + 1 && best + 1 <= last - span - 2) {
                float sm = Score(target, best - 1, len), sp = Score(target, best + 1, len);
                float den = sm - 2f * s0 + sp;
                if (den < -1e-12f) result += math.round(math.clamp(0.5f * (sm - sp) / den, -0.5f, 0.5f) * 4096f) / 4096f;
            }
            return math.max(first, result);
        }

        private readonly float Score(int target, int c, int len) {
            float dot = 0f, energy = 1e-9f;
            for (int i = 0; i < len; i++) { float b = mono[c + i]; dot += mono[target + i] * b; energy += b * b; }
            return dot / math.sqrt(energy);
        }

        private readonly int Scan(int target, int lo, int hi, int step, int len, int stride, out float bestScore) {
            int best = lo; bestScore = float.MinValue;
            for (int c = lo; c <= hi; c += step) {
                float dot = 0f, energy = 1e-9f;
                for (int i = 0; i < len; i += stride) {
                    float b = mono[c + i];
                    dot += mono[target + i] * b; energy += b * b;
                }
                float score = dot / math.sqrt(energy);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
    }
}

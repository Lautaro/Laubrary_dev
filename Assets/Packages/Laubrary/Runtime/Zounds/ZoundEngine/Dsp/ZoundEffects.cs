using Unity.Collections;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    /// <summary>
    /// Effect processing: one static method per effect over NativeArray<float> spans, a packed parameter block
    /// (block-start value + per-sample step for each parameter) and a per-voice state arena. Dispatch
    /// is a switch on the type enum. Nothing here allocates, calls Unity APIs or touches managed
    /// object graphs; each node is written so it would port to C unchanged.
    ///
    /// "Mix" means the same thing on every effect: a dry/wet crossfade, 0 = dry only, 1 = wet only.
    /// </summary>
    public static class ZoundEffects {

        /// <summary>Resets every node's state for a fresh play (the arena region is already zeroed).</summary>
        public static void ResetChain(in SapChainLayout L, NativeArray<float> state, int sampleRate) {
            for (int i = 0; i < L.nodeCount; i++) {
                int s = L.stateOffset[i];
                int q = L.paramOffset[i];
                switch (L.nodeType[i]) {
                    case ZoundEffectType.Delay: DelayEffect.Reset(state, s, L.pBase, q, sampleRate); break;
                    case ZoundEffectType.Reverb: ReverbEffect.Reset(state, s, sampleRate); break;
                    case ZoundEffectType.Flanger: ModDelayEffect.Reset(state, s, 12f, sampleRate); break;
                    case ZoundEffectType.Chorus: ModDelayEffect.Reset(state, s, 40f, sampleRate); break;
                    case ZoundEffectType.LowPass:
                    case ZoundEffectType.HighPass: BiquadEffect.Reset(state, s); break;
                    case ZoundEffectType.EQ: EqEffect.Reset(state, s); break;
                }
            }
        }

        public static void ProcessChain(in SapChainLayout L, NativeArray<float> state, NativeArray<float> pStart, NativeArray<float> pStep,
                                        NativeArray<float> bufL, NativeArray<float> bufR, int off, int n, in VoiceContext ctx) {
            for (int i = 0; i < L.nodeCount; i++) {
                if (!L.enabled[i]) continue;
                int s = L.stateOffset[i];
                int q = L.paramOffset[i];
                switch (L.nodeType[i]) {
                    case ZoundEffectType.Gain: GainEffect.Process(pStart, pStep, q, bufL, bufR, off, n); break;
                    case ZoundEffectType.Normalize: NormalizeEffect.Process(pStart, q, bufL, bufR, off, n, ctx.sourcePeak); break;
                    case ZoundEffectType.Fade: FadeEffect.Process(pStart, q, bufL, bufR, off, n, in ctx); break;
                    case ZoundEffectType.Limiter: DynamicsEffect.ProcessLimiter(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.Compressor: DynamicsEffect.ProcessCompressor(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.TransientShaper: DynamicsEffect.ProcessTransientShaper(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.LowPass: BiquadEffect.Process(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate, false); break;
                    case ZoundEffectType.HighPass: BiquadEffect.Process(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate, true); break;
                    case ZoundEffectType.EQ: EqEffect.Process(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.Delay: DelayEffect.Process(state, s, pStart, pStep, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.Reverb: ReverbEffect.Process(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.Flanger: ModDelayEffect.ProcessFlanger(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.Chorus: ModDelayEffect.ProcessChorus(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.Phaser: PhaserEffect.Process(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                    case ZoundEffectType.BitCrush: BitCrushEffect.Process(state, s, pStart, q, bufL, bufR, off, n); break;
                    case ZoundEffectType.Distortion: DistortionEffect.Process(state, s, pStart, q, bufL, bufR, off, n, ctx.sampleRate); break;
                }
            }
        }

        public static float DbToLinear(float db) => Mathf.Pow(10f, db / 20f);

        /// <summary>log2 accurate to ~0.005 (bit-trick plus one polynomial term); positive input only.</summary>
        public static float FastLog2(float x) {
            if (x <= 1e-30f) return -100f;
            int bits = Unity.Mathematics.math.asint(x);
            int e = ((bits >> 23) & 0xFF) - 127;
            float m = Unity.Mathematics.math.asfloat((bits & 0x007FFFFF) | 0x3F800000); // 1..2
            // minimax quadratic for log2(m) on [1,2)
            return e + (-0.34484843f * m + 2.02466578f) * m - 1.67487759f;
        }

        /// <summary>2^x accurate to ~0.1 %, |x| < 126.</summary>
        public static float FastExp2(float x) {
            if (x < -126f) x = -126f; else if (x > 126f) x = 126f;
            int xi = x >= 0f ? (int)x : (int)x - 1;
            float f = x - xi;
            float p = 1f + f * (0.6931f + f * (0.2402f + f * 0.0558f));
            return Unity.Mathematics.math.asfloat(Unity.Mathematics.math.asint(p) + (xi << 23));
        }

        /// <summary>Flushes denormals and NaNs a feedback loop could otherwise carry forever.</summary>
        public static float Sane(float v) {
            if (v != v) return 0f;                 // NaN
            if (v > -1e-18f && v < 1e-18f) return 0f;
            if (v > 8f) return 8f; if (v < -8f) return -8f;
            return v;
        }
    }

    public static class GainEffect {
        public static void Process(NativeArray<float> pStart, NativeArray<float> pStep, int q, NativeArray<float> L, NativeArray<float> R, int off, int n) {
            float g = pStart[q], step = pStep[q];
            for (int i = 0; i < n; i++) { L[off + i] *= g; R[off + i] *= g; g += step; }
        }
    }

    public static class NormalizeEffect {
        public static void Process(NativeArray<float> pStart, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, float sourcePeak) {
            if (sourcePeak <= 0.0001f) return;
            float g = ZoundEffects.DbToLinear(pStart[q]) / sourcePeak;
            for (int i = 0; i < n; i++) { L[off + i] *= g; R[off + i] *= g; }
        }
    }

    public static class FadeEffect {
        // Fade in from the source start, fade out into the source end (absolute seconds, like the old bake).
        public static void Process(NativeArray<float> pStart, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, in VoiceContext ctx) {
            float fadeIn = pStart[q], fadeOut = pStart[q + 1];
            bool sCurve = pStart[q + 2] >= 0.5f;
            float t0 = ctx.elapsedSeconds;
            float dt = 1f / ctx.sampleRate;
            float dur = ctx.sourceDuration;
            bool needIn = fadeIn > 0.0005f && t0 < fadeIn;
            bool needOut = fadeOut > 0.0005f && dur > 0f && t0 + n * dt > dur - fadeOut;
            if (!needIn && !needOut) return;
            for (int i = 0; i < n; i++) {
                float t = t0 + i * dt;
                float g = 1f;
                if (needIn && t < fadeIn) { float x = t / fadeIn; g *= sCurve ? x * x * (3f - 2f * x) : x; }
                if (needOut && t > dur - fadeOut) { float x = (dur - t) / fadeOut; if (x < 0f) x = 0f; g *= sCurve ? x * x * (3f - 2f * x) : x; }
                L[off + i] *= g; R[off + i] *= g;
            }
        }
    }

    // ── Dynamics: linked-stereo peak follower. State: [0] envelope. ──
    public static class DynamicsEffect {
        public static void ProcessCompressor(NativeArray<float> state, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            float thrDb = p[q], ratio = Mathf.Max(p[q + 1], 1f);
            float attack = Mathf.Exp(-1f / (Mathf.Max(p[q + 2], 0.1f) * 0.001f * sr));
            float release = Mathf.Exp(-1f / (Mathf.Max(p[q + 3], 1f) * 0.001f * sr));
            float makeup = ZoundEffects.DbToLinear(p[q + 4]);
            float thrLin = ZoundEffects.DbToLinear(thrDb);
            float slope = 1f - 1f / ratio;
            float thrLog2 = ZoundEffects.FastLog2(thrLin);
            float env = state[s];
            for (int i = 0; i < n; i++) {
                float l = L[off + i], r = R[off + i];
                float peak = l < 0f ? -l : l; float ar = r < 0f ? -r : r; if (ar > peak) peak = ar;
                env = peak > env ? attack * env + (1f - attack) * peak : release * env + (1f - release) * peak;
                float g = makeup;
                if (env > thrLin) {
                    // gain = (env/thr)^(-slope), in the log2 domain to avoid per-sample transcendentals
                    g *= ZoundEffects.FastExp2(-(ZoundEffects.FastLog2(env) - thrLog2) * slope);
                }
                L[off + i] = l * g; R[off + i] = r * g;
            }
            state[s] = ZoundEffects.Sane(env);
        }

        // Transient shaper (classic broadband design): two peak followers on the same signal, one fast
        // (0.2 ms attack) that catches the hit and one slow ("Speed" attack) that tracks the body. How far
        // the fast one sits above the slow one is the transient signal t in 0..1 (saturating at 12 dB);
        // gain = Attack dB scaled by t plus Sustain dB scaled by 1 - t. Level independent: a quiet hit
        // and a loud hit shape the same. State: [0] fast envelope, [1] slow envelope.
        public static void ProcessTransientShaper(NativeArray<float> state, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            float attackDb = p[q], sustainDb = p[q + 1];
            float fastA = Mathf.Exp(-1f / (0.0002f * sr));
            float slowA = Mathf.Exp(-1f / (Mathf.Max(p[q + 2], 2f) * 0.001f * sr));
            float rel = Mathf.Exp(-1f / (Mathf.Max(p[q + 3], 10f) * 0.001f * sr));
            const float floor = 1e-4f;          // -80 dBFS: silence reads as "no transient"
            const float log2PerDb = 1f / 6.0206f;
            float attackL2 = attackDb * log2PerDb, sustainL2 = sustainDb * log2PerDb;
            float fast = state[s], slow = state[s + 1];
            for (int i = 0; i < n; i++) {
                float l = L[off + i], r = R[off + i];
                float peak = l < 0f ? -l : l; float ar = r < 0f ? -r : r; if (ar > peak) peak = ar;
                fast = peak > fast ? fastA * fast + (1f - fastA) * peak : rel * fast + (1f - rel) * peak;
                // The slow follower tracks the fast envelope, not the raw rectified signal: fed the raw signal
                // its long attack would only see the quarter-cycle where the wave is above it and settle well
                // below the true level on any tone, reading a steady body as a permanent transient. Downward
                // it follows immediately (a second release would lag the fast one by a t·e^-t term and leave
                // the slow follower sitting above the fast one when the next hit lands, halving the detection).
                slow = fast > slow ? slowA * slow + (1f - slowA) * fast : fast;
                float t = (ZoundEffects.FastLog2(fast + floor) - ZoundEffects.FastLog2(slow + floor)) * 0.5f; // 12 dB above = 1
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                float g = ZoundEffects.FastExp2(attackL2 * t + sustainL2 * (1f - t));
                L[off + i] = l * g; R[off + i] = r * g;
            }
            state[s] = ZoundEffects.Sane(fast);
            state[s + 1] = ZoundEffects.Sane(slow);
        }

        // Zero-latency feedback limiter: instant attack, parameterized release, hard ceiling.
        public static void ProcessLimiter(NativeArray<float> state, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            float ceiling = ZoundEffects.DbToLinear(p[q]);
            float release = Mathf.Exp(-1f / (Mathf.Max(p[q + 1], 1f) * 0.001f * sr));
            float env = state[s];
            for (int i = 0; i < n; i++) {
                float l = L[off + i], r = R[off + i];
                float peak = l < 0f ? -l : l; float ar = r < 0f ? -r : r; if (ar > peak) peak = ar;
                env = peak > env ? peak : release * env + (1f - release) * peak;
                float g = env > ceiling ? ceiling / env : 1f;
                l *= g; r *= g;
                if (l > ceiling) l = ceiling; else if (l < -ceiling) l = -ceiling;
                if (r > ceiling) r = ceiling; else if (r < -ceiling) r = -ceiling;
                L[off + i] = l; R[off + i] = r;
            }
            state[s] = ZoundEffects.Sane(env);
        }
    }

    // ── Biquad (RBJ). State per filter: [0..3] L x1 x2 y1 y2, [4..7] R, [8..12] b0 b1 b2 a1 a2, [13] lastF, [14] lastQ/gain. ──
    public static class BiquadEffect {
        public const int FLOATS = 16;

        public static void Reset(NativeArray<float> st, int s) { st[s + 13] = -1f; }

        public static void SetLowPass(NativeArray<float> st, int s, float cutoff, float qf, int sr) {
            cutoff = Mathf.Clamp(cutoff, 10f, sr * 0.45f);
            float w = 2f * Mathf.PI * cutoff / sr;
            float alpha = Mathf.Sin(w) / (2f * Mathf.Max(qf, 0.05f));
            float cosW = Mathf.Cos(w);
            float norm = 1f / (1f + alpha);
            st[s + 8] = ((1f - cosW) * 0.5f) * norm;
            st[s + 9] = (1f - cosW) * norm;
            st[s + 10] = ((1f - cosW) * 0.5f) * norm;
            st[s + 11] = (-2f * cosW) * norm;
            st[s + 12] = (1f - alpha) * norm;
        }

        public static void SetHighPass(NativeArray<float> st, int s, float cutoff, float qf, int sr) {
            cutoff = Mathf.Clamp(cutoff, 10f, sr * 0.45f);
            float w = 2f * Mathf.PI * cutoff / sr;
            float alpha = Mathf.Sin(w) / (2f * Mathf.Max(qf, 0.05f));
            float cosW = Mathf.Cos(w);
            float norm = 1f / (1f + alpha);
            st[s + 8] = ((1f + cosW) * 0.5f) * norm;
            st[s + 9] = -(1f + cosW) * norm;
            st[s + 10] = ((1f + cosW) * 0.5f) * norm;
            st[s + 11] = (-2f * cosW) * norm;
            st[s + 12] = (1f - alpha) * norm;
        }

        public static void SetPeaking(NativeArray<float> st, int s, float freq, float gainDb, float qf, int sr) {
            freq = Mathf.Clamp(freq, 10f, sr * 0.45f);
            float a = Mathf.Pow(10f, gainDb / 40f);
            float w = 2f * Mathf.PI * freq / sr;
            float alpha = Mathf.Sin(w) / (2f * qf);
            float cosW = Mathf.Cos(w);
            float norm = 1f / (1f + alpha / a);
            st[s + 8] = (1f + alpha * a) * norm;
            st[s + 9] = (-2f * cosW) * norm;
            st[s + 10] = (1f - alpha * a) * norm;
            st[s + 11] = (-2f * cosW) * norm;
            st[s + 12] = (1f - alpha / a) * norm;
        }

        /// <summary>Runs one stereo biquad (Direct Form I) over the block.</summary>
        public static void Run(NativeArray<float> st, int s, NativeArray<float> L, NativeArray<float> R, int off, int n) {
            float b0 = st[s + 8], b1 = st[s + 9], b2 = st[s + 10], a1 = st[s + 11], a2 = st[s + 12];
            float lx1 = st[s], lx2 = st[s + 1], ly1 = st[s + 2], ly2 = st[s + 3];
            float rx1 = st[s + 4], rx2 = st[s + 5], ry1 = st[s + 6], ry2 = st[s + 7];
            for (int i = 0; i < n; i++) {
                float x = L[off + i];
                float y = b0 * x + b1 * lx1 + b2 * lx2 - a1 * ly1 - a2 * ly2;
                lx2 = lx1; lx1 = x; ly2 = ly1; ly1 = y; L[off + i] = y;
                x = R[off + i];
                y = b0 * x + b1 * rx1 + b2 * rx2 - a1 * ry1 - a2 * ry2;
                rx2 = rx1; rx1 = x; ry2 = ry1; ry1 = y; R[off + i] = y;
            }
            st[s] = lx1; st[s + 1] = lx2; st[s + 2] = ZoundEffects.Sane(ly1); st[s + 3] = ZoundEffects.Sane(ly2);
            st[s + 4] = rx1; st[s + 5] = rx2; st[s + 6] = ZoundEffects.Sane(ry1); st[s + 7] = ZoundEffects.Sane(ry2);
        }

        // Coefficients are recomputed at control rate (block start) when cutoff or Q moved; the block is
        // short enough (64 samples) that a swept cutoff sounds continuous.
        public static void Process(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr, bool highPass) {
            float cutoff = p[q], qf = p[q + 1];
            if (st[s + 13] != cutoff || st[s + 14] != qf) {
                if (highPass) SetHighPass(st, s, cutoff, qf, sr); else SetLowPass(st, s, cutoff, qf, sr);
                st[s + 13] = cutoff; st[s + 14] = qf;
            }
            Run(st, s, L, R, off, n);
        }
    }

    // ── EQ: seven peaking bands + low cut + high cut, nine biquads of 16 floats each. ──
    public static class EqEffect {
        private static readonly float[] bandFreq = { 60f, 150f, 400f, 1000f, 2500f, 6000f, 12000f };
        private static readonly float[] bandQ = { 0.7f, 0.8f, 1.0f, 1.0f, 1.0f, 0.8f, 0.7f };

        public static void Reset(NativeArray<float> st, int s) {
            for (int b = 0; b < 9; b++) st[s + b * BiquadEffect.FLOATS + 13] = float.NaN;
        }

        public static void Process(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            for (int b = 0; b < 7; b++) {
                float gain = p[q + b];
                if (gain > -0.01f && gain < 0.01f) continue;
                int bs = s + b * BiquadEffect.FLOATS;
                if (st[bs + 13] != gain) { BiquadEffect.SetPeaking(st, bs, bandFreq[b], gain, bandQ[b], sr); st[bs + 13] = gain; }
                BiquadEffect.Run(st, bs, L, R, off, n);
            }
            float lowCut = p[q + 7];
            if (lowCut > 20f) {
                int bs = s + 7 * BiquadEffect.FLOATS;
                if (st[bs + 13] != lowCut) { BiquadEffect.SetHighPass(st, bs, lowCut, 0.707f, sr); st[bs + 13] = lowCut; }
                BiquadEffect.Run(st, bs, L, R, off, n);
            }
            float highCut = p[q + 8];
            if (highCut < 21900f) {
                int bs = s + 8 * BiquadEffect.FLOATS;
                if (st[bs + 13] != highCut) { BiquadEffect.SetLowPass(st, bs, highCut, 0.707f, sr); st[bs + 13] = highCut; }
                BiquadEffect.Run(st, bs, L, R, off, n);
            }
        }
    }

    // ── Delay: state [0] write index, [1] ring frames, [2] unused, [4..4+ring) L ring, then R ring. ──
    public static class DelayEffect {
        private const int HEADER = 4;

        public static void Reset(NativeArray<float> st, int s, NativeArray<float> pBase, int q, int sr) {
            float maxMs = Mathf.Clamp(pBase[q + 3], 10f, ZoundEffectDescriptors.MAX_DELAY_MS);
            int ring = Mathf.CeilToInt(maxMs * 0.001f * sr) + 4;
            st[s] = 0f;
            st[s + 1] = ring;
        }

        public static void Process(NativeArray<float> st, int s, NativeArray<float> pStart, NativeArray<float> pStep, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            int ring = (int)st[s + 1];
            if (ring < 8) return;
            int w = (int)st[s];
            int baseL = s + HEADER, baseR = baseL + ring;
            float timeMs = pStart[q], timeStep = pStep[q];
            float fb = pStart[q + 1], mix = pStart[q + 2];
            bool pingPong = pStart[q + 4] >= 0.5f;
            float maxDelay = ring - 3;
            float dry = 1f - mix;
            for (int i = 0; i < n; i++) {
                float d = timeMs * 0.001f * sr;
                if (d < 1f) d = 1f; else if (d > maxDelay) d = maxDelay;
                float rp = w - d; if (rp < 0f) rp += ring;
                int r0 = (int)rp; float t = rp - r0;
                // At a delay of a whole number of samples, w - d lands a hair below zero and
                // adding ring rounds back up to exactly ring, so r0 == ring and st[baseL + ring]
                // is the first sample of the RIGHT channel's ring: the left channel played the
                // right channel's audio for two samples at every buffer wrap (~twice a second at
                // the default max time). Found by comparing this engine against the native port.
                if (r0 >= ring) r0 -= ring;
                int r1 = r0 + 1; if (r1 >= ring) r1 -= ring;
                float wetL = st[baseL + r0] + (st[baseL + r1] - st[baseL + r0]) * t;
                float wetR = st[baseR + r0] + (st[baseR + r1] - st[baseR + r0]) * t;
                float inL = L[off + i], inR = R[off + i];
                if (pingPong) {
                    st[baseL + w] = ZoundEffects.Sane(inL + wetR * fb);
                    st[baseR + w] = ZoundEffects.Sane(inR + wetL * fb);
                }
                else {
                    st[baseL + w] = ZoundEffects.Sane(inL + wetL * fb);
                    st[baseR + w] = ZoundEffects.Sane(inR + wetR * fb);
                }
                L[off + i] = inL * dry + wetL * mix;
                R[off + i] = inR * dry + wetR * mix;
                w++; if (w >= ring) w = 0;
                timeMs += timeStep;
            }
            st[s] = w;
        }
    }

    // ── Reverb: Freeverb (8 combs + 4 allpasses per channel, stereo spread). ──
    // State: header 64 floats (comb index/filterstore ×16, allpass index ×8), then the buffers in order.
    public static class ReverbEffect {
        private const int HEADER = 64;
        private const float FIXED_GAIN = 0.015f;
        private const float SCALE_WET = 3f;
        private const float SCALE_DAMP = 0.4f;
        private const float SCALE_ROOM = 0.28f;
        private const float OFFSET_ROOM = 0.7f;

        private static int CombLen(int i, int ch, int sr) => Mathf.CeilToInt(ZoundEffectDescriptors.ReverbCombTuning[i] * (sr / 44100f)) + (ch == 1 ? ZoundEffectDescriptors.ReverbStereoSpread : 0);
        private static int AllpassLen(int i, int ch, int sr) => Mathf.CeilToInt(ZoundEffectDescriptors.ReverbAllpassTuning[i] * (sr / 44100f)) + (ch == 1 ? ZoundEffectDescriptors.ReverbStereoSpread : 0);

        // Header layout: [0..31] comb (index, filterstore) ×16, [32..39] allpass index ×8, [40..55] comb lengths ×16, [56..63] allpass lengths ×8.
        public static void Reset(NativeArray<float> st, int s, int sr) {
            for (int i = 0; i < HEADER; i++) st[s + i] = 0f;
            for (int c = 0; c < 8; c++) for (int ch = 0; ch < 2; ch++) st[s + 40 + c * 2 + ch] = CombLen(c, ch, sr);
            for (int a = 0; a < 4; a++) for (int ch = 0; ch < 2; ch++) st[s + 56 + a * 2 + ch] = AllpassLen(a, ch, sr);
        }

        public static void Process(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            float room = p[q] * SCALE_ROOM + OFFSET_ROOM;
            float damp = p[q + 1] * SCALE_DAMP;
            float width = p[q + 2];
            float mix = p[q + 3];
            float wet = mix * SCALE_WET;
            float wet1 = wet * (width * 0.5f + 0.5f);
            float wet2 = wet * ((1f - width) * 0.5f);
            float dry = 1f - mix;
            float damp1 = damp, damp2 = 1f - damp;

            if (st[s + 40] == 0f) Reset(st, s, sr); // lengths not laid out yet (layout built before Reset ran)
            // Scratch (mono input, accL, accR) lives at the end of the header region; blocks never exceed
            // CONTROL_BLOCK samples. Each comb/allpass is then run over the whole block with local cursors.
            int inp = s + HEADER, accL = inp + SCRATCH, accR = accL + SCRATCH;
            for (int i = 0; i < n; i++) {
                st[inp + i] = (L[off + i] + R[off + i]) * FIXED_GAIN;
                st[accL + i] = 0f; st[accR + i] = 0f;
            }
            int buf = s + HEADER + 3 * SCRATCH;
            for (int c = 0; c < 8; c++) {
                for (int ch = 0; ch < 2; ch++) {
                    int len = (int)st[s + 40 + c * 2 + ch];
                    int hi = s + (c * 2 + ch) * 2; // [hi] index, [hi+1] filterstore
                    int idx = (int)st[hi];
                    float fs = st[hi + 1];
                    int acc = ch == 0 ? accL : accR;
                    for (int i = 0; i < n; i++) {
                        float output = st[buf + idx];
                        fs = output * damp2 + fs * damp1;
                        st[buf + idx] = st[inp + i] + fs * room;
                        idx++; if (idx >= len) idx = 0;
                        st[acc + i] += output;
                    }
                    st[hi] = idx; st[hi + 1] = ZoundEffects.Sane(fs);
                    buf += len;
                }
            }
            for (int a = 0; a < 4; a++) {
                for (int ch = 0; ch < 2; ch++) {
                    int len = (int)st[s + 56 + a * 2 + ch];
                    int hi = s + 32 + a * 2 + ch;
                    int idx = (int)st[hi];
                    int acc = ch == 0 ? accL : accR;
                    for (int i = 0; i < n; i++) {
                        float x = st[acc + i];
                        float bufout = st[buf + idx];
                        st[acc + i] = -x + bufout;
                        st[buf + idx] = ZoundEffects.Sane(x + bufout * 0.5f);
                        idx++; if (idx >= len) idx = 0;
                    }
                    st[hi] = idx;
                    buf += len;
                }
            }
            for (int i = 0; i < n; i++) {
                float l = L[off + i], r = R[off + i];
                float oL = st[accL + i], oR = st[accR + i];
                L[off + i] = oL * wet1 + oR * wet2 + l * dry;
                R[off + i] = oR * wet1 + oL * wet2 + r * dry;
            }
        }

        private const int SCRATCH = ZoundDspConstants.CONTROL_BLOCK;
    }

    // ── Flanger / Chorus: one stereo ring, LFO-modulated read taps. State: [0] write idx, [1] ring frames, [2] LFO phase, [4..) L ring then R ring. ──
    public static class ModDelayEffect {
        private const int HEADER = 4;

        public static void Reset(NativeArray<float> st, int s, float maxMs, int sr) {
            st[s] = 0f;
            st[s + 1] = ZoundEffectDescriptors.ModDelayFrames(maxMs, sr);
            st[s + 2] = 0f;
        }

        private static float ReadTap(NativeArray<float> st, int baseIdx, int ring, int w, float delaySamples) {
            if (delaySamples < 1f) delaySamples = 1f; else if (delaySamples > ring - 3) delaySamples = ring - 3;
            float rp = w - delaySamples; if (rp < 0f) rp += ring;
            int r0 = (int)rp; float t = rp - r0;
            int r1 = r0 + 1; if (r1 >= ring) r1 -= ring;
            return st[baseIdx + r0] + (st[baseIdx + r1] - st[baseIdx + r0]) * t;
        }

        public static void ProcessFlanger(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            int ring = (int)st[s + 1]; if (ring < 8) return;
            int w = (int)st[s];
            float phase = st[s + 2];
            float rate = p[q], depthMs = p[q + 1], fb = p[q + 2], mix = p[q + 3];
            float dry = 1f - mix;
            float phaseInc = rate / sr;
            int baseL = s + HEADER, baseR = baseL + ring;
            for (int i = 0; i < n; i++) {
                float lfo = 0.5f + 0.5f * Mathf.Sin(phase * 6.2831853f);
                float d = (0.5f + depthMs * lfo) * 0.001f * sr;
                float wetL = ReadTap(st, baseL, ring, w, d);
                float wetR = ReadTap(st, baseR, ring, w, d);
                float inL = L[off + i], inR = R[off + i];
                st[baseL + w] = ZoundEffects.Sane(inL + wetL * fb);
                st[baseR + w] = ZoundEffects.Sane(inR + wetR * fb);
                L[off + i] = inL * dry + wetL * mix;
                R[off + i] = inR * dry + wetR * mix;
                w++; if (w >= ring) w = 0;
                phase += phaseInc; if (phase >= 1f) phase -= 1f;
            }
            st[s] = w; st[s + 2] = phase;
        }

        public static void ProcessChorus(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            int ring = (int)st[s + 1]; if (ring < 8) return;
            int w = (int)st[s];
            float phase = st[s + 2];
            float rate = p[q], depthMs = p[q + 1];
            int voices = (int)p[q + 2]; if (voices < 1) voices = 1; else if (voices > 4) voices = 4;
            float mix = p[q + 3];
            float dry = 1f - mix;
            float phaseInc = rate / sr;
            float baseMs = 10f;
            float invVoices = 1f / voices;
            int baseL = s + HEADER, baseR = baseL + ring;
            for (int i = 0; i < n; i++) {
                float inL = L[off + i], inR = R[off + i];
                st[baseL + w] = inL; st[baseR + w] = inR;
                float wetL = 0f, wetR = 0f;
                for (int v = 0; v < voices; v++) {
                    float ph = phase + v * (1f / voices);
                    float lfo = Mathf.Sin(ph * 6.2831853f);
                    float d = (baseMs + depthMs * lfo) * 0.001f * sr;
                    // Alternate voices between channels for width.
                    if ((v & 1) == 0) { wetL += ReadTap(st, baseL, ring, w, d); wetR += ReadTap(st, baseR, ring, w, d * 1.03f); }
                    else { wetL += ReadTap(st, baseL, ring, w, d * 1.03f); wetR += ReadTap(st, baseR, ring, w, d); }
                }
                L[off + i] = inL * dry + wetL * invVoices * mix;
                R[off + i] = inR * dry + wetR * invVoices * mix;
                w++; if (w >= ring) w = 0;
                phase += phaseInc; if (phase >= 1f) phase -= 1f;
            }
            st[s] = w; st[s + 2] = phase;
        }
    }

    // ── Phaser: first-order all-pass cascade swept by an LFO. State: [0] phase, [1] fbL, [2] fbR, [4 + (stage*2+ch)*2 ..] x1, y1. ──
    public static class PhaserEffect {
        public static void Process(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            float rate = p[q], depth = p[q + 1];
            int stages = (int)p[q + 2]; if (stages < 2) stages = 2; else if (stages > 12) stages = 12;
            float fb = p[q + 3], mix = p[q + 4];
            float dry = 1f - mix;
            float phase = st[s];
            float phaseInc = rate / sr;
            float fbL = st[s + 1], fbR = st[s + 2];
            // Sweep coefficient once per block (control rate); the sweep is slow by nature.
            float lfo = 0.5f + 0.5f * Mathf.Sin(phase * 6.2831853f);
            float freq = 200f + depth * 3000f * lfo;
            float wt = Mathf.Tan(Mathf.PI * Mathf.Min(freq, sr * 0.45f) / sr);
            float a = (1f - wt) / (1f + wt);
            for (int i = 0; i < n; i++) {
                float inL = L[off + i], inR = R[off + i];
                float xl = inL + fbL * fb, xr = inR + fbR * fb;
                for (int k = 0; k < stages; k++) {
                    int si = s + 4 + (k * 2) * 2;
                    float y = -a * xl + st[si] + a * st[si + 1];
                    st[si] = xl; st[si + 1] = y; xl = y;
                    si += 2;
                    y = -a * xr + st[si] + a * st[si + 1];
                    st[si] = xr; st[si + 1] = y; xr = y;
                }
                fbL = ZoundEffects.Sane(xl); fbR = ZoundEffects.Sane(xr);
                L[off + i] = inL * dry + xl * mix;
                R[off + i] = inR * dry + xr * mix;
            }
            phase += phaseInc * n; if (phase >= 1f) phase -= (int)phase;
            st[s] = phase; st[s + 1] = fbL; st[s + 2] = fbR;
        }
    }

    // ── Bit crusher: quantize to N bits, hold samples for a downsample factor. State: [0] holdL, [1] holdR, [2] counter. ──
    public static class BitCrushEffect {
        public static void Process(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n) {
            float bits = p[q]; if (bits < 1f) bits = 1f;
            float levels = Mathf.Pow(2f, bits - 1f);
            float invLevels = 1f / levels;
            int factor = (int)p[q + 1]; if (factor < 1) factor = 1;
            float mix = p[q + 2], dry = 1f - mix;
            float holdL = st[s], holdR = st[s + 1];
            int counter = (int)st[s + 2];
            for (int i = 0; i < n; i++) {
                if (counter <= 0) {
                    holdL = Mathf.Round(L[off + i] * levels) * invLevels;
                    holdR = Mathf.Round(R[off + i] * levels) * invLevels;
                    counter = factor;
                }
                counter--;
                L[off + i] = L[off + i] * dry + holdL * mix;
                R[off + i] = R[off + i] * dry + holdR * mix;
            }
            st[s] = holdL; st[s + 1] = holdR; st[s + 2] = counter;
        }
    }

    // ── Distortion: drive into a soft clipper, tone as a one-pole low-pass, DC blocker. State: [0] lpL, [1] lpR, [2..5] DC x1/y1 per channel. ──
    public static class DistortionEffect {
        public static void Process(NativeArray<float> st, int s, NativeArray<float> p, int q, NativeArray<float> L, NativeArray<float> R, int off, int n, int sr) {
            float drive = p[q], tone = p[q + 1], mix = p[q + 2];
            float dry = 1f - mix;
            float norm = 1f / SoftClip(drive); // unity for a full-scale input
            float toneHz = 500f * Mathf.Pow(24f, tone); // 500 Hz .. 12 kHz
            float lpCoeff = 1f - Mathf.Exp(-2f * Mathf.PI * toneHz / sr);
            float lpL = st[s], lpR = st[s + 1];
            float dcx1L = st[s + 2], dcy1L = st[s + 3], dcx1R = st[s + 4], dcy1R = st[s + 5];
            const float dcR = 0.995f;
            for (int i = 0; i < n; i++) {
                float inL = L[off + i], inR = R[off + i];
                float l = SoftClip(inL * drive) * norm;
                float r = SoftClip(inR * drive) * norm;
                lpL += (l - lpL) * lpCoeff; lpR += (r - lpR) * lpCoeff;
                float yl = lpL - dcx1L + dcR * dcy1L; dcx1L = lpL; dcy1L = yl;
                float yr = lpR - dcx1R + dcR * dcy1R; dcx1R = lpR; dcy1R = yr;
                L[off + i] = inL * dry + yl * mix;
                R[off + i] = inR * dry + yr * mix;
            }
            st[s] = ZoundEffects.Sane(lpL); st[s + 1] = ZoundEffects.Sane(lpR);
            st[s + 2] = dcx1L; st[s + 3] = ZoundEffects.Sane(dcy1L); st[s + 4] = dcx1R; st[s + 5] = ZoundEffects.Sane(dcy1R);
        }

        // tanh approximation, monotonic and bounded in [-1, 1].
        private static float SoftClip(float x) {
            if (x > 3f) return 1f; if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }
    }

}

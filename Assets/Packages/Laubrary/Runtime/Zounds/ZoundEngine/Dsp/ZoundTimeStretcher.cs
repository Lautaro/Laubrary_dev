using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds.Dsp {

    public sealed class TimeStretchDesc {
        public TimeStretchAlgorithm algorithm;
        public string displayName;
        public string summary;
        public bool available;
        public ParamDesc[] parameters;
    }

    /// <summary>Selectable time-stretch algorithms with their parameters (same pattern as the effect descriptors).</summary>
    public static class TimeStretchDescriptors {
        private static readonly TimeStretchDesc[] all = {
            new TimeStretchDesc { algorithm = TimeStretchAlgorithm.Granular, displayName = "Granular", available = true,
                summary = "Grains re-spaced to the new length. Characterful rather than transparent; cheap; often the wanted sound for magic and roars.",
                parameters = new[] {
                    new ParamDesc("Grain", "ms", 10f, 200f, 60f, ParamCurve.Logarithmic, false),
                    new ParamDesc("Overlap", "", 0.1f, 0.9f, 0.5f, ParamCurve.Linear, false),
                    new ParamDesc("Jitter", "", 0f, 1f, 0.2f, ParamCurve.Linear, false),
                    new ParamDesc("Keep hits", "ms", 0f, 200f, 40f, ParamCurve.Linear, false),
                } },
            new TimeStretchDesc { algorithm = TimeStretchAlgorithm.Wsola, displayName = "WSOLA", available = true,
                summary = "Overlap-add with waveform-similarity search. Transparent on voices, single sounds and speech; can double on dense material.",
                parameters = new[] {
                    new ParamDesc("Window", "ms", 10f, 80f, 30f, ParamCurve.Logarithmic, false),
                    new ParamDesc("Search", "ms", 1f, 25f, 10f, ParamCurve.Logarithmic, false),
                    new ParamDesc("Keep hits", "ms", 0f, 200f, 40f, ParamCurve.Linear, false),
                } },
            new TimeStretchDesc { algorithm = TimeStretchAlgorithm.PhaseVocoder, displayName = "Phase vocoder", available = false,
                summary = "Not built: needs an FFT and phase locking. Planned only if real material demands it.", parameters = new ParamDesc[0] },
            new TimeStretchDesc { algorithm = TimeStretchAlgorithm.Psola, displayName = "PSOLA", available = false,
                summary = "Not built: needs a pitch tracker and suits pitched monophonic material, which this library has little of.", parameters = new ParamDesc[0] },
        };
        public static TimeStretchDesc Get(TimeStretchAlgorithm a) => all[(int)a];
        public static int Count => all.Length;
    }

    /// <summary>
    /// Offline time-stretch of a PCM clip: duration changes, pitch stays. Runs on the main thread when a
    /// stretched variant is first needed and is cached beside the source PCM (one buffer per distinct
    /// settings, never an asset). Both algorithms walk OUTPUT frames and pull input at a position-dependent
    /// speed, so Uniform, Region and Envelope modes share one loop.
    /// </summary>
    public static class ZoundTimeStretcher {

        private static readonly Dictionary<long, PcmClip> cache = new Dictionary<long, PcmClip>();
        private static long cachedBytes;

        public static int CachedCount => cache.Count;
        public static long CachedBytes => cachedBytes;

        public static void Clear() { cache.Clear(); cachedBytes = 0; }

        /// <summary>The stretched buffer for a clip region under these settings (computed once, cached).</summary>
        public static PcmClip Get(PcmClip source, ZoundTimeStretch settings, float trimStart, float trimEnd) {
            if (source == null || !source.valid) return source;
            long key = ((long)(source.clip != null ? source.clip.GetInstanceID() : 0) << 32) ^ (uint)settings.SettingsHash(trimStart, trimEnd);
            if (cache.TryGetValue(key, out var pcm)) return pcm;
            pcm = Render(source, settings, trimStart, trimEnd);
            cache[key] = pcm;
            cachedBytes += pcm.Bytes;
            return pcm;
        }

        /// <summary>Speed multiplier at a source position (seconds into the region): 1 = unchanged, 0.5 = half speed.</summary>
        private static float SpeedAt(ZoundTimeStretch s, float posSeconds, float regionLength, float absoluteOffset) {
            switch (s.mode) {
                case TimeStretchMode.Uniform: return 1f / Mathf.Max(s.factor, 0.05f);
                case TimeStretchMode.Region: {
                    float abs = absoluteOffset + posSeconds;
                    return abs >= s.regionStart && abs < s.regionEnd ? 1f / Mathf.Max(s.factor, 0.05f) : 1f;
                }
                default: {
                    float t = regionLength > 0f ? posSeconds / regionLength : 0f;
                    return Mathf.Clamp(s.speedEnvelope != null ? s.speedEnvelope.Evaluate(Mathf.Clamp01(t)) : 1f, 0.05f, 20f);
                }
            }
        }

        /// <summary>Length of the stretched output for a region of <paramref name="regionLength"/> seconds starting at <paramref name="absoluteOffset"/> into the clip: the integral of 1/speed over the input.</summary>
        public static double OutputSeconds(ZoundTimeStretch s, float regionLength, float absoluteOffset) {
            const int steps = 2000;
            double outSeconds = 0;
            for (int i = 0; i < steps; i++) outSeconds += (regionLength / steps) / SpeedAt(s, (i + 0.5f) * regionLength / steps, regionLength, absoluteOffset);
            return outSeconds;
        }

        public static PcmClip Render(PcmClip src, ZoundTimeStretch s, float trimStart, float trimEnd) {
            int ch = src.channels, sr = src.frequency;
            int startFrame = Mathf.Clamp(Mathf.RoundToInt(trimStart * sr), 0, src.frames);
            int endFrame = trimEnd > trimStart ? Mathf.Clamp(Mathf.RoundToInt(trimEnd * sr), startFrame, src.frames) : src.frames;
            int inFrames = endFrame - startFrame;
            var result = new PcmClip { clip = src.clip, channels = ch, frequency = sr, valid = true };
            if (inFrames < 64) { result.samples = new float[0]; result.frames = 0; return result; }
            float regionLength = (float)inFrames / sr;
            s.EnsureParams();

            double outSeconds = OutputSeconds(s, regionLength, trimStart);
            int outFrames = Mathf.Max(64, (int)(outSeconds * sr));

            int keepParam = s.algorithm == TimeStretchAlgorithm.Wsola ? 2 : 3;
            var map = SpeedMap.Build(src.samples, ch, sr, startFrame, inFrames, s, regionLength, trimStart, s.Param(keepParam), outSeconds);

            float[] output;
            if (s.algorithm == TimeStretchAlgorithm.Wsola) output = Wsola(src.samples, ch, sr, startFrame, inFrames, outFrames, s, map);
            else output = Granular(src.samples, ch, sr, startFrame, inFrames, outFrames, s, map);

            result.samples = output;
            result.frames = outFrames;
            float peak = 0f;
            for (int i = 0; i < output.Length; i++) { float a = output[i] < 0 ? -output[i] : output[i]; if (a > peak) peak = a; }
            result.peak = peak;
            return result;
        }

        private static float Hann(int i, int n) => 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (n - 1));

        /// <summary>
        /// Effective playback speed over the input region at 1 ms resolution: the mode's own speed map,
        /// with every hit kept intact. Stretching percussive material by re-reading it repeats the attack
        /// (a coin at ×1.7 clinked like a machine gun: the 2026-09-18 "machine gun" report), so the
        /// "Keep hits" window after each detected onset is copied through at speed 1 and the remaining
        /// material is stretched a little more to land on the same total length.
        /// </summary>
        private sealed class SpeedMap {
            private float[] speed;   // per 1 ms of input
            private bool[] kept;
            private int sr;

            public float At(double inPosFrames) {
                int i = (int)(inPosFrames * 1000.0 / sr);
                if (i < 0) i = 0; else if (i >= speed.Length) i = speed.Length - 1;
                return speed[i];
            }
            public bool Kept(double inPosFrames) {
                int i = (int)(inPosFrames * 1000.0 / sr);
                if (i < 0) i = 0; else if (i >= kept.Length) i = kept.Length - 1;
                return kept[i];
            }

            public static SpeedMap Build(float[] src, int ch, int sr, int start, int inFrames, ZoundTimeStretch s, float regionLength, float absOffset, float keepMs, double desiredOutSeconds) {
                int ms = Mathf.Max(1, (int)(inFrames * 1000L / sr));
                var m = new SpeedMap { speed = new float[ms], kept = new bool[ms], sr = sr };
                for (int i = 0; i < ms; i++) m.speed[i] = SpeedAt(s, (i + 0.5f) * 0.001f, regionLength, absOffset);
                if (keepMs < 1f) return m;

                // Onsets: 2 ms block peaks; a block that jumps to 3× the decaying envelope and above 10 % of
                // the region's peak is a hit.
                int blk = Mathf.Max(1, sr / 500);
                int blocks = inFrames / blk;
                float peak = 0f;
                var env = new float[blocks];
                for (int b = 0; b < blocks; b++) {
                    float mx = 0f;
                    for (int f = b * blk; f < (b + 1) * blk; f++) for (int c = 0; c < ch; c++) { float a = src[(start + f) * ch + c]; if (a < 0f) a = -a; if (a > mx) mx = a; }
                    env[b] = mx; if (mx > peak) peak = mx;
                }
                float e = 0f; int keep = (int)keepMs;
                for (int b = 0; b < blocks; b++) {
                    float prev = e * 0.85f;
                    if (env[b] > peak * 0.1f && env[b] > prev * 3f) {
                        int from = b * 2, to = Mathf.Min(ms, from + keep);
                        for (int i = from; i < to; i++) m.kept[i] = true;
                    }
                    e = env[b] > prev ? env[b] : prev;
                }

                // Kept material plays at speed 1; the rest absorbs the whole stretch so the total length
                // is unchanged: k scales the free part's 1/speed to fit the remaining output time.
                double keptSeconds = 0, freeOut = 0;
                for (int i = 0; i < ms; i++) { if (m.kept[i]) keptSeconds += 0.001; else freeOut += 0.001 / m.speed[i]; }
                double room = desiredOutSeconds - keptSeconds;
                if (room <= 0.005 || freeOut <= 0.0005) { for (int i = 0; i < ms; i++) m.kept[i] = false; return m; } // nothing left to stretch: fall back to plain
                float k = (float)(freeOut / room); // >1 = the free part must stretch harder
                for (int i = 0; i < ms; i++) m.speed[i] = m.kept[i] ? 1f : m.speed[i] * k;
                return m;
            }
        }

        // Grains of `grain` ms with `overlap`, output-hop spaced; each grain's input position follows the
        // speed map, with optional random jitter so long stretches do not buzz at the grain rate.
        private static float[] Granular(float[] src, int ch, int sr, int start, int inFrames, int outFrames, ZoundTimeStretch s, SpeedMap map) {
            int grain = Mathf.Clamp(Mathf.RoundToInt(s.Param(0) * 0.001f * sr), 64, inFrames);
            float overlap = Mathf.Clamp(s.Param(1), 0.1f, 0.9f);
            float jitter = Mathf.Clamp01(s.Param(2));
            int hopOut = Mathf.Max(8, Mathf.RoundToInt(grain * (1f - overlap)));
            var output = new float[outFrames * ch];
            var norm = new float[outFrames];
            var rng = new System.Random(12345);
            double inPos = 0;
            for (int o = 0; o < outFrames; o += hopOut) {
                float speed = map.At(inPos);
                // Inside a kept hit the grains must line up exactly (speed 1, no jitter) so overlap-add is the identity.
                double jit = map.Kept(inPos) ? 0 : jitter * grain * 0.5 * (rng.NextDouble() * 2 - 1);
                int gStart = (int)System.Math.Round(inPos + jit);
                gStart = Mathf.Clamp(gStart, 0, Mathf.Max(0, inFrames - grain));
                for (int i = 0; i < grain && o + i < outFrames; i++) {
                    float w = Hann(i, grain);
                    int si = (start + gStart + i) * ch;
                    int oi = (o + i) * ch;
                    for (int c = 0; c < ch; c++) output[oi + c] += src[si + c] * w;
                    norm[o + i] += w;
                }
                inPos += hopOut * speed;
                if (inPos > inFrames - 1) inPos = inFrames - 1;
            }
            for (int i = 0; i < outFrames; i++) {
                float n = norm[i];
                if (n > 1e-4f) { float g = 1f / n; for (int c = 0; c < ch; c++) output[i * ch + c] *= g; }
            }
            return output;
        }

        // WSOLA: for each output frame, the input frame is taken near the nominal position but shifted to
        // where it best continues the previously written frame (cross-correlation over ±search).
        private static float[] Wsola(float[] src, int ch, int sr, int start, int inFrames, int outFrames, ZoundTimeStretch s, SpeedMap map) {
            int win = Mathf.Clamp(Mathf.RoundToInt(s.Param(0) * 0.001f * sr), 64, inFrames);
            int search = Mathf.Clamp(Mathf.RoundToInt(s.Param(1) * 0.001f * sr), 1, win);
            int hop = win / 2;
            var output = new float[outFrames * ch];
            var norm = new float[outFrames];
            // Mono guide for similarity search (average of channels).
            var mono = new float[inFrames];
            for (int i = 0; i < inFrames; i++) { float v = 0f; for (int c = 0; c < ch; c++) v += src[(start + i) * ch + c]; mono[i] = v / ch; }
            double inPos = 0;
            int prevIn = 0;      // input start of the previous frame
            bool first = true;
            for (int o = 0; o < outFrames; o += hop) {
                float speed = map.At(inPos);
                int nominal = (int)System.Math.Round(inPos);
                int best = nominal;
                // Inside a kept hit the frame continues the previous one exactly; no search.
                if (!first && !map.Kept(inPos)) {
                    // The natural continuation of the previous frame is prevIn + hop; find the candidate
                    // around nominal whose window best matches it. Coarse pass every 8 frames, then the
                    // neighbourhood of the winner sample by sample (the full search was ~200 ms per clip
                    // on the main thread at play time, the "UI lock-up" in the 2026-09-18 report).
                    int target = prevIn + hop;
                    int lo = Mathf.Max(0, nominal - search), hi = Mathf.Min(inFrames - win, nominal + search);
                    int coarse = BestCandidate(mono, inFrames, win, target, lo, hi, 8);
                    best = BestCandidate(mono, inFrames, win, target, Mathf.Max(lo, coarse - 8), Mathf.Min(hi, coarse + 8), 1);
                }
                best = Mathf.Clamp(best, 0, Mathf.Max(0, inFrames - win));
                for (int i = 0; i < win && o + i < outFrames; i++) {
                    float w = Hann(i, win);
                    int si = (start + best + i) * ch;
                    int oi = (o + i) * ch;
                    for (int c = 0; c < ch; c++) output[oi + c] += src[si + c] * w;
                    norm[o + i] += w;
                }
                prevIn = best;
                first = false;
                inPos += hop * speed;
                if (inPos > inFrames - 1) inPos = inFrames - 1;
            }
            for (int i = 0; i < outFrames; i++) {
                float n = norm[i];
                if (n > 1e-4f) { float g = 1f / n; for (int c = 0; c < ch; c++) output[i * ch + c] *= g; }
            }
            return output;
        }

        private static int BestCandidate(float[] mono, int inFrames, int win, int target, int lo, int hi, int step) {
            int best = lo;
            double bestScore = double.NegativeInfinity;
            for (int cand = lo; cand <= hi; cand += step) {
                double score = 0;
                for (int i = 0; i < win; i += 2) {
                    int ti = target + i, ci = cand + i;
                    if (ti >= inFrames || ci >= inFrames) break;
                    score += mono[ti] * mono[ci];
                }
                if (score > bestScore) { bestScore = score; best = cand; }
            }
            return best;
        }
    }

}

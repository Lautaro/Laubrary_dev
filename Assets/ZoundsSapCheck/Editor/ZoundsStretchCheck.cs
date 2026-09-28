using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks.EditorTools {

    /// <summary>
    /// The kept check for live time-stretch (T-0409 / T-0452). Everything here is a measurement against a known answer,
    /// because a real-time stretcher has no baked reference to compare against:
    ///
    /// - **Slicing invariance.** A correct incremental renderer produces the same samples however the host slices time.
    ///   Rendered in blocks of 1, 64, 333, 1024 and 4096 frames and compared with 256, on both the ordinary and the
    ///   compiled path. This catches state lost between blocks and off-by-ones at the hand-offs.
    /// - **Speed 1 is transparent**: with live speed on at speed 1, the output is compared with the direct read.
    /// - **Pitch and cleanliness** on a pure 440 Hz tone at 0.5, 0.25 and 1.5, and during a live sweep: the frequency
    ///   actually produced, and what is left after removing the best-fitting 440 Hz sine (the prototype reached 90 dB).
    /// - **Hits**: a 12-click train must keep 12 clicks at half and quarter speed (without the hit lock it doubles).
    /// - **Length** against the requested one, and **cost** of the compiled render.
    ///
    /// The compiled and ordinary paths are compared with each other only as a report, never as a pass condition: the
    /// sub-sample alignment legitimately tips differently under different rounding (the prototype found the same).
    /// </summary>
    public static class ZoundsStretchCheck {

        const int SR = 48000;

        [MenuItem("Laubrary/Zounds/Checks/15 - Live time-stretch (slicing, pitch, hits, length, cost)")]
        public static void RunFromMenu() => Debug.Log(Run());

        /// <summary>Where the last report is written, so a long run can be read without waiting on it.</summary>
        public static string ReportPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zounds-stretch-check.txt");

        /// <summary>Starts a run on the editor's next update and returns at once; the report lands at <see cref="ReportPath"/>.</summary>
        public static string RunLater() {
            System.IO.File.WriteAllText(ReportPath, "running\n");
            EditorApplication.delayCall += () => System.IO.File.WriteAllText(ReportPath, Run());
            return ReportPath;
        }

        public static string Run() {
            var sb = new StringBuilder("=== LIVE TIME-STRETCH CHECK ===\n");
            var tone = Tone(2.0f, 440f, 0.5f);
            var clicks = ClickTrain(12, 0.25f, 3.2f);
            var cfg = new SapStretchConfig { enabled = true, algorithm = SapStretch.Wsola, windowMs = 30f, keepHits = true, keepMs = 40f };
            bool allPass = true;

            // 1. Slicing invariance.
            sb.Append("\n1. Slicing invariance (same samples whatever the block size; difference vs 256-frame blocks):\n");
            foreach (float speed in new[] { 0.5f, 0.25f, 1.5f }) {
                var reference = RenderPlain(tone, cfg, speed, 256, out _);
                var refC = RenderCompiled(tone, cfg, speed, 256, out _);
                sb.Append("   speed ").Append(speed.ToString("0.00")).Append(":  ordinary");
                foreach (int blk in new[] { 1, 64, 333, 1024, 4096 }) {
                    float d = MaxDiff(reference, RenderPlain(tone, cfg, speed, blk, out _));
                    sb.Append("  ").Append(blk).Append('=').Append(d == 0f ? "0" : d.ToString("E1"));
                    if (d != 0f) allPass = false;
                }
                sb.Append("   | compiled");
                foreach (int blk in new[] { 64, 333, 1024, 4096 }) {
                    float d = MaxDiff(refC, RenderCompiled(tone, cfg, speed, blk, out _));
                    sb.Append("  ").Append(blk).Append('=').Append(d == 0f ? "0" : d.ToString("E1"));
                    if (d != 0f) allPass = false;
                }
                sb.Append("   | compiled vs ordinary ").Append(MaxDiff(reference, refC).ToString("E1")).Append('\n');
            }

            // 2. Speed 1 against the direct read.
            {
                var direct = RenderPlain(tone, SapStretchConfig.Off, 1f, 256, out _);
                var live = RenderPlain(tone, cfg, 1f, 256, out _);
                float db = ResidualDb(direct, live, SR / 10, Mathf.Min(direct.Length, live.Length) - SR / 10);
                sb.Append("\n2. Speed 1 with live speed on vs the direct read: difference ").Append(db.ToString("0.0")).Append(" dB below the signal")
                  .Append(db < -60f ? "  PASS\n" : "  FAIL\n");
                if (db >= -60f) allPass = false;
            }

            // 3. Pitch and cleanliness on a pure tone.
            sb.Append("\n3. Pure 440 Hz tone (pitch error; worst 2048-sample window left after removing the best-fitting 440 Hz sine):\n");
            foreach (float speed in new[] { 0.5f, 0.25f, 1.5f }) {
                var o = RenderPlain(tone, cfg, speed, 256, out _);
                ToneQuality(o, out float cents, out float worstDb);
                bool ok = Mathf.Abs(cents) < 1f && worstDb < -60f;
                if (!ok) allPass = false;
                sb.Append("   speed ").Append(speed.ToString("0.00")).Append(": ").Append(cents.ToString("+0.00;-0.00")).Append(" cents, worst ")
                  .Append(worstDb.ToString("0.0")).Append(" dB").Append(ok ? "  PASS\n" : "  FAIL\n");
            }
            {
                var o = RenderSweep(tone, cfg);
                ToneQuality(o, out float cents, out float worstDb);
                bool ok = Mathf.Abs(cents) < 1f && worstDb < -60f;
                if (!ok) allPass = false;
                sb.Append("   live sweep 1 -> 0.3 -> 1: ").Append(cents.ToString("+0.00;-0.00")).Append(" cents, worst ")
                  .Append(worstDb.ToString("0.0")).Append(" dB").Append(ok ? "  PASS\n" : "  FAIL\n");
            }

            // 4. Hits.
            sb.Append("\n4. 12-click train (hits counted):\n");
            foreach (float speed in new[] { 0.5f, 0.25f }) {
                int kept = CountHits(RenderPlain(clicks, cfg, speed, 256, out _));
                var noLock = cfg; noLock.keepHits = false;
                int unlocked = CountHits(RenderPlain(clicks, noLock, speed, 256, out _));
                if (kept != 12) allPass = false;
                sb.Append("   speed ").Append(speed.ToString("0.00")).Append(": ").Append(kept).Append(" with Keep hits")
                  .Append(kept == 12 ? " PASS" : " FAIL").Append(", ").Append(unlocked).Append(" without (shows why the lock exists)\n");
            }

            // 5. Length.
            sb.Append("\n5. Length against the requested one:\n");
            foreach (float speed in new[] { 0.5f, 0.25f, 1.5f }) {
                RenderPlain(clicks, cfg, speed, 256, out int produced);
                float want = clicks.frames / speed;
                float errMs = (produced - want) * 1000f / SR;
                bool ok = Mathf.Abs(errMs) < 60f;
                if (!ok) allPass = false;
                sb.Append("   speed ").Append(speed.ToString("0.00")).Append(": ").Append(errMs.ToString("+0;-0")).Append(" ms").Append(ok ? "  PASS\n" : "  FAIL\n");
            }

            // 6. Cost.
            {
                RenderCompiled(tone, cfg, 0.5f, 1024, out _);   // warm-up: the compiler's first-use cost is not audio time
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var o = RenderCompiled(tone, cfg, 0.5f, 1024, out _);
                watch.Stop();
                double audioSeconds = o.Length / (double)SR;
                sb.Append("\n6. Compiled cost: ").Append((watch.Elapsed.TotalSeconds / audioSeconds * 100.0).ToString("0.00"))
                  .Append(" % of one core per stereo voice (prototype: 0.55-0.65 %)\n");
            }

            sb.Append(allPass ? "\nALL PASS\n" : "\nSOMETHING FAILED (see above)\n");
            return sb.ToString();
        }

        // ── rendering ──

        static SapRealtimeVoice Voice(PcmClip clip, SapStretchConfig cfg, float speed) =>
            SapRealtimeVoice.Create(clip, ChainLayout.Empty, SR, 0d, clip.frames, 1f, 1f, clip.frames / (float)SR, false, 1, false,
                                    Allocator.Persistent, stretch: cfg, baseSpeed: speed);

        static float[] RenderPlain(PcmClip clip, SapStretchConfig cfg, float speed, int block, out int produced) {
            int total = (int)(clip.frames / Mathf.Min(speed, 1f)) + SR;
            var outL = new float[total];
            var v = Voice(clip, cfg, speed);
            int wrote = 0;
            try {
                while (wrote < total && !v.finished) {
                    int n = Mathf.Min(block, total - wrote);
                    v.RenderBlock(n);
                    for (int i = 0; i < n; i++) outL[wrote + i] = v.sap.bufL[i];
                    wrote += n;
                }
            }
            finally { v.Dispose(); }
            produced = wrote;
            System.Array.Resize(ref outL, wrote);
            return outL;
        }

        static float[] RenderSweep(PcmClip clip, SapStretchConfig cfg) {
            int total = (int)(clip.frames / 0.3f) + SR;
            var outL = new float[total];
            var v = Voice(clip, cfg, 1f);
            int wrote = 0;
            try {
                while (wrote < total && !v.finished) {
                    float t = wrote / (float)SR;
                    // 1 -> 0.3 over the first 1.2 s, hold 0.8 s, back to 1 over 1.2 s.
                    float sp = t < 1.2f ? Mathf.Lerp(1f, 0.3f, t / 1.2f) : t < 2f ? 0.3f : Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((t - 2f) / 1.2f));
                    v.Apply(SapVoiceCommand.Speed(sp));
                    int n = Mathf.Min(256, total - wrote);
                    v.RenderBlock(n);
                    for (int i = 0; i < n; i++) outL[wrote + i] = v.sap.bufL[i];
                    wrote += n;
                }
            }
            finally { v.Dispose(); }
            System.Array.Resize(ref outL, wrote);
            return outL;
        }

        static float[] RenderCompiled(PcmClip clip, SapStretchConfig cfg, float speed, int block, out int produced) {
            int total = (int)(clip.frames / Mathf.Min(speed, 1f)) + SR;
            var v = Voice(clip, cfg, speed);
            var jL = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var jR = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var tally = new NativeArray<int>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            try {
                new SapVoiceRenderJob { voice = v, outLeft = jL, outRight = jR, totalFrames = total, blockFrames = block, tally = tally }.Run();
                produced = tally[0];
                var o = new float[produced];
                for (int i = 0; i < produced; i++) o[i] = jL[i];
                return o;
            }
            finally { v.Dispose(); jL.Dispose(); jR.Dispose(); tally.Dispose(); }
        }

        // ── sources ──

        static PcmClip Tone(float seconds, float hz, float amp) {
            int frames = (int)(seconds * SR);
            var s = new float[frames * 2];
            for (int i = 0; i < frames; i++) { float v = amp * Mathf.Sin(2f * Mathf.PI * hz * i / SR); s[i * 2] = v; s[i * 2 + 1] = v; }
            return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = s, valid = true, peak = amp };
        }

        /// <summary>Short decaying clicks over a quiet pad, the prototype's hit test.</summary>
        static PcmClip ClickTrain(int count, float spacing, float seconds) {
            int frames = (int)(seconds * SR);
            var s = new float[frames * 2];
            for (int i = 0; i < frames; i++) {
                float v = 0.02f * Mathf.Sin(2f * Mathf.PI * 220f * i / SR);
                s[i * 2] = v; s[i * 2 + 1] = v;
            }
            int len = (int)(0.014f * SR);
            for (int c = 0; c < count; c++) {
                int at = (int)((0.1f + c * spacing) * SR);
                for (int k = 0; k < len && at + k < frames; k++) {
                    float v = 0.8f * Mathf.Exp(-k / (0.003f * SR)) * Mathf.Sin(2f * Mathf.PI * 1800f * k / SR);
                    s[(at + k) * 2] += v; s[(at + k) * 2 + 1] += v;
                }
            }
            return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = s, valid = true, peak = 0.82f };
        }

        // ── measurements ──

        /// <summary>
        /// Largest difference over the common length. A render stops at the end of whichever block finished the sound,
        /// so lengths differ by up to a block; anything past the shorter one must be silence, or it counts as a difference.
        /// </summary>
        static float MaxDiff(float[] a, float[] b) {
            int n = Mathf.Min(a.Length, b.Length);
            float m = 0f;
            for (int i = 0; i < n; i++) m = Mathf.Max(m, Mathf.Abs(a[i] - b[i]));
            var longer = a.Length > b.Length ? a : b;
            for (int i = n; i < longer.Length; i++) m = Mathf.Max(m, Mathf.Abs(longer[i]));
            return m;
        }

        static float ResidualDb(float[] reference, float[] test, int from, int to) {
            double sig = 1e-20, err = 1e-20;
            for (int i = from; i < to; i++) { sig += reference[i] * (double)reference[i]; double d = test[i] - reference[i]; err += d * d; }
            return (float)(10.0 * System.Math.Log10(err / sig));
        }

        /// <summary>Frequency by a least-squares fit across the whole middle, and the worst window's residual after
        /// removing the best-fitting sine at that window.</summary>
        static void ToneQuality(float[] o, out float cents, out float worstDb) {
            int from = SR / 5, to = o.Length - SR / 5;
            // Frequency from zero crossings with linear interpolation, over the whole middle section.
            double firstZ = -1, lastZ = -1; int crossings = 0;
            for (int i = from + 1; i < to; i++) {
                if (o[i - 1] < 0f && o[i] >= 0f) {
                    double z = i - 1 + o[i - 1] / (double)(o[i - 1] - o[i]);
                    if (firstZ < 0) firstZ = z; else crossings++;
                    lastZ = z;
                }
            }
            double hz = crossings > 0 ? crossings * SR / (lastZ - firstZ) : 0;
            cents = hz > 0 ? (float)(1200.0 * System.Math.Log(hz / 440.0, 2.0)) : float.NaN;

            worstDb = -200f;
            const int W = 2048;
            double w = 2 * System.Math.PI * 440.0 / SR;
            for (int a = from; a + W < to; a += W) {
                double ss = 0, sc = 0, cc = 0, ys = 0, yc = 0, e = 1e-20, sig = 1e-20;
                for (int i = 0; i < W; i++) { double s = System.Math.Sin(w * (a + i)), c = System.Math.Cos(w * (a + i)); ss += s * s; sc += s * c; cc += c * c; ys += o[a + i] * s; yc += o[a + i] * c; }
                double det = ss * cc - sc * sc;
                double A = (ys * cc - yc * sc) / det, B = (yc * ss - ys * sc) / det;
                for (int i = 0; i < W; i++) {
                    double fit = A * System.Math.Sin(w * (a + i)) + B * System.Math.Cos(w * (a + i));
                    double d = o[a + i] - fit; e += d * d; sig += fit * fit;
                }
                worstDb = Mathf.Max(worstDb, (float)(10.0 * System.Math.Log10(e / sig)));
            }
        }

        /// <summary>Hits: 2 ms peaks jumping above both half the loudest peak and 3x a decaying envelope.</summary>
        static int CountHits(float[] o) {
            int blk = SR / 500, blocks = o.Length / blk;
            float peak = 0f;
            var env = new float[blocks];
            for (int b = 0; b < blocks; b++) { float m = 0f; for (int i = b * blk; i < (b + 1) * blk; i++) m = Mathf.Max(m, Mathf.Abs(o[i])); env[b] = m; peak = Mathf.Max(peak, m); }
            int hits = 0; float e = 0f;
            for (int b = 0; b < blocks; b++) {
                float prev = e * 0.85f;
                if (env[b] > peak * 0.5f && env[b] > prev * 3f) hits++;
                e = Mathf.Max(env[b], prev);
            }
            return hits;
        }
    }
}

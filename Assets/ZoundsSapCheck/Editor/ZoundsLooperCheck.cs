using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks.EditorTools {

    /// <summary>
    /// The kept check for the Looper (T-0477). All measurements against a known answer, on synthetic sources:
    ///
    /// 1. **Seamless loop, no crossmix.** A steady 480 Hz tone looped on exactly 480 periods must come out as one
    ///    continuous tone: the worst window's residual after removing the ideal continuous sine, and the level across
    ///    the seams. At pitch 1 and 0.7.
    /// 2. **Seamless loop, crossmix.** The same, with a fixed crossmix and with a random range: the two copies line up,
    ///    so the level across every crossfade must stay flat and nothing may break at the seams.
    /// 3. **Material that does not line up.** The fade law follows the measured correlation of the two regions, so the
    ///    level must also stay flat for a tone whose copies meet a quarter-period apart and for noise (where a plain
    ///    equal-gain fade dips 3 dB). Reported only: copies in exactly opposite phase (they cancel; no law rescues that)
    ///    and a plain wrap on unaligned material (the seam a crossmix exists to hide).
    /// 4. **Per-cycle draws.** With a range, every crossfade's length is inside the range (give or take the 5 ms the seam
    ///    may be nudged to where the copies line up), the top is limited to half the loop, the lengths differ between
    ///    cycles, and the same play draws the same lengths every time.
    /// 5. **Live edits** of the loop region and crossmix on a playing voice, and a **release** ending the voice.
    /// 6. **Slicing invariance.** The same samples whatever the block size (1, 64, 333, 1024, 4096 against 256), on the
    ///    ordinary and the compiled path, for no crossmix, a fixed one and a range.
    /// </summary>
    public static class ZoundsLooperCheck {

        const int SR = 48000;

        [MenuItem("Laubrary/Zounds/Checks/16 - Looper (seamless seams, fades, draws, slicing)")]
        public static void RunFromMenu() => Debug.Log(Run());

        public static string ReportPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zounds-looper-check.txt");

        public static string RunLater() {
            System.IO.File.WriteAllText(ReportPath, "running\n");
            EditorApplication.delayCall += () => System.IO.File.WriteAllText(ReportPath, Run());
            return ReportPath;
        }

        public static string Run() {
            var sb = new StringBuilder("=== LOOPER CHECK ===\n");
            bool allPass = true;
            // 480 Hz at 48 kHz: exactly 100 samples a period, so a 1.0 s loop is exactly 480 periods.
            var aligned = Tone(1.3f, 480f, 0.5f);
            // 440.25 Hz: a 1.0 s loop holds 440 and a quarter periods, so the copies meet a quarter-period out of phase.
            var unaligned = Tone(1.3f, 440.25f, 0.5f);
            var noise = Noise(1.3f, 0.3f);
            // 440.5 Hz: half a period over — the copies meet in opposite phase, the one case no fade law can rescue.
            var opposite = Tone(1.3f, 440.5f, 0.5f);
            double s0 = 0.1 * SR, s1 = 1.1 * SR;   // loop region: 0.1 s .. 1.1 s (1.0 s)

            // 1 + 2: seamless on aligned material.
            sb.Append("\n1-2. Tone looped on whole periods (worst 480-sample window's residual after removing the ideal continuous\n")
              .Append("     tone; level range across the whole render, 10 ms windows):\n");
            foreach (var (label, xMin, xMax) in new[] { ("no crossmix     ", 0.0, 0.0), ("crossmix 0.2 s  ", 0.2, 0.2), ("range 0.05-0.3 s", 0.05, 0.3) }) {
                foreach (float pitch in new[] { 1f, 0.7f }) {
                    var o = Render(aligned, s0, s1, xMin * SR, xMax * SR, pitch, 256, 5.5f, out _);
                    Fit(o, 480f * pitch, (int)(0.05f * SR), o.Length, 480, out float worstDb, out float lo, out float hi);
                    bool ok = worstDb < -60f && (hi - lo) < 0.05f;
                    if (!ok) allPass = false;
                    sb.Append("     ").Append(label).Append(" pitch ").Append(pitch.ToString("0.0")).Append(": residual ")
                      .Append(worstDb.ToString("0.0")).Append(" dB, level ").Append(lo.ToString("+0.000;-0.000")).Append(" .. ")
                      .Append(hi.ToString("+0.000;-0.000")).Append(" dB").Append(ok ? "  PASS\n" : "  FAIL\n");
                }
            }

            // 3: material whose copies do not line up.
            sb.Append("\n3. Where the copies do NOT line up:\n");
            {
                foreach (var (label, xMin, xMax) in new[] { ("crossmix 0.2 s  ", 0.2, 0.2), ("range 0.05-0.3 s", 0.05, 0.3) }) {
                    var o = Render(unaligned, s0, s1, xMin * SR, xMax * SR, 1f, 256, 5.5f, out _);
                    Fit(o, 440.25f, (int)(0.05f * SR), o.Length, 480, out _, out float lo, out float hi);
                    bool ok = (hi - lo) < 0.2f;
                    if (!ok) allPass = false;
                    sb.Append("     440.25 Hz tone (copies meet a quarter-period apart), ").Append(label).Append(": level ").Append(lo.ToString("+0.00;-0.00"))
                      .Append(" .. ").Append(hi.ToString("+0.00;-0.00")).Append(" dB").Append(ok ? "  PASS\n" : "  FAIL\n");
                }
                var nz = Render(noise, s0, s1, 0.2 * SR, 0.2 * SR, 1f, 256, 10.5f, out _);
                float dip = NoiseMidFadeDip(nz, s0, s1, 0.2 * SR);
                bool nok = Mathf.Abs(dip) < 0.5f;
                if (!nok) allPass = false;
                sb.Append("     white noise, crossmix 0.2 s: mid-fade level ").Append(dip.ToString("+0.00;-0.00"))
                  .Append(" dB against outside the fades  (a plain equal-gain fade measured -3.1 dB here)").Append(nok ? "  PASS\n" : "  FAIL\n");
                var opp = Render(opposite, s0, s1, 0.2 * SR, 0.2 * SR, 1f, 256, 5.5f, out _);
                Fit(opp, 440.5f, (int)(0.05f * SR), opp.Length, 480, out _, out float olo, out float ohi);
                sb.Append("     440.5 Hz tone (copies in opposite phase), crossmix 0.2 s: level ").Append(olo.ToString("+0.0;-0.0"))
                  .Append(" .. ").Append(ohi.ToString("+0.0;-0.0")).Append(" dB  (report only: opposite copies cancel; boost capped at +6 dB)\n");
                var w = Render(unaligned, s0, s1, 0, 0, 1f, 256, 5.5f, out _);
                Fit(w, 440.25f, (int)(0.05f * SR), w.Length, 480, out float wrapDb, out _, out _);
                sb.Append("     440.25 Hz tone, plain wrap (no crossmix): worst window residual ").Append(wrapDb.ToString("0.0"))
                  .Append(" dB  (report only: the seam a crossmix exists to hide)\n");
            }

            // 4: per-cycle draws.
            sb.Append("\n4. Crossmix drawn per cycle (range 0.05-0.3 s, 12 cycles):\n");
            {
                var a = FadeLengths(aligned, s0, s1, 0.05 * SR, 0.3 * SR, 12, 7);
                var b = FadeLengths(aligned, s0, s1, 0.05 * SR, 0.3 * SR, 12, 7);
                var c = FadeLengths(aligned, s0, s1, 0.05 * SR, 0.3 * SR, 12, 8);
                // Each length may be nudged up to 5 ms to where the copies line up (AlignCrossmix).
                bool inRange = a.TrueForAll(x => x >= 0.045 * SR - 1 && x <= 0.305 * SR + 1);
                bool varied = new HashSet<double>(a).Count > a.Count / 2;
                bool same = a.Count == b.Count && a.TrueForAll(x => b.Contains(x));
                bool otherPlayDiffers = !c.TrueForAll(x => a.Contains(x));
                var capped = FadeLengths(aligned, s0, s1, 0.4 * SR, 0.9 * SR, 12, 7);
                bool limited = capped.TrueForAll(x => x <= 0.5 * (s1 - s0) + 1) && capped.Count > 0;
                bool ok = inRange && varied && same && otherPlayDiffers && limited && a.Count >= 10;
                if (!ok) allPass = false;
                sb.Append("     lengths (ms):");
                foreach (var x in a) sb.Append(' ').Append((x * 1000.0 / SR).ToString("0"));
                sb.Append("\n     in range ").Append(inRange).Append(", vary ").Append(varied).Append(", same play same draws ").Append(same)
                  .Append(", another play differs ").Append(otherPlayDiffers).Append(", range 0.4-0.9 s on a 1 s loop limited to 0.5 s ")
                  .Append(limited).Append(ok ? "  PASS\n" : "  FAIL\n");
            }

            // 5: live edits and release.
            sb.Append("\n5. Live edits and release:\n");
            {
                var v = Voice(aligned, s0, s1, 0.1 * SR, 0.1 * SR, 1f, 1);
                var o = new List<float>();
                bool sawNew = false; bool bad = false;
                try {
                    for (int blk = 0; blk < (int)(6f * SR / 256); blk++) {
                        if (blk == (int)(1.5f * SR / 256)) {
                            v.Apply(SapVoiceCommand.LoopStart((int)(0.2 * SR)));
                            v.Apply(SapVoiceCommand.LoopEnd((int)(1.2 * SR)));
                            v.Apply(SapVoiceCommand.CrossmixMax((int)(0.25 * SR)));
                            v.Apply(SapVoiceCommand.CrossmixMin((int)(0.25 * SR)));
                        }
                        v.RenderBlock(256);
                        if (v.sap.looping.inFade && System.Math.Abs(v.sap.looping.fadeLen - 0.25 * SR) < 1) sawNew = true;
                        for (int i = 0; i < 256; i++) { float x = v.sap.bufL[i]; if (float.IsNaN(x) || Mathf.Abs(x) > 1f) bad = true; o.Add(x); }
                    }
                }
                finally { v.Dispose(); }
                Fit(o.ToArray(), 480f, (int)(0.05f * SR), o.Count, 480, out float worst, out float lo, out float hi);
                bool ok = sawNew && !bad && worst < -60f && (hi - lo) < 0.05f;
                if (!ok) allPass = false;
                sb.Append("     region 0.1-1.1 -> 0.2-1.2 s and crossmix 0.1 -> 0.25 s mid-play: new crossmix heard ").Append(sawNew)
                  .Append(", residual ").Append(worst.ToString("0.0")).Append(" dB, level ").Append(lo.ToString("+0.000;-0.000")).Append(" .. ")
                  .Append(hi.ToString("+0.000;-0.000")).Append(" dB").Append(ok ? "  PASS\n" : "  FAIL\n");

                var r = Voice(aligned, s0, s1, 0.2 * SR, 0.2 * SR, 1f, 1);
                int after = -1;
                try {
                    for (int blk = 0; blk < (int)(4f * SR / 256); blk++) {
                        if (blk == (int)(1.25f * SR / 256)) r.Apply(SapVoiceCommand.Release());   // mid-crossfade
                        r.RenderBlock(256);
                        if (after < 0 && r.finished) after = blk * 256 - (int)(1.25f * SR);
                    }
                }
                finally { r.Dispose(); }
                bool rok = after >= 0 && after < SR;
                if (!rok) allPass = false;
                sb.Append("     release mid-crossfade: voice finished ").Append(after >= 0 ? (after * 1000 / SR) + " ms later" : "NEVER")
                  .Append(rok ? "  PASS\n" : "  FAIL\n");
            }

            // 6: slicing invariance.
            sb.Append("\n6. Slicing invariance (difference vs 256-frame blocks):\n");
            foreach (var (label, xMin, xMax) in new[] { ("no crossmix", 0.0, 0.0), ("crossmix 0.2 s", 0.2, 0.2), ("range 0.05-0.3 s", 0.05, 0.3) }) {
                var reference = Render(aligned, s0, s1, xMin * SR, xMax * SR, 0.7f, 256, 4f, out _);
                var refC = RenderCompiled(aligned, s0, s1, xMin * SR, xMax * SR, 0.7f, 256, 4f);
                sb.Append("     ").Append(label).Append(":  ordinary");
                foreach (int blk in new[] { 1, 64, 333, 1024, 4096 }) {
                    float d = MaxDiff(reference, Render(aligned, s0, s1, xMin * SR, xMax * SR, 0.7f, blk, 4f, out _));
                    sb.Append("  ").Append(blk).Append('=').Append(d == 0f ? "0" : d.ToString("E1"));
                    if (d != 0f) allPass = false;
                }
                sb.Append("   | compiled");
                foreach (int blk in new[] { 64, 333, 1024, 4096 }) {
                    float d = MaxDiff(refC, RenderCompiled(aligned, s0, s1, xMin * SR, xMax * SR, 0.7f, blk, 4f));
                    sb.Append("  ").Append(blk).Append('=').Append(d == 0f ? "0" : d.ToString("E1"));
                    if (d != 0f) allPass = false;
                }
                sb.Append("   | compiled vs ordinary ").Append(MaxDiff(reference, refC).ToString("E1")).Append('\n');
            }

            sb.Append(allPass ? "\nALL PASS\n" : "\nSOMETHING FAILED (see above)\n");
            return sb.ToString();
        }

        // ── rendering ──

        static SapRealtimeVoice Voice(PcmClip clip, double start, double end, double xMin, double xMax, float pitch, long token) {
            var v = SapRealtimeVoice.Create(clip, ChainLayout.Empty, SR, start, end, pitch, 1f, (float)((end - start) / SR), true, token, false,
                                            Allocator.Persistent);
            v.SetLoopCrossmix(xMin, xMax);
            return v;
        }

        static float[] Render(PcmClip clip, double start, double end, double xMin, double xMax, float pitch, int block, float seconds, out int produced) {
            int total = (int)(seconds * SR);
            var outL = new float[total];
            var v = Voice(clip, start, end, xMin, xMax, pitch, 1);
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

        static float[] RenderCompiled(PcmClip clip, double start, double end, double xMin, double xMax, float pitch, int block, float seconds) {
            int total = (int)(seconds * SR);
            var v = Voice(clip, start, end, xMin, xMax, pitch, 1);
            var jL = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var jR = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var tally = new NativeArray<int>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            try {
                new SapVoiceRenderJob { voice = v, outLeft = jL, outRight = jR, totalFrames = total, blockFrames = block, tally = tally }.Run();
                int produced = tally[0];
                var o = new float[produced];
                for (int i = 0; i < produced; i++) o[i] = jL[i];
                return o;
            }
            finally { v.Dispose(); jL.Dispose(); jR.Dispose(); tally.Dispose(); }
        }

        /// <summary>Each crossfade's length as the engine chose it, over <paramref name="cycles"/> seams.</summary>
        static List<double> FadeLengths(PcmClip clip, double start, double end, double xMin, double xMax, int cycles, long token) {
            var lengths = new List<double>();
            var v = Voice(clip, start, end, xMin, xMax, 1f, token);
            try {
                bool was = false;
                for (int guard = 0; guard < 100000 && lengths.Count < cycles; guard++) {
                    v.RenderBlock(64);
                    bool now = v.sap.looping.inFade;
                    if (now && !was) lengths.Add(v.sap.looping.fadeLen);
                    was = now;
                }
            }
            finally { v.Dispose(); }
            return lengths;
        }

        // ── sources ──

        static PcmClip Tone(float seconds, float hz, float amp) {
            int frames = (int)(seconds * SR);
            var s = new float[frames * 2];
            for (int i = 0; i < frames; i++) { float v = amp * Mathf.Sin(2f * Mathf.PI * hz * i / SR); s[i * 2] = v; s[i * 2 + 1] = v; }
            return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = s, valid = true, peak = amp };
        }

        static PcmClip Noise(float seconds, float amp) {
            int frames = (int)(seconds * SR);
            var s = new float[frames * 2];
            uint h = 12345u;
            for (int i = 0; i < frames; i++) {
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                float v = amp * ((h & 0xFFFFFF) / 8388608f - 1f);
                s[i * 2] = v; s[i * 2 + 1] = v;
            }
            return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = s, valid = true, peak = amp };
        }

        // ── measurements ──

        static float MaxDiff(float[] a, float[] b) {
            int n = Mathf.Min(a.Length, b.Length);
            float m = 0f;
            for (int i = 0; i < n; i++) m = Mathf.Max(m, Mathf.Abs(a[i] - b[i]));
            var longer = a.Length > b.Length ? a : b;
            for (int i = n; i < longer.Length; i++) m = Mathf.Max(m, Mathf.Abs(longer[i]));
            return m;
        }

        /// <summary>
        /// Over half-overlapping windows of <paramref name="W"/> samples: the best-fitting sine at <paramref name="hz"/> (its amplitude
        /// gives the level) and what is left after removing it (a break at a seam leaves a residual). Reports the worst
        /// residual and the level range, in dB relative to the median level.
        /// </summary>
        static void Fit(float[] o, float hz, int from, int to, int W, out float worstDb, out float lo, out float hi) {
            worstDb = -200f;
            var levels = new List<float>();
            double w = 2 * System.Math.PI * hz / SR;
            // Windows every half window, so a seam can never hide on a window boundary (at pitch 1 every wrap of a 1.0 s
            // loop fell exactly on one, and a real break went unseen).
            for (int a = from; a + W <= to; a += W / 2) {
                double ss = 0, sc = 0, cc = 0, ys = 0, yc = 0, e = 1e-20, sig = 1e-20;
                for (int i = 0; i < W; i++) { double s = System.Math.Sin(w * (a + i)), c = System.Math.Cos(w * (a + i)); ss += s * s; sc += s * c; cc += c * c; ys += o[a + i] * s; yc += o[a + i] * c; }
                double det = ss * cc - sc * sc;
                double A = (ys * cc - yc * sc) / det, B = (yc * ss - ys * sc) / det;
                for (int i = 0; i < W; i++) {
                    double fit = A * System.Math.Sin(w * (a + i)) + B * System.Math.Cos(w * (a + i));
                    double d = o[a + i] - fit; e += d * d; sig += fit * fit;
                }
                worstDb = Mathf.Max(worstDb, (float)(10.0 * System.Math.Log10(e / sig)));
                levels.Add((float)(20.0 * System.Math.Log10(System.Math.Sqrt(A * A + B * B) + 1e-20)));
            }
            if (levels.Count == 0) { lo = hi = 0f; return; }
            var sorted = new List<float>(levels); sorted.Sort();
            float median = sorted[sorted.Count / 2];
            lo = sorted[0] - median; hi = sorted[sorted.Count - 1] - median;
        }

        /// <summary>
        /// The level in the middle quarter of every crossfade against the level well away from any fade, from the whole
        /// render's power (noise needs averaging over many cycles). Assumes a fixed crossmix of <paramref name="x"/> frames.
        /// </summary>
        static float NoiseMidFadeDip(float[] o, double start, double end, double x) {
            double period = (end - start) - x;              // output samples per cycle at pitch 1
            double mid = 0, midN = 0, far = 0, farN = 0;
            for (int i = (int)(0.05 * SR); i < o.Length; i++) {
                double t = i - ((end - start) - x);         // first fade starts at (end - x) - start into the render
                if (t < 0) { far += o[i] * (double)o[i]; farN++; continue; }
                double inCycle = t % period;                // 0..x is a fade
                double pf = inCycle / x;
                if (pf >= 0.375 && pf <= 0.625) { mid += o[i] * (double)o[i]; midN++; }
                else if (inCycle > x + 0.1 * SR && inCycle < period - 0.1 * SR) { far += o[i] * (double)o[i]; farN++; }
            }
            if (midN == 0 || farN == 0) return 0f;
            return (float)(10.0 * System.Math.Log10((mid / midN) / (far / farN)));
        }
    }
}

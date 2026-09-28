using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks.EditorTools {

    /// <summary>
    /// The kept check for a Klip's pitch and time curves (T-0479 and its sub-tasks). Every measurement is against a known
    /// answer on synthetic sources, through the real voice (the ordinary path and the compiled one):
    ///
    /// 1. **The pitch curve's scale.** The Ratio scale's fixed points (middle x1, top x4, bottom x1/4, +12 semitones x2,
    ///    half depth x2 at the top); a new pitch curve is flat at x1 and leaves the play length alone; converting an
    ///    old-scale (Set) curve to the Ratio scale leaves every point's multiplier AND the rendered audio unchanged;
    ///    rendering with a Ratio pitch curve gives the same samples whatever the block size.
    /// 2. **Old stretch settings are heard.** A Klip's old Uniform / Region / Curve stretch, played through the live
    ///    stretcher as the engine plays it (the same plan StartVoice uses): the declared length and the length a real
    ///    voice actually renders both change by the stretch (x0.5 -> half as long, x2 -> twice), and agree.
    /// 3. **Time curve and keep length.** A pitch curve held at +12 semitones: tape-style it halves the length and doubles
    ///    the pitch; with keep length the length stays and the pitch still doubles. A time curve at half speed alone
    ///    doubles the length and leaves the pitch alone. Pitch is measured on the render (zero crossings of a 440 Hz
    ///    tone), lengths declared and rendered. And the stretched path with a moving pitch curve and keep length gives
    ///    the same samples at every multiple of the control grid.
    /// </summary>
    public static class ZoundsCurvesCheck {

        const int SR = 48000;

        [MenuItem("Laubrary/Zounds/Checks/17 - Pitch and time curves (scale, conversion, length, slicing)")]
        public static void RunFromMenu() => Debug.Log(Run());

        public static string Run() {
            var sb = new StringBuilder("=== PITCH AND TIME CURVES CHECK ===\n");
            bool allPass = true;
            allPass &= PitchScale(sb);
            allPass &= OldStretch(sb);
            allPass &= TimeAndKeepLength(sb);
            sb.Append(allPass ? "\nALL PASS\n" : "\nSOMETHING FAILED (see above)\n");
            return sb.ToString();
        }

        // ── 1. the pitch curve's scale ──

        static bool PitchScale(StringBuilder sb) {
            bool ok = true;
            var pd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Pitch];
            float R(float pos, float depth) => ModulationMath.Apply(ModulationCombine.Ratio, 1f, pos, depth, pd.min, pd.max, true);
            float mid = R(0.5f, 1f), top = R(1f, 1f), bottom = R(0f, 1f), plus12 = R(0.75f, 1f), halfTop = R(1f, 0.5f);
            bool fixedOk = Near(mid, 1f) && Near(top, 4f) && Near(bottom, 0.25f) && Near(plus12, 2f) && Near(halfTop, 2f);
            sb.Append("\n1. Pitch curve scale (Ratio): middle x").Append(mid.ToString("0.0000")).Append(", top x").Append(top.ToString("0.0000"))
              .Append(", bottom x").Append(bottom.ToString("0.0000")).Append(", +12 st x").Append(plus12.ToString("0.0000"))
              .Append(", top at half depth x").Append(halfTop.ToString("0.0000")).Append(fixedOk ? "  PASS\n" : "  FAIL\n");
            ok &= fixedOk;

            // A new pitch curve, made exactly as the Klip window makes one.
            var newCurve = NewRatioCurve();
            var fresh = PitchChain(newCurve, ModulationCombine.Ratio);
            var tone = Tone(1.2f, 440f, 0.5f);
            float srcSec = 1.0f;
            float flatPitch = ZoundDspPlayback.PitchAtSource(fresh, 0.5f, srcSec);
            float flatLen = ZoundDspPlayback.DurationUnderPitchModulation(KlipWith(fresh), srcSec);
            int produced = Render(tone, fresh, 256, 3f).Length;
            bool newOk = Near(flatPitch, 1f) && Mathf.Abs(flatLen - srcSec) < 1e-4f && Mathf.Abs(produced - srcSec * SR) <= 256;
            sb.Append("   New flat pitch curve: plays x").Append(flatPitch.ToString("0.0000")).Append(", declared length ").Append(flatLen.ToString("0.0000"))
              .Append(" s, rendered ").Append((produced / (float)SR).ToString("0.000")).Append(" s of a 1.000 s source").Append(newOk ? "  PASS\n" : "  FAIL\n");
            ok &= newOk;

            // Conversion of an old-scale curve: the owner's own points (New Klip2 and Game over 4) plus the old default
            // flat line (x4) and a point near the bottom of what two octaves can hold.
            float[] oldValues = { 0.608f, 0.537f, 0.692f, 0.549f, 0.662f, 0.632f, 0.661f, 0.466f, 1f, 0.25f };
            var oldCurve = new Envelope(0.1f, 2f);
            var pts = oldCurve.GetPointsList();
            pts.Clear();
            for (int i = 0; i < oldValues.Length; i++) pts.Add(new ZUIEnvelopePoint(i / (float)(oldValues.Length - 1), oldValues[i], 1f));
            var before = PitchChain(oldCurve.DeepCopy(), ModulationCombine.Set);
            var after = PitchChain(oldCurve.DeepCopy(), ModulationCombine.Set);
            bool converted = ChainModulationCompat.ConvertSetToRatio(after, after.bindings[0], out int clamped);
            float worstRel = 0f;
            for (int i = 0; i <= 200; i++) {
                float t = i / 200f;
                float a = ZoundDspPlayback.PitchAtSource(before, t, srcSec), b = ZoundDspPlayback.PitchAtSource(after, t, srcSec);
                worstRel = Mathf.Max(worstRel, Mathf.Abs(a - b) / a);
            }
            float lenBefore = ZoundDspPlayback.DurationUnderPitchModulation(KlipWith(before), srcSec);
            float lenAfter = ZoundDspPlayback.DurationUnderPitchModulation(KlipWith(after), srcSec);
            var ra = Render(tone, before, 256, 4f);
            var rb = Render(tone, after, 256, 4f);
            float diff = MaxDiff(ra, rb);
            float diffDb = 20f * Mathf.Log10(Mathf.Max(diff, 1e-12f) / 0.5f);
            bool convOk = converted && clamped == 0 && worstRel < 1e-4f && Mathf.Abs(lenBefore - lenAfter) < 1e-4f
                          && ra.Length == rb.Length && diffDb < -70f && after.bindings[0].combine == ModulationCombine.Ratio;
            sb.Append("   Old-scale curve converted: combine now ").Append(after.bindings[0].combine).Append(", points held ").Append(clamped)
              .Append(", worst multiplier difference ").Append((worstRel * 100f).ToString("0.0000")).Append(" %, length ")
              .Append(lenBefore.ToString("0.0000")).Append(" -> ").Append(lenAfter.ToString("0.0000")).Append(" s, rendered ")
              .Append(ra.Length).Append(" vs ").Append(rb.Length).Append(" samples, largest sample difference ").Append(diffDb.ToString("0.0")).Append(" dB")
              .Append(convOk ? "  PASS\n" : "  FAIL\n");
            ok &= convOk;

            // Slicing invariance with a moving Ratio pitch curve.
            var moving = NewRatioCurve();
            var mp = moving.GetPointsList();
            mp[0].value = 0.3f; mp[mp.Count - 1].value = 0.8f;
            var movingChain = PitchChain(moving, ModulationCombine.Ratio);
            var reference = Render(tone, movingChain, 256, 3f);
            // Blocks that are whole multiples of the 64-sample control grid (which is what the mixer uses) must give the
            // SAME samples. Other sizes split a control block across calls: the ramps are then advanced in pieces, which
            // rounds a hair differently, and the tiny rate difference shows as a slow phase drift -- allowed down to
            // -60 dB of the signal, and reported.
            sb.Append("   Slicing (Ratio pitch curve sweeping x0.47..x2.3), largest difference vs 256-sample blocks:");
            bool sliceOk = true;
            foreach (int blk in new[] { 1, 64, 333, 1024, 4096 }) {
                float d = MaxDiff(reference, Render(tone, movingChain, blk, 3f));
                float db = 20f * Mathf.Log10(Mathf.Max(d, 1e-12f) / 0.5f);
                sb.Append(" ").Append(blk).Append("=").Append(d == 0f ? "0" : db.ToString("0") + " dB");
                sliceOk &= blk % 64 == 0 ? d == 0f : db < -60f;
            }
            float dc = MaxDiff(reference, RenderCompiled(tone, movingChain, 256, 3f));
            sb.Append(" | compiled vs ordinary ").Append(dc.ToString("0.###E0"));
            sliceOk &= dc < 1e-4f;
            sb.Append(sliceOk ? "  PASS\n" : "  FAIL\n");
            ok &= sliceOk;
            return ok;
        }

        // ── 2. old stretch settings are heard ──

        static bool OldStretch(StringBuilder sb) {
            bool ok = true;
            var tone = Tone(2.2f, 440f, 0.5f);
            double frames = 2.0 * SR;   // a 2.0 s source
            sb.Append("\n2. Old stretch settings through the live stretcher (2.00 s source; declared / rendered, and ratio to no stretch):\n");
            float baseDeclared = 0f, baseRendered = 0f;
            var cases = new (string label, TimeStretchMode mode, float factor, float expected)[] {
                ("no stretch          ", TimeStretchMode.Uniform, 1f, 1f),
                ("Uniform length x0.5 ", TimeStretchMode.Uniform, 0.5f, 0.5f),
                ("Uniform length x2   ", TimeStretchMode.Uniform, 2f, 2f),
                ("Region x2 (middle ½)", TimeStretchMode.Region, 2f, 1.5f),
                ("Curve speed 0.5     ", TimeStretchMode.Envelope, 2f, 2f),
            };
            foreach (var c in cases) {
                var k = new Klip(0) { effectChain = new ZoundEffectChain() };
                k.timeStretch.enabled = c.factor != 1f;
                k.timeStretch.mode = c.mode;
                k.timeStretch.factor = c.factor;
                k.timeStretch.regionStart = 0.5f; k.timeStretch.regionEnd = 1.5f;
                if (c.mode == TimeStretchMode.Envelope) foreach (var pt in k.timeStretch.speedEnvelope.GetPointsList()) pt.value = 0.5f;
                var plan = ZoundSapPlayback.Plan(k, 0, frames, SR);
                float declared = ZoundSapPlayback.PlayLength(in plan, frames / SR, 1f, false);
                float rendered = RenderPlan(tone, plan, frames, 256, 6f) / (float)SR;
                if (c.factor == 1f) { baseDeclared = declared; baseRendered = rendered; }
                float ratioD = declared / baseDeclared, ratioR = rendered / baseRendered;
                // The stretcher ends within a window or so of the requested length (check 15 measured +37..+43 ms).
                bool pass = (c.factor == 1f || plan.stretched) && Mathf.Abs(ratioD - c.expected) < 0.01f
                            && Mathf.Abs(rendered - declared) < 0.08f;
                sb.Append("   ").Append(c.label).Append(": declared ").Append(declared.ToString("0.000")).Append(" s, rendered ")
                  .Append(rendered.ToString("0.000")).Append(" s, x").Append(ratioD.ToString("0.00")).Append(" / x").Append(ratioR.ToString("0.00"))
                  .Append(" (expected x").Append(c.expected.ToString("0.00")).Append(")").Append(plan.stretched ? ", live stretcher" : ", direct read")
                  .Append(pass ? "  PASS\n" : "  FAIL\n");
                ok &= pass;
            }
            return ok;
        }

        /// <summary>Renders a play made to <paramref name="plan"/> until the voice ends; returns the samples up to the last sound.</summary>
        static int RenderPlan(PcmClip clip, ZoundSapPlayback.PlayPlan plan, double endFrame, int block, float maxSeconds) {
            var layout = plan.chain != null && !plan.chain.IsEmpty ? ChainLayout.Build(plan.chain, SR) : ChainLayout.Empty;
            float sourceSeconds = (float)(endFrame / SR);
            var v = SapRealtimeVoice.Create(clip, layout, SR, 0, endFrame, 1f, 1f, sourceSeconds, false, 1, layout.heavy, Allocator.Persistent,
                                            stretch: plan.stretch, baseSpeed: plan.authoredSpeed);
            int total = (int)(maxSeconds * SR), wrote = 0, last = 0;
            try {
                while (wrote < total && !v.finished) {
                    int n = Mathf.Min(block, total - wrote);
                    v.RenderBlock(n);
                    for (int i = 0; i < n; i++) if (Mathf.Abs(v.sap.bufL[i]) > 1e-5f) last = wrote + i + 1;
                    wrote += n;
                }
            }
            finally { v.Dispose(); }
            return last;
        }

        // ── 3. time curve and keep length ──

        static bool TimeAndKeepLength(StringBuilder sb) {
            bool ok = true;
            var tone = Tone(1.2f, 440f, 0.5f);
            double frames = 1.0 * SR;
            sb.Append("\n3. Time curve and keep length (1.00 s source, 440 Hz tone):\n");
            var cases = new (string label, float pitchPos, float timePos, bool keep, float expLen, float expHz)[] {
                ("pitch +12 st, tape-style    ", 0.75f, -1f, false, 0.5f, 880f),
                ("pitch +12 st, keep length   ", 0.75f, -1f, true, 1.0f, 880f),
                ("pitch -12 st, keep length   ", 0.25f, -1f, true, 1.0f, 220f),
                ("time curve at half speed    ", -1f, 0.25f, false, 2.0f, 440f),
                ("pitch +12 st + time x2, keep", 0.75f, 0.75f, true, 0.5f, 880f),
            };
            foreach (var c in cases) {
                var k = KlipWithCurves(c.pitchPos, c.timePos, c.keep);
                var plan = ZoundSapPlayback.Plan(k, 0, frames, SR);
                float declared = ZoundSapPlayback.PlayLength(in plan, frames / SR, 1f, false);
                var o = RenderPlanSamples(tone, plan, frames, 256, 4f);
                float rendered = o.Length / (float)SR;
                float hz = Frequency(o);
                bool pass = Mathf.Abs(declared - c.expLen) < 0.01f && Mathf.Abs(rendered - c.expLen) < 0.08f
                            && Mathf.Abs(hz - c.expHz) / c.expHz < 0.01f;
                sb.Append("   ").Append(c.label).Append(": declared ").Append(declared.ToString("0.000")).Append(" s, rendered ")
                  .Append(rendered.ToString("0.000")).Append(" s, pitch ").Append(hz.ToString("0")).Append(" Hz (expected ")
                  .Append(c.expLen.ToString("0.0")).Append(" s, ").Append(c.expHz.ToString("0")).Append(" Hz)")
                  .Append(plan.stretched ? ", live stretcher" : ", direct read").Append(pass ? "  PASS\n" : "  FAIL\n");
                ok &= pass;
            }
            // Slicing on the stretched path, a moving pitch curve with keep length.
            var sk = KlipWithCurves(0.4f, -1f, true);
            var mp = ((ZoundModifier)sk.effectChain.modifiers[0]).curve.GetPointsList();
            mp[mp.Count - 1].value = 0.7f;
            var splan = ZoundSapPlayback.Plan(sk, 0, frames, SR);
            var reference = RenderPlanSamples(tone, splan, frames, 256, 3f);
            sb.Append("   Slicing (keep length, pitch curve sweeping), largest difference vs 256-sample blocks:");
            bool sliceOk = true;
            foreach (int blk in new[] { 1, 64, 333, 1024, 4096 }) {
                float d = MaxDiff(reference, RenderPlanSamples(tone, splan, frames, blk, 3f));
                float db = 20f * Mathf.Log10(Mathf.Max(d, 1e-12f) / 0.5f);
                sb.Append(" ").Append(blk).Append("=").Append(d == 0f ? "0" : db.ToString("0") + " dB");
                sliceOk &= blk % 64 == 0 ? d == 0f : db < -40f;
            }
            sb.Append(sliceOk ? "  PASS\n" : "  FAIL\n");
            ok &= sliceOk;
            return ok;
        }

        /// <summary>A Klip with a Ratio pitch curve flat at <paramref name="pitchPos"/> and a time curve flat at
        /// <paramref name="timePos"/> (negative: none), and keep length set as asked.</summary>
        static Klip KlipWithCurves(float pitchPos, float timePos, bool keep) {
            var chain = new ZoundEffectChain();
            if (pitchPos >= 0f) {
                var pc = new Envelope(0f, 1f);
                foreach (var pt in pc.GetPointsList()) pt.value = pitchPos;
                chain.modifiers.Add(new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch", curve = pc });
                chain.bindings.Add(new ZoundModifierBinding { modifierIndex = chain.modifiers.Count - 1, nodeIndex = -1, paramIndex = SourceStageParam.Pitch,
                    combine = ModulationCombine.Ratio, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            }
            if (timePos >= 0f) {
                var tc = new Envelope(0f, 1f);
                foreach (var pt in tc.GetPointsList()) pt.value = timePos;
                LegacyStretch.AddTimeCurve(chain, tc, "Time");
            }
            var k = new Klip(0) { effectChain = chain };
            k.timeStretch.pitchKeepsLength = keep;
            return k;
        }

        static float[] RenderPlanSamples(PcmClip clip, ZoundSapPlayback.PlayPlan plan, double endFrame, int block, float maxSeconds) {
            var layout = plan.chain != null && !plan.chain.IsEmpty ? ChainLayout.Build(plan.chain, SR) : ChainLayout.Empty;
            float sourceSeconds = (float)(endFrame / SR);
            var v = SapRealtimeVoice.Create(clip, layout, SR, 0, endFrame, 1f, 1f, sourceSeconds, false, 1, layout.heavy, Allocator.Persistent,
                                            stretch: plan.stretch, baseSpeed: plan.authoredSpeed);
            int total = (int)(maxSeconds * SR), wrote = 0;
            var o = new float[total];
            try {
                while (wrote < total && !v.finished) {
                    int n = Mathf.Min(block, total - wrote);
                    v.RenderBlock(n);
                    for (int i = 0; i < n; i++) o[wrote + i] = v.sap.bufL[i];
                    wrote += n;
                }
            }
            finally { v.Dispose(); }
            int last = wrote;
            while (last > 0 && Mathf.Abs(o[last - 1]) < 1e-5f) last--;
            System.Array.Resize(ref o, last);
            return o;
        }

        /// <summary>A tone's frequency from its rising zero crossings over the middle of the render.</summary>
        static float Frequency(float[] o) {
            int a = o.Length / 4, b = o.Length * 3 / 4;
            int first = -1, last = -1, count = 0;
            for (int i = a + 1; i < b; i++) {
                if (o[i - 1] <= 0f && o[i] > 0f) {
                    if (first < 0) first = i;
                    last = i; count++;
                }
            }
            return count > 1 && last > first ? (count - 1) * SR / (float)(last - first) : 0f;
        }

        // ── helpers ──

        /// <summary>The Klip window's own "new pitch curve" (internal to the editor assembly, so reached by name).</summary>
        static Envelope NewRatioCurve() {
            var t = System.Type.GetType("Laubrary.Zounds.KlipChainEnvelopes, com.Lautaro-Arino.Laubrary.Zounds.Editor");
            var m = t?.GetMethod("NewRatioCurve", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return m != null ? (Envelope)m.Invoke(null, null) : null;
        }

        static ZoundEffectChain PitchChain(Envelope curve, ModulationCombine combine) {
            var chain = new ZoundEffectChain();
            chain.modifiers.Add(new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch", curve = curve });
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = -1, paramIndex = SourceStageParam.Pitch,
                combine = combine, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            return chain;
        }

        static Klip KlipWith(ZoundEffectChain chain) => new Klip(0) { effectChain = chain };

        static SapRealtimeVoice Voice(PcmClip clip, ZoundEffectChain chain, float seconds) {
            var layout = ChainLayout.Build(chain, SR);
            double end = seconds * SR;
            return SapRealtimeVoice.Create(clip, layout, SR, 0, System.Math.Min(end, clip.frames), 1f, 1f, seconds, false, 1, layout.heavy, Allocator.Persistent);
        }

        static float[] Render(PcmClip clip, ZoundEffectChain chain, int block, float maxSeconds, float sourceSeconds = 1f) {
            int total = (int)(maxSeconds * SR);
            var o = new float[total];
            var v = Voice(clip, chain, sourceSeconds);
            int wrote = 0;
            try {
                while (wrote < total && !v.finished) {
                    int n = Mathf.Min(block, total - wrote);
                    v.RenderBlock(n);
                    for (int i = 0; i < n; i++) o[wrote + i] = v.sap.bufL[i];
                    wrote += n;
                }
            }
            finally { v.Dispose(); }
            // Trailing silence after the source ran out is not part of the play: trim to the last sound.
            int last = wrote;
            while (last > 0 && Mathf.Abs(o[last - 1]) < 1e-7f) last--;
            System.Array.Resize(ref o, last);
            return o;
        }

        static float[] RenderCompiled(PcmClip clip, ZoundEffectChain chain, int block, float maxSeconds, float sourceSeconds = 1f) {
            int total = (int)(maxSeconds * SR);
            var v = Voice(clip, chain, sourceSeconds);
            var jL = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var jR = new NativeArray<float>(total, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var tally = new NativeArray<int>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            try {
                new SapVoiceRenderJob { voice = v, outLeft = jL, outRight = jR, totalFrames = total, blockFrames = block, tally = tally }.Run();
                int produced = tally[0];
                while (produced > 0 && Mathf.Abs(jL[produced - 1]) < 1e-7f) produced--;
                var o = new float[produced];
                for (int i = 0; i < produced; i++) o[i] = jL[i];
                return o;
            }
            finally { v.Dispose(); jL.Dispose(); jR.Dispose(); tally.Dispose(); }
        }

        static PcmClip Tone(float seconds, float hz, float amp) {
            int frames = (int)(seconds * SR);
            var s = new float[frames * 2];
            for (int i = 0; i < frames; i++) { float v = amp * Mathf.Sin(2f * Mathf.PI * hz * i / SR); s[i * 2] = v; s[i * 2 + 1] = v; }
            return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = s, valid = true, peak = amp };
        }

        static float MaxDiff(float[] a, float[] b) {
            int n = Mathf.Min(a.Length, b.Length);
            float m = Mathf.Abs(a.Length - b.Length) > 0 ? 1f : 0f;
            for (int i = 0; i < n; i++) m = Mathf.Max(m, Mathf.Abs(a[i] - b[i]));
            return m;
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f * Mathf.Max(1f, Mathf.Abs(b));
    }
}

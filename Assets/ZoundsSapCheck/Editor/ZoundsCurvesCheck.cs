using Laubrary.Audio;
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
    /// 4. **Random points.** The draw's spread (share of 20,000 draws inside half the radius: 25 % at bias 0.5, i.e. even by
    ///    area; most at bias 0.1; few at 0.9; never outside the ellipse), determinism (same play, same draw; plays differ),
    ///    point order kept under a huge X radius, the declared length (drawn on the main thread) matching the rendered one
    ///    play by play while the plays differ, and the same samples at every multiple of the control grid.
    /// 5. **Real plays differ (T-0484).** The owner heard no difference: every real play of a sound was started with the
    ///    sound's id as its play number, so every play drew the same. Now: consecutive plays of one sound get different
    ///    play numbers; the real start path (StartVoice on an audio source) gives the two voices different seeds; and at an
    ///    audible scale, a pitch curve whose ends may move +-12 semitones, played twice through those play numbers, starts at
    ///    the pitch predicted from each play's own draw, and the two plays clearly differ.
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
            allPass &= RandomPoints(sb);
            allPass &= RealPlaysDiffer(sb);
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

        // ── 4. random points ──

        static bool RandomPoints(StringBuilder sb) {
            bool ok = true;
            sb.Append("\n4. Random points:\n");
            // Spread.
            sb.Append("   Share of 20,000 draws within half the radius (ellipse 1 x 1):");
            foreach (var (bias, lo, hi) in new[] { (0.5f, 0.235f, 0.265f), (0.1f, 0.6f, 1f), (0.9f, 0f, 0.1f) }) {
                int inside = 0, outside = 0;
                for (int i = 0; i < 20000; i++) {
                    EnvelopeRandom.Offset((uint)(i * 2654435761u + 12345u), 3, i, 1f, 1f, bias, out float dx, out float dy);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r <= 0.5f) inside++;
                    if (r > 1.0001f) outside++;
                }
                float share = inside / 20000f;
                bool pass = share >= lo && share <= hi && outside == 0;
                sb.Append(" bias ").Append(bias.ToString("0.0")).Append(" ").Append((share * 100f).ToString("0.0")).Append(" %").Append(outside > 0 ? " (" + outside + " outside!)" : "").Append(pass ? " PASS;" : " FAIL;");
                ok &= pass;
            }
            sb.Append('\n');
            // Determinism.
            EnvelopeRandom.Offset(EnvelopeRandom.SeedFor(42), 1, 2, 0.1f, 0.2f, 0.5f, out float ax, out float ay);
            EnvelopeRandom.Offset(EnvelopeRandom.SeedFor(42), 1, 2, 0.1f, 0.2f, 0.5f, out float bx, out float by);
            EnvelopeRandom.Offset(EnvelopeRandom.SeedFor(43), 1, 2, 0.1f, 0.2f, 0.5f, out float cx, out float cy);
            bool det = ax == bx && ay == by && (ax != cx || ay != cy);
            sb.Append("   Same play draws the same offset, another play differs: ").Append(det).Append(det ? "  PASS\n" : "  FAIL\n");
            ok &= det;
            // Order kept under a huge X radius.
            var wild = new Envelope(0f, 1f);
            var wp = wild.GetPointsList(); wp.Clear();
            for (int i = 0; i <= 8; i++) wp.Add(new ZUIEnvelopePoint(i / 8f, 0.5f) { randomX = 5f, randomY = 0f });
            bool ordered = true;
            for (int seed = 1; seed < 200 && ordered; seed++) {
                float prev = -1f;
                for (int i = 0; i <= 8; i++) {
                    EnvelopeRandom.Offset((uint)seed, 0, i, 5f, 0f, 0.5f, out float dx, out _);
                    float t = EnvelopeRandom.DrawnTime(wp[i].time, i > 0 ? wp[i - 1].time : wp[i].time, i < 8 ? wp[i + 1].time : wp[i].time, i == 0 || i == 8, dx);
                    if (t < prev) ordered = false;
                    prev = t;
                }
            }
            sb.Append("   Point order kept with an X radius five times the curve (199 plays): ").Append(ordered).Append(ordered ? "  PASS\n" : "  FAIL\n");
            ok &= ordered;
            // Declared vs rendered, play by play: a pitch curve whose middle point may move +-12 semitones (tape-style).
            var tone = Tone(1.2f, 440f, 0.5f);
            double frames = 1.0 * SR;
            var pc = new Envelope(0f, 1f);
            var pp = pc.GetPointsList(); pp.Clear();
            pp.Add(new ZUIEnvelopePoint(0f, 0.5f)); pp.Add(new ZUIEnvelopePoint(0.5f, 0.5f) { randomX = 0.2f, randomY = 0.25f, randomBias = 0.5f }); pp.Add(new ZUIEnvelopePoint(1f, 0.5f));
            var k = new Klip(0) { effectChain = PitchChain(pc, ModulationCombine.Ratio) };
            sb.Append("   A pitch point that may move +-12 st, per play declared / rendered:");
            float minLen = float.MaxValue, maxLen = 0f; bool agree = true;
            foreach (long token in new long[] { 11, 22, 33, 44, 55 }) {
                var plan = ZoundSapPlayback.Plan(k, 0, frames, SR);
                plan.drawn = true; plan.seed = EnvelopeRandom.SeedFor(token);
                float declared = ZoundSapPlayback.PlayLength(in plan, frames / SR, 1f, false);
                float rendered = RenderToken(tone, plan, frames, 256, 3f, token).Length / (float)SR;
                sb.Append(" ").Append(declared.ToString("0.000")).Append("/").Append(rendered.ToString("0.000"));
                agree &= Mathf.Abs(declared - rendered) < 0.004f;
                minLen = Mathf.Min(minLen, rendered); maxLen = Mathf.Max(maxLen, rendered);
            }
            bool differ = maxLen - minLen > 0.01f;
            sb.Append(" s; agree ").Append(agree).Append(", plays differ ").Append(differ).Append(agree && differ ? "  PASS\n" : "  FAIL\n");
            ok &= agree && differ;
            // Slicing with a random curve.
            var rplan = ZoundSapPlayback.Plan(k, 0, frames, SR);
            var reference = RenderToken(tone, rplan, frames, 256, 3f, 77);
            sb.Append("   Slicing (random pitch curve), largest difference vs 256-sample blocks:");
            bool sliceOk = true;
            foreach (int blk in new[] { 1, 64, 333, 1024, 4096 }) {
                float d = MaxDiff(reference, RenderToken(tone, rplan, frames, blk, 3f, 77));
                float db = 20f * Mathf.Log10(Mathf.Max(d, 1e-12f) / 0.5f);
                sb.Append(" ").Append(blk).Append("=").Append(d == 0f ? "0" : db.ToString("0") + " dB");
                sliceOk &= blk % 64 == 0 ? d == 0f : db < -40f;
            }
            sb.Append(sliceOk ? "  PASS\n" : "  FAIL\n");
            ok &= sliceOk;
            return ok;
        }

        // ── 5. real plays differ ──

        static bool RealPlaysDiffer(StringBuilder sb) {
            bool ok = true;
            sb.Append("\n5. Real plays of one sound draw differently:\n");
            var k = new Klip(4242) { effectChain = new ZoundEffectChain() };
            long id1 = ZoundSapPlayback.NextPlayId(k), id2 = ZoundSapPlayback.NextPlayId(k);
            bool idsDiffer = id1 != id2 && EnvelopeRandom.SeedFor(id1) != EnvelopeRandom.SeedFor(id2);
            sb.Append("   Consecutive play numbers ").Append(id1).Append(", ").Append(id2).Append(": seeds differ ").Append(idsDiffer).Append(idsDiffer ? "  PASS\n" : "  FAIL\n");
            ok &= idsDiffer;

            // The real start path, twice, on a temporary audio source (a generated tone; nothing in the project).
            var pc = new Envelope(0f, 1f);
            foreach (var pt in pc.GetPointsList()) { pt.value = 0.5f; pt.randomY = 0.25f; pt.randomBias = 0.5f; }
            k.effectChain = PitchChain(pc, ModulationCombine.Ratio);
            var clipData = Tone(1.2f, 440f, 0.5f);
            var clip = AudioClip.Create("curves-check-tone", clipData.frames, 2, SR, false);
            clip.SetData(clipData.samples, 0);
            var go = new GameObject("curves-check-source") { hideFlags = HideFlags.HideAndDontSave };
            uint seedA = 0, seedB = 0; string why = "";
            try {
                var src = go.AddComponent<AudioSource>();
                var g1 = ZoundSapPlayback.StartVoice(k, src, clip, 1f, 1f, ZoundSapPlayback.NextPlayId(k), out why, out _);
                if (g1 != null) seedA = g1.CurveSeed;
                var g2 = ZoundSapPlayback.StartVoice(k, src, clip, 1f, 1f, ZoundSapPlayback.NextPlayId(k), out why, out _);
                if (g2 != null) seedB = g2.CurveSeed;
                src.generator = null;
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(clip); }
            bool startDiffer = seedA != 0 && seedB != 0 && seedA != seedB;
            sb.Append("   Real start path twice: voice seeds ").Append(seedA).Append(" / ").Append(seedB).Append(string.IsNullOrEmpty(why) ? "" : " (" + why + ")")
              .Append(startDiffer ? "  PASS\n" : "  FAIL\n");
            ok &= startDiffer;

            // Audible scale: predicted vs measured starting pitch, two plays.
            double frames = 1.0 * SR;
            var plan = ZoundSapPlayback.Plan(k, 0, frames, SR);
            var hz = new float[2]; var predicted = new float[2];
            long[] ids = { ZoundSapPlayback.NextPlayId(k), ZoundSapPlayback.NextPlayId(k) };
            for (int i = 0; i < 2; i++) {
                var o = RenderToken(clipData, plan, frames, 256, 4f, ids[i]);
                // The first tenth of the render, against the curve's value at that part of the source.
                int n = Mathf.Min(o.Length, (int)(0.05f * SR));
                var head = new float[n]; System.Array.Copy(o, head, n);
                hz[i] = FrequencyAll(head);
                float v = EnvelopeRandom.Evaluate(pc, true, EnvelopeRandom.SeedFor(ids[i]), 0, 0.01f);
                predicted[i] = 440f * ModulationMath.RatioFromPosition(v);
            }
            bool match = Mathf.Abs(hz[0] - predicted[0]) / predicted[0] < 0.03f && Mathf.Abs(hz[1] - predicted[1]) / predicted[1] < 0.03f;
            bool apart = Mathf.Abs(hz[0] - hz[1]) / Mathf.Min(hz[0], hz[1]) > 0.03f;
            // The bug as it was: both plays numbered with the sound's id.
            float sameDiff = MaxDiff(RenderToken(clipData, plan, frames, 256, 4f, k.id), RenderToken(clipData, plan, frames, 256, 4f, k.id));
            sb.Append("   As it was (both plays numbered with the sound's id): largest sample difference between the two plays ")
              .Append(sameDiff.ToString("0.###")).Append(sameDiff == 0f ? " - identical, which is what the owner heard\n" : "\n");
            sb.Append("   Pitch curve ends may move +-12 st, two real plays: start at ").Append(hz[0].ToString("0")).Append(" Hz (predicted ")
              .Append(predicted[0].ToString("0")).Append(") and ").Append(hz[1].ToString("0")).Append(" Hz (predicted ").Append(predicted[1].ToString("0"))
              .Append("); match ").Append(match).Append(", plays differ ").Append(apart).Append(match && apart ? "  PASS\n" : "  FAIL\n");
            ok &= match && apart;
            return ok;
        }

        /// <summary>A tone's frequency from its rising zero crossings over the whole buffer.</summary>
        static float FrequencyAll(float[] o) {
            int first = -1, last = -1, count = 0;
            for (int i = 1; i < o.Length; i++) if (o[i - 1] <= 0f && o[i] > 0f) { if (first < 0) first = i; last = i; count++; }
            return count > 1 && last > first ? (count - 1) * SR / (float)(last - first) : 0f;
        }

        static float[] RenderToken(PcmClip clip, ZoundSapPlayback.PlayPlan plan, double endFrame, int block, float maxSeconds, long token) {
            var layout = plan.chain != null && !plan.chain.IsEmpty ? ChainLayout.Build(plan.chain, SR) : ChainLayout.Empty;
            var v = SapRealtimeVoice.Create(clip, layout, SR, 0, endFrame, 1f, 1f, (float)(endFrame / SR), false, token, layout.heavy, Allocator.Persistent,
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

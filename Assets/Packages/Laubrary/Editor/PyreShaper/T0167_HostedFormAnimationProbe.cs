// TEMP PROBE T-0167 — PM deletes after running
//
// Exercises all nine Pyre generators hosted behind PyreFormCompositeSource (T-0112) and confirms each one's
// ZUIValue dials actually animate over the Shaper phase now that PyreFormCompositeSource.Render resolves them
// through PyreShaperEval.Eval instead of freezing at their static value. For every form: creates a fresh
// instance, finds every public animatable ZUIValue field via reflection, switches each one to Curve mode with a
// ramp spanning the field's OWN authored [Range] (EnsureCurveDefaults, then forces yMin/yMax apart so the ramp is
// never a flat 0->0), renders at phase 0 / 0.5 / 1 into a small buffer, and reports whether the three renders
// differ.
//
// Range-respecting, not a blanket 0..1: the first version of this probe pinned EVERY field's Curve to a literal
// 0..1 span regardless of its own [Range] attribute. For a field whose real authored range sits nowhere near
// [0,1] -- Scale [0.2,2], R0 [0.5,10 source px], Hi [0.3,16] -- that forced Lo and Hi to the SAME degenerate
// number at every phase (Hi-Lo == 0, guarded to a tiny epsilon) and Scale/R0/RootR down to near-zero source px,
// which on a source frame 150-300 px wide maps to a WELL sub-canvas-pixel blob radius no per-pixel sample could
// ever land inside -- the jet family (RadialJetForm's default Corona variant, 700 slots into a 176 px frame) can
// render fully transparent at every one of the three sampled phases purely from that scale collapse, which
// LOOKS like "phase never reaches the draw" but is actually the probe dialling every jet form to a combination
// no author could ever reach through the real ZUI slider (its own [Range] never lets Scale below 0.2 or R0 below
// 0.5). Reading each field's own [Range(min,max)] for the Curve's Y span (falling back to 0..1 only when a field
// carries none) keeps every forced dial inside the same bounds ZUI's own control would clamp it to.
using System;
using System.Reflection;
using System.Text;
using Laubrary.Pyre;
using Laubrary.Pyre.Forms.Kiln;
using Laubrary.PyreShaper;
using UnityEngine;

namespace Laubrary.PyreShaper.Editor
{
    public static class T0167_HostedFormAnimationProbe
    {
        // Bumped from 24x24: the jet family's source frames run 144-300 px wide, and a too-small canvas leaves
        // even a properly-mid-range-scaled blob sub-pixel (see the class doc above).
        const int W = 64, H = 64;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            Type[] formTypes =
            {
                typeof(ArcBurstForm), typeof(ForkBlastForm), typeof(InfernoForm), typeof(OrbForm),
                typeof(ExplosiveJetForm), typeof(PlasmaBloomForm), typeof(RadialJetForm), typeof(JetForm),
                typeof(TorchForm),
            };

            foreach (var t in formTypes)
            {
                try { sb.AppendLine(ProbeOne(t)); }
                catch (Exception e) { sb.AppendLine($"{t.Name}: EXCEPTION {e.GetType().Name}: {e.Message}"); }
            }
            return sb.ToString();
        }

        static string ProbeOne(Type formType)
        {
            var form = (PyreForm)Activator.CreateInstance(formType);
            var animated = AnimateEveryDial(form);

            var src = new PyreFormCompositeSource { form = form };
            var a = Render(src, 0f);
            var b = Render(src, 0.5f);
            var c = Render(src, 1f);

            bool abDiffer = !SequenceEqualColors(a, b);
            bool acDiffer = !SequenceEqualColors(a, c);
            bool anyDiffer = abDiffer || acDiffer;

            string verdict = anyDiffer
                ? "ANIMATES (differs across phase)"
                : (animated == 0
                    ? "IDENTICAL — no animatable ZUIValue field found by reflection (nothing to animate)"
                    : "IDENTICAL — dials forced to Curve mode but output did not change across phase");

            return $"{formType.Name}: {animated} dial(s) forced to Curve, phase0 vs phase0.5 {(abDiffer ? "DIFFER" : "same")}, " +
                   $"phase0 vs phase1 {(acDiffer ? "DIFFER" : "same")} -> {verdict}";
        }

        /// Reflects every public instance ZUIValue field on the form — including one level down into nested
        /// plain-serializable settings objects (ArcBurst's per-layout settings boxes are the reason this recurses:
        /// its dials are not direct fields of ArcBurstForm itself, see PyreForm.Clone's own DeepCopyValue, which
        /// walks the same shape for the same reason) — and switches each one to a Curve ramp spanning a distinct
        /// low->high range, so ANY dial the form actually reads through Prepare's Eval will differ between phase 0
        /// and phase 1. Returns how many fields were touched.
        static int AnimateEveryDial(object obj, int depth = 0)
        {
            if (obj == null || depth > 2) return 0;
            int n = 0;
            foreach (var f in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(ZUIValue))
                {
                    var v = (ZUIValue)f.GetValue(obj);
                    if (v == null) { v = new ZUIValue(); f.SetValue(obj, v); }

                    var range = f.GetCustomAttribute<RangeAttribute>();
                    v.yMin = range != null ? range.min : 0f;
                    v.yMax = range != null ? range.max : 1f;
                    if (v.yMax <= v.yMin) v.yMax = v.yMin + 1f; // never a flat ramp regardless of an odd Range
                    v.mode = ZUIValue.Mode.Curve;
                    v.points.Clear();
                    v.EnsureCurveDefaults(); // seeds (0, yMin) -> (1, yMax): guaranteed to differ across phase
                    n++;
                    continue;
                }
                if (PyreForm.IsPlainSerializableClass(f.FieldType))
                    n += AnimateEveryDial(f.GetValue(obj), depth + 1);
            }
            return n;
        }

        static Color32[] Render(PyreFormCompositeSource src, float phase01)
        {
            var buf = new Color32[W * H];
            src.Render(W, H, phase01, 12345u, buf);
            return buf;
        }

        static bool SequenceEqualColors(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }
}

using Laubrary.Audio;
// A kept check for the curve-editing rules added 2026-09-30 (T-0509, T-0513).
//
//   1. Reset: a curve's "no change" value is read from its one binding: Ratio -> the middle, Scale -> one, Shift -> nought,
//      Set -> where the setting is set (as a position on the curve). A Klip's new pitch, volume and time curves each give
//      theirs, and the value really is no change: applying the combine to it returns the set value.
//   2. Bend: dragging down is the exact mirror of dragging up. A step up then the same step down returns the exponent, and
//      up by d gives e, down by d gives 1/e; t^e and t^(1/e) are inverse functions, so the two segment shapes are mirror
//      images across the straight segment (checked at sample points).
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsCurveEditCheck {

    [MenuItem("Laubrary/Zounds/Checks/28 - Curve editing: Reset values and a symmetric bend")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
        var kce = typeof(Laubrary.Zounds.Uitk.NamePopup).Assembly.GetType("Laubrary.Zounds.KlipChainEnvelopes");
        var neutral = kce.GetMethod("NeutralValue", BindingFlags.Public | BindingFlags.Static);
        var waveNeutral = kce.GetMethod("WaveformCurveNeutral", BindingFlags.Public | BindingFlags.Static);

        // ── 1: neutral values per combine, on one in-memory chain ──
        var chain = new ZoundEffectChain();
        var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2000f;
        chain.nodes.Add(lp);
        float Neutral(ModulationCombine c, int node, int param) {
            var m = new ZoundModifier(ZoundModifierType.Envelope);
            chain.modifiers.Add(m);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = chain.modifiers.Count - 1, nodeIndex = node, paramIndex = param, combine = c, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var args = new object[] { chain, m, 0f };
            bool ok = (bool)neutral.Invoke(null, args);
            return ok ? (float)args[2] : float.NaN;
        }
        float nRatio = Neutral(ModulationCombine.Ratio, -1, SourceStageParam.Pitch);
        float nScale = Neutral(ModulationCombine.Scale, -1, SourceStageParam.Volume);
        float nShift = Neutral(ModulationCombine.Shift, 0, 0);
        float nSet = Neutral(ModulationCombine.Set, 0, 0);
        Check(Mathf.Approximately(nRatio, 0.5f), "1. Ratio (pitch): the middle (" + nRatio + ")");
        Check(Mathf.Approximately(nScale, 1f), "1. Scale (volume): one (" + nScale + ")");
        Check(Mathf.Approximately(nShift, 0f), "1. Shift (cutoff): nought (" + nShift + ")");
        var pd = ZoundEffectDescriptors.Get(ZoundEffectType.LowPass).parameters[0];
        bool rs = ModulationMath.IsRatioSpaced(pd.curve);
        float heard = ModulationMath.Apply(ModulationCombine.SetFromZero, 2000f, nSet, 1f, pd.min, pd.max, rs);
        Check(Mathf.Abs(heard - 2000f) < 1f, "1. Set (cutoff 2000 Hz): the curve holds it where it is set (" + nSet.ToString("0.000") + " -> " + heard.ToString("0.0") + " Hz)");
        Check(Mathf.Abs(ModulationMath.Apply(ModulationCombine.Ratio, 1.3f, nRatio, 1f, 0.1f, 4f, true) - 1.3f) < 1e-4f, "1. Ratio's neutral really leaves the value alone");
        Check(Mathf.Abs(ModulationMath.Apply(ModulationCombine.Scale, 0.7f, nScale, 1f, 0f, 4f, false) - 0.7f) < 1e-4f, "1. Scale's neutral really leaves the value alone");
        Check(Mathf.Abs(ModulationMath.Apply(ModulationCombine.Shift, 2000f, nShift, 1f, pd.min, pd.max, rs) - 2000f) < 1f, "1. Shift's neutral really leaves the value alone");

        // A Klip's own new curves, in memory (the waveform's pitch, volume and time curves)
        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k) { src = k; break; }
        if (src != null) {
            var klip = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
            typeof(Zound).GetField("id").SetValue(klip, -9801);
            klip.effectChain = new ZoundEffectChain(); klip.chainPresetId = 0;
            string[] names = { "volume", "pitch", "time" };
            string[] setters = { "SetVolumeEnabled", "SetPitchEnabled", "SetTimeEnabled" };
            // A new volume curve is a Set curve across the Volume setting's range (0..4): x1 is where 1 sits on it.
            var vpd = ZoundEffectDescriptors.SourceStageParams[SourceStageParam.Volume];
            float[] expect = { ModulationMath.ToPosition(1f, vpd.min, vpd.max, ModulationMath.IsRatioSpaced(vpd.curve)), 0.5f, 0.5f };
            for (int w = 0; w < 3; w++) {
                kce.GetMethod(setters[w], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { klip, true });
                var a = new object[] { klip, w, 0f };
                bool ok = (bool)waveNeutral.Invoke(null, a);
                Check(ok && Mathf.Abs((float)a[2] - expect[w]) < 1e-4f, "1. a Klip's new " + names[w] + " curve: Reset goes to " + (ok ? ((float)a[2]).ToString("0.###") : "nothing") + " (no change is " + expect[w] + ")");
            }
        }

        // ── 2: the bend ──
        float up = Laubrary.Zui.ZuiSkinEnvelope.BendStep(1f, 0.1f), down = Laubrary.Zui.ZuiSkinEnvelope.BendStep(1f, -0.1f);
        Check(Mathf.Abs(up * down - 1f) < 1e-4f, "2. up by d gives e, down by d gives 1/e (" + up.ToString("0.000") + " x " + down.ToString("0.000") + " = " + (up * down).ToString("0.0000") + ")");
        float back = Laubrary.Zui.ZuiSkinEnvelope.BendStep(Laubrary.Zui.ZuiSkinEnvelope.BendStep(2.3f, 0.07f), -0.07f);
        Check(Mathf.Abs(back - 2.3f) < 1e-3f, "2. a step up then the same step down returns the exponent (" + back.ToString("0.0000") + ")");
        float worst = 0f;
        for (int i = 1; i < 20; i++) {
            float t = i / 20f;
            float y = Mathf.Pow(t, up);                 // bent up
            float mirrorX = Mathf.Pow(y, 1f / up);      // reflect (t, y) across the straight segment -> (y, t)
            worst = Mathf.Max(worst, Mathf.Abs(Mathf.Pow(y, down) - t));   // the bent-down curve at y must be t
            worst = Mathf.Max(worst, Mathf.Abs(mirrorX - t));
        }
        Check(worst < 1e-4f, "2. the bent-down shape is the mirror of the bent-up one across the straight segment (worst " + worst.ToString("0.000000") + ")");

        return (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - curve editing: Reset values and a symmetric bend\n" + sb;
    }
}

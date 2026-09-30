// A kept check for anchoring curves to seconds in the source (T-0501, T-0560).
//
//   1. Converting a trim-anchored curve to source anchoring plays identically: a bent volume curve and a pitch curve on a
//      trimmed synthetic tone render the same samples before and after conversion.
//   2. The same with extra time after the source, where a segment crosses from the audio into the tail and is split.
//   3. After conversion, re-trimming leaves the curve on the same audio: the level heard at source second 1.0 is the same
//      under two different trims; with the old anchoring it is not (shown for contrast).
//   4. The play length worked out from the curves is the same before and after conversion.
// Everything is in memory: a generated tone and chains that are never part of the project.
using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsCurveAnchorCheck {

    [MenuItem("Laubrary/Zounds/Checks/30 - Curves stay on the audio through a re-trim")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    const int SR = 48000;

    static float[] Tone(float seconds) {
        int n = (int)(seconds * SR);
        var s = new float[n];
        for (int i = 0; i < n; i++) s[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 440f * i / SR);
        return s;
    }

    static ZoundEffectChain Chain(bool withPitch, float extra) {
        var c = new ZoundEffectChain();
        var gain = new ZoundEffectNode(ZoundEffectType.Gain); gain.p[0] = 1f;
        c.nodes.Add(gain);
        var vol = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume" };
        vol.p[0] = extra; vol.p[1] = 0f;                       // extra time, Waveform time base
        var vp = vol.curve.GetPointsList(); vp.Clear();
        vp.Add(new ZUIEnvelopePoint(0f, 1f, 1f)); vp.Add(new ZUIEnvelopePoint(0.4f, 0.2f, 2.5f)); vp.Add(new ZUIEnvelopePoint(1f, 0.9f, 0.6f));
        c.modifiers.Add(vol);
        c.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = -1, paramIndex = SourceStageParam.Volume,
            combine = ModulationCombine.Scale, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
        if (withPitch) {
            var pit = new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch" };
            pit.p[0] = extra; pit.p[1] = 0f;
            var pp = pit.curve.GetPointsList(); pp.Clear();
            pp.Add(new ZUIEnvelopePoint(0f, 0.5f, 1f)); pp.Add(new ZUIEnvelopePoint(0.6f, 0.62f, 1.8f)); pp.Add(new ZUIEnvelopePoint(1f, 0.45f, 1f));
            c.modifiers.Add(pit);
            c.bindings.Add(new ZoundModifierBinding { modifierIndex = 1, nodeIndex = -1, paramIndex = SourceStageParam.Pitch,
                combine = ModulationCombine.Ratio, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
        }
        return c;
    }

    static float[] Render(float[] src, ZoundEffectChain chain, float a, float b, float seconds) {
        var r = ZoundDspOffline.Render(src, 1, SR, SR, chain, 1f, 1f, seconds, a, b);
        return r != null ? r.left : null;
    }

    static float MaxDiff(float[] x, float[] y) {
        if (x == null || y == null) return float.NaN;
        int n = Math.Min(x.Length, y.Length); float m = 0f;
        for (int i = 0; i < n; i++) m = Math.Max(m, Math.Abs(x[i] - y[i]));
        return m;
    }

    static float Rms(float[] x, int from, int count) {
        double s = 0; int n = 0;
        for (int i = Math.Max(0, from); i < Math.Min(x.Length, from + count); i++) { s += x[i] * x[i]; n++; }
        return n > 0 ? (float)Math.Sqrt(s / n) : float.NaN;
    }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        var src = Tone(2f);
        var axis = new CurveAnchor.Axis { trimStart = 0.5f, trimEnd = 1.5f, sourceLength = 2f };

        // ── 1: no extra time ──
        var legacy = Chain(true, 0f);
        var conv = legacy.DeepCopy();
        bool c1 = true; foreach (var m in conv.modifiers) c1 &= CurveAnchor.ConvertToSource(m, axis);
        float d1 = MaxDiff(Render(src, legacy, 0.5f, 1.5f, 1.6f), Render(src, conv, 0.5f, 1.5f, 1.6f));
        Check(c1 && d1 < 1e-4f, "1. converted curves render the same samples as before (largest difference " + d1.ToString("0.0e0") + ")");

        // ── 2: with extra time ──
        var legacyX = Chain(true, 0.3f);
        var convX = legacyX.DeepCopy();
        foreach (var m in convX.modifiers) CurveAnchor.ConvertToSource(m, axis);
        float d2 = MaxDiff(Render(src, legacyX, 0.5f, 1.5f, 1.9f), Render(src, convX, 0.5f, 1.5f, 1.9f));
        Check(d2 < 5e-3f, "2. with 0.3 s of extra time too (largest difference " + d2.ToString("0.0e0") + "; a bent segment crossing into the tail is split)");

        // ── 3: re-trim keeps the curve on the audio (volume curve only, fixed pitch) ──
        var volOnly = Chain(false, 0f);
        var volConv = volOnly.DeepCopy();
        CurveAnchor.ConvertToSource(volConv.modifiers[0], axis);
        int win = SR / 50;                                   // 20 ms around source second 1.0
        float LevelAt(ZoundEffectChain ch, float a) { var o = Render(src, ch, a, 1.5f, 1.6f - a); return Rms(o, (int)((1.0f - a) * SR) - win / 2, win); }
        float sA = LevelAt(volConv, 0.5f), sB = LevelAt(volConv, 0.7f);
        float oA = LevelAt(volOnly, 0.5f), oB = LevelAt(volOnly, 0.7f);
        Check(Math.Abs(sA - sB) / Math.Max(sA, 1e-6f) < 0.02f,
              "3. source-anchored: the level at source second 1.0 is the same under trims 0.5-1.5 and 0.7-1.5 (" + sA.ToString("0.000") + " vs " + sB.ToString("0.000") + ")");
        Check(Math.Abs(oA - oB) / Math.Max(oA, 1e-6f) > 0.05f,
              "3. (contrast) trim-anchored: the same point moves onto different audio (" + oA.ToString("0.000") + " vs " + oB.ToString("0.000") + ")");

        // ── 4: the play length from the curves is unchanged ──
        float l0 = ZoundDspPlayback.PlayLengthOverSource(legacy, 1f, false, false, false, 0, axis);
        float l1 = ZoundDspPlayback.PlayLengthOverSource(conv, 1f, false, false, false, 0, axis);
        Check(Math.Abs(l0 - l1) < 1e-3f, "4. the play length worked out from the pitch curve is the same (" + l0.ToString("0.0000") + " s vs " + l1.ToString("0.0000") + " s)");

        return (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - curves anchored to the source\n" + sb;
    }
}

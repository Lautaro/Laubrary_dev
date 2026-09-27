// A kept check for the thing the owner reported: modulator settings producing values that make no sense for whatever they
// were attached to.
//
// Three claims are made about the fix, and all three are the kind that sound obviously true and can quietly not be:
//
//   1. One depth means one thing on every parameter. The test attaches the SAME oscillator at the SAME depth to two
//      parameters whose ranges have nothing in common — a cutoff spanning twenty to twenty thousand, and a resonance
//      spanning nought to one — and requires them to sweep the same share of their own travel. Under the old arrangement
//      these two differed by a factor of twenty thousand, which is the entire complaint.
//
//   2. An oscillator sweeps AROUND the value you set rather than collapsing onto an end stop. The old way of attaching one
//      drove the parameter negative for half of every cycle, where it was clamped to the minimum and sat. Measured then:
//      a cutoff set to 2000 Hz ranged from 1999.6 Hz down to exactly the 20 Hz floor. The test requires the sweep to be
//      centred on the authored value — geometrically, for a control spaced by ratio, which is the honest centre for one.
//
//   3. A parameter with nothing attached does not move, in the same render. This is the control: it is what catches a
//      "fix" that simply moves everything a bit.
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsModulationSanityCheck {

    const int SR = 48000;
    const int BLOCK = 512;
    const float SECONDS = 1.0f;
    const float DEPTH = 0.25f;

    [MenuItem("Laubrary/Zounds/Checks/13 - Modulator depth means the same on every parameter")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        int failures = 0;

        // Parameter 0 of a low pass is its cutoff, spaced by ratio; parameter 1 is its resonance, evenly spaced. Two
        // parameters of the SAME effect, so nothing about the effect itself can explain a difference between them.
        var cutoff = Sweep(0, 2000f, sb, out float cutMin, out float cutMax);
        var reso = Sweep(1, 0.5f, sb, out float resMin, out float resMax);
        if (cutoff == null || reso == null) return sb.ToString() + "could not build the test chains\n";

        var lpDesc = ZoundEffectDescriptors.Get(ZoundEffectType.LowPass);
        var cutDesc = lpDesc.parameters[0];
        var resDesc = lpDesc.parameters[1];

        // ── claim 1: the same depth sweeps the same share of each parameter's own travel ──
        float cutShare = ModulationMath.ToPosition(cutMax, cutDesc.min, cutDesc.max, ModulationMath.IsRatioSpaced(cutDesc.curve))
                       - ModulationMath.ToPosition(cutMin, cutDesc.min, cutDesc.max, ModulationMath.IsRatioSpaced(cutDesc.curve));
        float resShare = ModulationMath.ToPosition(resMax, resDesc.min, resDesc.max, ModulationMath.IsRatioSpaced(resDesc.curve))
                       - ModulationMath.ToPosition(resMin, resDesc.min, resDesc.max, ModulationMath.IsRatioSpaced(resDesc.curve));
        sb.Append("cutoff swept    ").Append(cutMin.ToString("F1")).Append(" .. ").Append(cutMax.ToString("F1"))
          .Append(" Hz  = ").Append((cutShare * 100f).ToString("F1")).Append("% of its control\n");
        sb.Append("resonance swept ").Append(resMin.ToString("F3")).Append(" .. ").Append(resMax.ToString("F3"))
          .Append("      = ").Append((resShare * 100f).ToString("F1")).Append("% of its control\n");
        sb.Append("raw spans differ by a factor of ")
          .Append(((cutMax - cutMin) / Mathf.Max(resMax - resMin, 1e-9f)).ToString("F0"))
          .Append(", which is why a raw depth could never mean one thing\n");

        if (Mathf.Abs(cutShare - resShare) > 0.02f) {
            sb.Append("   <<< the same depth must sweep the same share of each parameter's control\n"); failures++;
        }
        float expected = 2f * DEPTH;   // a full-swing oscillator reaches the depth either side
        if (Mathf.Abs(cutShare - expected) > 0.03f) {
            sb.Append("   <<< a depth of ").Append(DEPTH).Append(" should sweep ").Append(expected * 100f).Append("% of the control\n"); failures++;
        }

        // ── claim 2: the sweep is centred on the value that was set ──
        //
        // "Centre" has to be asked of the parameter rather than assumed. A control spaced by ratio is centred
        // geometrically — halfway between 200 and 800 on such a control is 400, not 500 — and the first version of this
        // check assumed the resonance was evenly spaced and duly reported a failure against a perfectly correct sweep.
        // The centre is now computed by going back through the parameter's own spacing, which cannot make that mistake.
        float cutCentre = CentreOf(cutMin, cutMax, cutDesc);
        float resCentre = CentreOf(resMin, resMax, resDesc);
        sb.Append("cutoff sweep centred on ").Append(cutCentre.ToString("F1")).Append(" Hz (set to 2000)\n");
        sb.Append("resonance sweep centred on ").Append(resCentre.ToString("F3")).Append(" (set to 0.500)\n");
        if (Mathf.Abs(cutCentre - 2000f) / 2000f > 0.05f) {
            sb.Append("   <<< the sweep must be centred on the authored value, not collapsed toward an end stop\n"); failures++;
        }
        if (Mathf.Abs(resCentre - 0.5f) / 0.5f > 0.05f) {
            sb.Append("   <<< same for the other parameter\n"); failures++;
        }
        // The specific old failure: half the cycle pinned at the floor.
        if (cutMin <= cutDesc.min * 1.05f) {
            sb.Append("   <<< the cutoff reached its floor, which is the old collapse this was meant to end\n"); failures++;
        }

        // ── claim 3: the control case ──
        sb.Append("resonance while only the cutoff was modulated: ")
          .Append(cutoff[0].ToString("F4")).Append(" .. ").Append(cutoff[1].ToString("F4")).Append(" (set to 0.700)\n");
        if (Mathf.Abs(cutoff[0] - 0.7f) > 1e-5f || Mathf.Abs(cutoff[1] - 0.7f) > 1e-5f) {
            sb.Append("   <<< a parameter with nothing bound to it must not move\n"); failures++;
        }

        sb.Append(failures == 0
            ? "PASS — one depth, one meaning, on parameters twenty thousand times apart in scale.\n"
            : "FAIL — " + failures + " problem(s) above.\n");
        return sb.ToString();
    }

    /// <summary>
    /// Renders a low pass with an oscillator shifting one of its parameters, and reports how far that parameter actually
    /// travelled. Returns the range the OTHER parameter held, as the untouched control.
    /// </summary>
    static float[] Sweep(int paramIndex, float baseValue, StringBuilder sb, out float lo, out float hi) {
        lo = 0f; hi = 0f;
        var chain = new ZoundEffectChain();
        var lp = new ZoundEffectNode(ZoundEffectType.LowPass);
        lp.p[0] = paramIndex == 0 ? baseValue : 2000f;
        lp.p[1] = paramIndex == 1 ? baseValue : 0.7f;
        chain.nodes.Add(lp);

        var lfo = new ZoundModifier(ZoundModifierType.Lfo);
        lfo.p[0] = 1f;   // full swing
        lfo.p[1] = 8f;   // several cycles inside the render
        lfo.p[2] = 0f;   // sine
        if (lfo.p.Length > 3) lfo.p[3] = 1f;
        chain.modifiers.Add(lfo);

        chain.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = paramIndex,
            combine = ModulationCombine.Shift, depth = DEPTH, schema = ChainModulationCompat.CURRENT_SCHEMA
        });

        var layout = ChainLayout.Build(chain, SR);
        if (layout.error != null) { sb.Append("layout refused: ").Append(layout.error).Append('\n'); return null; }
        int moved = SapVoiceRegistry.FlatIndexOf(layout, 0, paramIndex);
        int other = SapVoiceRegistry.FlatIndexOf(layout, 0, paramIndex == 0 ? 1 : 0);

        lo = float.MaxValue; hi = float.MinValue;
        float otherLo = float.MaxValue, otherHi = float.MinValue;

        int frames = Mathf.CeilToInt(SECONDS * SR);
        var pcm = Noise(frames);
        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, 1f, 1f,
                                           (float)pcm.frames / SR, false, 1, true, Allocator.Persistent);
        try {
            int w = 0;
            while (w < frames && !voice.finished) {
                int n = Mathf.Min(BLOCK, frames - w);
                voice.RenderBlock(n);
                float v = voice.sap.pLive[moved];
                if (v < lo) lo = v; if (v > hi) hi = v;
                float o = voice.sap.pLive[other];
                if (o < otherLo) otherLo = o; if (o > otherHi) otherHi = o;
                w += n;
            }
        }
        finally { voice.Dispose(); }
        return new[] { otherLo, otherHi };
    }

    /// <summary>The middle of a sweep, measured the way the parameter's own control is spaced.</summary>
    static float CentreOf(float lo, float hi, ParamDesc pd) {
        bool ratio = ModulationMath.IsRatioSpaced(pd.curve);
        float mid = (ModulationMath.ToPosition(lo, pd.min, pd.max, ratio)
                   + ModulationMath.ToPosition(hi, pd.min, pd.max, ratio)) * 0.5f;
        return ModulationMath.FromPosition(mid, pd.min, pd.max, ratio);
    }

    static PcmClip Noise(int frames) {
        var data = new float[frames * 2];
        var rng = new System.Random(11);
        float peak = 0f;
        for (int i = 0; i < frames; i++) {
            float s = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.4f;
            data[i * 2] = s; data[i * 2 + 1] = s;
            float a = s < 0f ? -s : s; if (a > peak) peak = a;
        }
        return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = data, valid = true, peak = peak };
    }
}

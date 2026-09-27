// A kept check for the number the parameter sliders now display: the value the engine is USING for a parameter, after every
// modifier attached to it has had its say.
//
// Why this needs checking rather than reasoning about. A slider that shows a second, animated value is believable whatever it
// draws — if the number it reads were stale, or were the authored value all along, or were some neighbouring parameter's, the
// display would still look alive and plausible. So the check is built around the one thing a wrong reading cannot fake at the
// same time: a parameter WITH an oscillator on it must move, and a parameter WITHOUT one must not budge by even a rounding
// error, in the very same render. Getting both right at once is hard to do by accident.
//
// It renders offline, block by block, and samples the value between blocks. That covers where the value comes from and
// whether it is right. It does not cover the hand-off from the audio thread to the editor, which only exists while the graph
// is running — that is the same shared-memory arrangement the output monitor already uses, and the monitor's own check
// exercises it.
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsLiveParamReadCheck {

    const int SR = 48000;
    const int BLOCK = 512;
    const float SECONDS = 1.0f;

    [MenuItem("Laubrary/Zounds/Checks/12 - A modulated parameter's live value")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        int failures = 0;

        // A low-pass whose cutoff an oscillator sweeps, and a resonance nobody touches. One chain, so both are measured
        // under identical conditions and neither result can be explained by the setup.
        var chain = new ZoundEffectChain();
        var lp = new ZoundEffectNode(ZoundEffectType.LowPass);
        lp.p[0] = 2000f;   // cutoff, the modulated one
        lp.p[1] = 0.7f;    // resonance, the control
        chain.nodes.Add(lp);

        var lfo = new ZoundModifier(ZoundModifierType.Lfo);
        lfo.p[0] = 1f;     // amount: full
        lfo.p[1] = 8f;     // rate: fast enough to see several cycles in a second
        lfo.p[2] = 0f;     // shape: sine
        if (lfo.p.Length > 3) lfo.p[3] = 1f;  // reset phase on, so the run is repeatable
        chain.modifiers.Add(lfo);

        chain.bindings.Add(new ZoundModifierBinding {
            modifierIndex = 0, nodeIndex = 0, paramIndex = 0, op = ModifierOp.Multiply, depth = 1f
        });

        var layout = ChainLayout.Build(chain, SR);
        if (layout.error != null) return "layout refused the chain: " + layout.error;

        int cutoffFlat = SapVoiceRegistry.FlatIndexOf(layout, 0, 0);
        int resFlat = SapVoiceRegistry.FlatIndexOf(layout, 0, 1);
        sb.Append("cutoff sits at flat ").Append(cutoffFlat).Append(", resonance at flat ").Append(resFlat).Append('\n');
        if (cutoffFlat < 0 || resFlat < 0 || cutoffFlat == resFlat) {
            sb.Append("   <<< the two parameters must resolve to two different slots\n");
            return sb.ToString();
        }

        var cutoff = new System.Collections.Generic.List<float>();
        var resonance = new System.Collections.Generic.List<float>();

        int frames = Mathf.CeilToInt(SECONDS * SR);
        var pcm = Noise(frames);
        var voice = SapRealtimeVoice.Create(pcm, layout, SR, 0d, pcm.frames, 1f, 1f,
                                           (float)pcm.frames / SR, false, 1, true, Allocator.Persistent);
        try {
            int w = 0;
            while (w < frames && !voice.finished) {
                int n = Mathf.Min(BLOCK, frames - w);
                voice.RenderBlock(n);
                cutoff.Add(voice.sap.pLive[cutoffFlat]);
                resonance.Add(voice.sap.pLive[resFlat]);
                w += n;
            }
        }
        finally { voice.Dispose(); }

        sb.Append("blocks sampled: ").Append(cutoff.Count).Append('\n');
        if (cutoff.Count < 20) { sb.Append("   <<< too few blocks to judge movement\n"); failures++; }

        Span(cutoff, out float cLo, out float cHi);
        Span(resonance, out float rLo, out float rHi);
        sb.Append("modulated cutoff:   ").Append(cLo.ToString("F1")).Append(" .. ").Append(cHi.ToString("F1"))
          .Append(" Hz   (authored ").Append(lp.p[0].ToString("F0")).Append(")\n");
        sb.Append("untouched resonance: ").Append(rLo.ToString("F4")).Append(" .. ").Append(rHi.ToString("F4"))
          .Append("   (authored ").Append(lp.p[1].ToString("F4")).Append(")\n");

        // The modulated one must actually move, and by a margin nobody could mistake for noise.
        if (cHi - cLo < 1f) { sb.Append("   <<< the oscillator is bound to the cutoff, so it must not sit still\n"); failures++; }

        // The untouched one must be EXACTLY its authored value. This is the half that proves the reading is specific: if the
        // display were reading the wrong slot, or smearing across slots, this would wobble.
        if (Mathf.Abs(rHi - lp.p[1]) > 1e-6f || Mathf.Abs(rLo - lp.p[1]) > 1e-6f) {
            sb.Append("   <<< a parameter with nothing bound to it must read exactly what was authored\n"); failures++;
        }

        // And it must stay inside the parameter's own limits, or a slider would draw fill past its own end.
        float min = layout.pMin[cutoffFlat], max = layout.pMax[cutoffFlat];
        sb.Append("cutoff limits: ").Append(min.ToString("F1")).Append(" .. ").Append(max.ToString("F1")).Append('\n');
        if (cLo < min - 0.01f || cHi > max + 0.01f) {
            sb.Append("   <<< the live value left the parameter's range\n"); failures++;
        }

        // It should sweep, not jump between two values: a sine-driven parameter visits the middle of its travel often.
        int inMiddle = 0;
        float band = (cHi - cLo) * 0.25f;
        float centre = (cHi + cLo) * 0.5f;
        foreach (float v in cutoff) if (Mathf.Abs(v - centre) < band) inMiddle++;
        sb.Append("blocks in the middle of the sweep: ").Append(inMiddle).Append(" of ").Append(cutoff.Count).Append('\n');
        if (inMiddle < 3) { sb.Append("   <<< a sine sweep should pass through its middle, not flip between extremes\n"); failures++; }

        sb.Append(failures == 0
            ? "PASS — the live reading moves where a modifier is attached and is exact where none is.\n"
            : "FAIL — " + failures + " problem(s) above.\n");
        return sb.ToString();
    }

    static void Span(System.Collections.Generic.List<float> values, out float lo, out float hi) {
        lo = float.MaxValue; hi = float.MinValue;
        foreach (float v in values) { if (v < lo) lo = v; if (v > hi) hi = v; }
    }

    /// <summary>Broadband material, so the filter has something to work on and nothing depends on a project asset.</summary>
    static PcmClip Noise(int frames) {
        var data = new float[frames * 2];
        var rng = new System.Random(7);
        float peak = 0f;
        for (int i = 0; i < frames; i++) {
            float s = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.4f;
            data[i * 2] = s;
            data[i * 2 + 1] = s;
            float a = s < 0f ? -s : s;
            if (a > peak) peak = a;
        }
        return new PcmClip { channels = 2, frequency = SR, frames = frames, samples = data, valid = true, peak = peak };
    }
}

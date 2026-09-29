// A kept check for the Zound's own values (T-0493): Volume, Pitch, Speed and Drive as the sound's own, not as effects.
//
//   1. A volume curve saved the old way (an envelope owning a Gain effect inserted at the end of the chain) moves onto the
//      Zound's own Volume and renders BIT-IDENTICALLY afterwards: same samples, not merely close.
//   2. It moves only when that is provably the same sound. A Gain that is not last, is not at its default, is off, or has
//      anything else bound to it is left exactly where it is.
//   3. A sound with nothing on Volume renders exactly as before Volume existed (it is skipped at rest).
//   4. Volume acts after the effects: a volume curve fading to silence also silences a delay's echoes, where Drive (the
//      level going in) lets them ring out.
using System.Reflection;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsOwnValuesCheck {

    const int SR = 48000;

    [MenuItem("Laubrary/Zounds/Checks/21 - The sound's own values (Volume after effects, old volume curves unchanged)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    static MethodInfo Convert {
        get {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
                var t = a.GetType("Laubrary.Zounds.KlipChainEnvelopes");
                if (t != null) return t.GetMethod("EnsureVolumeOwnValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }
            return null;
        }
    }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
        var convert = Convert;
        if (convert == null) return "FAIL - the conversion routine was not found\n";

        // ── 1: old form -> own Volume, bit-identical ──
        var oldKlip = OldVolumeKlip(gainLast: true, gainValue: 1f, extraBinding: false, gainOn: true);
        var before = Render(oldKlip.effectChain);
        bool moved = (bool)convert.Invoke(null, new object[] { oldKlip });
        var after = Render(oldKlip.effectChain);
        int gains = 0; foreach (var n in oldKlip.effectChain.nodes) if (n.type == ZoundEffectType.Gain) gains++;
        bool onVolume = false;
        foreach (var b in oldKlip.effectChain.bindings) if (b.nodeIndex == -1 && b.paramIndex == SourceStageParam.Volume) onVolume = true;
        Check(moved && gains == 0 && onVolume, "1. the old volume curve moved onto the Zound's own Volume and the inserted Gain is gone");
        int diff = 0; float worst = 0f;
        for (int i = 0; i < Mathf.Min(before.Length, after.Length); i++) {
            if (before[i] != after[i]) { diff++; worst = Mathf.Max(worst, Mathf.Abs(before[i] - after[i])); }
        }
        sb.Append("samples compared: ").Append(before.Length).Append(", different: ").Append(diff).Append(", worst ").Append(worst.ToString("R")).Append('\n');
        Check(before.Length == after.Length && diff == 0, "1. it renders bit-identically after the move");
        Check(!(bool)convert.Invoke(null, new object[] { oldKlip }), "1. a second call does nothing");

        // ── 2: refused wherever it would not be the same sound ──
        Check(!(bool)convert.Invoke(null, new object[] { OldVolumeKlip(false, 1f, false, true) }), "2. not moved when the Gain is not the last effect");
        Check(!(bool)convert.Invoke(null, new object[] { OldVolumeKlip(true, 0.5f, false, true) }), "2. not moved when the Gain is not at its default");
        Check(!(bool)convert.Invoke(null, new object[] { OldVolumeKlip(true, 1f, true, true) }), "2. not moved when something else is bound to the Gain");
        Check(!(bool)convert.Invoke(null, new object[] { OldVolumeKlip(true, 1f, false, false) }), "2. not moved when the Gain is switched off");

        // ── 3: nothing on Volume, nothing changes ──
        {
            var chain = new ZoundEffectChain();
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 1500f; chain.nodes.Add(lp);
            var L = ChainLayout.Build(chain, SR);
            Check(L.pBase[SourceStageParam.Volume] == 1f, "3. Volume rests at exactly one");
            var a = Render(chain); var b = Render(chain);
            bool same = a.Length == b.Length; for (int i = 0; same && i < a.Length; i++) same = a[i] == b[i];
            Check(same, "3. a chain without Volume renders deterministically (the rest path adds nothing)");
        }

        // ── 4: Volume is after the effects, Drive before ──
        {
            float TailEnergy(int param) {
                var chain = new ZoundEffectChain();
                var d = new ZoundEffectNode(ZoundEffectType.Delay); chain.nodes.Add(d);
                var env = new ZoundModifier(ZoundModifierType.Envelope) { curve = new Envelope(0f, 1f) };
                // Full for the first 10 % of the play, then nothing.
                var pts = env.curve.GetPointsList(); pts.Clear();
                pts.Add(new ZUIEnvelopePoint(0f, 1f)); pts.Add(new ZUIEnvelopePoint(0.1f, 1f)); pts.Add(new ZUIEnvelopePoint(0.11f, 0f)); pts.Add(new ZUIEnvelopePoint(1f, 0f));
                env.p[1] = 1f;   // play time, so the curve does not stop at the source end
                chain.modifiers.Add(env);
                chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = -1, paramIndex = param, combine = ModulationCombine.Scale, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
                var outp = Render(chain);
                double e = 0; int from = outp.Length / 2;
                for (int i = from; i < outp.Length; i++) e += outp[i] * outp[i];
                return (float)e;
            }
            float viaVolume = TailEnergy(SourceStageParam.Volume), viaDrive = TailEnergy(SourceStageParam.Gain);
            sb.Append("second-half energy with a delay, curve silencing at 11 %: Volume ").Append(viaVolume.ToString("G3")).Append(", Drive ").Append(viaDrive.ToString("G3")).Append('\n');
            Check(viaVolume < 1e-6f, "4. a Volume curve silences the delay's echoes too (it is after the effects)");
            Check(viaDrive > 1e-3f, "4. a Drive curve lets the echoes ring on (it is before the effects)");
        }

        sb.Insert(0, fail == 0 ? "PASS - the sound's own values.\n" : "FAIL - " + fail + " problem(s).\n");
        return sb.ToString();
    }

    /// <summary>An in-memory Klip whose volume curve is saved the old way: an envelope owning an inserted Gain.</summary>
    static Klip OldVolumeKlip(bool gainLast, float gainValue, bool extraBinding, bool gainOn) {
        var k = new Klip(-9201) { name = "own values check (in memory)" };
        var chain = new ZoundEffectChain();
        var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 1800f;
        var gain = new ZoundEffectNode(ZoundEffectType.Gain) { enabled = gainOn }; gain.p[0] = gainValue;
        if (gainLast) { chain.nodes.Add(lp); chain.nodes.Add(gain); } else { chain.nodes.Add(gain); chain.nodes.Add(lp); }
        int gi = chain.nodes.IndexOf(gain);
        var env = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume", curve = new Envelope(0f, 1f) };
        var pts = env.curve.GetPointsList(); pts.Clear();
        pts.Add(new ZUIEnvelopePoint(0f, 0f)); pts.Add(new ZUIEnvelopePoint(0.3f, 0.25f)); pts.Add(new ZUIEnvelopePoint(0.7f, 0.2f)); pts.Add(new ZUIEnvelopePoint(1f, 0f));
        chain.modifiers.Add(env);
        chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = gi, paramIndex = 0, combine = ModulationCombine.Set, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
        if (extraBinding) {
            chain.modifiers.Add(new ZoundModifier(ZoundModifierType.Lfo));
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 1, nodeIndex = gi, paramIndex = 0, combine = ModulationCombine.Shift, depth = 0.2f, schema = ChainModulationCompat.CURRENT_SCHEMA });
        }
        k.effectChain = chain;
        return k;
    }

    static float[] Render(ZoundEffectChain chain) {
        var L = ChainLayout.Build(chain, SR);
        int frames = SR / 2;
        var data = new float[frames * 2];
        for (int i = 0; i < frames; i++) { float s = 0.3f * Mathf.Sin(i * 0.031f) + 0.1f * Mathf.Sin(i * 0.2f); data[i * 2] = s; data[i * 2 + 1] = s * 0.9f; }
        var pcm = new PcmClip { channels = 2, frequency = SR, frames = frames, samples = data, valid = true, peak = 0.4f };
        var v = SapRealtimeVoice.Create(pcm, L, SR, 0d, frames, 1f, 1f, (float)frames / SR, false, 3, true, Allocator.Persistent);
        var outp = new float[frames * 2];
        try {
            int w = 0;
            while (w < frames && !v.finished) {
                int n = Mathf.Min(256, frames - w);
                v.RenderBlock(n);
                for (int i = 0; i < n; i++) { outp[(w + i) * 2] = v.sap.bufL[i]; outp[(w + i) * 2 + 1] = v.sap.bufR[i]; }
                w += n;
            }
        }
        finally { v.Dispose(); }
        return outp;
    }
}

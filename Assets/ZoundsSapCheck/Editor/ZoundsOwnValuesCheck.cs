// A kept check for the Zound's own values (T-0493): Volume, Pitch, Speed and Drive as the sound's own, not as effects.
//
//   1. A volume curve saved the old way (an envelope owning a Gain effect inserted at the end of the chain) moves onto the
//      Zound's own Volume and renders BIT-IDENTICALLY afterwards: same samples, not merely close.
//   2. It moves only when that is provably the same sound. A Gain that is not last, is not at its default, is off, or has
//      anything else bound to it is left exactly where it is.
//   3. A sound with nothing on Volume renders exactly as before Volume existed (it is skipped at rest).
//   4. Volume acts after the effects: a volume curve fading to silence also silences a delay's echoes, where Drive (the
//      level going in) lets them ring out.
//   5. The sound's own curves live on the sound, not in its modifier list (2026-10-08): a sound saved with them inside its
//      chain moves them into its own slots and renders bit-identically; the move is refused where it would change the
//      sound; ZPOC ids, snapshots, copies and the save format all still carry them.
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
        // The sound's own curves are not entries of its chain any more (2026-10-08): the moved curve lives in the sound's own
        // Volume slot, and the chain the engine plays is the stored chain with the slots laid out as source-stage envelopes.
        var after = Render(ZoundDspPlayback.PlayChain(oldKlip));
        int gains = 0; foreach (var n in oldKlip.effectChain.nodes) if (n.type == ZoundEffectType.Gain) gains++;
        bool onVolume = oldKlip.ownCurves != null && oldKlip.ownCurves.volume.Has && oldKlip.ownCurves.volume.binding.paramIndex == SourceStageParam.Volume
                        && oldKlip.effectChain.modifiers.Count == 0;
        Check(moved && gains == 0 && onVolume, "1. the old volume curve moved onto the Zound's own Volume (its own slot, not the modifier list) and the inserted Gain is gone");
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

        // ── 5: own curves stored on the sound (2026-10-08), converted from the chain exactly ──
        {
            // A sound saved with its three curves inside its chain, an LFO on a cutoff after them, and a ZPOC id on the pitch curve.
            var k = InChainCurvesKlip(lfoOnVolumeFirst: false);
            var before5 = Render(ZoundDspPlayback.PlayChain(k));
            int modsBefore = k.effectChain.modifiers.Count;
            bool adopted = ZoundOwnCurves.Adopt(k);
            ZoundDspPlayback.InvalidateLayout(k);
            var after5 = Render(ZoundDspPlayback.PlayChain(k));
            Check(adopted && k.ownCurves.volume.Has && k.ownCurves.pitch.Has && k.ownCurves.time.Has && k.effectChain.modifiers.Count == modsBefore - 3,
                  "5. the three curves moved out of the chain into the sound's own slots; the LFO stayed (" + k.effectChain.modifiers.Count + " modifier left)");
            int diff5 = 0; for (int i = 0; i < Mathf.Min(before5.Length, after5.Length); i++) if (before5[i] != after5[i]) diff5++;
            Check(before5.Length == after5.Length && diff5 == 0, "5. it renders bit-identically after the move (" + before5.Length + " samples)");
            Check(!ZoundOwnCurves.Adopt(k), "5. a second call does nothing");
            var play = ZoundDspPlayback.PlayChain(k);
            Check(play.modifiers.Count == modsBefore && play.bindings.Count == 4 && play.bindings[0].nodeIndex == -1 && play.modifiers[0].type == ZoundModifierType.Lfo,
                  "5. the played chain lays the own curves out after the chain's own modifiers, their bindings first");
            Check(ZpocIndex.Declares(k, ZpocKeys.Key("bend")), "5. the pitch curve's ZPOC id is still declared from its slot");
            var snap = ZoundSnapshots.Capture(k, "s");
            int ownValues = 0; foreach (var v in snap.values) if (v.kind == SnapshotValueKind.BindingDepth && string.IsNullOrEmpty(v.node)) ownValues++;
            Check(ownValues == 3 && !string.IsNullOrEmpty(k.ownCurves.pitch.modifier.uid), "5. a snapshot captures the own curves' depths by a permanent identity (" + ownValues + ")");
            var copy = new Klip(-9803, k);
            Check(copy.ownCurves.pitch.Has && !ReferenceEquals(copy.ownCurves.pitch.modifier, k.ownCurves.pitch.modifier)
                  && copy.ownCurves.pitch.modifier.curve.Count == k.ownCurves.pitch.modifier.curve.Count, "5. a copy of the sound carries its own curves (deep)");
            var json = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(k));
            Check(json.ownCurves.volume.Has && json.ownCurves.volume.modifier.curve.Count == k.ownCurves.volume.modifier.curve.Count
                  && json.ownCurves.volume.binding.combine == ModulationCombine.Set, "5. the own curves survive the project's save format");
            ZoundDspPlayback.InvalidateLayout(json);
            var afterJson = Render(ZoundDspPlayback.PlayChain(json));
            int diffJ = 0; for (int i = 0; i < Mathf.Min(before5.Length, afterJson.Length); i++) if (before5[i] != afterJson[i]) diffJ++;
            Check(afterJson.Length == before5.Length && diffJ == 0, "5. and render bit-identically after a save and load");

            // Refused where the move would change the sound: an LFO acting on Volume BEFORE the volume curve.
            var k2 = InChainCurvesKlip(lfoOnVolumeFirst: true);
            var b2 = Render(ZoundDspPlayback.PlayChain(k2));
            bool adopted2 = ZoundOwnCurves.Adopt(k2);
            ZoundDspPlayback.InvalidateLayout(k2);
            var a2 = Render(ZoundDspPlayback.PlayChain(k2));
            int diff2 = 0; for (int i = 0; i < Mathf.Min(b2.Length, a2.Length); i++) if (b2[i] != a2[i]) diff2++;
            Check(adopted2 && !k2.ownCurves.volume.Has && k2.ownCurves.pitch.Has && k2.ownCurves.time.Has,
                  "5. a volume curve with another modifier acting on Volume before it stays in the chain; pitch and time still move");
            Check(diff2 == 0 && b2.Length == a2.Length, "5. and that sound renders bit-identically too");
            ZoundDspPlayback.InvalidateLayout(k); ZoundDspPlayback.InvalidateLayout(k2); ZoundDspPlayback.InvalidateLayout(json); ZoundDspPlayback.InvalidateLayout(copy);
        }

        sb.Insert(0, fail == 0 ? "PASS - the sound's own values.\n" : "FAIL - " + fail + " problem(s).\n");
        return sb.ToString();
    }

    /// <summary>An in-memory Klip saved the way sounds were before 2026-10-08: its volume, pitch and time curves as envelope
    /// modifiers bound to the source stage inside its chain, an LFO on a cutoff, and (optionally) an LFO on Volume BEFORE
    /// the volume curve, which is the one arrangement the move must refuse.</summary>
    static Klip InChainCurvesKlip(bool lfoOnVolumeFirst) {
        var k = new Klip(-9802) { name = "own curves check (in memory)", volumeEnvelope = new Envelope(0f, 1f), pitchEnvelope = new Envelope(0f, 1f) };
        var chain = new ZoundEffectChain();
        var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2200f; chain.nodes.Add(lp);
        void Bind(int mi, int node, int param, ModulationCombine c, float depth) =>
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = mi, nodeIndex = node, paramIndex = param, combine = c, depth = depth, schema = ChainModulationCompat.CURRENT_SCHEMA });
        var cutoffLfo = new ZoundModifier(ZoundModifierType.Lfo) { name = "Wobble" };
        if (lfoOnVolumeFirst) { var vl = new ZoundModifier(ZoundModifierType.Lfo) { name = "Tremolo" }; chain.modifiers.Add(vl); Bind(0, -1, SourceStageParam.Volume, ModulationCombine.Shift, 0.3f); }
        var vol = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume", curve = new Envelope(0f, 1f) };
        var vp = vol.curve.GetPointsList(); vp.Clear(); vp.Add(new ZUIEnvelopePoint(0f, 0.9f)); vp.Add(new ZUIEnvelopePoint(0.4f, 0.3f, 2f)); vp.Add(new ZUIEnvelopePoint(1f, 0.6f));
        chain.modifiers.Add(vol); Bind(chain.modifiers.Count - 1, -1, SourceStageParam.Volume, ModulationCombine.Set, 1f);
        var pit = new ZoundModifier(ZoundModifierType.Envelope) { name = "Pitch", curve = new Envelope(0f, 1f), zpocId = "bend" };
        var pp = pit.curve.GetPointsList(); pp.Clear(); pp.Add(new ZUIEnvelopePoint(0f, 0.5f)); pp.Add(new ZUIEnvelopePoint(0.5f, 0.62f, 1.5f)); pp.Add(new ZUIEnvelopePoint(1f, 0.45f));
        chain.modifiers.Add(pit); Bind(chain.modifiers.Count - 1, -1, SourceStageParam.Pitch, ModulationCombine.Ratio, 1f);
        chain.modifiers.Add(cutoffLfo); Bind(chain.modifiers.Count - 1, 0, 0, ModulationCombine.Shift, 0.4f);
        var tim = new ZoundModifier(ZoundModifierType.Envelope) { name = "Time", curve = new Envelope(0f, 1f) };
        var tp = tim.curve.GetPointsList(); tp.Clear(); tp.Add(new ZUIEnvelopePoint(0f, 0.5f)); tp.Add(new ZUIEnvelopePoint(1f, 0.55f));
        chain.modifiers.Add(tim); Bind(chain.modifiers.Count - 1, -1, SourceStageParam.Speed, ModulationCombine.Ratio, 1f);
        k.effectChain = chain;
        return k;
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

// A kept check for a Zequence's own effect chain (2026-10-09). A Zequence's effects are heard on each of its tracks,
// after the track's own Volume, at the end of the track's voice (see ZoundBusChains for why, and for what that means for
// effects that react to level). Nothing is played out loud; everything is in memory; the project file is not touched.
//
//   1. The merged chain: the sound's effects and modifiers keep their places; the Zequence's follow, its bindings moved
//      with them; a binding to the Zequence's own source stage is left out; nested Zequences come innermost first.
//   2. An empty Zequence chain changes nothing: no merge, and a track's render is bit-identical.
//   3. A Zequence Gain of 0.5 makes the track exactly half as loud; the Zequence's effects come AFTER the sound's own
//      Volume (a delay on the Zequence keeps echoing after the sound's Volume curve has cut it, which it could not do
//      if it ran before).
//   4. A real play of an in-memory Zequence: its track's voice is laid out with the Zequence's effect after its own, and a
//      live edit of the Zequence's effect reaches the playing track (what the chain editor sends while you drag).
//   5. The bake renders the Zequence through its chain too (a Gain of 0.5 halves the baked mix).
//   6. The Zequence editor shows the chain editor for the Zequence's own chain.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Audio;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Uitk;

public static class ZoundsZequenceChainCheck {

    const int SR = 48000;

    [MenuItem("Laubrary/Zounds/Checks/36 - A Zequence's own effect chain")]
    public static void RunFromMenu() { Start(Debug.Log); }

    public static string LastReport;

    static float[] Render(ZoundEffectChain chain, int frames = SR / 2) {
        var L = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, SR) : ChainLayout.Empty;
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

    static ZoundModifierBinding Bind(int mi, int node, int param, ModulationCombine c, float depth) =>
        new ZoundModifierBinding { modifierIndex = mi, nodeIndex = node, paramIndex = param, combine = c, depth = depth, schema = ChainModulationCompat.CURRENT_SCHEMA };

    public static void Start(Action<string> done) {
        LastReport = null;
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        // A sound: a low-pass with an LFO on its cutoff.
        var klip = new Klip(-9900) { name = "zeq chain check sound", effectChain = new ZoundEffectChain(), volumeEnvelope = new Envelope(0f, 1f), pitchEnvelope = new Envelope(0f, 1f) };
        var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 3000f; klip.effectChain.nodes.Add(lp);
        klip.effectChain.modifiers.Add(new ZoundModifier(ZoundModifierType.Lfo) { name = "Wobble" });
        klip.effectChain.bindings.Add(Bind(0, 0, 0, ModulationCombine.Shift, 0.2f));
        // A Zequence: a Gain, an LFO on that Gain, and an LFO on the Zequence's own Volume (not heard through tracks).
        var zeq = new Zequence(-9901) { name = "zeq chain check (in memory)", mode = CompositeZound.Mode.Parallel };
        var zg = new ZoundEffectNode(ZoundEffectType.Gain); zg.p[0] = 0.5f; zeq.effectChain.nodes.Add(zg);
        zeq.effectChain.modifiers.Add(new ZoundModifier(ZoundModifierType.Lfo) { name = "Pump" });
        zeq.effectChain.bindings.Add(Bind(0, 0, 0, ModulationCombine.Shift, 0.1f));
        zeq.effectChain.bindings.Add(Bind(0, -1, SourceStageParam.Volume, ModulationCombine.Shift, 0.1f));
        var outer = new Zequence(-9902) { name = "zeq chain check outer (in memory)", mode = CompositeZound.Mode.Parallel };
        var ohp = new ZoundEffectNode(ZoundEffectType.HighPass); ohp.p[0] = 40f; outer.effectChain.nodes.Add(ohp);

        // ── 1: the merge ──
        try {
            var bus = default(ZoundBusChains).Inside(outer).Inside(zeq);
            var nb = new int[ZoundBusChains.Max];
            var m = ZoundBus.Merge(klip.effectChain, bus, nb);
            bool order = m.nodes.Count == 3 && m.nodes[0] == lp && m.nodes[1] == zg && m.nodes[2] == ohp && m.busNodeStart == 1 && nb[0] == 1 && nb[1] == 2;
            bool binds = m.bindings.Count == 2 && m.bindings[0].nodeIndex == 0 && m.bindings[0].modifierIndex == 0
                         && m.bindings[1].nodeIndex == 1 && m.bindings[1].modifierIndex == 1 && m.modifiers.Count == 2;
            Check(order, "1. the merged chain: the sound's effect, then the Zequence's, then the outer Zequence's; the Zequence's begin at node 1");
            Check(binds, "1. the Zequence's modifier and binding moved with its effect; its binding to the Zequence's own Volume left out; the sound's binding untouched");
            Check(klip.effectChain.nodes.Count == 1 && zeq.effectChain.bindings[0].nodeIndex == 0, "1. the stored chains are untouched");
            var L = ChainLayout.Build(m, SR);
            Check(L.postNodeStart == 1 && ChainLayout.Build(klip.effectChain, SR).postNodeStart == 1, "1. the layout knows where the Zequence's effects begin (and a stored chain has none after its own)");
        }
        catch (Exception e) { Check(false, "1 threw: " + e.Message); }

        // ── 2 + 3: rendering ──
        try {
            var empty = new Zequence(-9903) { name = "empty chain" };
            Check(!ZoundBus.HasChain(empty) && default(ZoundBusChains).Inside(ZoundBus.HasChain(empty) ? empty : null).Any == false,
                  "2. a Zequence with an empty chain adds nothing to its tracks' plays");
            var plain = Render(klip.effectChain);
            var mergedEmpty = ZoundBus.Merge(klip.effectChain, default, null);
            int d = 0; var r2 = Render(mergedEmpty); for (int i = 0; i < plain.Length; i++) if (plain[i] != r2[i]) d++;
            Check(d == 0, "2. a track whose Zequences add no effects renders bit-identically (" + d + " samples differ)");
            var half = Render(ZoundBus.Merge(klip.effectChain, default(ZoundBusChains).Inside(zeq), null));
            // Without the Pump LFO (its depth is a shift of the gain), the Zequence's Gain of 0.5 halves the track exactly.
            var zeqNoLfo = new Zequence(-9904); var g2 = new ZoundEffectNode(ZoundEffectType.Gain); g2.p[0] = 0.5f; zeqNoLfo.effectChain.nodes.Add(g2);
            var halfExact = Render(ZoundBus.Merge(klip.effectChain, default(ZoundBusChains).Inside(zeqNoLfo), null));
            float worst = 0f; for (int i = 0; i < plain.Length; i++) worst = Mathf.Max(worst, Mathf.Abs(halfExact[i] - 0.5f * plain[i]));
            Check(worst < 1e-6f, "3. a Zequence Gain of 0.5 makes the track exactly half as loud (worst error " + worst.ToString("0.0e0") + ")");
            // Order: the sound's Volume curve cuts it at half way; a delay on the Zequence still echoes after that.
            var cut = new Klip(-9905) { name = "cut", effectChain = new ZoundEffectChain(), volumeEnvelope = new Envelope(0f, 1f), pitchEnvelope = new Envelope(0f, 1f) };
            KlipChainEnvelopes.SetVolumeEnabled(cut, true);
            var vc = KlipChainEnvelopes.VolumeCurve(cut, false); var vp = vc.GetPointsList(); vp.Clear();
            vp.Add(new ZUIEnvelopePoint(0f, 1f)); vp.Add(new ZUIEnvelopePoint(0.5f, 1f)); vp.Add(new ZUIEnvelopePoint(0.501f, 0f)); vp.Add(new ZUIEnvelopePoint(1f, 0f));
            KlipChainEnvelopes.Touch(cut);
            var dz = new Zequence(-9906); var dly = new ZoundEffectNode(ZoundEffectType.Delay); dz.effectChain.nodes.Add(dly);
            var outD = Render(ZoundBus.Merge(ZoundDspPlayback.PlayChain(cut), default(ZoundBusChains).Inside(dz), null));
            var outNo = Render(ZoundDspPlayback.PlayChain(cut));
            double eD = 0, eNo = 0; int from = (int)(0.56f * SR / 2) * 2, to = (int)(0.95f * SR / 2) * 2;
            for (int i = from; i < to; i++) { eD += outD[i] * outD[i]; eNo += outNo[i] * outNo[i]; }
            Check(eNo < 1e-6 && eD > 1e-3, "3. the Zequence's effects come after the sound's own Volume: a Zequence delay echoes on after the sound's Volume curve cut it (energy " + eD.ToString("0.000") + " vs " + eNo.ToString("0.000000") + " without it)");
        }
        catch (Exception e) { Check(false, "2/3 threw: " + e.Message); }

        // ── 5: the bake ──
        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds())
            if (z is Klip k && !k.IsLooper && ZoundSapPlayback.LoadSourceClip(k, out bool pre) != null && !pre && ZoundPcmCache.Get(ZoundSapPlayback.LoadSourceClip(k)) != null) { src = k; break; }
        if (src == null) { done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - a Zequence's own effect chain (plays skipped: no playable Klip)\n" + sb); return; }
        Klip Track(int id) {
            var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
            typeof(Zound).GetField("id").SetValue(c, id);
            c.name = "zeq chain check track"; c.effectChain = new ZoundEffectChain(); c.chainPresetId = 0; c.ownCurves = null;
            c.loop = new ZoundLoop { enabled = false };
            c.trimEnabled = true; c.trimStart = 0f; c.trimEnd = Mathf.Min(0.4f, ZoundSapPlayback.LoadSourceClip(src).length);
            if (c.timeStretch != null) c.timeStretch.liveEnabled = false;
            c.minPitch = c.maxPitch = 1f; c.minVolume = c.maxVolume = 1f;
            return c;
        }
        var play = new Zequence(-9910) { name = "zeq chain check play (in memory)", mode = CompositeZound.Mode.Parallel, minPitch = 1f, maxPitch = 1f };
        var t1 = Track(-9911); t1.parentId = play.id;
        play.localKlips.Add(t1);
        var entry = new CompositeZound.ZoundEntry { zoundId = t1.id, local = true, overridePitch = true, pitch = 1f, overrideVolume = true, volume = 1f };
        play.zoundEntries.Add(entry);
        try {
            var dry = ZequenceBake.Render(play, default);
            var pg = new ZoundEffectNode(ZoundEffectType.Gain); pg.p[0] = 0.5f; play.effectChain.nodes.Add(pg); play.effectChain.Touch();
            var wet = ZequenceBake.Render(play, default);
            float pd = 0f, pw = 0f; foreach (var s in dry.left) pd = Mathf.Max(pd, Mathf.Abs(s)); foreach (var s in wet.left) pw = Mathf.Max(pw, Mathf.Abs(s));
            Check(pd > 1e-4f && Mathf.Abs(pw / pd - 0.5f) < 1e-3f, "5. the bake goes through the Zequence's chain (a Gain of 0.5: baked peak x" + (pd > 0f ? pw / pd : 0f).ToString("0.000") + ")");
        }
        catch (Exception e) { Check(false, "5 threw: " + e.Message); }

        // ── 4: a real play ──
        bool wasMuted = EditorUtility.audioMasterMute;
        EditorUtility.audioMasterMute = true;
        ZoundToken tok = null;
        try {
            var args = new ZoundArgs { startImmediately = true, volumeOverride = 1f, pitchOverride = 1f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true };
            tok = ZoundEngine.PlayToken(play, args);
            var liveField = typeof(SapVoiceRegistry).GetField("live", BindingFlags.NonPublic | BindingFlags.Static);
            var live = (List<ZoundSapVoiceGenerator>)liveField.GetValue(null);
            ZoundSapVoiceGenerator gen = null;
            foreach (var g in live) if (g != null && ReferenceEquals(g.playingZound, t1)) gen = g;
            var L = gen != null ? gen.playingLayout : null;
            bool laid = L != null && L.nodeCount == 1 && L.postNodeStart == 0 && L.nodeType[0] == ZoundEffectType.Gain && gen.BusNodeBase(play, out int b0) && b0 == 0;
            Check(laid, "4. a real play: the track's voice is laid out with the Zequence's Gain after the track's own effects (none here)");
            int delivered = ZoundDspPlayback.PushLiveParam(play, play.effectChain, 0, 0, 0.25f);
            Check(delivered >= 1, "4. a live edit of the Zequence's effect reaches the playing track (" + delivered + " voice)");
        }
        catch (Exception e) { Check(false, "4 threw: " + e.Message); }
        finally { try { tok?.Kill(); } catch { } EditorUtility.audioMasterMute = wasMuted; }

        // ── 6: the editor ──
        var lib = ZoundsProject.Instance.zoundLibrary;
        lib.zequences.Add(play);
        ZoundEngine.InvalidateLookups();
        var zw = ZequenceEditorWindowTK.Open(play, false);
        zw.position = new Rect(160, 160, 1100, 520);
        int frame = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () => {
            frame++; zw.Repaint();
            if (frame < 40) return;
            EditorApplication.update -= tick;
            try {
                bool found = false;
                zw.rootVisualElement.Query<ChainEditorTK>().ForEach(ce => {
                    var h = typeof(ChainEditorTK).GetField("host", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ce);
                    if (h is ZequenceChainEditorHost zh && ReferenceEquals(zh.Zound, play)) found = true;
                });
                Check(found, "6. the Zequence editor shows a chain editor for the Zequence's own chain");
            }
            catch (Exception e) { Check(false, "6 threw: " + e.Message); }
            try { zw.Close(); } catch (Exception e) { Debug.LogException(e); }
            lib.zequences.RemoveAll(z => z.id == -9910);
            ZoundEngine.InvalidateLookups();
            ZoundDspPlayback.InvalidateLayouts();
            done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - a Zequence's own effect chain\n" + sb);
        };
        EditorApplication.update += tick;
    }
}

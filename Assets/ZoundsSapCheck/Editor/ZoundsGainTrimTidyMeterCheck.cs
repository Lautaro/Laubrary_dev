// A kept check for the owner's list of 2026-10-09 (gain, random trim edges, Tidy, the level meter, the curve bar's eyes,
// the curves-behind setting). Everything runs on in-memory sounds; the windows it opens are closed, the Undo steps
// reverted, the Settings and EditorPrefs values it touches put back, and the project file written back byte for byte.
//
//   1. Gain (a fixed per-sound factor, 50 % to 800 %, stored as the Klip's boost): held to 0.5..8 with 0/unset/NaN read
//      as 1; at 100 % (set, or unset) a sound renders bit-identically to one rendered without a sound at all; 50 %, 200 %
//      and 800 % scale a sound with no effects exactly; it acts at the start of the chain (before a distortion it drives
//      it: the same as a x2 Gain effect first in the chain, not last); the slider's mapping puts 100 % a quarter of the
//      way along and types 1000 % as 800 % and 10 % as 50 %; the waveform is drawn with the factor; a sound saved with a
//      Gain-curve slot (an earlier build of this branch) loads without error and the slot is dropped.
//   2. Random trim edges: with no range set the region is exactly the trim; with a range, every draw stays within it,
//      plays differ, the same play's seed draws the same edges, and a real voice start (no sound played) lasts the drawn
//      length. A cut into the range narrows it, an insert at the edge leaves it, and a destructive edit ripples it.
//   3. The level meter's arithmetic: decibels, the two-second hold then the fall, the loudest peak kept until a click or
//      until nothing has been triggered for the idle time.
//   4. Windows, real pointer events: the curve bar has no Gain chip; the Gain slider in the Klip editor's top row is
//      dragged to 200 %, 800 % and 50 % and the sound follows, one Undo step per drag; the waveform draws red marks at
//      800 %; on a local track the slider sits beside Chance and edits that sound; on a shared sound the first drag goes
//      to a copy (the original keeps 100 %); a right-click on a trim
//      edge without dragging asks for that edge's random range (both hosts); right-clicks on an eye cycle solo / none /
//      as before; the curves-behind setting reaches a backdrop curve; a click on Tidy fits the timeline exactly to the
//      end of the last piece (not to the longer authored length); a right-click on Tidy switches auto-tidy on, which
//      stops the view from zooming or moving, and off again.
using System;
using System.Collections.Generic;
using System.IO;
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
using Laubrary.Zounds.Destructive;
using Laubrary.Zui;
using Curve = Laubrary.Zounds.AudioSpectrumView.Curve;

public static class ZoundsGainTrimTidyMeterCheck {

    const int SR = 48000;

    [MenuItem("Laubrary/Zounds/Checks/35 - Gain, trim randomness, Tidy and the level meter")]
    public static void RunFromMenu() { Start(Debug.Log); }

    public static string LastReport;

    static void Send(VisualElement target, EventType type, Vector2 local, int button, int clicks = 1) {
        var ev = new Event { type = type, mousePosition = target.LocalToWorld(local), button = button, clickCount = clicks };
        EventBase e = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(ev)
                    : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev) : PointerMoveEvent.GetPooled(ev);
        using (e) target.panel.visualTree.SendEvent(e);
    }

    static void Click(VisualElement target, int button) {
        var c = target.contentRect.center;
        Send(target, EventType.MouseDown, c, button);
        Send(target, EventType.MouseUp, c, button);
    }

    static float[] Render(ZoundEffectChain chain, Zound zound = null) {
        var L = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, SR) : ChainLayout.Empty;
        int frames = SR / 4;
        var data = new float[frames * 2];
        for (int i = 0; i < frames; i++) { float s = 0.3f * Mathf.Sin(i * 0.031f) + 0.1f * Mathf.Sin(i * 0.2f); data[i * 2] = s; data[i * 2 + 1] = s * 0.9f; }
        var pcm = new PcmClip { channels = 2, frequency = SR, frames = frames, samples = data, valid = true, peak = 0.4f };
        var v = SapRealtimeVoice.Create(pcm, L, SR, 0d, frames, 1f, 1f, (float)frames / SR, false, 3, true, Allocator.Persistent, zound);
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

    /// <summary>
    /// The save that follows the shared sound's copy-on-edit makes a shipped file for the copy (named after it) and lists
    /// it in the Addressables group: both are removed again, so the run leaves nothing behind.
    /// </summary>
    static void RemoveMadeFiles() {
        foreach (var guid in AssetDatabase.FindAssets("check t:AudioClip", new[] { "Assets" })) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(path).StartsWith("check 35", StringComparison.Ordinal)) continue;
            ZoundsAudioEdits.ForgetAddressable(guid);
            AssetDatabase.DeleteAsset(path);
        }
        // The group that listed it is written now (only it), not left dirty for the next save.
        foreach (var g in AssetDatabase.FindAssets("t:AddressableAssetGroup"))
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(g)));
    }

    static int Diff(float[] a, float[] b) { if (a.Length != b.Length) return -1; int d = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d++; return d; }

    /// <summary>A Klip with a chain of one effect-free modifier-free nothing: only its own curves act.</summary>
    static Klip Plain(int id) => new Klip(id) { name = "check 35 (in memory)", volumeEnvelope = new Envelope(0f, 1f), pitchEnvelope = new Envelope(0f, 1f), effectChain = new ZoundEffectChain() };

    static void SetFlat(Envelope e, float v) { foreach (var p in e.GetPointsList()) p.value = v; }

    public static void Start(Action<string> done) {
        LastReport = null;
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }
        // The project file and the Undo history as they are now: everything below is put back to them at the end.
        string projectJson = ZoundsProjectInitialization.GetZoundsProjectPath();
        byte[] projectBytes = !string.IsNullOrEmpty(projectJson) && File.Exists(ZoundsProtection.Absolute(projectJson)) ? File.ReadAllBytes(ZoundsProtection.Absolute(projectJson)) : null;
        Undo.IncrementCurrentGroup();
        int startGroup = Undo.GetCurrentGroup();
        void PutBack() {
            try { Undo.RevertAllDownToGroup(startGroup); } catch (Exception e) { Debug.LogException(e); }
            if (projectBytes != null) {
                ZoundsWindow.isSavingJSON = true;
                try { File.WriteAllBytes(ZoundsProtection.Absolute(projectJson), projectBytes); AssetDatabase.ImportAsset(projectJson); }
                finally { ZoundsWindow.isSavingJSON = false; }
            }
        }

        // ─────────── 1: Gain ───────────
        try {
            var k = new Klip(-9849);
            k.boost = 0.4f; float a1 = k.BoostApplied; k.boost = 14f; float a2 = k.BoostApplied; k.boost = 0f; float a3 = k.BoostApplied; k.boost = float.NaN; float a4 = k.BoostApplied;
            Check(a1 == 0.5f && a2 == 8f && a3 == 1f && a4 == 1f, "1. the gain is held to 50 % .. 800 %; 0 (saved before it existed) and NaN read 100 % (" + a1 + ", " + a2 + ", " + a3 + ", " + a4 + ")");

            var none = Render(new ZoundEffectChain());
            var unity = Plain(-9850); unity.boost = 1f;
            var unset = Plain(-9851); unset.boost = 0f;
            int d1 = Diff(none, Render(ZoundDspPlayback.PlayChain(unity), unity)), d2 = Diff(none, Render(ZoundDspPlayback.PlayChain(unset), unset));
            Check(d1 == 0 && d2 == 0, "1. at 100 % (set or unset) a sound renders bit-identically to one with no gain at all (" + d1 + ", " + d2 + " samples differ)");
            foreach (float g in new[] { 0.5f, 2f, 8f }) {
                var kg = Plain(-9852); kg.boost = g;
                var outp = Render(ZoundDspPlayback.PlayChain(kg), kg);
                float worst = 0f;
                for (int i = 0; i < none.Length; i++) worst = Mathf.Max(worst, Mathf.Abs(outp[i] - g * none[i]));
                Check(worst < 1e-6f, "1. at " + GainSliderTK.Percent(g) + " a sound with no effects comes out exactly x" + g + " (worst error " + worst.ToString("0.0e0") + ")");
            }
            // Before the effects: x2 into a distortion is a x2 Gain effect FIRST in the chain, not one after it.
            ZoundEffectChain DistChain(int gainAt) {
                var c = new ZoundEffectChain();
                var dist = new ZoundEffectNode(ZoundEffectType.Distortion);
                var two = new ZoundEffectNode(ZoundEffectType.Gain); two.p[0] = 2f;
                if (gainAt == 0) c.nodes.Add(two);
                c.nodes.Add(dist);
                if (gainAt == 1) c.nodes.Add(two);
                return c;
            }
            var kd = Plain(-9853); kd.boost = 2f; kd.effectChain = DistChain(-1);
            var viaGain = Render(DistChain(-1), kd);
            var first = Render(DistChain(0)); var last = Render(DistChain(1));
            float eFirst = 0f, eLast = 0f;
            for (int i = 0; i < viaGain.Length; i++) { eFirst = Mathf.Max(eFirst, Mathf.Abs(viaGain[i] - first[i])); eLast = Mathf.Max(eLast, Mathf.Abs(viaGain[i] - last[i])); }
            Check(eFirst < 1e-5f && eLast > 1e-3f, "1. the gain acts before the effects: x2 into a distortion matches a x2 Gain effect first in the chain (" + eFirst.ToString("0.0e0") + ") and not one last (" + eLast.ToString("0.000") + ")");

            // The slider: logarithmic, 100 % a quarter of the way along, whole percents, typed values clamped.
            float t100 = Mathf.InverseLerp(GainSliderTK.ToSlider(0.5f), GainSliderTK.ToSlider(8f), GainSliderTK.ToSlider(1f));
            Check(Mathf.Abs(t100 - 0.25f) < 1e-5f && GainSliderTK.ToGain(GainSliderTK.ToSlider(2f)) == 2f && GainSliderTK.ToGain(-5f) == 0.5f && GainSliderTK.ToGain(9f) == 8f && GainSliderTK.Percent(1.234f) == "123%",
                  "1. the slider: 100 % sits at " + (t100 * 100f).ToString("0") + " % of its track, its ends are 50 % and 800 %, values read as whole percents");
            var probe = Plain(-9854); probe.boost = 1f;
            var slider = GainSliderTK.Create(() => probe, null, null, ZuiSkinSlider.LabelMode.LabelAndValue, 110f, 18f);
            // In memory: the typed edits are recorded (Undo) but the project file is put back at the end of the check.
            slider.TypeForTest("1000%"); float t1 = probe.boost;
            slider.TypeForTest("10"); float t2 = probe.boost;
            slider.TypeForTest("250"); float t3 = probe.boost;
            slider.TypeForTest("abc"); float t4 = probe.boost;
            Check(t1 == 8f && t2 == 0.5f && t3 == 2.5f && t4 == 2.5f, "1. typed: 1000 % -> " + GainSliderTK.Percent(t1) + ", 10 -> " + GainSliderTK.Percent(t2) + ", 250 -> " + GainSliderTK.Percent(t3) + ", text ignored -> " + GainSliderTK.Percent(t4));
            Check(WaveSurfaceTK.GainOf(kd) == 2f && WaveSurfaceTK.GainOf(unset) == 1f && WaveSurfaceTK.GainOf(new Zequence(-9855)) == 1f, "1. the waveform is drawn with the gain (x2 at 200 %, x1 unset, x1 for a Zequence)");

            // A sound saved by the earlier build of this branch, which had a Gain-curve slot: loads, slot dropped, plays as before.
            var withCurves = Plain(-9856); KlipChainEnvelopes.SetVolumeEnabled(withCurves, true);
            string json = JsonUtility.ToJson(withCurves);
            const string Key = "\"ownCurves\":{";
            int at = json.IndexOf(Key, StringComparison.Ordinal);
            string legacy = at < 0 ? json : json.Insert(at + Key.Length, "\"gain\":{\"has\":true,\"modifier\":{\"type\":0,\"enabled\":true},\"binding\":{\"nodeIndex\":-1,\"paramIndex\":1}},");
            Klip loaded = null; string err = null;
            try { loaded = JsonUtility.FromJson<Klip>(legacy); } catch (Exception e) { err = e.Message; }
            Check(at >= 0 && err == null && loaded != null && loaded.ownCurves != null && loaded.ownCurves.volume.Has && !JsonUtility.ToJson(loaded).Contains("\"gain\":{"),
                  "1. a sound saved with a Gain-curve slot loads without error, keeps its other curves and drops the slot" + (err != null ? " (threw " + err + ")" : ""));
        }
        catch (Exception e) { Check(false, "1 threw: " + e.Message); }

        // ─────────── 2: random trim edges ───────────
        try {
            var k = Plain(-9860); k.trimEnabled = true; k.trimStart = 1f; k.trimEnd = 2f;
            double s0 = SR * 1d, e0 = SR * 2d, s = s0, e = e0;
            ZoundSapPlayback.DrawnRegion(k, 12345u, SR, SR * 3d, ref s, ref e);
            Check(s == s0 && e == e0 && !k.HasTrimRandom, "2. with no range set, a play reads exactly the trim");
            k.trimStartRandom = 0.1f; k.trimEndRandom = 0.05f; k.trimEndRandomBias = 0.9f;
            bool within = true; var seen = new HashSet<long>(); float maxS = 0f, maxE = 0f;
            for (uint seed = 1; seed <= 300; seed++) {
                double a = s0, b = e0;
                ZoundSapPlayback.DrawnRegion(k, EnvelopeRandom.SeedFor(seed), SR, SR * 3d, ref a, ref b);
                float ds = (float)((a - s0) / SR), de = (float)((b - e0) / SR);
                if (Mathf.Abs(ds) > 0.1f + 1e-5f || Mathf.Abs(de) > 0.05f + 1e-5f) within = false;
                maxS = Mathf.Max(maxS, Mathf.Abs(ds)); maxE = Mathf.Max(maxE, Mathf.Abs(de));
                seen.Add((long)a * 7919 + (long)b);
            }
            Check(within && seen.Count > 250 && maxS > 0.07f && maxE > 0.035f, "2. every draw stays inside its range (start ±" + (maxS * 1000f).ToString("0") + " of 100 ms, end ±" + (maxE * 1000f).ToString("0") + " of 50 ms) and plays differ (" + seen.Count + " different of 300)");
            double a1 = s0, b1 = e0, a2 = s0, b2 = e0;
            ZoundSapPlayback.DrawnRegion(k, 777u, SR, SR * 3d, ref a1, ref b1);
            ZoundSapPlayback.DrawnRegion(k, 777u, SR, SR * 3d, ref a2, ref b2);
            Check(a1 == a2 && b1 == b2, "2. the same play's seed draws the same edges (one play keeps one draw)");
            // A cut into the start's range narrows it; an insert at the edge leaves it; the edit ripples the stored values.
            var cut = new AudioSpan { at = 0.95, removed = 0.02, inserted = 0, oldLength = 3, newLength = 2.98 };
            float r1 = AudioRipple.Radius(1f, 0.1f, cut, false);
            var ins = new AudioSpan { at = 1.0, removed = 0, inserted = 0.5, oldLength = 3, newLength = 3.5 };
            float r2 = AudioRipple.Radius(1f, 0.1f, ins, false);
            Check(Mathf.Abs(r1 - 0.08f) < 1e-4f && Mathf.Abs(r2 - 0.1f) < 1e-6f, "2. a cut into the range narrows it (0.100 -> " + r1.ToString("0.000") + " s); an insert at the edge leaves it (" + r2.ToString("0.000") + " s)");
            var kr = Plain(-9861); kr.trimEnabled = true; kr.trimStart = 1f; kr.trimEnd = 2f; kr.trimStartRandom = 0.1f;
            AudioRipple.Klip(kr, cut, SR);
            Check(Mathf.Abs(kr.trimStart - 0.98f) < 1e-4f && Mathf.Abs(kr.trimStartRandom - 0.08f) < 1e-4f, "2. a destructive edit ripples the edge and its range together (start " + kr.trimStart.ToString("0.000") + " s ±" + kr.trimStartRandom.ToString("0.000") + ")");
        }
        catch (Exception e) { Check(false, "2 threw: " + e.Message); }

        // A real voice start of a playable sound (nothing is played): its length follows the drawn trim.
        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds())
            if (z is Klip kk && ZoundSapPlayback.LoadSourceClip(kk, out bool pre) != null && !pre && ZoundPcmCache.Get(ZoundSapPlayback.LoadSourceClip(kk)) != null && ZoundSapPlayback.LoadSourceClip(kk).length > 0.6f) { src = kk; break; }
        if (src != null) {
            GameObject go = null;
            try {
                var clip = ZoundSapPlayback.LoadSourceClip(src);
                var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
                typeof(Zound).GetField("id").SetValue(c, -9862);
                c.effectChain = new ZoundEffectChain(); c.chainPresetId = 0; c.ownCurves = null;
                if (c.timeStretch != null) { c.timeStretch.liveEnabled = false; c.timeStretch.enabled = false; }
                if (c.loop != null) c.loop.enabled = false;
                c.trimEnabled = true; c.trimStart = clip.length * 0.3f; c.trimEnd = clip.length * 0.6f;
                c.trimStartRandom = Mathf.Min(0.05f, clip.length * 0.1f); c.trimEndRandom = c.trimStartRandom;
                go = new GameObject("check 35 voice") { hideFlags = HideFlags.HideAndDontSave };
                var asrc = go.AddComponent<AudioSource>();
                bool allMatch = true; var lengths = new HashSet<int>();
                for (long t = 1; t <= 6; t++) {
                    var gen = ZoundSapPlayback.StartVoice(c, asrc, clip, 1f, 1f, t, out string why, out float dur);
                    if (gen == null) { allMatch = false; sb.Append("    (voice did not start: " + why + ")\n"); break; }
                    var pcm = ZoundPcmCache.Get(clip);
                    double a = c.trimStart * pcm.frequency, b = c.trimEnd * pcm.frequency;
                    ZoundSapPlayback.DrawnRegion(c, EnvelopeRandom.SeedFor(t), pcm.frequency, pcm.frames, ref a, ref b);
                    float expect = (float)((b - a) / pcm.frequency);
                    if (Mathf.Abs(dur - expect) > 2e-3f) allMatch = false;
                    lengths.Add(Mathf.RoundToInt(dur * 10000f));
                    asrc.generator = null;
                }
                Check(allMatch && lengths.Count >= 4, "2. a real voice start lasts its play's drawn trim (" + lengths.Count + " different lengths in 6 starts)");
            }
            catch (Exception e) { Check(false, "2 (voice start) threw: " + e.Message); }
            finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); }
        }
        else sb.Append("  --   2. voice start skipped: no playable Klip longer than 0.6 s in this project\n");

        // ─────────── 3: the level meter's arithmetic ───────────
        {
            var st = LevelMeterMath.Cleared;
            LevelMeterMath.Step(ref st, 0.5f, 10.0, 0.03f, true, 4f, 0.0);
            Check(Mathf.Abs(st.maxDb + 6.0206f) < 1e-3f && Mathf.Abs(st.holdDb + 6.0206f) < 1e-3f && st.hasMax, "3. a peak of 0.5 reads -6.02 dB, held and kept");
            for (int i = 1; i <= 60; i++) LevelMeterMath.Step(ref st, 0f, 10.0 + i * 0.03, 0.03f, true, 4f, i * 0.03);
            float at18 = st.holdDb;
            for (int i = 61; i <= 80; i++) LevelMeterMath.Step(ref st, 0f, 10.0 + i * 0.03, 0.03f, true, 4f, i * 0.03);
            Check(Mathf.Abs(at18 + 6.0206f) < 1e-3f && st.holdDb < -6.5f && st.hasMax && Mathf.Abs(st.maxDb + 6.0206f) < 1e-3f,
                  "3. the mark holds for two seconds (" + at18.ToString("0.00") + " dB at 1.8 s) then falls (" + st.holdDb.ToString("0.00") + " dB at 2.4 s); the number stays");
            LevelMeterMath.Step(ref st, 0f, 15.0, 0.03f, true, 4f, 5.0);
            Check(!st.hasMax, "3. once nothing has been triggered for the idle time (5 s of 4), the number clears");
            var st2 = LevelMeterMath.Cleared;
            LevelMeterMath.Step(ref st2, 1.2f, 1.0, 0.03f, false, 4f, 50.0);
            LevelMeterMath.Step(ref st2, 0f, 1.03, 0.03f, false, 4f, 50.0);
            Check(st2.hasMax && st2.maxDb > 1.5f, "3. with the idle reset off the number stays (" + st2.maxDb.ToString("+0.00") + " dB, above 0: clipping)");
            Check(Mathf.Approximately(LevelMeterMath.Position(LevelMeterMath.FloorDb), 0f) && Mathf.Approximately(LevelMeterMath.Position(LevelMeterMath.TopDb), 1f), "3. the bar spans -60 to +6 dB");
        }

        // ─────────── 4: the windows ───────────
        if (src == null) { PutBack(); done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - gain, trim randomness, Tidy, level meter (windows skipped: no playable Klip)\n" + sb); return; }
        var sclip = ZoundSapPlayback.LoadSourceClip(src);
        float S = sclip.length;
        var es = ZoundsProject.Instance.projectSettings.editorStyle;
        bool keepDotted = es.backdropDotted, keepClear = es.backdropTransparent; float keepBonus = es.backdropWidthBonus;
        const string AutoTidyKey = "Laubrary.Zounds.Zequence.AutoTidy";
        bool keepAutoTidy = EditorPrefs.GetBool(AutoTidyKey, false);
        EditorPrefs.SetBool(AutoTidyKey, false);
        var keepPrompt = ZoundsProject.Instance.projectSettings.protectedEditPrompt;
        ZoundsProject.Instance.projectSettings.protectedEditPrompt = ZoundsProject.ProjectSettings.ProtectedEditPrompt.Notice;
        var lib = ZoundsProject.Instance.zoundLibrary;

        var zeq = new Zequence(-9870) { name = "check 35 zequence (in memory)", mode = CompositeZound.Mode.Parallel, minPitch = 1f, maxPitch = 1f };
        var piece = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(piece, -9871);
        piece.name = "check 35 piece"; piece.effectChain = new ZoundEffectChain(); piece.chainPresetId = 0; piece.ownCurves = null;
        // A pitch range: the authored length is worked out at its slowest end, the drawing at its middle.
        piece.minPitch = 0.8f; piece.maxPitch = 1.2f; piece.minVolume = piece.maxVolume = 1f; piece.clampToTrim = false;
        piece.trimEnabled = true; piece.trimStart = Mathf.Min(0.1f, S * 0.1f); piece.trimEnd = Mathf.Min(S, piece.trimStart + 0.8f);
        if (piece.timeStretch != null) piece.timeStretch.liveEnabled = false;
        piece.parentId = zeq.id;
        KlipChainEnvelopes.SetVolumeEnabled(piece, true);
        KlipChainEnvelopes.SetPitchEnabled(piece, true);
        zeq.localKlips.Add(piece);
        zeq.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = piece.id, local = true, overridePitch = false, pitch = 1f, overrideVolume = true, volume = 1f });
        lib.zequences.Add(zeq);
        // A shared sound (in the library, played by a second in-memory Zequence): its Gain edit goes to a copy.
        var shared = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(shared, -9880);
        shared.name = "check 35 shared"; shared.effectChain = new ZoundEffectChain(); shared.chainPresetId = 0; shared.ownCurves = null; shared.parentId = 0; shared.boost = 1f;
        lib.klips.Add(shared);
        var user = new Zequence(-9872) { name = "check 35 user (in memory)", mode = CompositeZound.Mode.Parallel };
        user.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = shared.id, local = false });
        lib.zequences.Add(user);
        ZoundEngine.InvalidateLookups();
        CompositeZoundEditing.AutoApplyDuration(zeq);

        var zw = ZequenceEditorWindowTK.Open(zeq, false);
        zw.position = new Rect(140, 140, 1200, 420);
        var kw = KlipEditorWindowTK.Open(piece, true);
        kw.position = new Rect(180, 180, 1000, 760);
        var randomAsked = new List<(WaveSurfaceTK s, bool end)>();
        WaveSurfaceTK.trimRandomProbe = (s, end) => randomAsked.Add((s, end));
        Klip C() => KlipEditorWindowTK.FindKlip(-9871);
        TrackStripTK Strip() { foreach (var st in zw.strips) if (st.Surface != null && st.Surface.Host.Sound != null && st.Surface.Host.Sound.id == -9871) return st; return null; }
        CurveBarTK KBar() => kw.Waveform?.Q<CurveBarTK>();
        List<CurveBarTK.Curve> Curves(CurveBarTK b) => (List<CurveBarTK.Curve>)typeof(CurveBarTK).GetField("curves", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(b);

        ZuiSkinSlider GainIn(VisualElement root) => root.Q<ZuiSkinSlider>(className: "zs-gain");
        // A real drag on a slider: press in its middle, move to a share of its width, release.
        void Drag(VisualElement sl, float frac) {
            // Say which it is if a drag cannot happen: the slider missing, or not laid out yet (a test-timing problem, not
            // the slider's).
            if (sl == null) throw new Exception("the Gain slider was not found in the window");
            if (sl.panel == null || sl.layout.width < 10f) throw new Exception("the Gain slider is not laid out yet (" + sl.layout.width + " px wide)");
            float w = sl.layout.width, y = sl.layout.height * 0.5f;
            Send(sl, EventType.MouseDown, new Vector2(w * 0.5f, y), 0);
            Send(sl, EventType.MouseDrag, new Vector2(w * frac, y), 0);
            Send(sl, EventType.MouseUp, new Vector2(w * frac, y), 0);
        }
        int Clipped(WaveSurfaceTK sf) => ((List<float>)typeof(WaveSurfaceTK).GetField("clipped", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sf)).Count;
        int clippedAt200 = -1;
        KlipEditorWindowTK kw2 = null;

        var steps = new List<Action>();
        steps.Add(() => {
            // ── no Gain chip; the Gain slider on a local track, beside Chance ──
            var cs = Curves(KBar());
            bool noChip = cs.Count == 3; foreach (var c in cs) if (c.label == "Gain") noChip = false;
            Check(noChip, "4. the curve bar has Time, Pitch and Vol only (" + cs.Count + " chips)");
            var ts = GainIn(zw.rootVisualElement);
            var chance = ts?.parent.Query<ZuiSkinSlider>(className: "zs-slider-chance").ToList().Find(x => Mathf.Abs(x.layout.y - ts.layout.y) < 1f);
            Check(ts != null && chance != null && ts.layout.width > 40f && ts.layout.x > chance.layout.xMax - 1f && !string.IsNullOrEmpty(ts.tooltip),
                  "4. a local track shows its sound's Gain slider beside Chance, on the same row, with a tooltip (" + (ts != null ? ts.layout.width.ToString("0") : "-") + " px)");
            Drag(ts, 0.5f);
        });
        steps.Add(() => {
            Check(Mathf.Approximately(C().boost, 2f), "4. dragging the track's slider to its middle sets the sound to 200 % (" + GainSliderTK.Percent(C().boost) + ")");
            // ── the Klip editor's top row ──
            var ks = GainIn(kw.rootVisualElement);
            Check(ks != null && ks.parent is ZoundFieldsRowTK && ks.layout.width > 60f, "4. the Klip editor's top row has the Gain slider (" + (ks != null ? ks.layout.width.ToString("0") : "-") + " px)");
            Drag(ks, 0.5f);
        });
        steps.Add(() => {
            Check(Mathf.Approximately(C().boost, 2f) && WaveSurfaceTK.GainOf(C()) == 2f, "4. Klip editor: a drag to the middle reads 200 %, and the waveform is drawn x2");
            clippedAt200 = Clipped(kw.Waveform.surface);
            Drag(GainIn(kw.rootVisualElement), 1f);
        });
        steps.Add(() => {
            int c8 = Clipped(kw.Waveform.surface);
            Check(Mathf.Approximately(C().boost, 8f) && c8 > 0 && c8 >= clippedAt200, "4. a drag to the right end reads 800 %, and the waveform marks where it clips (" + c8 + " columns, " + clippedAt200 + " at 200 %)");
            Drag(GainIn(kw.rootVisualElement), 0f);
        });
        steps.Add(() => {
            Check(Mathf.Approximately(C().boost, 0.5f) && Clipped(kw.Waveform.surface) == 0, "4. a drag to the left end reads 50 %, no clip marks");
            // ── Undo: each Gain drag is one step (50 % -> 800 % -> 200 %) ──
            Undo.PerformUndo();
        });
        float afterOne = 0f;
        steps.Add(() => { afterOne = C().boost; Undo.PerformUndo(); });
        steps.Add(() => {
            float afterTwo = C().boost;
            Check(Mathf.Approximately(afterOne, 8f) && Mathf.Approximately(afterTwo, 2f), "4. Undo takes the Gain back one drag at a time (" + GainSliderTK.Percent(afterOne) + ", then " + GainSliderTK.Percent(afterTwo) + ")");
            // ── a shared sound: the first drag goes to a copy ──
            kw2 = KlipEditorWindowTK.Open(shared, false);
            kw2.position = new Rect(220, 220, 1000, 760);
        });
        steps.Add(() => {
            Drag(GainIn(kw2.rootVisualElement), 0.5f);
        });
        steps.Add(() => {
            var original = KlipEditorWindowTK.FindKlip(-9880);
            Check(original != null && !ReferenceEquals(original, shared) && Mathf.Approximately(original.BoostApplied, 1f) && shared.id != -9880 && shared.name.EndsWith("(copy)") && Mathf.Approximately(shared.boost, 2f),
                  "4. on a shared sound the Gain edit goes to a copy ('" + shared.name + "' at " + GainSliderTK.Percent(shared.boost) + "); the original keeps " + (original != null ? GainSliderTK.Percent(original.BoostApplied) : "-"));
            try { kw2.Close(); } catch (Exception e) { Debug.LogException(e); }
            kw2 = null;
            // ── a right-click on a trim edge, without dragging, asks for that edge's random range ──
            foreach (var s in new[] { kw.Waveform.surface, Strip().Surface }) {
                s.Refresh();
                var r = s.AreaRect;
                s.Host.Heard(out float a, out float b);
                float x = s.Host.XOf(b, r) - 1f;
                randomAsked.Clear();
                Send(s.area, EventType.MouseDown, new Vector2(x, r.height * 0.5f), 1);
                Send(s.area, EventType.MouseUp, new Vector2(x, r.height * 0.5f), 1);
                Check(randomAsked.Count == 1 && randomAsked[0].end && ReferenceEquals(randomAsked[0].s, s) && Mathf.Abs(C().trimEnd - b) < 1e-5f,
                      "4. " + (s == kw.Waveform.surface ? "Klip editor" : "track") + ": a right-click on the trim end without a drag asks for its random range and leaves the trim where it was");
            }
        });
        steps.Add(() => {
            // ── the eyes: solo, none, as before ──
            var bar = KBar(); var cs = Curves(bar);
            var vol = cs[2]; var pit = cs[1];
            bool[] Shown() { var a = new bool[cs.Count]; for (int i = 0; i < cs.Count; i++) a[i] = cs[i].shown(); return a; }
            var before = Shown();
            Click(pit.eye, 1);
            var solo = Shown();
            Click(pit.eye, 1);
            var none = Shown();
            Click(pit.eye, 1);
            var back = Shown();
            // Only curves that exist have a drawing to show or hide (the time curve is off here).
            bool soloOk = solo[1] && !solo[2];
            bool noneOk = !none[1] && !none[2];
            bool backOk = true; for (int i = 0; i < before.Length; i++) if (back[i] != before[i]) backOk = false;
            Check(soloOk && noneOk && backOk && bar.EyeStep == 0, "4. right-clicks on the pitch eye: only pitch shown, then none, then every eye as before");
            // ── the curves-behind setting reaches a backdrop ──
            es.backdropDotted = true; es.backdropTransparent = false; es.backdropWidthBonus = 3f;
            kw.Waveform.Select(Curve.Pitch, true);
            kw.Waveform.Refresh();
            var env = kw.Waveform.surface.EnvelopeOf(Curve.Volume);
            Check(env.backdrop && env.backdropDotted && !env.backdropTransparent && Mathf.Approximately(env.backdropWidthBonus, 3f),
                  "4. with the pitch curve being edited, the volume curve is drawn as the Settings tab says (dotted, solid, 3 px wider)");
            es.backdropDotted = keepDotted; es.backdropTransparent = keepClear; es.backdropWidthBonus = keepBonus;
            kw.Waveform.Select(Curve.Pitch, false);
        });
        float fitT0 = 0f, fitT1 = 0f, piecesEnd = 0f, authored = 0f;
        steps.Add(() => {
            // ── Tidy fits exactly ──
            var tl = zw.timeline;
            tl.ZoomAround(0.2f, 0.5f);   // the view somewhere else first
            var tidy = (ZuiToggleButton)typeof(ZequenceEditorWindowTK).GetField("tidyButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(zw);
            Click(tidy, 0);
            tl.Rebuild();
            fitT0 = tl.t0; fitT1 = tl.t1; piecesEnd = tl.piecesEnd; authored = tl.authored;
            float end = 0f; foreach (var p in tl.tracks) if (p.IsKlip) end = Mathf.Max(end, p.End);
            Check(Mathf.Abs(fitT0) < 1e-5f && Mathf.Abs(fitT1 - end) < 1e-4f && authored > end + 1e-3f,
                  "4. Tidy fits the view exactly to the end of the last piece (" + fitT0.ToString("0.000") + " - " + fitT1.ToString("0.000") + " s, piece ends " + end.ToString("0.000") + " s), not to the authored length (" + authored.ToString("0.000") + " s, at the slowest pitch)");
            // ── auto-tidy ──
            Click(tidy, 1);
        });
        steps.Add(() => {
            var tl = zw.timeline;
            bool on = EditorPrefs.GetBool(AutoTidyKey, false);
            float a = tl.t0, b = tl.t1;
            tl.ZoomAround(0.2f, 0.5f); tl.Pan(0.3f); tl.Show(0.1f, 0.2f);
            Check(on && tl.autoTidy && !tl.CanMoveView && tl.t0 == a && tl.t1 == b && Mathf.Abs(b - fitT1) < 1e-4f, "4. a right-click on Tidy switches auto-tidy on: the view stays fitted and does not zoom or move");
            var tidy = (ZuiToggleButton)typeof(ZequenceEditorWindowTK).GetField("tidyButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(zw);
            Check(tidy.value && tidy.text.StartsWith("Tidy"), "4. the Tidy button shows auto-tidy is on (lit)");
            Click(tidy, 1);
        });
        steps.Add(() => {
            var tl = zw.timeline;
            tl.ZoomAround(0.2f, 0.5f);
            Check(!EditorPrefs.GetBool(AutoTidyKey, false) && tl.CanMoveView && !tl.fitted, "4. a second right-click switches auto-tidy off, and the view zooms again");
        });

        int frame = 0, step = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () => {
            frame++;
            zw.Repaint(); kw.Repaint(); kw2?.Repaint();
            if (frame < 40 || frame % 12 != 0) return;
            bool stop = false;
            try { steps[step](); }
            catch (Exception e) { Check(false, "window step " + (step + 1) + " threw: " + e); stop = true; }
            step++;
            if (!stop && step < steps.Count) return;
            EditorApplication.update -= tick;
            WaveSurfaceTK.trimRandomProbe = null;
            es.backdropDotted = keepDotted; es.backdropTransparent = keepClear; es.backdropWidthBonus = keepBonus;
            EditorPrefs.SetBool(AutoTidyKey, keepAutoTidy);
            try { if (zw != null) zw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { if (kw != null) kw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { if (kw2 != null) kw2.Close(); } catch (Exception e) { Debug.LogException(e); }
            ZoundsProject.Instance.projectSettings.protectedEditPrompt = keepPrompt;
            ZoundsEditGuard.ForgetAllowed(-9880);
            PutBack();
            var l2 = ZoundsProject.Instance.zoundLibrary;
            l2.zequences.RemoveAll(z => z.id == -9870 || z.id == -9872);
            l2.klips.RemoveAll(k => k.id == -9880 || (k.name != null && k.name.StartsWith("check 35 shared")));
            ZoundEngine.InvalidateLookups();
            RemoveMadeFiles();
            done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - gain, trim randomness, Tidy, level meter\n" + sb);
        };
        EditorApplication.update += tick;
    }
}

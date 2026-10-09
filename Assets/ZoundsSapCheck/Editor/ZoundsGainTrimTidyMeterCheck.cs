// A kept check for the owner's list of 2026-10-09 (gain, random trim edges, Tidy, the level meter, the curve bar's eyes,
// the curves-behind setting). Everything runs on in-memory sounds; the windows it opens are closed, the Undo steps
// reverted, the Settings and EditorPrefs values it touches put back, and the project file written back byte for byte.
//
//   1. Gain: a sound saved before the Gain curve existed, the same sound with an empty Gain slot, with a Gain curve
//      switched off, and with a flat (0 dB) Gain curve all render bit-identically; a Gain curve held at x2 doubles what
//      the effects receive (a sound with no effects comes out exactly twice as loud); GainAt (the waveform's drawing)
//      says x2 too.
//   2. Random trim edges: with no range set the region is exactly the trim; with a range, every draw stays within it,
//      plays differ, the same play's seed draws the same edges, and a real voice start (no sound played) lasts the drawn
//      length. A cut into the range narrows it, an insert at the edge leaves it, and a destructive edit ripples it.
//   3. The level meter's arithmetic: decibels, the two-second hold then the fall, the loudest peak kept until a click or
//      until nothing has been triggered for the idle time.
//   4. Windows, real pointer events: the Gain chip switches a Klip's Gain curve on (Klip editor); a right-click on a trim
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

    static float[] Render(ZoundEffectChain chain) {
        var L = chain != null && !chain.IsEmpty ? ChainLayout.Build(chain, SR) : ChainLayout.Empty;
        int frames = SR / 4;
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

    static int Diff(float[] a, float[] b) { if (a.Length != b.Length) return -1; int d = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d++; return d; }

    /// <summary>A Klip with a chain of one effect-free modifier-free nothing: only its own curves act.</summary>
    static Klip Plain(int id) => new Klip(id) { name = "check 35 (in memory)", volumeEnvelope = new Envelope(0f, 1f), pitchEnvelope = new Envelope(0f, 1f), effectChain = new ZoundEffectChain() };

    static void SetFlat(Envelope e, float v) { foreach (var p in e.GetPointsList()) p.value = v; }

    public static void Start(Action<string> done) {
        LastReport = null;
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        // ─────────── 1: Gain ───────────
        try {
            var old = Plain(-9850);
            var lp = new ZoundEffectNode(ZoundEffectType.LowPass); lp.p[0] = 2500f; old.effectChain.nodes.Add(lp);
            old.ownCurves = null;   // saved before own curves existed at all
            var reference = Render(ZoundDspPlayback.PlayChain(old));
            var withSlot = Plain(-9851); withSlot.effectChain.nodes.Add(lp); withSlot.ownCurves = new ZoundOwnCurves();
            Check(Diff(reference, Render(ZoundDspPlayback.PlayChain(withSlot))) == 0, "1. a sound with an empty Gain slot renders bit-identically to one saved before it existed");
            var off = Plain(-9852); off.effectChain.nodes.Add(lp);
            KlipChainEnvelopes.SetGainEnabled(off, true); KlipChainEnvelopes.SetGainEnabled(off, false);
            Check(off.ownCurves.gain.Has && !off.ownCurves.gain.modifier.enabled && Diff(reference, Render(ZoundDspPlayback.PlayChain(off))) == 0,
                  "1. with a Gain curve switched off it renders bit-identically");
            var flat = Plain(-9853); flat.effectChain.nodes.Add(lp);
            KlipChainEnvelopes.SetGainEnabled(flat, true);
            int d0 = Diff(reference, Render(ZoundDspPlayback.PlayChain(flat)));
            Check(d0 == 0, "1. with a flat (0 dB, middle) Gain curve on it renders bit-identically (" + d0 + " samples differ)");
            // x2: on the Ratio scale the curve's 0..1 spans x1/4..x4 evenly in ratio, so x2 sits three quarters of the way up.
            var two = Plain(-9854);
            KlipChainEnvelopes.SetGainEnabled(two, true);
            var plainRef = Render(ZoundDspPlayback.PlayChain(Plain(-9855)));
            SetFlat(KlipChainEnvelopes.GainCurve(two, false), 0.75f);
            KlipChainEnvelopes.Touch(two);
            var twice = Render(ZoundDspPlayback.PlayChain(two));
            float worst = 0f;
            for (int i = SR / 50; i < plainRef.Length; i++) worst = Mathf.Max(worst, Mathf.Abs(twice[i] - 2f * plainRef[i]));
            Check(worst < 1e-4f, "1. a Gain curve held at x2 makes a sound with no effects exactly twice as loud (worst error " + worst.ToString("0.0e0") + ")");
            var axis = new CurveAnchor.Axis { trimStart = 0f, trimEnd = 1f, sourceLength = 1f };
            float g = KlipChainEnvelopes.GainAt(two, 0.5f, axis);
            Check(Mathf.Abs(g - 2f) < 1e-3f, "1. the waveform is drawn through it: x" + g.ToString("0.000") + " at the middle");
            Check(KlipChainEnvelopes.GainAt(off, 0.5f, axis) == 1f && KlipChainEnvelopes.GainAt(old, 0.5f, axis) == 1f, "1. a switched-off or missing Gain curve draws the waveform as recorded (x1)");
            Check(KlipChainEnvelopes.IsOwnCurve(two, two.ownCurves.gain.modifier), "1. the Gain curve is one of the sound's own curves (not a row of its modifier list)");
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
        if (src == null) { done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - gain, trim randomness, Tidy, level meter (windows skipped: no playable Klip)\n" + sb); return; }
        var sclip = ZoundSapPlayback.LoadSourceClip(src);
        float S = sclip.length;
        string projectJson = ZoundsProjectInitialization.GetZoundsProjectPath();
        byte[] projectBytes = !string.IsNullOrEmpty(projectJson) && File.Exists(ZoundsProtection.Absolute(projectJson)) ? File.ReadAllBytes(ZoundsProtection.Absolute(projectJson)) : null;
        var es = ZoundsProject.Instance.projectSettings.editorStyle;
        bool keepDotted = es.backdropDotted, keepClear = es.backdropTransparent; float keepBonus = es.backdropWidthBonus;
        const string AutoTidyKey = "Laubrary.Zounds.Zequence.AutoTidy";
        bool keepAutoTidy = EditorPrefs.GetBool(AutoTidyKey, false);
        EditorPrefs.SetBool(AutoTidyKey, false);
        Undo.IncrementCurrentGroup();
        int startGroup = Undo.GetCurrentGroup();
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

        var steps = new List<Action>();
        steps.Add(() => {
            // ── the Gain chip ──
            var bar = KBar(); var cs = Curves(bar); var gain = cs[3];
            Check(gain.label == "Gain" && !gain.enabled(), "4. the Klip editor's curve bar has a Gain chip, off for a sound without one");
            Click(gain.enable, 0);
        });
        steps.Add(() => {
            var g = C().ownCurves != null ? C().ownCurves.gain : null;
            Check(g != null && g.Has && g.modifier.enabled && CurveAnchor.FollowsWaveform(g.modifier), "4. a click on the Gain chip gives the sound its own Gain curve, on, following the waveform");
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
            bool soloOk = solo[1] && !solo[2] && !solo[3];
            bool noneOk = !none[1] && !none[2] && !none[3];
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
            zw.Repaint(); kw.Repaint();
            if (frame < 40 || frame % 12 != 0) return;
            bool stop = false;
            try { steps[step](); }
            catch (Exception e) { Check(false, "window step " + (step + 1) + " threw: " + e.Message); stop = true; }
            step++;
            if (!stop && step < steps.Count) return;
            EditorApplication.update -= tick;
            WaveSurfaceTK.trimRandomProbe = null;
            es.backdropDotted = keepDotted; es.backdropTransparent = keepClear; es.backdropWidthBonus = keepBonus;
            EditorPrefs.SetBool(AutoTidyKey, keepAutoTidy);
            try { if (zw != null) zw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { if (kw != null) kw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { Undo.RevertAllDownToGroup(startGroup); } catch (Exception e) { Debug.LogException(e); }
            ZoundsProject.Instance.zoundLibrary.zequences.RemoveAll(z => z.id == -9870);
            ZoundEngine.InvalidateLookups();
            if (projectBytes != null) {
                ZoundsWindow.isSavingJSON = true;
                try { File.WriteAllBytes(ZoundsProtection.Absolute(projectJson), projectBytes); AssetDatabase.ImportAsset(projectJson); }
                finally { ZoundsWindow.isSavingJSON = false; }
            }
            done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - gain, trim randomness, Tidy, level meter\n" + sb);
        };
        EditorApplication.update += tick;
    }
}

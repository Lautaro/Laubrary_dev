// A kept check for the waveform surface (2026-10-09): the Klip editor and a Zequence track draw and edit a sound with ONE
// component, so what is about the sound behaves the same in both. It opens a Klip editor and a Zequence editor on an
// in-memory copy of a playable Klip (a local track of an in-memory Zequence), waits for them to lay out, and drives both
// with real pointer events:
//
//   1. Both hosts hold the same component (the same waveform surface class).
//   2. A right-click on the waveform plays only this sound from the second under the pointer, in both hosts: at three
//      places the second it asks for is the second the host draws there (within a pixel). Nothing is actually played.
//   3. The backdrop rule: with the pitch curve selected for editing, the volume curve is a backdrop (no points, half
//      transparent, twice as wide) and the pitch curve shows its points, in both.
//   4. Curve editing goes through the one hit-test: a real press-and-drag on a point of the selected curve moves it, in
//      both hosts, and one Undo puts it back exactly.
//   5. A real drag of the trim's end edge moves the sound's trim end, in both hosts, and one Undo puts it back exactly.
//   6. A left click that nothing on the surface takes is the host's: the Klip editor's edit cursor lands on the clicked
//      second; on a track it reaches the timeline (a double click selects the piece).
// Everything it adds is removed again (the windows closed, the Undo steps reverted, the project file written back as it was).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Uitk;
using Laubrary.Zounds.Destructive;
using Curve = Laubrary.Zounds.AudioSpectrumView.Curve;

public static class ZoundsWaveSurfaceCheck {

    [MenuItem("Laubrary/Zounds/Checks/34 - One waveform surface (Klip editor and Zequence tracks)")]
    public static void RunFromMenu() { Start(Debug.Log); }

    /// <summary>The report of the last run (null while one runs).</summary>
    public static string LastReport;

    static void Send(VisualElement target, EventType type, Vector2 local, int button, int clicks = 1, Vector2 delta = default) {
        var ev = new Event { type = type, mousePosition = target.LocalToWorld(local), button = button, clickCount = clicks, delta = delta };
        EventBase e = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(ev)
                    : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev) : PointerMoveEvent.GetPooled(ev);
        using (e) target.panel.visualTree.SendEvent(e);
    }

    static void Drag(VisualElement area, Vector2 from, Vector2 to) {
        Send(area, EventType.MouseDown, from, 0);
        Vector2 last = from;
        for (int i = 1; i <= 4; i++) { var p = Vector2.Lerp(from, to, i / 4f); Send(area, EventType.MouseDrag, p, 0, 1, p - last); last = p; }
        Send(area, EventType.MouseUp, to, 0);
    }

    public static void Start(Action<string> done) {
        LastReport = null;
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && ZoundSapPlayback.LoadSourceClip(k, out bool pre) != null && !pre && ZoundPcmCache.Get(ZoundSapPlayback.LoadSourceClip(k)) != null) { src = k; break; }
        if (src == null) { done(LastReport = "SKIPPED - no playable Klip in this project\n"); return; }
        var clip = ZoundSapPlayback.LoadSourceClip(src);
        float S = clip.length;

        string projectJson = ZoundsProjectInitialization.GetZoundsProjectPath();
        byte[] projectBytes = !string.IsNullOrEmpty(projectJson) && File.Exists(ZoundsProtection.Absolute(projectJson)) ? File.ReadAllBytes(ZoundsProtection.Absolute(projectJson)) : null;
        // The gestures below are the default mouse scheme's (Settings > Waveform mouse): it is set for the run, put back after.
        var keepScheme = ZoundsMachineSettings.WaveMouseScheme;
        ZoundsWaveMouseMenuCheck.SetSchemeQuietly(ZoundsMachineSettings.WaveMouse.ClickSelects);
        Undo.IncrementCurrentGroup();
        int startGroup = Undo.GetCurrentGroup();
        var lib = ZoundsProject.Instance.zoundLibrary;

        // An in-memory Zequence with one local track playing a copy of the Klip, trimmed, with a volume and a pitch curve.
        var zeq = new Zequence(-9800) { name = "surface check (in memory)", mode = CompositeZound.Mode.Parallel, minPitch = 1f, maxPitch = 1f };
        var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(c, -9801);
        c.name = "surface check piece"; c.effectChain = new ZoundEffectChain(); c.chainPresetId = 0; c.ownCurves = null;
        c.minPitch = c.maxPitch = 1f; c.minVolume = c.maxVolume = 1f; c.clampToTrim = false;
        c.trimEnabled = true; c.trimStart = Mathf.Min(0.1f, S * 0.1f); c.trimEnd = Mathf.Min(S, c.trimStart + 1f);
        if (c.timeStretch != null) c.timeStretch.liveEnabled = false;
        c.parentId = zeq.id;
        KlipChainEnvelopes.SetVolumeEnabled(c, true);
        KlipChainEnvelopes.SetPitchEnabled(c, true);
        var pitch = KlipChainEnvelopes.PitchCurve(c, false);
        KlipChainEnvelopes.EnsureSourceAnchored(c);
        KlipChainEnvelopes.EnsurePitchRatio(c);   // already on the current scale, so an edit is only the edit
        pitch = KlipChainEnvelopes.PitchCurve(c, false);
        var pp = pitch.GetPointsList();
        // A point in the middle of the trim (the curve is on the file's own seconds, plus its extra time), below the top.
        var pmod = KlipChainEnvelopes.ModifierOf(c, pitch);
        float midX = (c.trimStart + c.trimEnd) * 0.5f / (S + Mathf.Max(0f, pmod != null ? pmod.Param(0) : 0f));
        int ins = pp.FindIndex(q => q.time > midX); if (ins < 1) ins = Mathf.Max(1, pp.Count - 1);
        pp.Insert(ins, new ZUIEnvelopePoint(midX, 0.4f, 1f));
        int Mid(List<ZUIEnvelopePoint> l) { int best = 0; for (int i = 1; i < l.Count; i++) if (Mathf.Abs(l[i].time - midX) < Mathf.Abs(l[best].time - midX)) best = i; return best; }
        zeq.localKlips.Add(c);
        var entry = new CompositeZound.ZoundEntry { zoundId = c.id, local = true, overridePitch = true, pitch = 1f, overrideVolume = true, volume = 1f };
        zeq.zoundEntries.Add(entry);
        lib.zequences.Add(zeq);
        ZoundEngine.InvalidateLookups();

        var zw = ZequenceEditorWindowTK.Open(zeq, false);
        zw.position = new Rect(120, 120, 1200, 420);
        var kw = KlipEditorWindowTK.Open(c, true);
        kw.position = new Rect(160, 160, 1000, 760);

        var recorded = new List<(WaveSurfaceTK s, float sec)>();
        WaveSurfaceTK.playFromProbe = (s, sec) => recorded.Add((s, sec));
        // After an Undo the project's objects are new ones and the windows rebuild: everything is looked up afresh.
        Klip C() => KlipEditorWindowTK.FindKlip(-9801);
        Envelope Pitch() => KlipChainEnvelopes.PitchCurve(C(), false);
        TrackStripTK Strip() { foreach (var st in zw.strips) if (st.Surface != null && st.Surface.Host.Sound != null && st.Surface.Host.Sound.id == -9801) return st; return null; }
        WaveSurfaceTK KS() => kw.Waveform?.surface;
        WaveSurfaceTK TS() => Strip()?.Surface;
        (string, WaveSurfaceTK)[] Hosts() => new[] { ("Klip editor", KS()), ("track", TS()) };
        void Settle() { kw.Waveform?.Refresh(); Strip()?.Sync(); }

        var steps = new List<Action>();
        steps.Add(() => {
            var k = KS(); var t = TS();
            Check(k != null && t != null && k.GetType() == t.GetType() && k.GetType() == typeof(WaveSurfaceTK),
                  "1. the Klip editor and the track both draw the sound with the waveform surface");
            if (k == null || t == null) throw new Exception("a surface is missing; the rest cannot run");
            // ── 2: right-click plays from the second under the pointer ──
            foreach (var (name, s) in Hosts()) {
                s.Refresh();
                var r = s.AreaRect;
                s.Host.Heard(out float a, out float b);
                float worst = 0f, perPixel = 0f; int got = 0;
                foreach (float f in new[] { 0.25f, 0.5f, 0.75f }) {
                    float sec = Mathf.Lerp(a, b, f);
                    float x = s.Host.XOf(sec, r);
                    perPixel = Mathf.Max(perPixel, Mathf.Abs(s.Host.SourceAt(x + 1f, r) - s.Host.SourceAt(x, r)));
                    recorded.Clear();
                    Send(s.area, EventType.MouseDown, new Vector2(x, r.height * 0.12f), 1);
                    Send(s.area, EventType.MouseUp, new Vector2(x, r.height * 0.12f), 1);
                    if (recorded.Count == 1 && ReferenceEquals(recorded[0].s, s)) { got++; worst = Mathf.Max(worst, Mathf.Abs(recorded[0].sec - sec)); }
                }
                Check(got == 3 && worst <= perPixel * 1.5f + 1e-4f, "2. " + name + ": a right-click plays only this sound from the second under the pointer (" + got + "/3, worst " + (worst * 1000f).ToString("0.0") + " ms, a pixel is " + (perPixel * 1000f).ToString("0.0") + " ms)");
            }
            kw.Waveform.Select(Curve.Pitch, true);
            Strip().SetEditing(SourceStageParam.Pitch);
            Settle();
        });
        steps.Add(() => {
            // ── 3: the backdrop rule ──
            foreach (var (name, s) in Hosts()) {
                var vol = s.EnvelopeOf(Curve.Volume); var pit = s.EnvelopeOf(Curve.Pitch);
                bool ok = vol.resolvedStyle.display != DisplayStyle.None && vol.backdrop && !vol.showHandles && pit.showHandles && !pit.backdrop;
                Check(ok, "3. " + name + ": with the pitch curve selected, the volume curve is a backdrop (no points) and the pitch curve shows its points");
            }
        });
        foreach (int hi in new[] { 0, 1 }) {
            float before = 0f, beforeT = 0f, after = 0f; bool over = false, up = true; string dbg = "";
            steps.Add(() => {
                // ── 4: a real drag on a point of the selected curve ──
                var (name, s) = Hosts()[hi];
                s.Refresh();
                var env = s.EnvelopeOf(Curve.Pitch);
                var pl0 = Pitch().GetPointsList(); var pt = pl0[Mid(pl0)];
                before = pt.value; beforeT = pt.time;
                var local = env.PointToLocal(pt.time, pt.value);
                over = env.IsOverPoint(local);
                dbg = " [same list " + ReferenceEquals(env.points, Pitch().GetPointsList()) + ", selected " + s.Host.SelectedCurve + ", editable " + (env.rt != null && env.rt.editable) + ", at " + env.ChangeCoordinatesTo(s.area, local) + " of " + s.AreaRect + ", env " + env.contentRect + " " + env.resolvedStyle.display + " x " + (env.rt != null ? env.rt.xMin + ".." + env.rt.xMax : "-") + " pts " + (env.points != null ? env.points.Count : -1) + " over " + over + "]";
                var at = env.ChangeCoordinatesTo(s.area, local);
                var cur = Pitch();
                up = pt.value < (cur.yMin + cur.yMax) * 0.5f;
                Drag(s.area, at, at + new Vector2(0f, up ? -12f : 12f));
                var pl1 = Pitch().GetPointsList(); after = pl1[Mid(pl1)].value;
                Undo.PerformUndo();
            });
            steps.Add(() => {
                var name = hi == 0 ? "Klip editor" : "track";
                var pl2 = Pitch().GetPointsList(); var back = pl2[Mid(pl2)];
                Check(over && (up ? after > before + 1e-4f : after < before - 1e-4f) && Mathf.Abs(back.value - before) < 1e-5f && Mathf.Abs(back.time - beforeT) < 1e-5f,
                      "4. " + name + ": dragging a point of the selected curve " + (up ? "up raises" : "down lowers") + " it (" + before.ToString("0.000") + " -> " + after.ToString("0.000") + "); Undo puts it back (" + back.value.ToString("0.000") + ")" + (over && after != before ? "" : dbg));
                // The next host's turn: the same curve selected there.
                kw.Waveform.Select(Curve.Pitch, true);
                Strip()?.SetEditing(SourceStageParam.Pitch);
                Settle();
            });
        }
        steps.Add(() => { kw.Waveform.Select(Curve.Pitch, false); Strip()?.SetEditing(-1); Settle(); });
        foreach (int hi in new[] { 0, 1 }) {
            float endBefore = 0f, endAfter = 0f;
            steps.Add(() => {
                // ── 5: a real drag of the trim's end edge ──
                var (name, s) = Hosts()[hi];
                s.Refresh();
                var r = s.AreaRect;
                endBefore = C().trimEnd;
                float x = s.Host.XOf(endBefore, r) - 1f;
                Drag(s.area, new Vector2(x, r.height * 0.5f), new Vector2(x - 30f, r.height * 0.5f));
                endAfter = C().trimEnd;
                Undo.PerformUndo();
            });
            steps.Add(() => {
                var name = hi == 0 ? "Klip editor" : "track";
                Check(endAfter < endBefore - 1e-3f && Mathf.Abs(C().trimEnd - endBefore) < 1e-5f,
                      "5. " + name + ": dragging the trim's end edge left moves the trim end (" + endBefore.ToString("0.000") + " -> " + endAfter.ToString("0.000") + " s); Undo puts it back (" + C().trimEnd.ToString("0.000") + ")");
                Settle();
            });
        }
        steps.Add(() => {
            // ── 6: a left click nothing on the surface takes is the host's ──
            var k = KS(); var r = k.AreaRect;
            var kc = C();
            float sec = Mathf.Lerp(kc.trimStart, kc.trimEnd, 0.4f);
            float x = k.Host.XOf(sec, r);
            Send(k.area, EventType.MouseDown, new Vector2(x, r.height * 0.85f), 0);
            Send(k.area, EventType.MouseUp, new Vector2(x, r.height * 0.85f), 0);
            float per = Mathf.Abs(k.Host.SourceAt(x + 1f, r) - k.Host.SourceAt(x, r));
            Check(Mathf.Abs((float)kw.Waveform.cursor - sec) <= per * 1.5f + 1e-4f, "6. Klip editor: a left click places the edit cursor at the clicked second (" + kw.Waveform.cursor.ToString("0.000") + " for " + sec.ToString("0.000") + ")");
            // On a track: a double click on the piece (which plays nothing) reaches the timeline, which selects the piece.
            var t = TS(); var tr = t.AreaRect; var tl = zw.timeline;
            tl.ClearSelection();
            float tx = t.Host.XOf(Mathf.Lerp(kc.trimStart, kc.trimEnd, 0.5f), tr);
            Send(t.area, EventType.MouseDown, new Vector2(tx, tr.height * 0.85f), 0, 2);
            Send(t.area, EventType.MouseUp, new Vector2(tx, tr.height * 0.85f), 0, 2);
            var p = tl.byEntry.TryGetValue(tl.zeq.zoundEntries[0], out var pl) ? pl : null;
            Check(tl.hasSel && p != null && Mathf.Abs(tl.selA - p.start) < 1e-3f && Mathf.Abs(tl.selB - p.End) < 1e-3f,
                  "6. track: a left (double) click is the timeline's: it selects the piece (" + tl.selA.ToString("0.000") + " - " + tl.selB.ToString("0.000") + " s)");
        });

        int frame = 0, step = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () => {
            frame++;
            zw.Repaint(); kw.Repaint();
            if (frame < 40 || frame % 12 != 0) return;
            bool stop = false;
            try { steps[step](); }
            catch (Exception e) { Check(false, "step " + (step + 1) + " threw: " + e.Message); stop = true; }
            step++;
            if (!stop && step < steps.Count) return;
            EditorApplication.update -= tick;
            WaveSurfaceTK.playFromProbe = null;
            ZoundsWaveMouseMenuCheck.SetSchemeQuietly(keepScheme);
            try { if (zw != null) zw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { if (kw != null) kw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { Undo.RevertAllDownToGroup(startGroup); } catch (Exception e) { Debug.LogException(e); }
            ZoundsProject.Instance.zoundLibrary.zequences.RemoveAll(z => z.id == -9800);
            ZoundEngine.InvalidateLookups();
            if (projectBytes != null) {
                ZoundsWindow.isSavingJSON = true;
                try { File.WriteAllBytes(ZoundsProtection.Absolute(projectJson), projectBytes); AssetDatabase.ImportAsset(projectJson); }
                finally { ZoundsWindow.isSavingJSON = false; }
            }
            done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - one waveform surface\n" + sb);
        };
        EditorApplication.update += tick;
    }
}

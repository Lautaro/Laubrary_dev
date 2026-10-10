// A kept check for the Klip editor waveform's mouse schemes, its selection menu and the shared edit verbs (owner,
// 2026-10-10). It opens a Klip editor on an in-memory copy of a playable Klip and a Zequence editor (edit tools shown) on
// an in-memory Zequence, waits for them to lay out, and drives them with real pointer events. Nothing is actually played
// (the plays are recorded instead) and no audio file is written (the destructive verbs are recorded, not run).
//
//   1. Icons and shared buttons: every button of the Klip editor's edit bar and of the Zequence timeline bar is a
//      shared verb button with an icon and a tooltip; the Klip bar offers the Zequence bar's single-sound verbs (play
//      from the marker, play the selection, trim, delete, copy, paste), and a verb wears the same icon in both bars.
//   2. Without a marker, Paste, Insert and play-from-marker are off and say "Place a marker first (click the wave)."
//   3. Click to select (the default scheme): a left click places the marker at the clicked second; a left drag selects;
//      a double-click inside the selection plays from the pointer and leaves the selection as it was; a right-click
//      outside the selection plays from the pointer and opens no menu; a right-click inside it opens the menu.
//   4. The menu: every item has its icon, a disabled one still has a tooltip; each enabled item, clicked, runs exactly the
//      verb its bar button runs (both recorded through the one verb path); for real: Copy keeps the selection's length,
//      Play selection plays exactly the selection, Trim sets the sound's trim to it (one Undo puts it back), Select all
//      selects what the sound plays.
//   5. The scheme setting is one Undo step. Halves: a faint middle line shows; a left click in the lower half plays and
//      leaves the marker; a left click in the upper half places it; a drag there selects; a right-click inside the
//      selection in the lower half plays, in the upper half opens the menu; the marker-less message names the upper half.
// Everything it adds is removed again (windows closed, Undo reverted, the kept audio, the scheme and the edit-tools
// preference put back, the project file written back as it was).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Uitk;
using Laubrary.Zounds.Destructive;
using WaveMouse = Laubrary.Zounds.ZoundsMachineSettings.WaveMouse;

public static class ZoundsWaveMouseMenuCheck {

    [MenuItem("Laubrary/Zounds/Checks/37 - Waveform mouse schemes, selection menu and the shared edit verbs")]
    public static void RunFromMenu() { Start(Debug.Log); }

    public static string LastReport;

    static void Send(VisualElement target, EventType type, Vector2 local, int button, int clicks = 1) {
        var ev = new Event { type = type, mousePosition = target.LocalToWorld(local), button = button, clickCount = clicks };
        EventBase e = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(ev)
                    : type == EventType.MouseUp ? PointerUpEvent.GetPooled(ev) : PointerMoveEvent.GetPooled(ev);
        using (e) target.panel.visualTree.SendEvent(e);
    }

    static void Click(VisualElement target, Vector2 local, int button, int clicks = 1) {
        Send(target, EventType.MouseDown, local, button, clicks);
        Send(target, EventType.MouseUp, local, button, clicks);
    }

    static void Drag(VisualElement area, Vector2 from, Vector2 to) {
        Send(area, EventType.MouseDown, from, 0);
        for (int i = 1; i <= 4; i++) Send(area, EventType.MouseDrag, Vector2.Lerp(from, to, i / 4f), 0);
        Send(area, EventType.MouseUp, to, 0);
    }

    static void SetSchemeQuietly(WaveMouse v) {
        var s = ZoundsMachineSettings.instance;
        typeof(ZoundsMachineSettings).GetField("waveMouse", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(s, (int)v);
        typeof(ZoundsMachineSettings).GetMethod("SaveIfChanged", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(s, null);
    }

    static Texture IconOf(VisualElement b) {
        var i = b.Q(className: "zs-verb__icon");
        return i != null ? i.resolvedStyle.backgroundImage.texture : null;
    }

    public static void Start(Action<string> done) {
        LastReport = null;
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds())
            if (z is Klip k && ZoundSapPlayback.LoadSourceClip(k, out bool pre) != null && !pre && ZoundPcmCache.Get(ZoundSapPlayback.LoadSourceClip(k)) != null && ZoundSapPlayback.LoadSourceClip(k).length > 0.5f) { src = k; break; }
        if (src == null) { done(LastReport = "SKIPPED - no playable Klip in this project\n"); return; }
        var clip = ZoundSapPlayback.LoadSourceClip(src);
        float S = clip.length;

        string projectJson = ZoundsProjectInitialization.GetZoundsProjectPath();
        byte[] projectBytes = !string.IsNullOrEmpty(projectJson) && File.Exists(ZoundsProtection.Absolute(projectJson)) ? File.ReadAllBytes(ZoundsProtection.Absolute(projectJson)) : null;
        var keepScheme = ZoundsMachineSettings.WaveMouseScheme;
        const string EditToolsKey = "Laubrary.Zounds.Zequence.EditTools";
        bool keepEditTools = EditorPrefs.GetBool(EditToolsKey, false);
        EditorPrefs.SetBool(EditToolsKey, true);
        var keepClipboard = ZoundsAudioEdits.Clipboard;
        SetSchemeQuietly(WaveMouse.ClickSelects);
        Undo.IncrementCurrentGroup();
        int startGroup = Undo.GetCurrentGroup();
        var lib = ZoundsProject.Instance.zoundLibrary;

        // An in-memory Zequence with one local track playing a copy of the Klip (trimmed to most of its file).
        var zeq = new Zequence(-9900) { name = "check 37 (in memory)", mode = CompositeZound.Mode.Parallel, minPitch = 1f, maxPitch = 1f };
        var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(c, -9901);
        c.name = "check 37 piece"; c.effectChain = new ZoundEffectChain(); c.chainPresetId = 0; c.ownCurves = null;
        c.minPitch = c.maxPitch = 1f; c.minVolume = c.maxVolume = 1f; c.clampToTrim = false;
        c.trimEnabled = true; c.trimStart = S * 0.05f; c.trimEnd = S * 0.95f;
        if (c.timeStretch != null) c.timeStretch.liveEnabled = false;
        c.parentId = zeq.id;
        zeq.localKlips.Add(c);
        zeq.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = c.id, local = true, overridePitch = true, pitch = 1f, overrideVolume = true, volume = 1f });
        lib.zequences.Add(zeq);
        ZoundEngine.InvalidateLookups();

        var zw = ZequenceEditorWindowTK.Open(zeq, false);
        zw.position = new Rect(120, 120, 1300, 420);
        var kw = KlipEditorWindowTK.Open(c, true);
        kw.position = new Rect(160, 160, 1000, 760);

        var plays = new List<float>();
        WaveSurfaceTK.playFromProbe = (s, sec) => plays.Add(sec);
        var verbs = new List<EditVerb>();
        Klip C() => KlipEditorWindowTK.FindKlip(-9901);
        KlipWaveformTK W() => kw.Waveform;
        WaveSurfaceTK KS() => kw.Waveform?.surface;
        VisualElement MenuEl() => kw.rootVisualElement.panel?.visualTree.Q("zui-menu");
        List<VisualElement> MenuRows() => MenuEl()?.Query(className: "zui-menu__item").ToList() ?? new List<VisualElement>();
        string RowLabel(VisualElement row) => row.Q<Label>(className: "zui-menu__label")?.text;
        void CloseMenu() { W()?.lastMenu?.Close(); if (W() != null) W().lastMenu = null; }
        float X(double sec) => KS().Host.XOf((float)sec, KS().AreaRect);
        float Per() { var r = KS().AreaRect; float x = r.width * 0.5f; return Mathf.Abs(KS().Host.SourceAt(x + 1f, r) - KS().Host.SourceAt(x, r)); }
        double a0 = c.trimStart + (c.trimEnd - c.trimStart) * 0.3, b0 = c.trimStart + (c.trimEnd - c.trimStart) * 0.6;

        var steps = new List<Action>();
        steps.Add(() => {
            // ── 1: icons and the shared buttons ──
            var kb = W().EditButtons;
            bool allIcons = true, allTips = true;
            foreach (var kv in kb) { if (IconOf(kv.Value) == null) allIcons = false; if (string.IsNullOrEmpty(kv.Value.tooltip)) allTips = false; }
            Check(kb.Count == 10 && allIcons && allTips, "1. the Klip edit bar has " + kb.Count + " verb buttons, each with an icon and a tooltip");
            var zbar = zw.rootVisualElement.Q(className: "zs-timeline-header__edit-bar");
            var zctl = zbar != null ? zbar.Query(className: "zs-timeline-header__control").ToList() : new List<VisualElement>();
            int zIcons = 0, zShared = 0; var zByVerb = new Dictionary<EditVerb, VisualElement>();
            foreach (var e in zctl) { if (IconOf(e) != null) zIcons++; if (e.ClassListContains("zs-verb") && e.userData is EditVerb v) { zShared++; zByVerb[v] = e; } }
            Check(zbar != null && zbar.resolvedStyle.display == DisplayStyle.Flex && zctl.Count == 14 && zIcons == 14 && zShared == 14,
                  "1. the Zequence timeline bar's " + zctl.Count + " buttons and switches are shared verb buttons with icons (" + zIcons + " icons)");
            var single = new[] { EditVerb.PlayFromMarker, EditVerb.PlaySelection, EditVerb.Trim, EditVerb.Delete, EditVerb.Copy, EditVerb.Paste };
            bool offered = true, sameIcon = true;
            foreach (var v in single) {
                if (!kb.TryGetValue(v, out var kbtn) || !kbtn.ClassListContains("zs-verb")) { offered = false; continue; }
                if (!zByVerb.TryGetValue(v, out var zbtn) || IconOf(zbtn) != IconOf(kbtn)) sameIcon = false;
            }
            Check(offered && sameIcon, "1. the Klip bar offers the Zequence bar's single-sound verbs (here, audition, trim, delete, copy, paste), built by the same verb faces with the same icons");
            // ── 2: no marker yet ──
            ZoundsAudioEdits.Clipboard = (AudioPcm.FromClip(clip).Slice(0, 100), "check 37");
            W().EditDone("", -1d, 0d, 0d);
            bool off = true; string why = "";
            foreach (var v in new[] { EditVerb.Paste, EditVerb.Insert, EditVerb.PlayFromMarker }) {
                var b = kb[v];
                if (b.enabledSelf || b.tooltip != "Place a marker first (click the wave).") { off = false; why += " " + v + "='" + b.tooltip + "'"; }
            }
            Check(off, "2. without a marker, Paste, Insert and Here are off and say 'Place a marker first (click the wave).'" + why);
        });
        steps.Add(() => {
            // ── 3: click to select ──
            var r = KS().AreaRect;
            Click(KS().area, new Vector2(X(a0), r.height * 0.7f), 0);
            Check(Math.Abs(W().cursor - a0) <= Per() * 1.5 + 1e-4 && !W().HasSelection && W().EditButtons[EditVerb.Paste].enabledSelf && W().EditButtons[EditVerb.Insert].enabledSelf,
                  "3. a left click places the marker at the clicked second (" + W().cursor.ToString("0.000") + " for " + a0.ToString("0.000") + "), and Paste and Insert come on");
            Drag(KS().area, new Vector2(X(a0), r.height * 0.3f), new Vector2(X(b0), r.height * 0.3f));
            Check(W().HasSelection && Math.Abs(W().selA - a0) <= Per() * 1.5 + 1e-4 && Math.Abs(W().selB - b0) <= Per() * 1.5 + 1e-4,
                  "3. a left drag selects (" + W().selA.ToString("0.000") + " - " + W().selB.ToString("0.000") + " s)");
        });
        steps.Add(() => {
            var r = KS().AreaRect;
            double sa = W().selA, sbb = W().selB, mid = (sa + sbb) * 0.5;
            plays.Clear();
            Click(KS().area, new Vector2(X(mid), r.height * 0.5f), 0, 1);
            Click(KS().area, new Vector2(X(mid), r.height * 0.5f), 0, 2);
            Check(plays.Count == 1 && Mathf.Abs(plays[0] - (float)mid) <= Per() * 1.5f + 1e-4f && W().selA == sa && W().selB == sbb,
                  "3. a double-click inside the selection plays from the pointer (" + (plays.Count > 0 ? plays[0].ToString("0.000") : "-") + " s) and leaves the selection as it was");
            plays.Clear(); CloseMenu();
            double outside = Math.Min(sbb + (c.trimEnd - sbb) * 0.5, c.trimEnd - 0.01);
            Click(KS().area, new Vector2(X(outside), r.height * 0.5f), 1);
            Check(plays.Count == 1 && MenuEl() == null && W().lastMenu == null, "3. a right-click outside the selection plays from the pointer (" + (plays.Count > 0 ? plays[0].ToString("0.000") : "-") + " s) and opens no menu");
            plays.Clear();
            Click(KS().area, new Vector2(X(mid), r.height * 0.5f), 1);
            Check(plays.Count == 0 && MenuEl() != null, "3. a right-click inside the selection opens the selection menu, and plays nothing");
        });
        steps.Add(() => {
            // ── 4: the menu ──
            var rows = MenuRows();
            int verbsInMenu = 0; foreach (var v in KlipWaveformTK.MenuVerbs) if (v.HasValue) verbsInMenu++;
            bool icons = true, tips = true, shortcuts = false;
            foreach (var row in rows) {
                var img = row.Q<Image>(className: "zui-menu__icon");
                if (img == null || img.image == null) icons = false;
                if (string.IsNullOrEmpty(row.tooltip)) tips = false;
                if (row.Q<Label>(className: "zui-menu__shortcut") != null) shortcuts = true;
            }
            var labels = new List<string>(); foreach (var row in rows) labels.Add(RowLabel(row));
            Check(rows.Count == verbsInMenu && icons && tips && shortcuts, "4. the menu has its " + rows.Count + " items (" + string.Join(", ", labels) + "), each with an icon and a tooltip, with shortcuts shown");
            CloseMenu();
        });
        // Each enabled menu item runs exactly the verb of its bar button (both recorded through the one verb path).
        var menuVerbs = new List<EditVerb>(); foreach (var v in KlipWaveformTK.MenuVerbs) if (v.HasValue) menuVerbs.Add(v.Value);
        var mismatches = new List<string>(); int compared = 0;
        foreach (var verb in menuVerbs) {
            steps.Add(() => {
                var r = KS().AreaRect;
                KlipWaveformTK.verbProbe = (w, v) => verbs.Add(v);
                verbs.Clear();
                Click(KS().area, new Vector2(X((W().selA + W().selB) * 0.5), r.height * 0.5f), 1);
                var row = MenuRows().Find(x => RowLabel(x) == EditVerbsTK.Of(verb).menu);
                bool enabled = W().CanRun(verb, out _);
                if (row != null) Click(row, row.contentRect.center, 0);
                var fromMenu = new List<EditVerb>(verbs);
                verbs.Clear();
                if (W().EditButtons.TryGetValue(verb, out var b)) Click(b, b.contentRect.center, 0);
                else if (enabled) W().RunVerb(verb);   // Select all has no bar button: its key's path
                var fromBar = new List<EditVerb>(verbs);
                KlipWaveformTK.verbProbe = null;
                CloseMenu();
                bool ok = row != null && (enabled ? fromMenu.Count == 1 && fromMenu[0] == verb && fromBar.Count == 1 && fromBar[0] == verb
                                                  : fromMenu.Count == 0);
                compared++;
                if (!ok) mismatches.Add(verb + " (menu " + string.Join("/", fromMenu) + ", bar " + string.Join("/", fromBar) + ", row " + (row != null) + ")");
            });
        }
        steps.Add(() => Check(mismatches.Count == 0 && compared == menuVerbs.Count, "4. each of the " + compared + " menu items runs the same verb as its bar button" + (mismatches.Count > 0 ? ": " + string.Join("; ", mismatches) : "")));
        // For real: Copy, Play selection, Trim (then Undo), Select all.
        void ViaMenu(EditVerb verb) {
            var r = KS().AreaRect;
            Click(KS().area, new Vector2(X((W().selA + W().selB) * 0.5), r.height * 0.5f), 1);
            var row = MenuRows().Find(x => RowLabel(x) == EditVerbsTK.Of(verb).menu);
            if (row == null) throw new Exception("no '" + EditVerbsTK.Of(verb).menu + "' in the menu");
            Click(row, row.contentRect.center, 0);
            CloseMenu();
        }
        steps.Add(() => {
            double len = W().selB - W().selA;
            ViaMenu(EditVerb.Copy);
            Check(Math.Abs(ZoundsAudioEdits.ClipboardSeconds - len) < 2.0 / clip.frequency, "4. for real, the menu's Copy keeps the selection (" + ZoundsAudioEdits.ClipboardSeconds.ToString("0.000") + " s of " + len.ToString("0.000") + ")");
            var keepPlay = W().onPlayRange;
            var ranges = new List<(float, float)>();
            W().onPlayRange = (x, y) => ranges.Add((x, y));
            ViaMenu(EditVerb.PlaySelection);
            W().onPlayRange = keepPlay;
            Check(ranges.Count == 1 && Mathf.Abs(ranges[0].Item1 - (float)W().selA) < 1e-5f && Mathf.Abs(ranges[0].Item2 - (float)W().selB) < 1e-5f,
                  "4. for real, the menu's Play selection plays exactly the selection");
        });
        double tA = 0, tB = 0; float trimS0 = 0, trimE0 = 0, trimS1 = 0, trimE1 = 0;
        steps.Add(() => {
            tA = W().selA; tB = W().selB; trimS0 = C().trimStart; trimE0 = C().trimEnd;
            ViaMenu(EditVerb.Trim);
            trimS1 = C().trimStart; trimE1 = C().trimEnd;
            Undo.PerformUndo();
        });
        steps.Add(() => {
            Check(C().trimEnabled && Mathf.Abs(trimS1 - (float)tA) < 1e-5f && Mathf.Abs(trimE1 - (float)tB) < 1e-5f && Mathf.Abs(C().trimStart - trimS0) < 1e-5f && Mathf.Abs(C().trimEnd - trimE0) < 1e-5f,
                  "4. for real, the menu's Trim to selection trims the sound to it (" + trimS1.ToString("0.000") + " - " + trimE1.ToString("0.000") + " s); one Undo puts the trim back (" + C().trimStart.ToString("0.000") + " - " + C().trimEnd.ToString("0.000") + ")");
            W().EditDone("", tA, tA, tB);
        });
        steps.Add(() => {
            ViaMenu(EditVerb.SelectAll);
            Check(Mathf.Abs((float)W().selA - C().trimStart) < 1e-5f && Mathf.Abs((float)W().selB - C().trimEnd) < 1e-5f, "4. for real, the menu's Select all selects what the sound plays (its trim)");
        });
        steps.Add(() => {
            // ── 5: the scheme setting, then Halves ──
            ZoundsMachineSettings.SetWaveMouse(WaveMouse.Halves);
            bool set = ZoundsMachineSettings.WaveMouseScheme == WaveMouse.Halves;
            Undo.PerformUndo();
            bool undone = ZoundsMachineSettings.WaveMouseScheme == WaveMouse.ClickSelects;
            Check(set && undone, "5. changing the waveform mouse is one Undo step (Halves, then back to Click to select)");
            ZoundsMachineSettings.SetWaveMouse(WaveMouse.Halves);
            W().EditDone("", -1d, 0d, 0d);
            W().Refresh();
        });
        steps.Add(() => {
            var line = KS().marks.Q(className: "zs-klip-waveform__zone-line");
            Check(line != null && line.resolvedStyle.display == DisplayStyle.Flex && KS().area.tooltip.Contains("Upper half"), "5. Halves: a faint line marks the middle, and the waveform's tooltip explains the halves");
            Check(W().EditButtons[EditVerb.Paste].tooltip == "Place a marker first (click the upper half of the wave).", "5. Halves: without a marker, Paste says to click the upper half ('" + W().EditButtons[EditVerb.Paste].tooltip + "')");
            var r = KS().AreaRect;
            plays.Clear();
            Click(KS().area, new Vector2(X(a0), r.height * 0.8f), 0);
            Check(plays.Count == 1 && W().cursor < 0d, "5. Halves: a left click in the lower half plays from the pointer and places no marker");
            Click(KS().area, new Vector2(X(a0), r.height * 0.2f), 0);
            Check(Math.Abs(W().cursor - a0) <= Per() * 1.5 + 1e-4, "5. Halves: a left click in the upper half places the marker");
            Drag(KS().area, new Vector2(X(a0), r.height * 0.2f), new Vector2(X(b0), r.height * 0.2f));
            Check(W().HasSelection && Math.Abs(W().selB - b0) <= Per() * 1.5 + 1e-4, "5. Halves: a drag in the upper half selects");
            plays.Clear(); CloseMenu();
            double mid = (W().selA + W().selB) * 0.5;
            Click(KS().area, new Vector2(X(mid), r.height * 0.8f), 1);
            Check(plays.Count == 1 && MenuEl() == null, "5. Halves: a right-click inside the selection in the lower half plays, no menu");
            plays.Clear();
            Click(KS().area, new Vector2(X(mid), r.height * 0.2f), 1);
            Check(plays.Count == 0 && MenuEl() != null, "5. Halves: a right-click inside the selection in the upper half opens the menu");
            CloseMenu();
        });

        int frame = 0, step = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () => {
            frame++;
            zw.Repaint(); kw.Repaint();
            if (frame < 40 || frame % 12 != 0) return;
            bool stop = false;
            try { steps[step](); }
            catch (Exception e) { Check(false, "step " + (step + 1) + " threw: " + e); stop = true; }
            step++;
            if (!stop && step < steps.Count) return;
            EditorApplication.update -= tick;
            WaveSurfaceTK.playFromProbe = null;
            KlipWaveformTK.verbProbe = null;
            try { CloseMenu(); } catch { }
            try { if (zw != null) zw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { if (kw != null) kw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { Undo.RevertAllDownToGroup(startGroup); } catch (Exception e) { Debug.LogException(e); }
            ZoundsAudioEdits.Clipboard = keepClipboard;
            SetSchemeQuietly(keepScheme);
            EditorPrefs.SetBool(EditToolsKey, keepEditTools);
            ZoundsProject.Instance.zoundLibrary.zequences.RemoveAll(z => z.id == -9900);
            ZoundEngine.InvalidateLookups();
            if (projectBytes != null) {
                ZoundsWindow.isSavingJSON = true;
                try { File.WriteAllBytes(ZoundsProtection.Absolute(projectJson), projectBytes); AssetDatabase.ImportAsset(projectJson); }
                finally { ZoundsWindow.isSavingJSON = false; }
            }
            done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - waveform mouse schemes, selection menu, shared edit verbs\n" + sb);
        };
        EditorApplication.update += tick;
    }
}

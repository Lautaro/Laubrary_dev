// A kept check for the pinnable tool groups (owner, 2026-10-10): the playback options (Play on change, Burst, Loop…) and
// the edit tools, in the Klip editor and the Zequence editor, each a popover from its anchor or pinned into the window.
// It opens a Klip editor on an in-memory copy of a playable Klip and a Zequence editor on an in-memory Zequence, waits for
// them to lay out, and drives them with real pointer events (presses and releases sent through each window's panel).
//
//   1. Default: with no pin saved, nothing is pinned: no edit bar, no overview strip, no playback card in either window.
//   2. Klip editor: a right-click on Play opens the playback options as a popover (card and an unlit Pin); its Pin, clicked,
//      closes the popover and puts the card under the Play row (Pin lit). A click on Edit tools opens the edit tools (the
//      ten verb buttons and a Pin), a right-click does too; Pin puts the edit bar over the waveform.
//   3. Zequence editor: the same for Play (pinned: the first row on Play's row, the second under it) and for Edit tools
//      (pinned: the edit bar and the overview strip over the timeline). In the edit popover a switch (Follow) leaves it
//      open and an action (Fit) closes it.
//   4. Both windows closed and opened again: all four groups are still pinned, laid out as before.
//   5. Unpinning, each a different way: the Klip card's lit Pin; a right-click on the Klip edit bar away from its buttons
//      offers Unpin; a right-click on the Zequence Play button offers Unpin; the Zequence edit bar's lit Pin. Each group
//      leaves the window, and a right-click on the anchor opens its popover again.
//   6. Closed and opened again: all four stay unpinned.
// Everything it adds is removed again (windows closed, Undo reverted to where it started, the four pins put back as they
// were, auto-tidy put back, the in-memory sounds removed, the project file written back if anything rewrote it).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;
using Laubrary.Zounds.Uitk;
using Laubrary.Zounds.Destructive;

public static class ZoundsToolGroupPinCheck {

    [MenuItem("Laubrary/Zounds/Checks/38 - Pinnable tool groups (playback options and edit tools, both editors)")]
    public static void RunFromMenu() { Start(Debug.Log); }

    public static string LastReport;

    static void Send(VisualElement target, EventType type, Vector2 local, int button) {
        var ev = new Event { type = type, mousePosition = target.LocalToWorld(local), button = button, clickCount = 1 };
        EventBase e = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(ev) : PointerUpEvent.GetPooled(ev);
        using (e) target.panel.visualTree.SendEvent(e);
    }

    /// <summary>A real press and release at the element's middle (or at a local point).</summary>
    static void Click(VisualElement target, int button, Vector2? local = null) {
        var at = local ?? target.contentRect.center;
        Send(target, EventType.MouseDown, at, button);
        Send(target, EventType.MouseUp, at, button);
    }

    static readonly string[] Keys = {
        KlipEditorWindowTK.PlaybackPinKey, KlipWaveformTK.EditPinKey,
        ZequenceEditorWindowTK.PlaybackPinKey, ZequenceEditorWindowTK.EditToolsKey,
    };

    public static void Start(Action<string> done) {
        LastReport = null;
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds())
            if (z is Klip k && ZoundSapPlayback.LoadSourceClip(k, out bool pre) != null && !pre && ZoundSapPlayback.LoadSourceClip(k).length > 0.2f) { src = k; break; }
        if (src == null) { done(LastReport = "SKIPPED - no playable Klip in this project\n"); return; }
        float S = ZoundSapPlayback.LoadSourceClip(src).length;

        string projectJson = ZoundsProjectInitialization.GetZoundsProjectPath();
        byte[] projectBytes = !string.IsNullOrEmpty(projectJson) && File.Exists(ZoundsProtection.Absolute(projectJson)) ? File.ReadAllBytes(ZoundsProtection.Absolute(projectJson)) : null;
        var keep = new bool[Keys.Length];
        for (int i = 0; i < Keys.Length; i++) { keep[i] = PinnableToolGroupTK.IsPinned(Keys[i]); EditorPrefs.DeleteKey(Keys[i]); }   // no pin saved: the default
        const string AutoTidyKey = "Laubrary.Zounds.Zequence.AutoTidy";   // auto-tidy greys Fit, which step 3 clicks
        bool keepAutoTidy = EditorPrefs.GetBool(AutoTidyKey, false);
        EditorPrefs.SetBool(AutoTidyKey, false);
        Undo.IncrementCurrentGroup();
        int startGroup = Undo.GetCurrentGroup();
        var lib = ZoundsProject.Instance.zoundLibrary;

        // An in-memory Zequence with one local track playing a copy of the Klip.
        var zeq = new Zequence(-9800) { name = "check 38 (in memory)", mode = CompositeZound.Mode.Parallel, minPitch = 1f, maxPitch = 1f };
        var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
        typeof(Zound).GetField("id").SetValue(c, -9801);
        c.name = "check 38 piece"; c.effectChain = new ZoundEffectChain(); c.chainPresetId = 0; c.ownCurves = null;
        c.minPitch = c.maxPitch = 1f; c.minVolume = c.maxVolume = 1f; c.retriggerEnabled = false;
        c.trimEnabled = true; c.trimStart = 0f; c.trimEnd = S;
        if (c.timeStretch != null) c.timeStretch.liveEnabled = false;
        c.parentId = zeq.id;
        zeq.localKlips.Add(c);
        zeq.zoundEntries.Add(new CompositeZound.ZoundEntry { zoundId = c.id, local = true, overridePitch = true, pitch = 1f, overrideVolume = true, volume = 1f });
        lib.zequences.Add(zeq);
        ZoundEngine.InvalidateLookups();

        ZequenceEditorWindowTK zw = null;
        KlipEditorWindowTK kw = null;
        void OpenBoth() {
            zw = ZequenceEditorWindowTK.Open(zeq, false);
            zw.position = new Rect(120, 120, 1400, 460);
            kw = KlipEditorWindowTK.Open(c, true);
            kw.position = new Rect(160, 200, 1000, 760);
        }
        void CloseBoth() {
            try { if (zw != null) zw.Close(); } catch (Exception e) { Debug.LogException(e); }
            try { if (kw != null) kw.Close(); } catch (Exception e) { Debug.LogException(e); }
            zw = null; kw = null;
        }
        OpenBoth();

        // ── what each window shows ──
        VisualElement KRoot() => kw.rootVisualElement;
        VisualElement ZRoot() => zw.rootVisualElement;
        VisualElement KEditBar() => KRoot().Q(className: "zs-klip-waveform__edit-bar");
        VisualElement KCard() => KRoot().Q(className: "zs-klip-editor__pinned")?.Q(className: "zs-audition-card");
        VisualElement ZEditBar() => ZRoot().Q(className: "zs-timeline-header__edit-bar");
        VisualElement ZOverview() => ZRoot().Q(className: "zs-timeline-header__overview");
        VisualElement ZInline() => ZRoot().Q(className: "zs-zequence-editor__pinned-inline");
        VisualElement ZCard() => ZRoot().Q(className: "zs-zequence-editor__pinned")?.Q(className: "zs-audition-card");
        bool Shown(VisualElement e) => e != null && e.resolvedStyle.display != DisplayStyle.None && e.panel != null;
        ZuiToggleButton PinIn(VisualElement e) => e?.Q<ZuiToggleButton>("toolgroup-pin");
        bool Open(PinnableToolGroupTK g) => g != null && g.Popover != null && g.Popover.IsOpen;
        VisualElement Pop(PinnableToolGroupTK g) => Open(g) ? g.Popover.Panel : null;
        string Layout() => "Klip: edit bar " + (Shown(KEditBar()) ? "shown" : "none") + ", card " + (Shown(KCard()) ? "shown" : "none")
                         + "; Zequence: edit bar " + (Shown(ZEditBar()) ? "shown" : "none") + ", overview " + (Shown(ZOverview()) ? "shown" : "hidden")
                         + ", Play row holds " + (ZInline()?.childCount ?? -1) + ", card " + (Shown(ZCard()) ? "shown" : "none");
        bool AllPinnedLayout() => Shown(KEditBar()) && Shown(KCard()) && Shown(ZEditBar()) && Shown(ZOverview()) && ZInline() != null && ZInline().Q(className: "zs-toolgroup__pin") != null && Shown(ZCard());
        bool NonePinnedLayout() => !Shown(KEditBar()) && KCard() == null && !Shown(ZEditBar()) && !Shown(ZOverview()) && ZInline() != null && ZInline().childCount == 0 && ZCard() == null;
        bool AllKeys(bool v) { foreach (var k in Keys) if (PinnableToolGroupTK.IsPinned(k) != v) return false; return true; }
        VisualElement MenuItem(string label) {
            foreach (var row in KRoot().panel.visualTree.Query(className: "zui-menu__item").ToList()) if (row.Q<Label>(className: "zui-menu__label")?.text == label) return row;
            foreach (var row in ZRoot().panel.visualTree.Query(className: "zui-menu__item").ToList()) if (row.Q<Label>(className: "zui-menu__label")?.text == label) return row;
            return null;
        }

        var steps = new List<Action> {
            // 1. Default
            () => {
                Check(AllKeys(false) && NonePinnedLayout(), "1. default (no pin saved): nothing pinned. " + Layout());
                Check(kw.Waveform.EditAnchor != null && kw.Waveform.EditAnchor.tooltip.Contains("Click or right-click") && kw.PlayButton.tooltip.Contains("Right-click: the playback options"),
                      "1. the anchors say how to reach the groups ('" + kw.Waveform.EditAnchor?.tooltip.Split('.')[0] + "…')");
                // 2. Klip: right-click Play
                Click(kw.PlayButton, 1);
            },
            () => {
                var p = Pop(kw.Playback);
                var pin = PinIn(p);
                Check(p != null && p.Q(className: "zs-audition-card") != null && pin != null && !pin.value, "2. Klip: a right-click on Play opens the playback options (card and an unlit Pin)");
                if (pin != null) Click(pin, 0);
            },
            () => {
                Check(!Open(kw.Playback) && PinnableToolGroupTK.IsPinned(KlipEditorWindowTK.PlaybackPinKey) && Shown(KCard()) && PinIn(KCard()) != null && PinIn(KCard()).value,
                      "2. Klip: Pin closes the popover and puts the card under the Play row, its Pin lit");
                Click(kw.Waveform.EditAnchor, 0);   // a left click opens the edit tools
            },
            () => {
                var p = Pop(kw.Waveform.EditTools);
                int verbs = p != null ? p.Query(className: "zs-klip-waveform__edit-button").ToList().Count : 0;
                Check(p != null && verbs == 10 && PinIn(p) != null, "2. Klip: a click on Edit tools opens the edit tools (" + verbs + " verb buttons and a Pin)");
                kw.Waveform.EditTools.ClosePopover();
                Click(kw.Waveform.EditAnchor, 1);   // a right-click opens them too
            },
            () => {
                var p = Pop(kw.Waveform.EditTools);
                Check(p != null, "2. Klip: a right-click on Edit tools opens them too");
                if (PinIn(p) != null) Click(PinIn(p), 0);
            },
            () => {
                Check(!Open(kw.Waveform.EditTools) && PinnableToolGroupTK.IsPinned(KlipWaveformTK.EditPinKey) && Shown(KEditBar()) && kw.Waveform.EditButtons.Count == 10,
                      "2. Klip: Pin puts the edit bar over the waveform (" + kw.Waveform.EditButtons.Count + " verbs followed)");
                // 3. Zequence: right-click Play
                Click(zw.PlayButton, 1);
            },
            () => {
                var p = Pop(zw.Playback);
                Check(p != null && p.Q(className: "zs-audition-card") != null && PinIn(p) != null && !PinIn(p).value, "3. Zequence: a right-click on Play opens the playback options");
                if (PinIn(p) != null) Click(PinIn(p), 0);
            },
            () => { },   // the Zequence rebuilds on its next tick
            () => {
                bool inline = ZInline() != null && ZInline().Q(className: "zs-toolgroup__pin") != null && ZInline().Q<ZuiToggleButton>() != null;
                Check(PinnableToolGroupTK.IsPinned(ZequenceEditorWindowTK.PlaybackPinKey) && inline && Shown(ZCard()), "3. Zequence: Pin puts the first row on Play's row and the second under it. " + Layout());
                Click(zw.EditToolsButton, 1);
            },
            () => {
                var p = Pop(zw.EditTools);
                var follow = p?.Q<ZuiToggleButton>("verb-Follow");
                Check(p != null && p.Q(className: "zs-timeline-header__edit-bar") != null && PinIn(p) != null, "3. Zequence: a right-click on Edit tools opens the timeline's edit tools");
                if (follow != null) Click(follow, 0);
            },
            () => {
                Check(Open(zw.EditTools), "3. Zequence: a switch in the popover (Follow) leaves it open");
                var fit = Pop(zw.EditTools)?.Q<Button>("verb-FitView");
                if (fit != null) Click(fit, 0);
            },
            () => {
                Check(!Open(zw.EditTools), "3. Zequence: an action in the popover (Fit) closes it");
                Click(zw.EditToolsButton, 0);
            },
            () => {
                var p = Pop(zw.EditTools);
                if (PinIn(p) != null) Click(PinIn(p), 0);
            },
            () => { },
            () => {
                Check(AllKeys(true) && AllPinnedLayout(), "3. Zequence: Pin puts the edit bar and the overview over the timeline; all four pinned. " + Layout());
                // 4. Reopen
                CloseBoth(); OpenBoth();
            },
            () => { },
            () => {
                Check(AllKeys(true) && AllPinnedLayout(), "4. closed and opened again: all four still pinned. " + Layout());
                // 5. Unpin, four ways. Klip card: its lit Pin.
                var pin = PinIn(KCard());
                if (pin != null) Click(pin, 0);
            },
            () => {
                Check(!PinnableToolGroupTK.IsPinned(KlipEditorWindowTK.PlaybackPinKey) && KCard() == null, "5. Klip: the card's lit Pin unpins the playback options");
                // Klip edit bar: a right-click on its readout (no control) offers Unpin.
                var readout = KEditBar()?.Q(className: "zs-klip-waveform__edit-readout");
                if (readout != null) Click(readout, 1);
            },
            () => {
                var item = MenuItem("Unpin");
                Check(item != null && kw.Waveform.EditTools.UnpinMenu != null, "5. Klip: a right-click on the edit bar away from its buttons offers Unpin");
                if (item != null) Click(item, 0);
            },
            () => {
                Check(!PinnableToolGroupTK.IsPinned(KlipWaveformTK.EditPinKey) && !Shown(KEditBar()), "5. Klip: Unpin takes the edit bar out of the window");
                Click(zw.PlayButton, 1);   // pinned: offers Unpin
            },
            () => {
                var item = MenuItem("Unpin");
                Check(item != null && !Open(zw.Playback), "5. Zequence: a right-click on Play while pinned offers Unpin (no popover)");
                if (item != null) Click(item, 0);
            },
            () => { },
            () => {
                Check(!PinnableToolGroupTK.IsPinned(ZequenceEditorWindowTK.PlaybackPinKey) && ZCard() == null && ZInline() != null && ZInline().childCount == 0, "5. Zequence: Unpin empties Play's row and the row under it");
                var pin = PinIn(ZEditBar());
                if (pin != null) Click(pin, 0);
            },
            () => { },
            () => {
                Check(AllKeys(false) && NonePinnedLayout(), "5. Zequence: the edit bar's lit Pin unpins it (overview hidden too); none pinned. " + Layout());
                Click(zw.PlayButton, 1);
            },
            () => {
                Check(Open(zw.Playback), "5. unpinned, a right-click on Play opens the popover again");
                zw.Playback.ClosePopover();
                CloseBoth(); OpenBoth();
            },
            () => { },
            () => Check(AllKeys(false) && NonePinnedLayout(), "6. closed and opened again: all four stay unpinned. " + Layout()),
        };

        int frame = 0, step = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () => {
            frame++;
            zw?.Repaint(); kw?.Repaint();
            if (frame < 40 || frame % 12 != 0) return;
            bool stop = false;
            try { steps[step](); }
            catch (Exception e) { Check(false, "step " + (step + 1) + " threw: " + e); stop = true; }
            step++;
            if (!stop && step < steps.Count) return;
            EditorApplication.update -= tick;
            try { zw?.Playback?.ClosePopover(); zw?.EditTools?.ClosePopover(); kw?.Playback?.ClosePopover(); kw?.Waveform?.EditTools?.ClosePopover(); } catch { }
            CloseBoth();
            try { Undo.RevertAllDownToGroup(startGroup); } catch (Exception e) { Debug.LogException(e); }
            for (int i = 0; i < Keys.Length; i++) PinnableToolGroupTK.SetPinnedQuietly(Keys[i], keep[i]);
            EditorPrefs.SetBool(AutoTidyKey, keepAutoTidy);
            ZoundsProject.Instance.zoundLibrary.zequences.RemoveAll(z => z.id == -9800);
            ZoundEngine.InvalidateLookups();
            if (projectBytes != null) {
                var now = File.ReadAllBytes(ZoundsProtection.Absolute(projectJson));
                bool same = now.Length == projectBytes.Length;
                for (int i = 0; same && i < now.Length; i++) same = now[i] == projectBytes[i];
                if (!same) {
                    ZoundsWindow.isSavingJSON = true;
                    try { File.WriteAllBytes(ZoundsProtection.Absolute(projectJson), projectBytes); AssetDatabase.ImportAsset(projectJson); }
                    finally { ZoundsWindow.isSavingJSON = false; }
                    sb.Append("  (the project file had been rewritten during the check; written back as it was)\n");
                }
            }
            done(LastReport = (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - pinnable tool groups (playback options, edit tools; Klip and Zequence editors)\n" + sb);
        };
        EditorApplication.update += tick;
    }
}

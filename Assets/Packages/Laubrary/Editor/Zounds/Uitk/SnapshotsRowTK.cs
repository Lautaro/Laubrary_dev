using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A Zound's snapshots (T-0498), as one row: Default (the settings as authored) and each saved snapshot as a chip, then
    /// Capture and the glide time. Clicking a chip glides every play of this sound in the editor to it over the glide time,
    /// so a snapshot can be heard the way game code will use it; the chip being glided to fills as the glide goes.
    /// Right-click a chip to load it into the editor (to edit it), capture it again from the current settings, rename or
    /// delete it. Every change to the saved data is one Undo step.
    /// </summary>
    public class SnapshotsRowTK : VisualElement {

        const float RowH = 20f;
        readonly Zound zound;
        readonly Action<string, Action> modify;   // (undo name, change): the window's Undo-recording edit
        readonly List<(string name, VisualElement chip, VisualElement fill)> chips = new List<(string, VisualElement, VisualElement)>();
        string builtSig;
        static float glideMs = 800f;

        public SnapshotsRowTK(Zound zound, Action<string, Action> modify) {
            this.zound = zound; this.modify = modify;
            AddToClassList("zs-snapshots-row__root");
            schedule.Execute(Tick).Every(33);
        }

        string Signature() {
            var sb = new System.Text.StringBuilder();
            if (zound.snapshots != null) foreach (var s in zound.snapshots) sb.Append(s?.name).Append('|');
            return sb.ToString();
        }

        void Tick() {
            if (panel == null) return;
            var sig = Signature();
            if (sig != builtSig) { builtSig = sig; Build(); }
            // Progress of any glide under way on a play of this sound.
            string target = null; float progress = 0f; bool any = false;
            var tokens = ZoundEngine.LiveTokens;
            if (tokens != null) foreach (var t in tokens) {
                if (t == null || !ReferenceEquals(t.zound, zound) || !t.isRunning) continue;
                if (t.TryGetGlideProgress(out var s, out var p)) { target = s; progress = p; any = true; }
            }
            foreach (var c in chips) {
                bool on = any && ZpocKeys.Same(c.name, target ?? "");
                c.fill.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (on) c.fill.style.width = Length.Percent(progress * 100f);
                c.chip.EnableInClassList("zs-snapchip--target", on);
            }
        }

        void Build() {
            Clear(); chips.Clear();
            var title = new Label("Snapshots") {
                tooltip = "Named sets of this sound's settings. Game code glides a playing sound to one over a time it chooses (token.GlideToSnapshot(\"name\", ms)), from wherever the sound is. Click one to hear that here: every play of this sound glides to it. Right-click one to load it into the editor, capture it again, rename or delete it. Right-click this title for the whole list, to glide to any of them."
            };
            title.AddToClassList("zs-guilabel");
            title.AddToClassList("zs-snapshots-row__title");
            // Right-click the title: every snapshot, for when there are more chips than the strip can show.
            title.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 1) return;
                var items = new List<ZUI.ZUIMenuItem> { ZUI.MenuItem(ZoundSnapshots.DefaultName, () => GlideAll(ZoundSnapshots.DefaultName)) };
                if (zound.snapshots != null) foreach (var sn in zound.snapshots) if (sn != null) { string n = sn.name; items.Add(ZUI.MenuItem(n, () => GlideAll(n))); }
                ZUI.ContextMenu(items.ToArray());
                e.StopPropagation();
            });
            Add(title);
            // Capture and the glide time come first and never move; the chips, whose number varies, go last in a strip
            // of their own that clips at the pane's edge (their full list is also on the title's right-click).
            Add(ZS.Button("Capture", "Saves the settings as they are now as a new snapshot (effects, their on/off, modifiers and their strengths, where each ZPOC rests, and the volume and pitch ranges).", "RichButton",
                () => modify("capture snapshot", () => {
                    if (zound.snapshots == null) zound.snapshots = new List<ZoundSnapshot>();
                    zound.snapshots.Add(ZoundSnapshots.Capture(zound, UniqueName("Snapshot")));
                }), ZUICornerMask.All, 64f, RowH - 2f));
            Add(Gap(6f));
            ZuiSkinSlider glide = null;
            glide = ZS.Slider("Glide " + glideMs.ToString("0") + " ms", glideMs, 0f, 5000f,
                "How long the glide takes when you click a snapshot here, to hear it. Game code chooses its own time.",
                v => { glideMs = Mathf.Round(v / 10f) * 10f; glide.text = "Glide " + glideMs.ToString("0") + " ms"; }, ZuiSkinSlider.LabelMode.LabelOnly, 800f, "Default", 130f, RowH - 2f);
            Add(glide);
            Add(Gap(8f));
            strip = new VisualElement();
            strip.AddToClassList("zs-snapshots-row__strip");
            strip.AddToClassList("zs-snapshots-row__strip");
            Add(strip);
            AddChip(ZoundSnapshots.DefaultName, null);
            if (zound.snapshots != null) foreach (var s in zound.snapshots) if (s != null) AddChip(s.name, s);
        }

        VisualElement strip;

        /// <summary>Why a snapshot cannot take this name (empty, "Default", or another of this sound's snapshots), or null.</summary>
        string RenameProblem(string n, ZoundSnapshot self) {
            if (string.IsNullOrWhiteSpace(n)) return "A snapshot needs a name.";
            if (ZpocKeys.Same(n, ZoundSnapshots.DefaultName)) return "\"Default\" is the sound as authored; a snapshot cannot take that name.";
            if (zound.snapshots != null) foreach (var s in zound.snapshots)
                if (s != null && s != self && ZpocKeys.Same(s.name, n)) return "Another snapshot of this sound is already called that (names are matched ignoring case and spaces).";
            return null;
        }

        string UniqueName(string stem) {
            for (int i = 1; ; i++) {
                string n = stem + " " + i;
                if (ZoundSnapshots.Find(zound, n) == null || ZpocKeys.Same(n, ZoundSnapshots.DefaultName)) return n;
            }
        }

        void AddChip(string name, ZoundSnapshot snapshot) {
            var chip = new VisualElement();
            chip.AddToClassList("zs-snapchip");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("zs-snapchip__fill");
            var text = new Label(name) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("zs-snapchip__text");
            text.AddToClassList("zs-snapshots-row__add-chip-text");
            chip.Add(fill); chip.Add(text);
            float w = Mathf.Clamp(24f + name.Length * 6.5f, 56f, 140f);
            chip.style.width = w; chip.AddToClassList("zs-snapshots-row__snapshot-chip");
            chip.tooltip = snapshot == null
                ? "Default: the settings as they are in the editor. Click: every play of this sound glides back to them. Right-click: capture them as a new snapshot."
                : "'" + name + "'. Click: every play of this sound here glides to it over the glide time, as game code would. Right-click: load it into the editor to edit it, capture it again from the current settings, rename or delete it.";
            chip.RegisterCallback<PointerDownEvent>(e => {
                if (e.button == 0) { GlideAll(name); e.StopPropagation(); }
                else if (e.button == 1) { Menu(name, snapshot, chip); e.StopPropagation(); }
            });
            strip.Add(chip);
            chips.Add((name, chip, fill));
        }

        void GlideAll(string name) {
            var tokens = ZoundEngine.LiveTokens;
            if (tokens == null) return;
            foreach (var t in tokens)
                if (t != null && ReferenceEquals(t.zound, zound) && t.isRunning && !t.isChildZound) t.GlideToSnapshot(name, glideMs);
        }

        void Menu(string name, ZoundSnapshot snapshot, VisualElement chip) {
            var items = new List<ZUI.ZUIMenuItem>();
            if (snapshot == null) {
                items.Add(ZUI.MenuItem("Capture as new", () => modify("capture snapshot", () => {
                    if (zound.snapshots == null) zound.snapshots = new List<ZoundSnapshot>();
                    zound.snapshots.Add(ZoundSnapshots.Capture(zound, UniqueName("Snapshot")));
                })));
            }
            else {
                items.Add(ZUI.MenuItem("Load into editor", () => modify("load snapshot", () => LoadIntoEditor(snapshot))));
                items.Add(ZUI.MenuItem("Capture again", () => modify("capture snapshot again", () => {
                    var fresh = ZoundSnapshots.Capture(zound, snapshot.name);
                    snapshot.values = fresh.values;
                })));
                items.Add(ZUI.MenuItem("Rename…", () => NamePopup.Show(chip.worldBound, "Name", snapshot.name,
                    "What game code calls this snapshot: token.GlideToSnapshot(\"name\", ms). Matched the way Zound names are. It only has to be unique within this sound.",
                    n => modify("rename snapshot", () => snapshot.name = n.Trim()),
                    n => RenameProblem(n, snapshot))));
                items.Add(ZUI.MenuItem("Delete", () => modify("delete snapshot", () => zound.snapshots.Remove(snapshot))));
            }
            ZUI.ContextMenu(items.ToArray());
        }

        /// <summary>Writes a snapshot's values into the sound's settings, so it can be edited and captured again.</summary>
        void LoadIntoEditor(ZoundSnapshot s) {
            var chain = Dsp.ZoundDspPlayback.ResolveChain(zound, out _);
            foreach (var v in s.values) {
                switch (v.kind) {
                    case SnapshotValueKind.EffectParam: {
                        int n = ZoundSnapshots.NodeIndex(chain, v.node);
                        if (n < 0) break;
                        chain.nodes[n].EnsureParams();
                        if (v.param >= 0 && v.param < chain.nodes[n].p.Length) chain.nodes[n].p[v.param] = v.a;
                        break;
                    }
                    case SnapshotValueKind.EffectOn: { int n = ZoundSnapshots.NodeIndex(chain, v.node); if (n >= 0) chain.nodes[n].enabled = v.a >= 0.5f; break; }
                    case SnapshotValueKind.ModifierParam: {
                        int m = ZoundSnapshots.ModifierIndex(chain, v.mod);
                        if (m < 0) break;
                        chain.modifiers[m].EnsureParams();
                        if (v.param >= 0 && v.param < chain.modifiers[m].p.Length) chain.modifiers[m].p[v.param] = v.a;
                        break;
                    }
                    case SnapshotValueKind.ZpocRest: { int m = ZoundSnapshots.ModifierIndex(chain, v.mod); if (m >= 0) chain.modifiers[m].zpocRest = v.a; break; }
                    case SnapshotValueKind.BindingDepth: {
                        int m = ZoundSnapshots.ModifierIndex(chain, v.mod);
                        int tn = string.IsNullOrEmpty(v.node) ? -1 : ZoundSnapshots.NodeIndex(chain, v.node);
                        foreach (var b in chain.bindings) if (b.modifierIndex == m && b.nodeIndex == tn && b.paramIndex == v.param) b.depth = v.a;
                        break;
                    }
                    case SnapshotValueKind.VolumeRange: zound.minVolume = v.a; zound.maxVolume = v.b; break;
                    case SnapshotValueKind.PitchRange: zound.minPitch = v.a; zound.maxPitch = v.b; break;
                }
            }
            chain?.Touch();
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.AddToClassList("zs-snapshots-row__gap"); return e; }
    }

    /// <summary>A one-field popover for naming something where it is declared. Changes apply on Enter or when it closes.</summary>
    public class NamePopup : PopupWindowContent {
        readonly string label, value, tooltip; readonly Action<string> apply; readonly Func<string, string> problem;
        TextField field; Label warn; bool cancelled;
        NamePopup(string label, string value, string tooltip, Action<string> apply, Func<string, string> problem) {
            this.label = label; this.value = value; this.tooltip = tooltip; this.apply = apply; this.problem = problem;
        }
        /// <summary><paramref name="problem"/> (optional) returns why a name cannot be used, or null; such a name is not applied.</summary>
        public static void Show(Rect anchor, string label, string value, string tooltip, Action<string> apply, Func<string, string> problem = null) =>
            UnityEditor.PopupWindow.Show(anchor, new NamePopup(label, value, tooltip, apply, problem));
        public override Vector2 GetWindowSize() => new Vector2(256f, 32f);
        public override void OnGUI(Rect rect) { }
        public override void OnOpen() {
            var root = editorWindow.rootVisualElement;
            ZS.Attach(root);
            root.AddToClassList("zs-snapshots-row__popup-root");
            var l = new Label(label) { tooltip = tooltip }; l.AddToClassList("zs-guilabel"); l.AddToClassList("zs-snapshots-row__popup-label");
            field = new TextField { value = value, tooltip = tooltip }; field.AddToClassList("zs-namefield");
            field.AddToClassList("zs-snapshots-row__popup-field");
            field.RegisterCallback<KeyDownEvent>(e => {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) editorWindow.Close();
                else if (e.keyCode == KeyCode.Escape) { cancelled = true; editorWindow.Close(); }
            });
            // The warning's space is always reserved, so it appearing moves nothing.
            warn = new Label("⚠"); warn.AddToClassList("zs-lbl"); warn.AddToClassList("zs-snapshots-row__popup-warn");
            warn.style.visibility = Visibility.Hidden;
            field.RegisterValueChangedCallback(e => {
                string why = problem?.Invoke(e.newValue);
                warn.style.visibility = why != null ? Visibility.Visible : Visibility.Hidden;
                warn.tooltip = why ?? "";
            });
            root.Add(l); root.Add(field); root.Add(warn);
            field.schedule.Execute(() => { field.Focus(); field.SelectAll(); });
        }
        /// <summary>Applies on Enter or on clicking away; Escape, and a name that cannot be used, leave the old name.</summary>
        public override void OnClose() {
            if (cancelled || field == null || field.value == value) return;
            if (problem?.Invoke(field.value) != null) return;
            apply?.Invoke(field.value);
        }
    }
}

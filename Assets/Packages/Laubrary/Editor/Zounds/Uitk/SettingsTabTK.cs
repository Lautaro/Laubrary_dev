using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the Zounds window's Settings tab (T-0470): the workspace folders, this machine's external
    /// audio source root, the engine settings, themes, the editor's visual style and the operational settings. The fields
    /// are UI Toolkit's own editor fields (the ones the old tab's EditorGUILayout calls draw), each writing the same
    /// serialized property; an edit marks the project unsaved exactly as the old window's apply does.
    /// </summary>
    internal class SettingsTabTK : VisualElement {

        readonly SerializedObject so;
        readonly List<Action> syncers = new List<Action>();
        string[] themes;
        VisualElement themeRow;

        public SettingsTabTK() {
            AddToClassList("zs-settings__root");
            so = new SerializedObject(ZoundsProject.Instance);
            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.AddToClassList("zs-settings__box");
            Add(box);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-settingsscroll");
            scroll.AddToClassList("zs-settings__scroll");
            box.Add(scroll);
            Build(scroll);
        }

        SerializedProperty P(string path) => so.FindProperty("projectSettings." + path);

        void Apply() {
            if (so.ApplyModifiedProperties()) ZoundsWindow.SetZoundsProjectDirty();
        }

        // ─────────────────────────── field makers (EditorGUILayout equivalents) ───────────────────────────

        static Label Bold(string text) {
            var l = new Label(text);
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-bold"); l.AddToClassList("zs-settingsheader");
            return l;
        }

        // ── ZUI controls on a label column (2026-10-08): every control has its own width, nothing stretches across the pane ──

        /// <summary>A settings row: the label in the label column, then the control(s). Height matches the old 20 pt pitch.</summary>
        VisualElement Row(string label, string tooltip, float lw, params VisualElement[] controls) {
            var r = new VisualElement();
            r.AddToClassList("zs-settingsrow");
            r.AddToClassList("zs-settings__row");
            var l = new Label(label) { tooltip = tooltip };
            l.AddToClassList("zs-lbl");
            l.AddToClassList("zs-settings__row-label");
            l.style.width = lw;
            r.Add(l);
            foreach (var c in controls) { c.AddToClassList("zs-settings__row-control"); r.Add(c); }
            return r;
        }

        const float PathW = 360f, SliderW = 200f, NumberW = 60f;

        /// <summary>A project path: ZUI's text input, sized for a path, writing the serialized property (one undo step per edit).</summary>
        VisualElement Text(string label, string tooltip, string path, float lw) {
            var f = Z.TextInput(P(path).stringValue, tooltip, v => { so.Update(); P(path).stringValue = v; Apply(); Undo.SetCurrentGroupName("change " + label); }, PathW);
            f.AddToClassList("zs-settingsfield");
            syncers.Add(() => {
                if (f.focusController?.focusedElement is VisualElement x && (x == f || f.Contains(x))) return;
                string now = P(path).stringValue;
                if (f.value != now) f.SetValueWithoutNotify(now);
            });
            return Row(label, tooltip, lw, f);
        }

        /// <summary>A bounded setting: ZUI's micro slider (label and value inside the track), writing the serialized property.</summary>
        VisualElement Slider(string label, string tooltip, string path, float min, float max, float lw, int decimals = 2) {
            var s = Z.MicroSlider(label, P(path).floatValue, min, max, tooltip, v => { so.Update(); P(path).floatValue = v; Apply(); Undo.SetCurrentGroupName("change " + label); }, SliderW, decimals: decimals);
            s.AddToClassList("zs-settings__slider");
            syncers.Add(() => { float now = P(path).floatValue; if (!Mathf.Approximately(s.value, now)) s.value = now; });
            return Row(label, tooltip, lw, s);
        }

        /// <summary>An unbounded number (no honest cap): ZUI's scrub-draggable float field.</summary>
        VisualElement Number(string label, string tooltip, string path, float lw, Func<float, float> clamp, int decimals) {
            var f = Z.Float(P(path).floatValue, tooltip, v => { so.Update(); P(path).floatValue = clamp(v); Apply(); Undo.SetCurrentGroupName("change " + label); }, NumberW, decimals);
            f.AddToClassList("zs-settingsfield");
            syncers.Add(() => {
                if (f.focusController?.focusedElement is VisualElement x && (x == f || f.Contains(x))) return;
                float now = P(path).floatValue;
                if (!Mathf.Approximately(f.value, now)) f.SetValueWithoutNotify(now);
            });
            return Row(label, tooltip, lw, f);
        }

        /// <summary>An unbounded whole number: ZUI's scrub-draggable integer field.</summary>
        VisualElement Count(string label, string tooltip, string path, float lw, Func<int, int> clamp) {
            var f = Z.Int(P(path).intValue, tooltip, v => { so.Update(); P(path).intValue = clamp(v); Apply(); Undo.SetCurrentGroupName("change " + label); }, NumberW);
            f.AddToClassList("zs-settingsfield");
            syncers.Add(() => {
                if (f.focusController?.focusedElement is VisualElement x && (x == f || f.Contains(x))) return;
                int now = P(path).intValue;
                if (f.value != now) f.SetValueWithoutNotify(now);
            });
            return Row(label, tooltip, lw, f);
        }

        /// <summary>A yes/no setting: ZUI's latching toggle button (never a checkbox).</summary>
        VisualElement Flag(string label, string tooltip, string path, float lw) {
            var t = Z.Toggle(label, tooltip, P(path).boolValue, v => { so.Update(); P(path).boolValue = v; Apply(); Undo.SetCurrentGroupName("change " + label); });
            t.AddToClassList("zs-settings__toggle");
            syncers.Add(() => { bool now = P(path).boolValue; if (t.value != now) t.SetValueWithoutNotify(now); });
            return Row(label, tooltip, lw, t);
        }

        /// <summary>A sheet-styled Zounds button at the settings row height.</summary>
        static Button Btn(string label, string tooltip, Action onClick, float width) {
            var b = ZS.Button(label, tooltip, "RichButton", onClick, ZUICornerMask.All, width, 18f);
            b.AddToClassList("zs-settings__button");
            return b;
        }

        // ── colours: ZUI's colour control (Z.Color), sized to its content, never stretched across the pane (2026-10-08) ──

        /// <summary>The colour control for one setting: ZUI's swatch with its eyedropper, alpha included (every colour here
        /// may be translucent), 110 px wide, writing the serialized property (one undo step per change).</summary>
        ColorField ColorControl(string path, string tooltip, string label) {
            var f = Z.Color(P(path).colorValue, tooltip, v => { so.Update(); P(path).colorValue = v; Apply(); Undo.SetCurrentGroupName("change " + label + " colour"); }, ColorW);
            f.AddToClassList("zs-settingsfield");
            f.AddToClassList("zs-settings__color-field");
            syncers.Add(() => {
                if (f.focusController?.focusedElement is VisualElement x && (x == f || f.Contains(x))) return;
                var now = P(path).colorValue;
                if (f.value != now) f.SetValueWithoutNotify(now);
            });
            return f;
        }

        /// <summary>
        /// The swatch alone hides a translucent colour: a 4 %-white lane reads as solid white, its alpha bar a hairline
        /// (PM, 2026-10-08). So a fixed-width readout beside every swatch says the opacity whenever it is not full.
        /// </summary>
        Label AlphaReadout(string path) {
            var l = new Label();
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-settings__alpha");
            void Sync() {
                float a = P(path).colorValue.a;
                l.text = a >= 0.995f ? "" : Mathf.RoundToInt(a * 100f) + "% opaque";
                l.tooltip = a >= 0.995f ? "" : "This colour is translucent: " + Mathf.RoundToInt(a * 100f) + " % opacity. What is behind it shows through; the swatch shows the colour at full strength.";
            }
            Sync();
            syncers.Add(Sync);
            return l;
        }

        const float ColorW = 110f;

        /// <summary>A colour setting on its own row: the label in the label column, then the swatch.</summary>
        VisualElement Color(string label, string path, float lw, string tooltip) {
            var r = new VisualElement();
            r.AddToClassList("zs-settingsrow");
            r.AddToClassList("zs-settings__color-row");
            var l = new Label(label) { tooltip = tooltip };
            l.AddToClassList("zs-lbl");
            l.AddToClassList("zs-settings__color-label");
            l.style.width = lw;
            r.Add(l);
            // The swatches of the group line up in one column: this row has no thickness box, so it leaves that box's room.
            var spacer = new VisualElement();
            spacer.AddToClassList("zs-settings__thickness-spacer");
            r.Add(spacer);
            r.Add(ColorControl(path, tooltip, label));
            r.Add(AlphaReadout(path));
            return r;
        }

        /// <summary>
        /// The one place an editor's background is painted from: the Settings tab's Editor Background when it has any
        /// opacity, else the skin's own box. Called when a window is built and on its tick, so a change here (or an undo
        /// of one) shows at once in every open Klip and Zequence editor.
        /// </summary>
        public static void ApplyEditorBackground(VisualElement box) {
            if (box == null || ZoundsProject.Instance == null) return;
            var c = ZoundsProject.Instance.projectSettings.editorStyle.editorBackgroundColor;
            if (c.a > 0f) {
                if (box.style.backgroundImage.keyword != StyleKeyword.None) box.style.backgroundImage = StyleKeyword.None;
                if (box.style.backgroundColor.keyword != StyleKeyword.Undefined || box.style.backgroundColor.value != c) box.style.backgroundColor = c;
            }
            else if (box.style.backgroundColor.keyword != StyleKeyword.Null || box.style.backgroundImage.keyword != StyleKeyword.Null) {
                box.style.backgroundImage = StyleKeyword.Null;
                box.style.backgroundColor = StyleKeyword.Null;
            }
        }

        // ─────────────────────────── the tab ───────────────────────────

        void Build(VisualElement into) {
            const float lw = 190f;   // one label column for the whole tab, wide enough for its longest label

            into.Add(Bold("Workspace Directories"));
            into.Add(Text("System Folder Path", "The project folder (under Assets) where Zounds keeps its system data.", "systemFolderPath", lw));
            into.Add(Text("Library Folder Path", "The project folder where library clips are stored. These are included in builds.", "libraryFolderPath", lw));
            into.Add(Text("Sources Folder Path", "The project folder where source clips are stored. Not included in builds unless referenced.", "sourcesFolderPath", lw));
            into.Add(Text("Themes Folder Path", "The project folder where UI themes are stored.", "themesFolderPath", lw));
            into.Add(ZequenceEditorWindowTK.Space(4f));

            into.Add(Bold("External Audio Sources (Local)"));
            into.Add(ExternalRoot(lw));
            into.Add(ZequenceEditorWindowTK.Space(10f));

            const float sw = lw;
            into.Add(Bold("Engine"));
            into.Add(Slider("Player Volume", "Master volume (0 to 1) while the game runs; entering play mode sets the master volume to it.", "playerVolume", 0f, 1f, sw));
            into.Add(Slider("System Volume Modifier", "A multiplier (0 to 1) on the master volume, for when the whole game needs to be quieter.", "systemVolumeModifier", 0f, 1f, sw));
            into.Add(Slider("Editor Volume", "Master volume (0 to 1) in edit mode; leaving play mode sets the master volume to it.", "editorVolume", 0f, 1f, sw));
            into.Add(Number("Cooldown Duration", "Seconds after a sound plays during which the same sound will not play again. Drag to scrub, or type.", "cooldownDuration", sw, v => v < 0f ? 0f : v, 2));
            into.Add(Count("Max Played Zound Instances", "When more sounds than this are playing and a sound in a culling group triggers, it plays and the one playing longest is culled. Drag to scrub, or type.", "maxPlayedZoundInstances", sw, v => v < 1 ? 1 : v));
            into.Add(Slider("Cull Fade Duration", "Seconds a culled sound takes to fade out.", "cullFadeDuration", 0f, 0.5f, sw));
            into.Add(ZequenceEditorWindowTK.Space(10f));

            into.Add(Bold("Themes"));
            themeRow = new VisualElement();
            into.Add(themeRow);
            BuildThemeRow(sw);
            into.Add(ZequenceEditorWindowTK.Space(10f));

            into.Add(Bold("Editor Style (Visual)"));
            into.Add(ThicknessColor("Player Head", "editorStyle.playerHeadThickness", "editorStyle.playerHeadColor", sw, "The line that shows where a play is, over the waveform: its thickness in pixels and its colour."));
            into.Add(ThicknessColor("Volume Envelope", "editorStyle.volumeEnvelopeThickness", "editorStyle.volumeEnvelopeColor", sw, "The sound's own volume curve, drawn over its waveform and on the curve bar's Vol chips: thickness in pixels and colour."));
            into.Add(ThicknessColor("Pitch Envelope", "editorStyle.pitchEnvelopeThickness", "editorStyle.pitchEnvelopeColor", sw, "The sound's own pitch curve, drawn over its waveform and on the curve bar's Pitch chips: thickness in pixels and colour."));
            into.Add(ThicknessColor("Trim Handle", "editorStyle.trimHandleThickness", "editorStyle.trimHandleColor", sw, "The two handles that mark the trimmed part of a source, and the curve bar's Trim chip: thickness in pixels and colour."));
            into.Add(Color("Waveform", "editorStyle.waveformColor", sw, "The colour the audio's waveform is drawn in."));
            into.Add(Color("Waveform Background", "editorStyle.klipWaveformBGColor", sw, "The colour behind the waveform in the Klip editor and in a Zequence track's piece."));
            into.Add(Color("Trim Area", "editorStyle.trimAreaColor", sw, "The shade over the part of the source outside the trim (translucent: the waveform shows through)."));
            into.Add(Color("Selected Curve Line", "editorStyle.selectedEnvelopeLineColor", sw, "The colour of a curve's line while it is selected for editing."));
            into.Add(Color("Selected Curve Handle", "editorStyle.selectedEnvelopeHandleColor", sw, "The colour of a curve's points while it is selected for editing."));
            // The editors' backgrounds (owner, 2026-10-08). Alpha counts: a fully transparent editor background keeps the skin's box.
            into.Add(Color("Editor Background", "editorStyle.editorBackgroundColor", sw, "Behind a Klip or Zequence editor's content. Fully transparent keeps the skin's own box."));
            into.Add(Color("Track Background", "editorStyle.trackBackgroundColor", sw, "The band behind every other track in a Zequence."));
            into.Add(Color("Track Background (alternate)", "editorStyle.trackAltBackgroundColor", sw, "The band behind the tracks in between."));
            into.Add(Color("Track Lane", "editorStyle.trackLaneColor", sw, "The lane a track's audio is drawn in, outside the piece that plays."));
            into.Add(ZequenceEditorWindowTK.Space(10f));

            into.Add(Bold("Operational Settings"));
            into.Add(Flag("Auto Render", "On: a sound that needs a rendered file (one whose source cannot go through the chain) is rendered when the project is saved. Off: render it yourself from its editor.", "editorStyle.autoRender", sw));
            into.Add(Slider("Envelope Handle Size", "The size, in pixels, of the points on a curve being edited (1 to 10).", "editorStyle.envelopeHandleSize", 1f, 10f, sw, 1));
            into.Add(ProtectedEdits(sw));
        }

        /// <summary>
        /// Destructive editing (2026-10-09): how an edit that has to go to a copy is announced -- a shared sound edited from
        /// an editor, or an audio file Zounds never overwrites. A three-way choice, so a segmented control; one undo step.
        /// </summary>
        VisualElement ProtectedEdits(float lw) {
            const string path = "protectedEditPrompt";
            const string tip = "When an edit would change a sound other sounds use (its trim, curves or audio), or an audio file Zounds must not overwrite (a source, library or outside file, or one several sounds play), the edit goes to a copy. This decides how you hear about it.";
            var values = new[] { ZoundsProject.ProjectSettings.ProtectedEditPrompt.Ask, ZoundsProject.ProjectSettings.ProtectedEditPrompt.Notice, ZoundsProject.ProjectSettings.ProtectedEditPrompt.Silent };
            var seg = Z.Segmented(Array.IndexOf(values, (ZoundsProject.ProjectSettings.ProtectedEditPrompt)P(path).intValue),
                new[] { "Ask first", "Tell me", "Silent" }, tip, i => {
                    if (i < 0 || i >= values.Length) return;
                    so.Update(); P(path).intValue = (int)values[i]; Apply(); Undo.SetCurrentGroupName("change protected edits");
                });
            seg.AddToClassList("zs-settings__protected-edits");
            seg.SegmentAt(0).tooltip = "Ask first: a dialog before the copy is made -- edit a copy, edit the original instead (not offered for a file Zounds never overwrites), or cancel.";
            seg.SegmentAt(1).tooltip = "Tell me (the default): the copy is made, and a notice in the editor says so, with Edit the original instead.";
            seg.SegmentAt(2).tooltip = "Silent: the copy is made without a word; the editor's title and the source row's badge still show which sound and file you are editing.";
            int shown = -2;
            syncers.Add(() => { int now = Array.IndexOf(values, (ZoundsProject.ProjectSettings.ProtectedEditPrompt)P(path).intValue); if (now != shown) { shown = now; seg.SetOn(i => i == now); } });
            return Row("Protected edits", tip, lw, seg);
        }

        /// <summary>A drawn line's row: the label in the label column, its thickness in a 45 px scrub-draggable number box, then its colour.</summary>
        VisualElement ThicknessColor(string label, string thicknessPath, string colorPath, float lw, string tooltip) {
            var r = new VisualElement();
            r.AddToClassList("zs-settingsrow");
            r.AddToClassList("zs-settings__thickness-color-row");
            var l = new Label(label) { tooltip = tooltip };
            l.AddToClassList("zs-lbl");
            l.AddToClassList("zs-settings__color-label");
            l.style.width = lw;
            r.Add(l);
            var t = Z.Float(P(thicknessPath).floatValue, "Thickness in pixels. Drag to scrub, or type.", v => { so.Update(); P(thicknessPath).floatValue = Mathf.Max(0f, v); Apply(); Undo.SetCurrentGroupName("change " + label + " thickness"); }, 45f, 1);
            t.AddToClassList("zs-settingsfield");
            t.AddToClassList("zs-settings__thickness-field");
            syncers.Add(() => {
                if (t.focusController?.focusedElement is VisualElement x && (x == t || t.Contains(x))) return;
                float now = P(thicknessPath).floatValue;
                if (t.value != now) t.SetValueWithoutNotify(now);
            });
            r.Add(t);
            r.Add(ColorControl(colorPath, tooltip, label));
            r.Add(AlphaReadout(colorPath));
            return r;
        }

        /// <summary>This machine's external source root (EditorPrefs, not the project): shown read-only, Browse, Clear, and a
        /// status line ("OK", or a warning box when the folder is not there).</summary>
        /// <summary>This machine's external source root (EditorPrefs, not the project): shown read-only, Browse, Clear, and the
        /// status ("OK", or a warning) inline on the same row, never a row of its own.</summary>
        VisualElement ExternalRoot(float lw) {
            const string tip = "A folder on this machine (not in the project) that Klips may take their audio from directly. Kept per machine, not saved with the project.";
            var path = Z.TextInput("", tip, null, PathW);
            path.isReadOnly = true;
            path.AddToClassList("zs-settingsfield");
            path.AddToClassList("zs-settings__external-root-path");
            var browse = Btn("Browse…", "Pick the external source root folder.", () => {
                string start = ProjectSettingsTab.ExternalSourceRoot;
                EditorApplication.delayCall += () => {
                    string selected = EditorUtility.OpenFolderPanel("Select External Audio Source Root", start, "");
                    if (!string.IsNullOrEmpty(selected)) ProjectSettingsTab.ExternalSourceRoot = selected;
                };
            }, 70f);
            var clear = Btn("Clear", "Forget the external source root on this machine.", () => ProjectSettingsTab.ExternalSourceRoot = "", 50f);
            var status = new Label();
            status.AddToClassList("zs-lbl"); status.AddToClassList("zs-settingsstatus"); status.AddToClassList("zs-settings__status");
            var r = Row("External Source Root", tip, lw, path, browse, clear, status);
            void Sync() {
                string root = ProjectSettingsTab.ExternalSourceRoot;
                bool has = !string.IsNullOrEmpty(root);
                bool exists = has && Directory.Exists(root);
                path.SetValueWithoutNotify(has ? root : "(not set)");
                path.EnableInClassList("zs-settingspath--missing", has && !exists);
                clear.SetEnabled(has);
                status.text = !has ? "" : exists ? "OK" : "Path not found on this machine.";
                status.tooltip = status.text;
                status.EnableInClassList("zs-settingsstatus--warn", has && !exists);
            }
            Sync();
            syncers.Add(Sync);
            return r;
        }

        /// <summary>The themes row: ZUI's dropdown over the theme files (a list that changes, so not radios), Refresh, and
        /// Save as theme… (a short label; the full sentence is its tooltip).</summary>
        void BuildThemeRow(float lw) {
            themeRow.Clear();
            themes = ProjectSettingsTab.ThemeNames();
            var names = new List<string>(themes);
            VisualElement pick;
            if (names.Count == 0) {
                var none = new Label("None saved") { tooltip = "No theme file in the themes folder yet. Save as theme… writes one from the current style." };
                none.AddToClassList("zs-lbl"); none.AddToClassList("zs-subtle"); none.AddToClassList("zs-settings__theme-none");
                pick = none;
            }
            else {
                var d = Z.Dropdown(-1, names, "Apply a saved theme to the editor style (one undo step).", i => { if (i >= 0 && i < names.Count) ProjectSettingsTab.ApplyTheme(names[i]); }, 220f);
                d.SetValueWithoutNotify(null);
                d.AddToClassList("zs-settingsfield"); d.AddToClassList("zs-settings__theme-dropdown");
                pick = d;
            }
            var refresh = Btn("Refresh", "Re-read the themes folder.", () => BuildThemeRow(lw), 70f);
            var save = Btn("Save as theme…", "Saves the current editor style as a new theme file in the themes folder.", () => { if (ProjectSettingsTab.SaveCurrentStyleAsTheme()) BuildThemeRow(lw); }, 110f);
            themeRow.Add(Row("Theme", "Saved editor styles (colours and line widths) you can switch between.", lw, pick, refresh, save));
        }

        public void Tick() {
            so.Update();
            foreach (var s in syncers) s();
        }
    }
}

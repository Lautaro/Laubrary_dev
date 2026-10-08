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

        T Field<T, TV>(T field, string path, float labelWidth, Func<SerializedProperty, TV> get, Action<SerializedProperty, TV> set) where T : BaseField<TV> {
            field.AddToClassList("zs-settingsfield");
            if (labelWidth > 0f) field.labelElement.style.minWidth = field.labelElement.style.width = labelWidth - 3f;
            field.SetValueWithoutNotify(get(P(path)));
            field.RegisterValueChangedCallback(e => { so.Update(); set(P(path), e.newValue); Apply(); });
            syncers.Add(() => {
                if (field.focusController?.focusedElement is VisualElement f && (f == field || field.Contains(f))) return;
                field.SetValueWithoutNotify(get(P(path)));
            });
            return field;
        }

        TextField Text(string label, string tooltip, string path, float lw) =>
            Field<TextField, string>(new TextField(label) { tooltip = tooltip, isDelayed = false }, path, lw, p => p.stringValue, (p, v) => p.stringValue = v);

        Slider Slider(string label, string tooltip, string path, float min, float max, float lw) {
            var s = Field<Slider, float>(new Slider(label, min, max) { tooltip = tooltip, showInputField = true }, path, lw, p => p.floatValue, (p, v) => p.floatValue = v);
            s.AddToClassList("zs-imgui-slider");
            return s;
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
            const float lw = 150f;

            into.Add(Bold("Workspace Directories"));
            into.Add(Text("System Folder Path", "Set the path for a folder under Resources where system data is stored.", "systemFolderPath", lw));
            into.Add(Text("Library Folder Path", "Path where library clips are stored. These are included in builds.", "libraryFolderPath", lw));
            into.Add(Text("Sources Folder Path", "Path where source clips are stored. Not included in builds unless referenced.", "sourcesFolderPath", lw));
            into.Add(Text("Themes Folder Path", "Path where UI themes are stored.", "themesFolderPath", lw));
            into.Add(ZequenceEditorWindowTK.Space(4f));

            into.Add(Bold("External Audio Sources (Local)"));
            into.Add(ExternalRoot(lw));
            into.Add(ZequenceEditorWindowTK.Space(10f));

            into.Add(Bold("Engine"));
            into.Add(Slider("Player Volume", "Master volume when the game is running. When switching to play mode, this value goes to the master volume.", "playerVolume", 0f, 1f, lw));
            into.Add(Slider("System Volume Modifier", "Modifier for the master volume. This is just used if there is a need to modify the overall volume for the game for any reason.", "systemVolumeModifier", 0f, 1f, lw));
            into.Add(Slider("Editor Volume", "Master volume when in edit mode. When switching to edit mode, this value goes to the master volume.", "editorVolume", 0f, 1f, lw));
            into.Add(Field<FloatField, float>(new FloatField("Cooldown Duration") { tooltip = " A timer for a Zound that prohibits the same Zound to be played again before the timer runs out." },
                "cooldownDuration", 170f, p => p.floatValue, (p, v) => p.floatValue = v < 0f ? 0f : v));
            into.Add(Field<IntegerField, int>(new IntegerField("Max Played Zound Instances") { tooltip = "If the number of Zounds playing is more than this threshold, when a Zound in a culling group triggers, then it will play, but the one that has been playing for the longest will be culled." },
                "maxPlayedZoundInstances", 170f, p => p.intValue, (p, v) => p.intValue = v < 0 ? 1 : v));
            into.Add(Slider("Cull Fade Duration", "Fade duration to kill a zound when Max Played Zound Instances is reached.", "cullFadeDuration", 0f, 0.5f, 170f));
            into.Add(ZequenceEditorWindowTK.Space(10f));

            into.Add(Bold("Themes"));
            themeRow = new VisualElement();
            into.Add(themeRow);
            BuildThemeRow(lw);
            var save = new Button(() => { if (ProjectSettingsTab.SaveCurrentStyleAsTheme()) BuildThemeRow(lw); }) { text = "Save Current Style as New Theme" };
            save.AddToClassList("zs-imgui-button"); save.AddToClassList("zs-settingsbutton");
            into.Add(save);
            into.Add(ZequenceEditorWindowTK.Space(10f));

            const float sw = 190f;
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
            into.Add(Field<Toggle, bool>(new Toggle(P("editorStyle.autoRender").displayName), "editorStyle.autoRender", sw, p => p.boolValue, (p, v) => p.boolValue = v));
            into.Add(Slider("Envelope Handle Size", "", "editorStyle.envelopeHandleSize", 1f, 10f, sw));
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
            return r;
        }

        /// <summary>This machine's external source root (EditorPrefs, not the project): shown read-only, Browse, Clear, and a
        /// status line ("OK", or a warning box when the folder is not there).</summary>
        VisualElement ExternalRoot(float lw) {
            var host = new VisualElement();
            var r = new VisualElement();
            r.AddToClassList("zs-settingsrow");
            r.AddToClassList("zs-settings__external-root-row");
            var l = new Label("External Source Root");
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-settings__external-root-label");
            r.Add(l);
            var path = new TextField { isReadOnly = true };
            path.AddToClassList("zs-settingsfield");
            path.AddToClassList("zs-settings__external-root-path");
            r.Add(path);
            var browse = new Button(() => {
                string start = ProjectSettingsTab.ExternalSourceRoot;
                EditorApplication.delayCall += () => {
                    string selected = EditorUtility.OpenFolderPanel("Select External Audio Source Root", start, "");
                    if (!string.IsNullOrEmpty(selected)) ProjectSettingsTab.ExternalSourceRoot = selected;
                };
            }) { text = "Browse" };
            browse.AddToClassList("zs-imgui-button"); browse.AddToClassList("zs-settings__external-root-browse");
            r.Add(browse);
            var clear = new Button(() => ProjectSettingsTab.ExternalSourceRoot = "") { text = "Clear" };
            clear.AddToClassList("zs-imgui-button"); clear.AddToClassList("zs-settings__external-root-clear");
            r.Add(clear);
            host.Add(r);
            var status = new Label();
            status.AddToClassList("zs-lbl"); status.AddToClassList("zs-settingsstatus");
            host.Add(status);
            void Sync() {
                string root = ProjectSettingsTab.ExternalSourceRoot;
                bool has = !string.IsNullOrEmpty(root);
                bool exists = has && Directory.Exists(root);
                path.SetValueWithoutNotify(has ? root : "(not set)");
                path.EnableInClassList("zs-settingspath--missing", has && !exists);
                clear.SetEnabled(has);
                status.text = !has ? "" : exists ? "OK" : "Path not found on this machine.";
                status.EnableInClassList("zs-settingsstatus--warn", has && !exists);
            }
            Sync();
            syncers.Add(Sync);
            return host;
        }

        void BuildThemeRow(float lw) {
            themeRow.Clear();
            themes = ProjectSettingsTab.ThemeNames();
            var r = new VisualElement();
            r.AddToClassList("zs-settings__theme-row-row");
            var names = new List<string>(themes);
            var popup = new PopupField<string>("Available Themes", names, -1);
            popup.AddToClassList("zs-settingsfield");
            popup.labelElement.AddToClassList("zs-settings__theme-row-popup-label-element");
            popup.AddToClassList("zs-settings__theme-row-popup");
            popup.RegisterValueChangedCallback(e => { if (!string.IsNullOrEmpty(e.newValue)) ProjectSettingsTab.ApplyTheme(e.newValue); });
            r.Add(popup);
            var refresh = new Button(() => BuildThemeRow(lw)) { text = "Refresh" };
            refresh.AddToClassList("zs-imgui-button"); refresh.AddToClassList("zs-settings__theme-row-refresh");
            r.Add(refresh);
            themeRow.Add(r);
        }

        public void Tick() {
            so.Update();
            foreach (var s in syncers) s();
        }
    }
}

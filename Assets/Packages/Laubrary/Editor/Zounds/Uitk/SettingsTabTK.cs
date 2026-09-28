using System;
using System.Collections.Generic;
using System.IO;
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
            style.flexGrow = 1; style.flexShrink = 1;
            so = new SerializedObject(ZoundsProject.Instance);
            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.style.flexGrow = 1; box.style.flexShrink = 1;
            Add(box);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-settingsscroll");
            scroll.style.flexGrow = 1; scroll.style.flexShrink = 1;
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

        ColorField Color(string label, string path, float lw) =>
            Field<ColorField, Color>(new ColorField(label), path, lw, p => p.colorValue, (p, v) => p.colorValue = v);

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
            into.Add(ThicknessColor("Player Head", "editorStyle.playerHeadThickness", "editorStyle.playerHeadColor", sw));
            into.Add(ThicknessColor("Volume Envelope", "editorStyle.volumeEnvelopeThickness", "editorStyle.volumeEnvelopeColor", sw));
            into.Add(ThicknessColor("Pitch Envelope", "editorStyle.pitchEnvelopeThickness", "editorStyle.pitchEnvelopeColor", sw));
            into.Add(ThicknessColor("Trim Handle", "editorStyle.trimHandleThickness", "editorStyle.trimHandleColor", sw));
            into.Add(Color(P("editorStyle.waveformColor").displayName, "editorStyle.waveformColor", sw));
            into.Add(Color(P("editorStyle.klipWaveformBGColor").displayName, "editorStyle.klipWaveformBGColor", sw));
            into.Add(Color(P("editorStyle.trimAreaColor").displayName, "editorStyle.trimAreaColor", sw));
            into.Add(Color(P("editorStyle.selectedEnvelopeLineColor").displayName, "editorStyle.selectedEnvelopeLineColor", sw));
            into.Add(Color(P("editorStyle.selectedEnvelopeHandleColor").displayName, "editorStyle.selectedEnvelopeHandleColor", sw));
            into.Add(ZequenceEditorWindowTK.Space(10f));

            into.Add(Bold("Operational Settings"));
            into.Add(Field<Toggle, bool>(new Toggle(P("editorStyle.autoRender").displayName), "editorStyle.autoRender", sw, p => p.boolValue, (p, v) => p.boolValue = v));
            into.Add(Slider("Envelope Handle Size", "", "editorStyle.envelopeHandleSize", 1f, 10f, sw));
        }

        /// <summary>DrawThicknessColor: the label in the label column, the thickness in a 45 wide number box, then the colour.</summary>
        VisualElement ThicknessColor(string label, string thicknessPath, string colorPath, float lw) {
            var r = new VisualElement();
            r.AddToClassList("zs-settingsrow");
            r.style.flexDirection = FlexDirection.Row;
            var l = new Label(label);
            l.AddToClassList("zs-lbl");
            l.style.width = lw; l.style.flexShrink = 0;
            r.Add(l);
            var t = Field<FloatField, float>(new FloatField(), thicknessPath, 0f, p => p.floatValue, (p, v) => p.floatValue = v);
            t.style.width = 45f; t.style.flexShrink = 0;
            r.Add(t);
            var c = Field<ColorField, Color>(new ColorField(), colorPath, 0f, p => p.colorValue, (p, v) => p.colorValue = v);
            c.style.flexGrow = 1;
            r.Add(c);
            return r;
        }

        /// <summary>This machine's external source root (EditorPrefs, not the project): shown read-only, Browse, Clear, and a
        /// status line ("OK", or a warning box when the folder is not there).</summary>
        VisualElement ExternalRoot(float lw) {
            var host = new VisualElement();
            var r = new VisualElement();
            r.AddToClassList("zs-settingsrow");
            r.style.flexDirection = FlexDirection.Row;
            var l = new Label("External Source Root");
            l.AddToClassList("zs-lbl"); l.style.width = lw; l.style.flexShrink = 0;
            r.Add(l);
            var path = new TextField { isReadOnly = true };
            path.AddToClassList("zs-settingsfield");
            path.style.flexGrow = 1;
            r.Add(path);
            var browse = new Button(() => {
                string start = ProjectSettingsTab.ExternalSourceRoot;
                EditorApplication.delayCall += () => {
                    string selected = EditorUtility.OpenFolderPanel("Select External Audio Source Root", start, "");
                    if (!string.IsNullOrEmpty(selected)) ProjectSettingsTab.ExternalSourceRoot = selected;
                };
            }) { text = "Browse" };
            browse.AddToClassList("zs-imgui-button"); browse.style.width = 60f;
            r.Add(browse);
            var clear = new Button(() => ProjectSettingsTab.ExternalSourceRoot = "") { text = "Clear" };
            clear.AddToClassList("zs-imgui-button"); clear.style.width = 50f;
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
            r.style.flexDirection = FlexDirection.Row;
            var names = new List<string>(themes);
            var popup = new PopupField<string>("Available Themes", names, -1);
            popup.AddToClassList("zs-settingsfield");
            popup.labelElement.style.minWidth = popup.labelElement.style.width = lw - 3f;
            popup.style.flexGrow = 1;
            popup.RegisterValueChangedCallback(e => { if (!string.IsNullOrEmpty(e.newValue)) ProjectSettingsTab.ApplyTheme(e.newValue); });
            r.Add(popup);
            var refresh = new Button(() => BuildThemeRow(lw)) { text = "Refresh" };
            refresh.AddToClassList("zs-imgui-button"); refresh.style.width = 60f;
            r.Add(refresh);
            themeRow.Add(r);
        }

        public void Tick() {
            so.Update();
            foreach (var s in syncers) s();
        }
    }
}

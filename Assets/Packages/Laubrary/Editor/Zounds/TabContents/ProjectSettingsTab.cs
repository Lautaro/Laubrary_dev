using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace Laubrary.Zounds {

    public class ProjectSettingsTab : TabContent {

        public override string name { get; set; } = "Project Settings";

        [SerializeField] private Vector2 scrollPos;

        private string[] availableThemes;
        private int selectedThemeIndex = -1;
        private string _cachedExternalRoot;
        private bool _cachedRootExists;

        private const string k_ExternalSourceRootPref = "Zounds_ExternalSourceRoot";

        /// <summary>
        /// Local-only (EditorPrefs) path to the external audio source root folder.
        /// If set and the folder exists, this machine can edit Klips with external sources.
        /// Not stored in the shared project JSON.
        /// </summary>
        public static string ExternalSourceRoot {
            get => EditorPrefs.GetString(k_ExternalSourceRootPref, "");
            set => EditorPrefs.SetString(k_ExternalSourceRootPref, value);
        }

        /// <summary>
        /// Returns true if this machine has a valid external source root configured and accessible.
        /// </summary>
        public static bool HasExternalSourceRoot {
            get {
                string root = ExternalSourceRoot;
                return !string.IsNullOrEmpty(root) && Directory.Exists(root);
            }
        }

        #region LABELS
        private GUIContent label_playerVolume           = new GUIContent("Player Volume", "Master volume when the game is running. When switching to play mode, this value goes to the master volume.");
        private GUIContent label_systemVolumeModifier   = new GUIContent("System Volume Modifier", "Modifier for the master volume. This is just used if there is a need to modify the overall volume for the game for any reason.");
        private GUIContent label_editorVolume           = new GUIContent("Editor Volume", "Master volume when in edit mode. When switching to edit mode, this value goes to the master volume.");
        private GUIContent label_systemFolderPath       = new GUIContent("System Folder Path", "Set the path for a folder under Resources where system data is stored.");
        private GUIContent label_libraryFolderPath      = new GUIContent("Library Folder Path", "Path where library clips are stored. These are included in builds.");
        private GUIContent label_sourcesFolderPath      = new GUIContent("Sources Folder Path", "Path where source clips are stored. Not included in builds unless referenced.");
        private GUIContent label_themesFolderPath       = new GUIContent("Themes Folder Path", "Path where UI themes are stored.");
        private GUIContent label_cooldownDuration       = new GUIContent("Cooldown Duration", " A timer for a Zound that prohibits the same Zound to be played again before the timer runs out.");
        private GUIContent label_maxPlayedZoundInstances= new GUIContent("Max Played Zound Instances", "If the number of Zounds playing is more than this threshold, when a Zound in a culling group triggers, then it will play, but the one that has been playing for the longest will be culled.");
        private GUIContent label_cullFadeDuration = new GUIContent("Cull Fade Duration", "Fade duration to kill a zound when Max Played Zound Instances is reached.");
        #endregion LABELS

        public override void OnGUI(SerializedObject serializedObject, Rect contentRect) {
            if (availableThemes == null) RefreshThemeList();

            using (ZUI.Box())
            {
            scrollPos = GUILayout.BeginScrollView(scrollPos);

            SerializedProperty projectSettings = serializedObject.FindProperty("projectSettings");
            SerializedProperty playerVolume         = projectSettings.FindPropertyRelative("playerVolume");
            SerializedProperty systemVolumeModifier = projectSettings.FindPropertyRelative("systemVolumeModifier");
            SerializedProperty editorVolume         = projectSettings.FindPropertyRelative("editorVolume");
            SerializedProperty systemFolderPath     = projectSettings.FindPropertyRelative("systemFolderPath");
            SerializedProperty libraryFolderPath    = projectSettings.FindPropertyRelative("libraryFolderPath");
            SerializedProperty sourcesFolderPath    = projectSettings.FindPropertyRelative("sourcesFolderPath");
            SerializedProperty themesFolderPath     = projectSettings.FindPropertyRelative("themesFolderPath");

            SerializedProperty cooldownDuration = projectSettings.FindPropertyRelative("cooldownDuration");
            SerializedProperty maxPlayedZoundInstances = projectSettings.FindPropertyRelative("maxPlayedZoundInstances");
            SerializedProperty cullFadeDuration = projectSettings.FindPropertyRelative("cullFadeDuration");

            SerializedProperty editorStyle = projectSettings.FindPropertyRelative("editorStyle");

            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 150f;

            EditorGUILayout.LabelField("Workspace Directories", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(systemFolderPath, label_systemFolderPath);
            EditorGUILayout.PropertyField(libraryFolderPath, label_libraryFolderPath);
            EditorGUILayout.PropertyField(sourcesFolderPath, label_sourcesFolderPath);
            EditorGUILayout.PropertyField(themesFolderPath, label_themesFolderPath);
            EditorGUILayout.Space(4f);

            // External source root — stored in EditorPrefs (local to this machine, not shared via git).
            // Cache the value at the start of the frame to avoid EditorPrefs changes mid-GUI causing layout mismatches.
            if (Event.current.type == EventType.Layout) {
                _cachedExternalRoot = ExternalSourceRoot;
                _cachedRootExists = !string.IsNullOrEmpty(_cachedExternalRoot) && Directory.Exists(_cachedExternalRoot);
            }
            EditorGUILayout.LabelField("External Audio Sources (Local)", EditorStyles.boldLabel);
            bool hasRoot = !string.IsNullOrEmpty(_cachedExternalRoot);
            string displayPath = hasRoot ? _cachedExternalRoot : "(not set)";
            string statusText = !hasRoot ? "" : _cachedRootExists ? "OK" : "Path not found on this machine.";
            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("External Source Root");
            var prevColor = GUI.color;
            if (hasRoot && !_cachedRootExists) GUI.color = new Color(1f, 0.7f, 0.5f);
            EditorGUILayout.SelectableLabel(displayPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            GUI.color = prevColor;
            if (GUILayout.Button("Browse", GUILayout.Width(60f))) {
                // Defer the modal dialog to avoid corrupting the IMGUI layout stack.
                string startDir = _cachedExternalRoot;
                EditorApplication.delayCall += () => {
                    string selected = EditorUtility.OpenFolderPanel("Select External Audio Source Root", startDir, "");
                    if (!string.IsNullOrEmpty(selected)) {
                        ExternalSourceRoot = selected;
                    }
                };
            }
            var prevEnabled = GUI.enabled;
            GUI.enabled = hasRoot;
            if (GUILayout.Button("Clear", GUILayout.Width(50f)) && hasRoot) {
                ExternalSourceRoot = "";
            }
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();
            // Status line — always drawn unconditionally to keep layout stable across passes.
            var statusStyle = (hasRoot && !_cachedRootExists) ? EditorStyles.helpBox : EditorStyles.miniLabel;
            EditorGUILayout.LabelField(statusText, statusStyle);
            EditorGUILayout.Space(10f);

            EditorGUILayout.LabelField("Engine", EditorStyles.boldLabel);
            EditorGUILayout.Slider(playerVolume, 0f, 1f, label_playerVolume);
            EditorGUILayout.Slider(systemVolumeModifier, 0f, 1f, label_systemVolumeModifier);
            EditorGUILayout.Slider(editorVolume, 0f, 1f, label_editorVolume);

            EditorGUIUtility.labelWidth = 170f;
            cooldownDuration.floatValue = EditorGUILayout.FloatField(label_cooldownDuration, cooldownDuration.floatValue);
            if (cooldownDuration.floatValue < 0f) cooldownDuration.floatValue = 0f;
            maxPlayedZoundInstances.intValue = EditorGUILayout.IntField(label_maxPlayedZoundInstances, maxPlayedZoundInstances.intValue);
            if (maxPlayedZoundInstances.intValue < 0f) maxPlayedZoundInstances.intValue = 1;
            EditorGUILayout.Slider(cullFadeDuration, 0f, 0.5f, label_cullFadeDuration);
            EditorGUILayout.Space(10f);

            EditorGUILayout.LabelField("Themes", EditorStyles.boldLabel);
            EditorGUIUtility.labelWidth = 150f;
            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            selectedThemeIndex = EditorGUILayout.Popup("Available Themes", selectedThemeIndex, availableThemes);
            if (EditorGUI.EndChangeCheck() && selectedThemeIndex >= 0) {
                ApplyTheme(availableThemes[selectedThemeIndex]);
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(60f))) RefreshThemeList();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Save Current Style as New Theme")) {
                if (SaveCurrentStyleAsTheme()) RefreshThemeList();
            }
            EditorGUILayout.Space(10f);

            EditorGUILayout.LabelField("Editor Style (Visual)", EditorStyles.boldLabel);
            EditorGUIUtility.labelWidth = 190f;
            
            SerializedProperty playerHeadColor = editorStyle.FindPropertyRelative("playerHeadColor");
            SerializedProperty playerHeadThickness = editorStyle.FindPropertyRelative("playerHeadThickness");
            SerializedProperty klipWaveformBGColor = editorStyle.FindPropertyRelative("klipWaveformBGColor");
            SerializedProperty volumeEnvelopeColor = editorStyle.FindPropertyRelative("volumeEnvelopeColor");
            SerializedProperty volumeEnvelopeThickness = editorStyle.FindPropertyRelative("volumeEnvelopeThickness");
            SerializedProperty pitchEnvelopeColor = editorStyle.FindPropertyRelative("pitchEnvelopeColor");
            SerializedProperty pitchEnvelopeThickness = editorStyle.FindPropertyRelative("pitchEnvelopeThickness");
            SerializedProperty trimHandleColor = editorStyle.FindPropertyRelative("trimHandleColor");
            SerializedProperty trimHandleThickness = editorStyle.FindPropertyRelative("trimHandleThickness");
            SerializedProperty waveformColor = editorStyle.FindPropertyRelative("waveformColor");
            SerializedProperty trimAreaColor = editorStyle.FindPropertyRelative("trimAreaColor");
            SerializedProperty selectedEnvelopeLineColor = editorStyle.FindPropertyRelative("selectedEnvelopeLineColor");
            SerializedProperty selectedEnvelopeHandleColor = editorStyle.FindPropertyRelative("selectedEnvelopeHandleColor");
            
            SerializedProperty autoRender = editorStyle.FindPropertyRelative("autoRender");
            SerializedProperty alertOnClosing = editorStyle.FindPropertyRelative("alertOnClosing");

            void DrawThicknessColor(GUIContent label, SerializedProperty thickness, SerializedProperty color) {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
                var prevWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 10f;
                EditorGUILayout.PropertyField(thickness, GUIContent.none, GUILayout.Width(45f));
                EditorGUILayout.PropertyField(color, GUIContent.none);
                EditorGUIUtility.labelWidth = prevWidth;
                GUILayout.EndHorizontal();
            }

            DrawThicknessColor(new GUIContent("Player Head"), playerHeadThickness, playerHeadColor);
            DrawThicknessColor(new GUIContent("Volume Envelope"), volumeEnvelopeThickness, volumeEnvelopeColor);
            DrawThicknessColor(new GUIContent("Pitch Envelope"), pitchEnvelopeThickness, pitchEnvelopeColor);
            DrawThicknessColor(new GUIContent("Trim Handle"), trimHandleThickness, trimHandleColor);

            EditorGUILayout.PropertyField(waveformColor);
            EditorGUILayout.PropertyField(klipWaveformBGColor);
            EditorGUILayout.PropertyField(trimAreaColor);
            EditorGUILayout.PropertyField(selectedEnvelopeLineColor);
            EditorGUILayout.PropertyField(selectedEnvelopeHandleColor);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Operational Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(autoRender);
            EditorGUILayout.Slider(editorStyle.FindPropertyRelative("envelopeHandleSize"), 1f, 10f, new GUIContent("Envelope Handle Size"));

            EditorGUIUtility.labelWidth = prevLabelWidth;

            GUILayout.EndScrollView();
            } // end ZUI.Box
        }

        private void RefreshThemeList() {
            availableThemes = ThemeNames();
        }

        // ── Shared with the UI Toolkit twin (T-0470) ──

        /// <summary>The theme files (names without ".json") in the project's themes folder.</summary>
        internal static string[] ThemeNames() {
            var themesPath = ZoundsProject.Instance.projectSettings.themesFolderPath;
            if (!Directory.Exists(themesPath)) return new string[0];
            return Directory.GetFiles(themesPath, "*.json").Select(Path.GetFileNameWithoutExtension).ToArray();
        }

        /// <summary>Loads the named theme file and applies it to the project settings (undoable).</summary>
        internal static void ApplyTheme(string themeName) {
            string path = Path.Combine(ZoundsProject.Instance.projectSettings.themesFolderPath, themeName + ".json");
            if (!File.Exists(path)) return;
            ZoundsTheme theme = JsonUtility.FromJson<ZoundsTheme>(File.ReadAllText(path));
            if (theme == null) return;
            ZoundsWindow.ModifyZoundsProject($"apply theme {themeName}", () => {
                ZoundsProject.Instance.projectSettings.ApplyTheme(theme);
            });
        }

        /// <summary>"Save Current Style as New Theme": asks where, writes the theme file. Returns whether one was written.</summary>
        internal static bool SaveCurrentStyleAsTheme() {
            string path = EditorUtility.SaveFilePanelInProject("Save Theme", "NewTheme", "json", "Select theme save location", ZoundsProject.Instance.projectSettings.themesFolderPath);
            if (string.IsNullOrEmpty(path)) return false;
            string json = JsonUtility.ToJson(ZoundsProject.Instance.projectSettings.ExtractTheme(), true);
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            return true;
        }
    }
}
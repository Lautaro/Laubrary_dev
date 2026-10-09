using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Laubrary.Zounds {

    public class ZoundsProject : ScriptableObject {

        internal static bool isJSONLoaded = false;

        public BrowserSettings browserSettings = new BrowserSettings();
        public ProjectSettings projectSettings = new ProjectSettings();
        public ZoundLibrary zoundLibrary = new ZoundLibrary();
        public ZoundRoutings zoundRoutings = new ZoundRoutings();

        // Native-DSP pipeline only. NOTE: not yet wired into project save/load (JSON round-trip) - a preset
        // added here at runtime does not persist yet. Fine for chainPresetId == 0 (inline effect chains,
        // which need none of this); flagged as a follow-up before shared chain presets are authored for real.
        public List<ZoundChainPreset> chainPresets = new List<ZoundChainPreset>();

        public ZoundChainPreset FindChainPreset(int id) {
            if (id == 0 || chainPresets == null) return null;
            for (int i = 0; i < chainPresets.Count; i++) if (chainPresets[i].id == id) return chainPresets[i];
            return null;
        }

        /// <summary>How many zounds reference a preset by live reference.</summary>
        public int CountChainPresetUsers(int presetId) {
            int n = 0;
            zoundLibrary.ForEachZound(z => { if (z.chainPresetId == presetId) n++; });
            return n;
        }

        [System.Serializable]
        public class BrowserSettings {
            public bool multicolumn = false;
            public float itemWidth = 300f;
            public bool killOnPlay = false;
            public bool showAudioClips = false;
            public bool msOnly = false; // only show either muted or solo

            // DRAW SETTINGS
            public bool showNameField = true;
            public bool showVolume = true;
            public bool showPitch = true;
            public bool showChance = true;
            // VPC slider display options.
            // true = "Volume 30-100" / "Pitch 50-150" / "Chance: 80"
            // false = "30-100" / "50-150" / "80"
            public bool vpcShowSliderType = true;
            // When true, each VPC slider also shows editable input boxes for its value(s).
            public bool vpcShowInputBoxes = false;
            public bool showTags = true;
            // List mode only: when true, tags are drawn on their own row(s) below the other
            // controls (all tags visible, wrap as many lines as needed). When false, tags are
            // drawn inline in a fixed-width area on row 1 and clipped past that area.
            public bool tagsOnOwnRow = false;
            public bool showMute = true;
            public bool showSolo = true;
            public bool showOpenEditor = true;
            // Gates the "Convert to Zeq" button in the Klip editor window (not a Browser row button —
            // converting a Klip to a Zequence is only offered there, for top-level Klips).
            public bool showConvertToZequence = true;
            public bool showRouting = true;
            public bool showDuplicate = true;
            public bool showRemove = true;

            // QUICK CONTROLS VISIBILITY
            public bool showAddZound = true;
            public bool showStopAll = true;
            public bool showMSClean = true;
            [HideInInspector] public bool showMuteSel = true;      // legacy — superseded by showMSClean
            [HideInInspector] public bool showSoloSel = true;      // legacy — superseded by showMSClean
            public bool showMasterVolume = true;
            public bool showSearch = true;
            [HideInInspector] public bool showTypes = true;        // legacy — superseded by showTypeKlip/Zeq/Files/Missing
            public bool showTypeKlip = true;
            public bool showTypeZeq = true;
            public bool showTypeFiles = true;
            public bool showTypeMissing = true;
            [HideInInspector] public bool typesInlineToggle = false; // legacy — no longer used
            public bool showTagsFilter = true;
            public enum ButtonSizeMode { Auto, Min, Fixed }
            public ButtonSizeMode buttonSizeMode = ButtonSizeMode.Fixed;

            public bool showGroupBy = true;
            public bool showColumnMode = true;
            [HideInInspector] public bool showReferences = true; // legacy — references feature removed
            public bool showPresetsAlways = false;
            public bool highQualityWaveform = false;
            public bool vpcPercentage = false;
            public bool vpcCompactLabel = false;
            public bool fancyTitle = true;
        }

        [System.Serializable]
        public class ProjectSettings {
            public float playerVolume = 1f;
            public float systemVolumeModifier = 1f;
            public float editorVolume = 1f;
            public string systemFolderPath = "Assets/ZoundsData/SystemFiles";
            [FormerlySerializedAs("userFolderPath")]
            public string libraryFolderPath = "Assets/ZoundsData/Library";
            [FormerlySerializedAs("sourceFolderPath")]
            public string sourcesFolderPath = "Assets/ZoundsData/Sources";
            public string themesFolderPath = "Assets/ZoundsData/Themes";

            public float cooldownDuration = 0.1f;
            public int maxPlayedZoundInstances = 10;
            public float cullFadeDuration = 0.4f;

            /// <summary>
            /// How an edit that would change something protected is announced (destructive editing, 2026-10-09): a sound
            /// other sounds use, or an audio file Zounds must not overwrite (a source, library or outside file, or one several
            /// sounds play). Such an edit always goes to a copy; this only decides whether you are asked first, told after,
            /// or not told. Editor-only behaviour; a game never reads it.
            /// </summary>
            public enum ProtectedEditPrompt { Ask = 1, Notice = 2, Silent = 3 }
            public ProtectedEditPrompt protectedEditPrompt = ProtectedEditPrompt.Notice;

            public string workFolderPath => systemFolderPath + "/WorkFiles";
            public string zoundFilesFolderPath => systemFolderPath + "/ZoundFiles";

            public EditorStyle editorStyle = new EditorStyle();

            public void ApplyTheme(ZoundsTheme theme) {
                var themeJson = JsonUtility.ToJson(theme);
                JsonUtility.FromJsonOverwrite(themeJson, editorStyle);
            }

            public ZoundsTheme ExtractTheme() {
                var theme = new ZoundsTheme();
                var styleJson = JsonUtility.ToJson(editorStyle);
                JsonUtility.FromJsonOverwrite(styleJson, theme);
                return theme;
            }

            [System.Serializable]
            public class EditorStyle {
                public Color playerHeadColor = new Color(0.1f, 0.1f, 0.9f, 0.75f);
                public float playerHeadThickness = 1.5f;
                public Color klipWaveformBGColor = new Color32(252, 192, 7, 255);
                public Color volumeEnvelopeColor = new Color(0.1f, 0.7f, 0.1f);
                public float volumeEnvelopeThickness = 1.5f;
                public Color pitchEnvelopeColor = new Color(0.9f, 0.2f, 0.1f);
                public float pitchEnvelopeThickness = 1.5f;
                public Color trimHandleColor = Color.white;
                public float trimHandleThickness = 2.0f;
                public Color waveformColor = Color.black;
                public Color trimAreaColor = new Color(0f, 0f, 0f, 0.5f);
                public Color selectedEnvelopeLineColor = new Color(0.1f, 0.7f, 0.9f);
                public Color selectedEnvelopeHandleColor = new Color(0.1f, 0.75f, 0.85f);
                public bool autoRender = false;
                public float envelopeHandleSize = 4.0f;
                // The editors' backgrounds (owner's request 2026-10-08): the box behind a Klip or Zequence editor, the
                // two alternating track bands of a Zequence, and the lane a track's audio is drawn in. Alpha counts: the
                // defaults are the translucent bands the windows used to draw, over the skin's box.
                public Color editorBackgroundColor = new Color(0f, 0f, 0f, 0f);
                public Color trackBackgroundColor = new Color(0.25f, 0.25f, 0.3f, 0.22f);
                public Color trackAltBackgroundColor = new Color(0.35f, 0.35f, 0.42f, 0.15f);
                public Color trackLaneColor = new Color(1f, 1f, 1f, 0.04f);
            }

        }

        private static ZoundsProject instance;

        public static ZoundsProject Instance {
            get {
                if (instance == null) {
                    instance = CreateInstance<ZoundsProject>();
                    instance.hideFlags = HideFlags.DontSave;
                }
                return instance;
            }
        }

        /// <summary>
        /// Wipes all in-memory project data and resets the singleton to a clean default state.
        /// Call this whenever the project JSON reference is cleared or becomes invalid.
        /// </summary>
        public static void ResetToDefault() {
            var inst = Instance;
            inst.browserSettings = new BrowserSettings();
            inst.projectSettings = new ProjectSettings();
            inst.zoundLibrary = new ZoundLibrary();
            inst.zoundRoutings = new ZoundRoutings();
            isJSONLoaded = false;
        }

        public static void LoadFromJSON(TextAsset jsonTextAsset) {
            LoadFromJSON(jsonTextAsset.text);
        }

        public static event System.Action onProjectLoaded;

        /// <summary>Raised after an editor-side edit of the in-memory project (see ZoundsWindow.ModifyZoundsProject).</summary>
        public static event System.Action onProjectModified;
        public static void NotifyModified() => onProjectModified?.Invoke();

        public static void LoadFromJSON(string jsonContent) {
            ProjectSerializer deserialized;
            try {
                deserialized = JsonUtility.FromJson<ProjectSerializer>(jsonContent);
            }
            catch {
                Debug.LogError("Invalid Json Content: " + jsonContent);
                deserialized = null;
            }
            if (deserialized == null) return;
            var inst = Instance;
            inst.browserSettings = deserialized.browserSettings;
            inst.projectSettings = deserialized.projectSettings;
            inst.zoundLibrary = deserialized.zoundLibrary;
            inst.zoundRoutings = deserialized.zoundRoutings;

            // A local zound belongs to its parent alone and carries no tags. Older data can still hold
            // tags on locals (copies made before the editor cleared them); strip them so tag lookups and
            // routing never see a local.
            int localTagsCleared = 0;
            inst.zoundLibrary.ForEachZound(z => {
                if (z.parentId != 0 && z.tags.Count > 0) { z.tags.Clear(); localTagsCleared++; }
            });
            if (localTagsCleared > 0)
                Debug.Log("[Zounds] Cleared tags from " + localTagsCleared + " local zound(s); tags belong to top-level zounds only.");

            // Clear the runtime engine caches so we don't hold onto stale sounds
            // after the JSON has been reloaded or reverted.
            ZoundEngine.ClearEngineCaches();

            // Folder creation moved out of here: this runs from a static startup hook and from asset
            // re-imports, and creating folders + refreshing the AssetDatabase there fires mid-import.
            // ZoundsWindow ensures the folders when it opens and when a new project is created.
            isJSONLoaded = true;
            onProjectLoaded?.Invoke();
        }

#if UNITY_EDITOR
        internal static void GenerateDefaultFiles() {
            EnsureDirectoryExists(instance.projectSettings.systemFolderPath);
            EnsureDirectoryExists(instance.projectSettings.workFolderPath);
            EnsureDirectoryExists(instance.projectSettings.zoundFilesFolderPath);
            EnsureDirectoryExists(instance.projectSettings.themesFolderPath);
            EnsureDirectoryExists(instance.projectSettings.systemFolderPath + "/Resources");
            EnsureDirectoryExists(instance.projectSettings.libraryFolderPath);
            EnsureDirectoryExists(instance.projectSettings.sourcesFolderPath);
            UnityEditor.AssetDatabase.Refresh();
        }

        public static void EnsureDirectoryExists(string path) {
            if (!path.StartsWith("Assets") && !path.StartsWith("Packages")) {
                Debug.LogError("Path must start with 'Assets' or 'Packages'.");
                return;
            }

            string[] folders = path.Split('/');
            string currentPath = folders[0];

            for (int i = 1; i < folders.Length; i++) {
                string newPath = $"{currentPath}/{folders[i]}";

                if (!UnityEditor.AssetDatabase.IsValidFolder(newPath)) {
                    UnityEditor.AssetDatabase.CreateFolder(currentPath, folders[i]);
                }

                currentPath = newPath;
            }
        }
#endif

        [System.Serializable]
        internal class ProjectSerializer {
            public BrowserSettings browserSettings = new BrowserSettings();
            public ProjectSettings projectSettings = new ProjectSettings();
            public ZoundLibrary zoundLibrary = new ZoundLibrary();
            public ZoundRoutings zoundRoutings = new ZoundRoutings();
        }
    }

}

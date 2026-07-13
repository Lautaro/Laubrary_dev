using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static Laubrary.Zounds.ZoundsWindowProperties.ZoundTabProperties;

namespace Laubrary.Zounds {
    public class ZoundsEditorPresets : ScriptableObject {

        private static ZoundsEditorPresets instance;
        public static ZoundsEditorPresets Instance {
            get {
                if (instance == null) {
                    instance = Resources.Load<ZoundsEditorPresets>("ZoundsEditorPresets");
                    if (instance == null) {
                        var systemPath = ZoundsProject.Instance.projectSettings.systemFolderPath;
                        EnsureFolderPathExists(systemPath + "/Resources");
                        instance = CreateInstance<ZoundsEditorPresets>();
                        UnityEditor.AssetDatabase.CreateAsset(instance, systemPath + "/Resources/ZoundsEditorPresets.asset");
                    }
                }
                return instance;
            }
        }

        private static void EnsureFolderPathExists(string folderPath) {
            if (string.IsNullOrEmpty(folderPath))
                return;

            folderPath = folderPath.Replace("\\", "/");

            string[] parts = folderPath.Split('/');
            string currentPath = "";

            for (int i = 0; i < parts.Length; i++) {
                string part = parts[i];
                string parentPath = string.IsNullOrEmpty(currentPath) ? "Assets" : currentPath;
                currentPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + "/" + part;

                if (!UnityEditor.AssetDatabase.IsValidFolder(currentPath)) {
                    UnityEditor.AssetDatabase.CreateFolder(parentPath, part);
                }
            }
        }

        internal void ApplyDefaultView() {
            var browserSettings = ZoundsProject.Instance.browserSettings;
            var zoundTabProperties = ZoundsWindowProperties.Instance.zoundTabProperties[0];
            Undo.RecordObject(ZoundsWindowProperties.Instance, "apply preset");
            ZoundsWindow.ModifyZoundsProject("apply preset", () => {
                browserSettings.multicolumn = false;
                browserSettings.showVolume = true;
                browserSettings.showPitch = true;
                browserSettings.showChance = true;
                browserSettings.itemWidth = 300f;
                browserSettings.showNameField = true;
                browserSettings.showTags = true;
            });
            zoundTabProperties.selectedTypes = ZoundType.None;
            zoundTabProperties.selectedFolders.Clear();
            zoundTabProperties.selectedTags.Clear();
            zoundTabProperties.selectedReferences.Clear();
            zoundTabProperties.groupBy = GroupBy.None;
            EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
            ZoundsWindow.RepaintWindow();
        }

        public List<ViewPreset> viewPresets = new List<ViewPreset>();
        public List<NameListPreset> typesPresets = new List<NameListPreset>();
        public List<NameListPreset> tagsPresets = new List<NameListPreset>();
        public List<NameListPreset> referencesPresets = new List<NameListPreset>();

        public interface IPreset {
            public string name { get; set; }
        }

        [System.Serializable]
        public class ViewPreset : IPreset {
            [SerializeField] string m_name = "New Preset";

            public string name { get => m_name; set => m_name = value; }

            // ── BrowserSettings snapshot (every non-legacy field) ─────────────────
            // General
            public bool multicolumn = false;
            public float itemWidth = 300f;
            public bool killOnPlay = false;
            public bool showAudioClips = false;
            public bool msOnly = false;
            // Row draw settings
            public bool showNameField = true;
            public bool showVolume = true;
            public bool showPitch = true;
            public bool showChance = true;
            public bool vpcShowSliderType = true;
            public bool vpcShowInputBoxes = false;
            public bool showTags = true;
            public bool tagsOnOwnRow = false;
            public bool showMute = true;
            public bool showSolo = true;
            public bool showOpenEditor = true;
            public bool showConvertToZequence = true;
            public bool showRouting = true;
            public bool showDuplicate = true;
            public bool showRemove = true;
            // Toolbar / quick controls visibility
            public bool showAddZound = true;
            public bool showStopAll = true;
            public bool showMSClean = true;
            public bool showMasterVolume = true;
            public bool showSearch = true;
            public bool showTypeKlip = true;
            public bool showTypeZeq = true;
            public bool showTypeFiles = true;
            public bool showTypeMissing = true;
            public bool showTagsFilter = true;
            public bool showGroupBy = true;
            public bool showColumnMode = true;
            public bool showPresetsAlways = false;
            // Behavior / visual
            public ZoundsProject.BrowserSettings.ButtonSizeMode buttonSizeMode = ZoundsProject.BrowserSettings.ButtonSizeMode.Fixed;
            public bool highQualityWaveform = false;
            public bool vpcPercentage = false;
            public bool vpcCompactLabel = false;
            public bool fancyTitle = true;

            // ── ZoundTabProperties snapshot ───────────────────────────────────────
            public string searchText = "";
            public ZoundType selectedTypes;
            public List<string> selectedFolders = new List<string>();
            public List<string> selectedTags = new List<string>();
            public List<int> selectedReferences = new List<int>(); // zound ids
            public GroupBy groupBy = GroupBy.None;

            public void SetFromCurrentSettings() {
                var b = ZoundsProject.Instance.browserSettings;
                var t = ZoundsWindowProperties.Instance.zoundTabProperties[0];

                multicolumn            = b.multicolumn;
                itemWidth              = b.itemWidth;
                killOnPlay             = b.killOnPlay;
                showAudioClips         = b.showAudioClips;
                msOnly                 = b.msOnly;
                showNameField          = b.showNameField;
                showVolume             = b.showVolume;
                showPitch              = b.showPitch;
                showChance             = b.showChance;
                vpcShowSliderType      = b.vpcShowSliderType;
                vpcShowInputBoxes      = b.vpcShowInputBoxes;
                showTags               = b.showTags;
                tagsOnOwnRow           = b.tagsOnOwnRow;
                showMute               = b.showMute;
                showSolo               = b.showSolo;
                showOpenEditor         = b.showOpenEditor;
                showConvertToZequence  = b.showConvertToZequence;
                showRouting            = b.showRouting;
                showDuplicate          = b.showDuplicate;
                showRemove             = b.showRemove;
                showAddZound           = b.showAddZound;
                showStopAll            = b.showStopAll;
                showMSClean            = b.showMSClean;
                showMasterVolume       = b.showMasterVolume;
                showSearch             = b.showSearch;
                showTypeKlip           = b.showTypeKlip;
                showTypeZeq            = b.showTypeZeq;
                showTypeFiles          = b.showTypeFiles;
                showTypeMissing        = b.showTypeMissing;
                showTagsFilter         = b.showTagsFilter;
                showGroupBy            = b.showGroupBy;
                showColumnMode         = b.showColumnMode;
                showPresetsAlways      = b.showPresetsAlways;
                buttonSizeMode         = b.buttonSizeMode;
                highQualityWaveform    = b.highQualityWaveform;
                vpcPercentage          = b.vpcPercentage;
                vpcCompactLabel        = b.vpcCompactLabel;
                fancyTitle             = b.fancyTitle;

                searchText             = t.searchText ?? "";
                selectedTypes          = t.selectedTypes;
                selectedFolders        = t.selectedFolders.ToList();
                selectedTags           = t.selectedTags.ToList();
                selectedReferences     = t.selectedReferences.ToList();
                groupBy                = t.groupBy;
            }

            internal void Apply() {
                var b = ZoundsProject.Instance.browserSettings;
                var t = ZoundsWindowProperties.Instance.zoundTabProperties[0];
                Undo.RecordObject(ZoundsWindowProperties.Instance, "apply preset");
                ZoundsWindow.ModifyZoundsProject("apply preset", () => {
                    b.multicolumn            = multicolumn;
                    b.itemWidth              = itemWidth;
                    b.killOnPlay             = killOnPlay;
                    b.showAudioClips         = showAudioClips;
                    b.msOnly                 = msOnly;
                    b.showNameField          = showNameField;
                    b.showVolume             = showVolume;
                    b.showPitch              = showPitch;
                    b.showChance             = showChance;
                    b.vpcShowSliderType      = vpcShowSliderType;
                    b.vpcShowInputBoxes      = vpcShowInputBoxes;
                    b.showTags               = showTags;
                    b.tagsOnOwnRow           = tagsOnOwnRow;
                    b.showMute               = showMute;
                    b.showSolo               = showSolo;
                    b.showOpenEditor         = showOpenEditor;
                    b.showConvertToZequence  = showConvertToZequence;
                    b.showRouting            = showRouting;
                    b.showDuplicate          = showDuplicate;
                    b.showRemove             = showRemove;
                    b.showAddZound           = showAddZound;
                    b.showStopAll            = showStopAll;
                    b.showMSClean            = showMSClean;
                    b.showMasterVolume       = showMasterVolume;
                    b.showSearch             = showSearch;
                    b.showTypeKlip           = showTypeKlip;
                    b.showTypeZeq            = showTypeZeq;
                    b.showTypeFiles          = showTypeFiles;
                    b.showTypeMissing        = showTypeMissing;
                    b.showTagsFilter         = showTagsFilter;
                    b.showGroupBy            = showGroupBy;
                    b.showColumnMode         = showColumnMode;
                    b.showPresetsAlways      = showPresetsAlways;
                    b.buttonSizeMode         = buttonSizeMode;
                    b.highQualityWaveform    = highQualityWaveform;
                    b.vpcPercentage          = vpcPercentage;
                    b.vpcCompactLabel        = vpcCompactLabel;
                    b.fancyTitle             = fancyTitle;
                });
                t.searchText          = searchText ?? "";
                t.selectedTypes       = selectedTypes;
                t.selectedFolders     = selectedFolders.ToList();
                t.selectedTags        = selectedTags.ToList();
                t.selectedReferences  = selectedReferences.ToList();
                t.groupBy             = groupBy;
                EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
                ZoundsWindow.RepaintWindow();
            }
        }

        [System.Serializable]
        public class NameListPreset : IPreset {
            [SerializeField] string m_name = "New Preset";

            public string name { get => m_name; set => m_name = value; }
            public List<string> names = new List<string>();
        }

    }
}

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds
{
    [InitializeOnLoad]
    public static class ZoundsProjectInitialization {
        static ZoundsProjectInitialization() {
#if !ADDRESSABLES_INSTALLED
            Debug.LogError("Zounds Dependency: Addressables package should be installed. Minimum version: 1.18.19");
#endif
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            ZoundsFilter.RefreshFolders();
            AutoLoadJSONProject();
        }

        private static void AutoLoadJSONProject() {
            string projectJsonPath = GetZoundsProjectPath();
            if (!string.IsNullOrEmpty(projectJsonPath)) {
                var projectJSONAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(projectJsonPath);
                if (projectJSONAsset != null) {
                    ZoundsProject.LoadFromJSON(projectJSONAsset);
                    ZoundEngine.editorLastOpenedProject = projectJSONAsset;
                }
                else {
                    // Stored path is stale — wipe it and start clean.
                    SetZoundsProjectPath(string.Empty);
                    ZoundsProject.ResetToDefault();
                    ZoundEngine.editorLastOpenedProject = null;
                }
            }
            else {
                ZoundsProject.ResetToDefault();
                ZoundEngine.editorLastOpenedProject = null;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange stateChange) {
            // When exiting play mode, clear the texture cache so the Zequence editor 
            // regenerates waveforms from the correct on-disk assets.
            if (stateChange == PlayModeStateChange.EnteredEditMode) {
                AudioWaveformUtility.ClearCache();
            }
        }

        public static string GetSettingsPath() {
            string assetsPath = Application.dataPath;
            string projectRoot = Path.GetDirectoryName(assetsPath);
            string projectSettingsPath = Path.Combine(projectRoot, "ProjectSettings");
            string targetFile = Path.Combine(projectSettingsPath, "ZoundsProjectPath.txt");

            if (!Directory.Exists(projectSettingsPath)) {
                Directory.CreateDirectory(projectSettingsPath);
            }

            return targetFile;
        }

        public static string GetZoundsProjectPath() {
            string targetFile = GetSettingsPath();

            if (!File.Exists(targetFile)) {
                File.WriteAllText(targetFile, string.Empty);
                return string.Empty;
            }

            return File.ReadAllText(targetFile);
        }

        public static void SetZoundsProjectPath(string path) {
            string targetFile = GetSettingsPath();
            File.WriteAllText(targetFile, path);
        }
    }

}

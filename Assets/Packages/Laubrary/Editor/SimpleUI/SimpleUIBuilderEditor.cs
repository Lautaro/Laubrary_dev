using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Laubrary.SimpleUI;

namespace Laubrary.SimpleUI.Editor
{
    [CustomEditor(typeof(SimpleUIBuilder))]
    public class SimpleUIBuilderEditor : UnityEditor.Editor
    {
        private const string SIMPLE_UI_SUBFOLDER = "SimpleUI";

        private Type[] _simpleUITypes;
        private string[] _simpleUITypeNames;
        private int _selectedIndex = 0;

        private void OnEnable()
        {
            RefreshAvailableTypes();
        }

        private void RefreshAvailableTypes()
        {
            _simpleUITypes = TypeCache.GetTypesWithAttribute<SimpleUIAttribute>()
                .Where(t => t != null && t.IsClass && !t.IsAbstract)
                .OrderBy(t => t.Name)
                .ToArray();

            _simpleUITypeNames = _simpleUITypes.Select(t => t.Name).ToArray();
        }

        public override void OnInspectorGUI()
        {
            var builder = (SimpleUIBuilder)target;

            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetView"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("settings"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layout Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("containerLayout"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fieldOrientation"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacing"));

            if (builder.containerLayout == SimpleUIBuilder.ContainerLayout.Grid)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("columns"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("cellSize"));
            }

            serializedObject.ApplyModifiedProperties();

            if (builder.targetView == null)
            {
                EditorGUILayout.HelpBox("Assign a Target View to begin.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Setup & Assets", EditorStyles.boldLabel);

            if (builder.settings == null)
            {
                EditorGUILayout.HelpBox(
                    "No Settings assigned. Click below to create a local prefab set " +
                    "copied from the master defaults, or assign an existing one manually.",
                    MessageType.Info);

                if (GUILayout.Button("Setup Local Prefabs"))
                {
                    SetupLocalPrefabs(builder);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hierarchy Generation", EditorStyles.boldLabel);

            RefreshAvailableTypes();

            if (_simpleUITypeNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No classes with [SimpleUI] attribute found in project.", MessageType.Error);
                return;
            }

            _selectedIndex = EditorGUILayout.Popup("POCO Type", _selectedIndex, _simpleUITypeNames);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Generate UI"))
            {
                var pocoType = _simpleUITypes[_selectedIndex];
                int count = SimpleUIHierarchyGenerator.GenerateHierarchy(builder, pocoType);
                if (count > 0)
                {
                    Debug.Log($"[SimpleUI] Generated {count} elements.");
                    EditorUtility.SetDirty(builder.targetView);
                }
            }

            if (GUILayout.Button("Clear Generated"))
            {
                var pocoType = _simpleUITypes[_selectedIndex];
                var targets = SimpleUIHierarchyGenerator.PreviewClearTargets(builder.targetView, pocoType);

                if (targets.Count > 0)
                {
                    string rowList = string.Join("\n", targets.Select(t => $"  • {t}"));
                    if (EditorUtility.DisplayDialog("Clear Generated UI",
                        $"Delete {targets.Count} row(s)?\n\n{rowList}", "Clear", "Cancel"))
                    {
                        SimpleUIHierarchyGenerator.ClearGeneratedHierarchy(builder.targetView, pocoType);
                        EditorUtility.SetDirty(builder.targetView);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Tip: Use the Builder to scaffold your UI, then disable or remove it to finalize your design.\n" +
                "Without Settings the builder generates procedural TMP rows. " +
                "With Settings it instantiates prefab-based rows from the assigned set.",
                MessageType.None);
        }

        /// <summary>
        /// Finds the master SimpleUISettings, copies its prefabs to a folder next to
        /// the active scene, creates a new local SimpleUISettings pointing to the copies,
        /// and assigns it to the builder.
        /// </summary>
        private void SetupLocalPrefabs(SimpleUIBuilder builder)
        {
            var masterSettings = FindMasterSettings();
            if (masterSettings == null)
            {
                EditorUtility.DisplayDialog("SimpleUI Setup",
                    "No master SimpleUISettings with all prefabs assigned was found.\n\n" +
                    "Create a SimpleUISettings asset with display, input, toggle, slider, and dropdown " +
                    "prefabs assigned to serve as the master template.",
                    "OK");
                return;
            }

            var activeScene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                EditorUtility.DisplayDialog("SimpleUI Setup",
                    "Save the current scene first so the local prefab set can be " +
                    "placed next to it.",
                    "OK");
                return;
            }

            string sceneFolder = Path.GetDirectoryName(activeScene.path);
            string localFolder = $"{sceneFolder}/{SIMPLE_UI_SUBFOLDER}";

            EnsureFolder(localFolder);

            // Copy each prefab from master — skip if a copy already exists
            var localSettings = ScriptableObject.CreateInstance<SimpleUISettings>();
            localSettings.displayPrefab = CopyPrefab(masterSettings.displayPrefab, localFolder);
            localSettings.inputPrefab = CopyPrefab(masterSettings.inputPrefab, localFolder);
            localSettings.togglePrefab = CopyPrefab(masterSettings.togglePrefab, localFolder);
            localSettings.sliderPrefab = CopyPrefab(masterSettings.sliderPrefab, localFolder);
            localSettings.dropdownPrefab = CopyPrefab(masterSettings.dropdownPrefab, localFolder);

            // Carry over text style from master
            localSettings.font = masterSettings.font;
            localSettings.fontSize = masterSettings.fontSize;
            localSettings.labelColor = masterSettings.labelColor;
            localSettings.valueColor = masterSettings.valueColor;

            string settingsPath = $"{localFolder}/SimpleUISettings.asset";

            // Avoid overwriting an existing local settings — reuse it instead
            var existingSettings = AssetDatabase.LoadAssetAtPath<SimpleUISettings>(settingsPath);
            if (existingSettings != null)
            {
                Debug.Log($"[SimpleUI] Reusing existing local settings at {settingsPath}.");
                builder.settings = existingSettings;
            }
            else
            {
                AssetDatabase.CreateAsset(localSettings, settingsPath);
                builder.settings = localSettings;
            }

            Undo.RecordObject(builder, "SimpleUI Setup Local Prefabs");
            EditorUtility.SetDirty(builder);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SimpleUI] Local prefab set created at {localFolder}.");
            EditorUtility.DisplayDialog("SimpleUI Setup",
                $"Created local prefab set at:\n{localFolder}\n\n" +
                "These are independent copies you can customize freely " +
                "without affecting the master defaults.",
                "OK");
        }

        /// <summary>
        /// Searches the project for a SimpleUISettings asset that has all five prefabs assigned.
        /// Prefers one located under a "Samples" folder path.
        /// </summary>
        private static SimpleUISettings FindMasterSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:SimpleUISettings");
            SimpleUISettings fallback = null;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<SimpleUISettings>(path);
                if (candidate == null) continue;

                if (!HasAllPrefabs(candidate)) continue;

                // Prefer the canonical location under Samples
                if (path.Contains("Samples/SimpleUI"))
                    return candidate;

                fallback ??= candidate;
            }

            return fallback;
        }

        /// <summary>
        /// Returns true when all five prefab slots on the settings are assigned.
        /// </summary>
        private static bool HasAllPrefabs(SimpleUISettings settings)
        {
            return settings.displayPrefab != null
                && settings.inputPrefab != null
                && settings.togglePrefab != null
                && settings.sliderPrefab != null
                && settings.dropdownPrefab != null;
        }

        /// <summary>
        /// Copies a single prefab asset into the target folder.
        /// Returns the copy (or the existing asset if it was already copied).
        /// </summary>
        private static GameObject CopyPrefab(GameObject sourcePrefab, string targetFolder)
        {
            if (sourcePrefab == null) return null;

            string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            string fileName = Path.GetFileName(sourcePath);
            string targetPath = $"{targetFolder}/{fileName}";

            // Reuse existing copy to avoid overwriting user edits
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (existing != null)
                return existing;

            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                Debug.LogError($"[SimpleUI] Failed to copy {sourcePath} to {targetPath}.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        }

        /// <summary>
        /// Creates all intermediate directories for the given asset folder path.
        /// </summary>
        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = Path.GetDirectoryName(folderPath);
            string folderName = Path.GetFileName(folderPath);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}

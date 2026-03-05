using UnityEngine;
using UnityEditor;
using Laubrary.SimpleMenu;

namespace Laubrary.SimpleMenu.Editor
{
    [CustomEditor(typeof(SimpleMenuBase), true)]
    [CanEditMultipleObjects]
    public class SimpleMenuEditor : UnityEditor.Editor
    {
        private bool showTransitions = false;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw Menu Settings section
            EditorGUILayout.LabelField("Menu Settings", EditorStyles.boldLabel);
            SerializedProperty menuSettingsProp = serializedObject.FindProperty("menuSettings");
            EditorGUILayout.PropertyField(menuSettingsProp);

            EditorGUILayout.Space();

            // Transitions Foldout
            showTransitions = EditorGUILayout.BeginFoldoutHeaderGroup(showTransitions, "Transition Settings");
            if (showTransitions)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionAnimationType"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionFromAlpha"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionFromPositionOffset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionFromScale"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionDuration"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionCurve"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionExitSpeedMultiplier"));
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Element Animations", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("elementAnimateEach"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("elementDirection"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("elementPickOrder"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("elementStagger"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useAlternateElementConfig"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("altElementConfig"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space();

            // Editor Preview section
            EditorGUILayout.LabelField("Editor Preview", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild Preview"))
            {
                foreach (var obj in targets)
                {
                    ((SimpleMenuBase)obj).RebuildEditorPreview();
                }
            }

            if (GUILayout.Button("Clear Preview"))
            {
                foreach (var obj in targets)
                {
                    ((SimpleMenuBase)obj).ClearEditorPreview();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Editor Preview creates temporary objects in the hierarchy to visualize the menu. These will be automatically cleared when entering Play Mode.", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}

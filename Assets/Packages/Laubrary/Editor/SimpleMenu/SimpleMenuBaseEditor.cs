using Laubrary.SimpleMenu;
using UnityEditor;
using UnityEngine;

namespace Laubrary.SimpleMenu.Editor
{
    [CustomEditor(typeof(SimpleMenuBase), true)]
    [CanEditMultipleObjects]
    public class SimpleMenuBaseEditor : UnityEditor.Editor
    {
        private const string TransitionFoldoutKey = "SimpleMenu_TransitionFoldout";
        private bool showTransitions;

        private void OnEnable()
        {
            showTransitions = EditorPrefs.GetBool(TransitionFoldoutKey, false);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ── Settings ──────────────────────────────────────────────────────
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("menuSettings"),
                new GUIContent("Settings"));

            EditorGUILayout.Space();

            // ── Transitions Foldout ───────────────────────────────────────────
            bool newShowTransitions = EditorGUILayout.BeginFoldoutHeaderGroup(showTransitions, "Transitions");
            if (newShowTransitions != showTransitions)
            {
                showTransitions = newShowTransitions;
                EditorPrefs.SetBool(TransitionFoldoutKey, showTransitions);
            }

            if (showTransitions)
            {
                EditorGUI.indentLevel++;

                // ── Container Transition ──────────────────────────────────────
                SerializedProperty animTypeProp = serializedObject.FindProperty("transitionAnimationType");
                EditorGUILayout.PropertyField(animTypeProp, new GUIContent("Transition Animation"));
                SimpleMenuAnimationType flags = (SimpleMenuAnimationType)animTypeProp.intValue;

                if (flags != SimpleMenuAnimationType.None)
                {
                    EditorGUI.indentLevel++;

                    if ((flags & SimpleMenuAnimationType.Alpha) != 0)
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("transitionFromAlpha"),
                            new GUIContent("From Alpha"));

                    if ((flags & SimpleMenuAnimationType.Position) != 0)
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("transitionFromPositionOffset"),
                            new GUIContent("From Position Offset"));

                    if ((flags & SimpleMenuAnimationType.Scale) != 0)
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("transitionFromScale"),
                            new GUIContent("From Scale"));

                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("transitionDuration"),
                        new GUIContent("Duration"));

                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("transitionCurve"),
                        new GUIContent("Curve"));

                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("transitionExitSpeedMultiplier"),
                        new GUIContent("Exit Speed Multiplier"));

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(4);

                // ── Animate Each ──────────────────────────────────────────────
                SerializedProperty animEachProp = serializedObject.FindProperty("elementAnimateEach");
                EditorGUILayout.PropertyField(animEachProp, new GUIContent("Animate Each"));

                if (animEachProp.boolValue)
                {
                    EditorGUI.indentLevel++;

                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("elementDirection"),
                        new GUIContent("Direction"));

                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("elementPickOrder"),
                        new GUIContent("Pick Order"));

                    SerializedProperty staggerProp = serializedObject.FindProperty("elementStagger");
                    EditorGUILayout.PropertyField(staggerProp, new GUIContent("Stagger"));

                    float stagger = staggerProp.floatValue;
                    float dur = serializedObject.FindProperty("transitionDuration").floatValue;
                    EditorGUILayout.LabelField(
                        $"Delay between elements: {stagger * dur:F2}s  |  Element duration: {dur:F2}s",
                        EditorStyles.miniLabel);

                    EditorGUILayout.Space(4);
                    SerializedProperty useAltProp = serializedObject.FindProperty("useAlternateElementConfig");
                    EditorGUILayout.PropertyField(useAltProp, new GUIContent("Stagger Animation"));

                    if (useAltProp.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.Space(2);
                        DrawElementAnimConfig("altElementConfig", "Alternate Animation");
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space();

            // ── Derived class fields ──────────────────────────────────────────
            DrawPropertiesExcluding(serializedObject,
                "m_Script",
                "menuSettings",
                "isEditorPreview",
                "transitionAnimationType",
                "transitionFromAlpha",
                "transitionFromPositionOffset",
                "transitionFromScale",
                "transitionDuration",
                "transitionCurve",
                "transitionExitSpeedMultiplier",
                "elementAnimateEach",
                "elementDirection",
                "elementPickOrder",
                "elementStagger",
                "useAlternateElementConfig",
                "altElementConfig");

            EditorGUILayout.Space();

            // ── Editor Preview ────────────────────────────────────────────────
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
            EditorGUILayout.HelpBox("Preview objects are temporary and auto-cleared on Play.", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawElementAnimConfig(string propPath, string header)
        {
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            SerializedProperty animTypeProp = serializedObject.FindProperty($"{propPath}.animationType");
            EditorGUILayout.PropertyField(animTypeProp, new GUIContent("Properties"));
            SimpleMenuAnimationType animType = (SimpleMenuAnimationType)animTypeProp.intValue;

            if ((animType & SimpleMenuAnimationType.Alpha) != 0)
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty($"{propPath}.fromAlpha"),
                    new GUIContent("From Alpha"));

            if ((animType & SimpleMenuAnimationType.Position) != 0)
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty($"{propPath}.fromPositionOffset"),
                    new GUIContent("From Position Offset"));

            if ((animType & SimpleMenuAnimationType.Scale) != 0)
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty($"{propPath}.fromScale"),
                    new GUIContent("From Scale"));

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty($"{propPath}.duration"),
                new GUIContent("Duration"));

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty($"{propPath}.curve"),
                new GUIContent("Curve"));

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty($"{propPath}.exitSpeedMultiplier"),
                new GUIContent("Exit Speed Multiplier"));
        }
    }
}

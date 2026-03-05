using Laubrary.SimpleMenu;
using UnityEditor;
using UnityEngine;

namespace Laubrary.SimpleMenu.Editor
{
    [CustomEditor(typeof(SimpleMenuVisual))]
    public class SimpleMenuVisualEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ── Container transition ──────────────────────────────────────────
            SerializedProperty animTypeProp = serializedObject.FindProperty("animationType");
            EditorGUILayout.PropertyField(animTypeProp, new GUIContent("Transition Animation"));
            SimpleMenuAnimationType animType = (SimpleMenuAnimationType)animTypeProp.intValue;

            if (animType != SimpleMenuAnimationType.None)
            {
                EditorGUI.indentLevel++;

                if ((animType & SimpleMenuAnimationType.Alpha) != 0)
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("enterFromAlpha"),
                        new GUIContent("From Alpha"));

                if ((animType & SimpleMenuAnimationType.Position) != 0)
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("enterFromPositionOffset"),
                        new GUIContent("From Position Offset"));

                if ((animType & SimpleMenuAnimationType.Scale) != 0)
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty("enterFromScale"),
                        new GUIContent("From Scale"));

                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("enterDuration"),
                    new GUIContent("Duration"));

                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("enterCurve"),
                    new GUIContent("Curve"));

                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("exitSpeedMultiplier"),
                    new GUIContent("Exit Speed Multiplier"));

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // ── Animate Each ──────────────────────────────────────────────────
            SerializedProperty animEachProp = serializedObject.FindProperty("animateElements");
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
                float dur = serializedObject.FindProperty("enterDuration").floatValue;
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

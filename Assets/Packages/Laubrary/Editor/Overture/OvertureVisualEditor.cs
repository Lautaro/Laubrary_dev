using Laubrary.Overture;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Overture.Editor
{
    [CustomEditor(typeof(OvertureVisual))]
    public class OvertureVisualEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ── Top-level fields (no headlines) ───────────────────────────────
            SerializedProperty targetModeProp = serializedObject.FindProperty("targetMode");
            EditorGUILayout.PropertyField(targetModeProp, new GUIContent("Targets"));
            VisualTargetMode targetMode = (VisualTargetMode)targetModeProp.intValue;

            EditorGUI.indentLevel++;
            if ((targetMode & VisualTargetMode.Children) != 0)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("childDepth"), new GUIContent("Depth"));
            if ((targetMode & VisualTargetMode.Targets) != 0)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetList"), new GUIContent("List"), true);
            EditorGUI.indentLevel--;

            SerializedProperty directionProp = serializedObject.FindProperty("direction");
            EditorGUILayout.PropertyField(directionProp, new GUIContent("Direction"));
            VisualDirection direction = (VisualDirection)directionProp.intValue;

            SerializedProperty animTypeProp = serializedObject.FindProperty("animationType");
            EditorGUILayout.PropertyField(animTypeProp, new GUIContent("Properties"));
            VisualAnimationType animType = (VisualAnimationType)animTypeProp.intValue;

            EditorGUILayout.Space();

            // ── Animation ─────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            if ((animType & VisualAnimationType.Alpha) != 0)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enterFromAlpha"), new GUIContent("From Alpha"));
            if ((animType & VisualAnimationType.Position) != 0)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enterFromPositionOffset"), new GUIContent("From Position Offset"));
            if ((animType & VisualAnimationType.Scale) != 0)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enterFromScale"), new GUIContent("From Scale"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("enterDuration"), new GUIContent("Duration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enterCurve"), new GUIContent("Curve"));

            if (direction == VisualDirection.Both)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("exitSpeedMultiplier"), new GUIContent("Exit Speed Multiplier"));

            EditorGUI.indentLevel--;

            // ── Blocking (only when relevant) ─────────────────────────────────
            if (direction != VisualDirection.ExitOnly)
            {
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("dontBlockOnEnter"),
                    new GUIContent("Don't Block OnEnter",
                        "When checked, the state proceeds to IsEntered without waiting for this animation."));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}

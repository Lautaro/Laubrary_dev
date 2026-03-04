using Laubrary.Overture;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Overture.Editor
{
    [CustomEditor(typeof(OvertureMultiTransition))]
    public class OvertureMultiTransitionEditor : UnityEditor.Editor
    {
        private SerializedProperty transitionTypeProp;
        private SerializedProperty transitionTargetsProp;

        private SerializedProperty enableScaleProp;
        private SerializedProperty scaleTargetProp;
        private SerializedProperty scaleDurationProp;
        private SerializedProperty scaleCurveProp;

        private SerializedProperty enablePositionProp;
        private SerializedProperty positionTargetProp;
        private SerializedProperty useLocalPositionProp;
        private SerializedProperty positionDurationProp;
        private SerializedProperty positionCurveProp;

        private SerializedProperty enableAlphaProp;
        private SerializedProperty alphaTargetProp;
        private SerializedProperty alphaDurationProp;
        private SerializedProperty alphaCurveProp;
        private SerializedProperty includeSpriteRenderersProp;
        private SerializedProperty includeMeshRenderersProp;
        private SerializedProperty includeCanvasGroupsProp;
        private SerializedProperty hierarchyDepthProp;

        private bool scaleTransitionFoldout = true;
        private bool positionTransitionFoldout = true;
        private bool alphaTransitionFoldout = true;

        private void OnEnable()
        {
            transitionTypeProp = serializedObject.FindProperty("_transitionType");
            transitionTargetsProp = serializedObject.FindProperty("transitionTargets");

            enableScaleProp = serializedObject.FindProperty("enableScale");
            scaleTargetProp = serializedObject.FindProperty("scaleTarget");
            scaleDurationProp = serializedObject.FindProperty("scaleDuration");
            scaleCurveProp = serializedObject.FindProperty("scaleCurve");

            enablePositionProp = serializedObject.FindProperty("enablePosition");
            positionTargetProp = serializedObject.FindProperty("positionTarget");
            useLocalPositionProp = serializedObject.FindProperty("useLocalPosition");
            positionDurationProp = serializedObject.FindProperty("positionDuration");
            positionCurveProp = serializedObject.FindProperty("positionCurve");

            enableAlphaProp = serializedObject.FindProperty("enableAlpha");
            alphaTargetProp = serializedObject.FindProperty("alphaTarget");
            alphaDurationProp = serializedObject.FindProperty("alphaDuration");
            alphaCurveProp = serializedObject.FindProperty("alphaCurve");
            includeSpriteRenderersProp = serializedObject.FindProperty("includeSpriteRenderers");
            includeMeshRenderersProp = serializedObject.FindProperty("includeMeshRenderers");
            includeCanvasGroupsProp = serializedObject.FindProperty("includeCanvasGroups");
            hierarchyDepthProp = serializedObject.FindProperty("hierarchyDepth");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Transition Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(transitionTypeProp, new GUIContent("Transition Type"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Transition Targets", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(transitionTargetsProp, true);

            EditorGUILayout.Space();
            scaleTransitionFoldout = EditorGUILayout.Foldout(scaleTransitionFoldout, "Scale Transition", true, EditorStyles.foldoutHeader);
            if (scaleTransitionFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(enableScaleProp, new GUIContent("Enable"));
                if (enableScaleProp.boolValue)
                {
                    EditorGUILayout.PropertyField(scaleTargetProp);
                    EditorGUILayout.PropertyField(scaleDurationProp);
                    EditorGUILayout.PropertyField(scaleCurveProp);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            positionTransitionFoldout = EditorGUILayout.Foldout(positionTransitionFoldout, "Position Transition", true, EditorStyles.foldoutHeader);
            if (positionTransitionFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(enablePositionProp, new GUIContent("Enable"));
                if (enablePositionProp.boolValue)
                {
                    EditorGUILayout.PropertyField(positionTargetProp);
                    EditorGUILayout.PropertyField(useLocalPositionProp);
                    EditorGUILayout.PropertyField(positionDurationProp);
                    EditorGUILayout.PropertyField(positionCurveProp);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            alphaTransitionFoldout = EditorGUILayout.Foldout(alphaTransitionFoldout, "Alpha Transition", true, EditorStyles.foldoutHeader);
            if (alphaTransitionFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(enableAlphaProp, new GUIContent("Enable"));
                if (enableAlphaProp.boolValue)
                {
                    EditorGUILayout.PropertyField(alphaTargetProp);
                    EditorGUILayout.PropertyField(alphaDurationProp);
                    EditorGUILayout.PropertyField(alphaCurveProp);
                    EditorGUILayout.PropertyField(includeSpriteRenderersProp);
                    EditorGUILayout.PropertyField(includeMeshRenderersProp);
                    EditorGUILayout.PropertyField(includeCanvasGroupsProp);
                    EditorGUILayout.PropertyField(hierarchyDepthProp);
                }
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}

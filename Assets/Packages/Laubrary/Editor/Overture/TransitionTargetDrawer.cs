using Laubrary.Overture;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Overture.Editor
{
    [CustomPropertyDrawer(typeof(TransitionTarget))]
    public class TransitionTargetDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect gameObjectRect = new Rect(position.x, position.y, position.width, lineHeight);
            Rect togglesRect = new Rect(position.x, position.y + lineHeight + spacing, position.width, lineHeight);

            SerializedProperty gameObjectProp = property.FindPropertyRelative("gameObject");
            SerializedProperty alphaProp = property.FindPropertyRelative("alpha");
            SerializedProperty positionProp = property.FindPropertyRelative("position");
            SerializedProperty scaleProp = property.FindPropertyRelative("scale");

            EditorGUI.PropertyField(gameObjectRect, gameObjectProp, GUIContent.none);

            float toggleWidth = (togglesRect.width - 10f) / 3f;
            float toggleX = togglesRect.x;

            Rect alphaRect = new Rect(toggleX, togglesRect.y, toggleWidth, lineHeight);
            toggleX += toggleWidth + 5f;
            Rect positionRect = new Rect(toggleX, togglesRect.y, toggleWidth, lineHeight);
            toggleX += toggleWidth + 5f;
            Rect scaleRect = new Rect(toggleX, togglesRect.y, toggleWidth, lineHeight);

            alphaProp.boolValue = EditorGUI.ToggleLeft(alphaRect, "Alpha", alphaProp.boolValue);
            positionProp.boolValue = EditorGUI.ToggleLeft(positionRect, "Position", positionProp.boolValue);
            scaleProp.boolValue = EditorGUI.ToggleLeft(scaleRect, "Scale", scaleProp.boolValue);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
        }
    }
}


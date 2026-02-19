using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(BoolStatModifierBase), true)]
public class BoolStatModifierPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var descriptionProp = property.FindPropertyRelative("description");
        var modifierTypeProp = property.FindPropertyRelative("modifierType");
        var isPausedProp = property.FindPropertyRelative("IsPaused");

        float pausedWidth = 60f;
        float labelWidth = position.width * 0.4f;
        float valueWidth = position.width - labelWidth - (isPausedProp != null && isPausedProp.boolValue ? pausedWidth + 5 : 0);

        Rect descRect = new Rect(position.x, position.y, labelWidth, position.height);
        Rect valueRect = new Rect(position.x + labelWidth, position.y, valueWidth, position.height);
        Rect pausedRect = new Rect(position.x + labelWidth + valueWidth + 5, position.y, pausedWidth, position.height);

        if (descriptionProp != null)
        {
            EditorGUI.LabelField(descRect, descriptionProp.stringValue);
        }
        
        if (modifierTypeProp != null)
        {
            string modifierTypeText = ((BoolStatModifierBase.BoolStatModifierType)modifierTypeProp.intValue).ToString();
            EditorGUI.LabelField(valueRect, modifierTypeText);
        }
        
        if (isPausedProp != null && isPausedProp.boolValue)
        {
            EditorGUI.LabelField(pausedRect, "(Paused)", EditorStyles.miniLabel);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}

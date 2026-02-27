using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FloatStatSwitchModifier), true)]
public class FloatStatSwitchModifierPropertyDrawer : PropertyDrawer
{
    private const float LEFT_COLUMN_WIDTH = 100f;
    private const float SPACING = 5f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var descProp = property.FindPropertyRelative("description");
        var modValueProp = property.FindPropertyRelative("modValue");
        var conditionMetProp = property.FindPropertyRelative("conditionMet");

        if (descProp == null || modValueProp == null || conditionMetProp == null)
        {
            EditorGUI.LabelField(position, "FloatStatSwitchModifier: Missing properties");
            EditorGUI.EndProperty();
            return;
        }

        bool conditionMet = conditionMetProp.boolValue;
        float value = modValueProp.floatValue;
        string sign = value >= 0 ? "+" : "";
        string valueText = $"{sign}{value:F1}";

        Rect mainRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        Rect leftRect = new Rect(mainRect.x, mainRect.y, LEFT_COLUMN_WIDTH, mainRect.height);
        Rect descRect = new Rect(mainRect.x + LEFT_COLUMN_WIDTH + SPACING, mainRect.y,
            mainRect.width - LEFT_COLUMN_WIDTH - SPACING, mainRect.height);

        GUIStyle valueStyle = new GUIStyle(EditorStyles.boldLabel);
        valueStyle.normal.textColor = conditionMet ? new Color(0.4f, 0.9f, 0.4f) : new Color(0.9f, 0.3f, 0.3f);

        EditorGUI.LabelField(leftRect, valueText, valueStyle);
        EditorGUI.LabelField(descRect, descProp.stringValue);

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}

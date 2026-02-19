using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(IntStat), true)]
public class IntStatPropertyDrawer : PropertyDrawer
{
    private const float LINE_HEIGHT = 18f;
    private const float SPACING = 2f;
    private const float PADDING = 4f;
    private const float BASE_VALUE_WIDTH = 60f;
    private const float FOLDOUT_WIDTH = 12f;

    private static readonly Color MODIFIER_BG_COLOR = new Color(0.5f, 0.6f, 0.65f, 0.2f);

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var baseValueProp = property.FindPropertyRelative("baseValue");
        var minValueProp = property.FindPropertyRelative("minValue");
        var maxValueProp = property.FindPropertyRelative("maxValue");
        var totalValueProp = property.FindPropertyRelative("totalValue");
        var modsProp = property.FindPropertyRelative("intStatMods");

        if (modsProp.arraySize == 0)
        {
            string foldoutKey = property.propertyPath + "_modifiers";
            bool foldout = EditorPrefs.GetBool(foldoutKey, false);

            Rect foldoutRect = new Rect(position.x, position.y, FOLDOUT_WIDTH, LINE_HEIGHT);
            bool newFoldout = EditorGUI.Foldout(foldoutRect, foldout, GUIContent.none);
            
            if (newFoldout != foldout)
            {
                EditorPrefs.SetBool(foldoutKey, newFoldout);
            }

            Rect labelRect = new Rect(position.x + FOLDOUT_WIDTH + 2f, position.y, EditorGUIUtility.labelWidth - FOLDOUT_WIDTH - 2f, LINE_HEIGHT);
            Rect valueRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, LINE_HEIGHT);

            EditorGUI.LabelField(labelRect, property.displayName);
            EditorGUI.PropertyField(valueRect, baseValueProp, GUIContent.none);

            if (newFoldout)
            {
                Rect currentRect = new Rect(position.x, position.y + LINE_HEIGHT + SPACING, position.width, LINE_HEIGHT);
                
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(currentRect, minValueProp, new GUIContent("Min Value"));
                currentRect.y += LINE_HEIGHT + SPACING;
                
                EditorGUI.PropertyField(currentRect, maxValueProp, new GUIContent("Max Value"));
                EditorGUI.indentLevel--;
            }
        }
        else
        {
            Rect boxRect = new Rect(position.x, position.y, position.width, GetPropertyHeight(property, label));
            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

            Rect currentRect = new Rect(position.x + PADDING, position.y + PADDING, position.width - PADDING * 2, LINE_HEIGHT);

            string foldoutKey = property.propertyPath + "_modifiers";
            bool foldout = EditorPrefs.GetBool(foldoutKey, true);

            Rect foldoutRect = new Rect(currentRect.x, currentRect.y, FOLDOUT_WIDTH, LINE_HEIGHT);
            bool newFoldout = EditorGUI.Foldout(foldoutRect, foldout, GUIContent.none);
            
            if (newFoldout != foldout)
            {
                EditorPrefs.SetBool(foldoutKey, newFoldout);
            }

            float labelStartX = currentRect.x + FOLDOUT_WIDTH + 2f;
            float labelWidth = EditorGUIUtility.labelWidth - FOLDOUT_WIDTH - 2f;
            float totalValueWidth = 50f;

            Rect labelRect = new Rect(labelStartX, currentRect.y, labelWidth, LINE_HEIGHT);
            Rect totalValueRect = new Rect(labelStartX + labelWidth, currentRect.y, totalValueWidth, LINE_HEIGHT);

            EditorGUI.LabelField(labelRect, property.displayName);
            
            GUIStyle boldWhiteStyle = new GUIStyle(EditorStyles.boldLabel);
            boldWhiteStyle.normal.textColor = Color.white;
            EditorGUI.LabelField(totalValueRect, totalValueProp.intValue.ToString(), boldWhiteStyle);

            currentRect.y += LINE_HEIGHT + SPACING;

            if (newFoldout)
            {
                float modifiersSectionHeight = GetModifiersSectionHeight(property);
                Rect modifierBgRect = new Rect(currentRect.x, currentRect.y, currentRect.width, modifiersSectionHeight);
                EditorGUI.DrawRect(modifierBgRect, MODIFIER_BG_COLOR);

                Rect baseValueLabelRect = new Rect(currentRect.x, currentRect.y, 70f, LINE_HEIGHT);
                Rect baseValueFieldRect = new Rect(currentRect.x + 75f, currentRect.y, BASE_VALUE_WIDTH, LINE_HEIGHT);
                
                EditorGUI.LabelField(baseValueLabelRect, "Base value");
                EditorGUI.PropertyField(baseValueFieldRect, baseValueProp, GUIContent.none);

                currentRect.y += LINE_HEIGHT + SPACING;

                Rect minValueLabelRect = new Rect(currentRect.x, currentRect.y, 70f, LINE_HEIGHT);
                Rect minValueFieldRect = new Rect(currentRect.x + 75f, currentRect.y, BASE_VALUE_WIDTH, LINE_HEIGHT);
                
                EditorGUI.LabelField(minValueLabelRect, "Min value");
                EditorGUI.PropertyField(minValueFieldRect, minValueProp, GUIContent.none);

                currentRect.y += LINE_HEIGHT + SPACING;

                Rect maxValueLabelRect = new Rect(currentRect.x, currentRect.y, 70f, LINE_HEIGHT);
                Rect maxValueFieldRect = new Rect(currentRect.x + 75f, currentRect.y, BASE_VALUE_WIDTH, LINE_HEIGHT);
                
                EditorGUI.LabelField(maxValueLabelRect, "Max value");
                EditorGUI.PropertyField(maxValueFieldRect, maxValueProp, GUIContent.none);

                currentRect.y += LINE_HEIGHT + SPACING;

                if (modsProp.arraySize > 0)
                {
                    for (int i = 0; i < modsProp.arraySize; i++)
                    {
                        SerializedProperty modProp = modsProp.GetArrayElementAtIndex(i);
                        
                        float mainModHeight = EditorGUI.GetPropertyHeight(modProp);
                        Rect mainModRect = new Rect(currentRect.x, currentRect.y, currentRect.width, mainModHeight);
                        
                        EditorGUI.PropertyField(mainModRect, modProp, GUIContent.none, true);
                        currentRect.y += mainModHeight + SPACING;
                    }
                }
            }
        }

        EditorGUI.EndProperty();
    }

    private float GetModifiersSectionHeight(SerializedProperty property)
    {
        float height = LINE_HEIGHT + SPACING; // Base value
        height += LINE_HEIGHT + SPACING; // Min value
        height += LINE_HEIGHT + SPACING; // Max value

        var modsProp = property.FindPropertyRelative("intStatMods");
        for (int i = 0; i < modsProp.arraySize; i++)
        {
            SerializedProperty modProp = modsProp.GetArrayElementAtIndex(i);
            height += EditorGUI.GetPropertyHeight(modProp) + SPACING;
        }

        return height;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var modsProp = property.FindPropertyRelative("intStatMods");
        
        if (modsProp.arraySize == 0)
        {
            string foldoutKey = property.propertyPath + "_modifiers";
            bool foldout = EditorPrefs.GetBool(foldoutKey, false);
            
            float height = LINE_HEIGHT;
            if (foldout)
            {
                height += LINE_HEIGHT + SPACING; // Min value
                height += LINE_HEIGHT + SPACING; // Max value
            }
            return height;
        }

        float totalHeight = PADDING * 2;
        totalHeight += LINE_HEIGHT + SPACING;

        string foldoutKey2 = property.propertyPath + "_modifiers";
        bool foldout2 = EditorPrefs.GetBool(foldoutKey2, true);

        if (foldout2)
        {
            totalHeight += GetModifiersSectionHeight(property);
        }

        return totalHeight;
    }
}

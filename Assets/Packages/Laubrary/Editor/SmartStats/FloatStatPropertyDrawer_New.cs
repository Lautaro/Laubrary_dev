using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FloatStat), true)]
public class FloatStatPropertyDrawer : PropertyDrawer
{
    private const float LINE_HEIGHT = 18f;
    private const float MERGED_GROUP_LINE_HEIGHT = 16f;
    private const float MERGED_INDENT = 15f;
    private const float MERGED_LINE_HEIGHT = 14f;
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
        var modsProp = property.FindPropertyRelative("floatStatMods");

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
            var floatStat = fieldInfo.GetValue(property.serializedObject.targetObject);
            var debugValues = GetDebugIntermediateValues(floatStat);

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
            EditorGUI.LabelField(totalValueRect, totalValueProp.floatValue.ToString("F1"), boldWhiteStyle);

            currentRect.y += LINE_HEIGHT + SPACING;

            if (newFoldout)
            {
                Rect modifierBgRect = new Rect(currentRect.x, currentRect.y, currentRect.width, GetModifiersSectionHeight(property, debugValues));
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
                    int debugValueIndex = 1;
                    
                    for (int i = 0; i < modsProp.arraySize; i++)
                    {
                        SerializedProperty modProp = modsProp.GetArrayElementAtIndex(i);
                        
                        var isMergedIntoAnotherProp = modProp.FindPropertyRelative("isMergedIntoAnother");
                        bool isMergedIntoAnother = isMergedIntoAnotherProp != null && isMergedIntoAnotherProp.boolValue;
                        
                        if (isMergedIntoAnother)
                        {
                            continue;
                        }
                        
                        var mergedMultipliersProp = modProp.FindPropertyRelative("mergedMultipliers");
                        bool hasMergedMultipliers = mergedMultipliersProp != null && mergedMultipliersProp.arraySize > 0;

                        if (hasMergedMultipliers)
                        {
                            for (int j = 0; j < mergedMultipliersProp.arraySize; j++)
                            {
                                SerializedProperty mergedProp = mergedMultipliersProp.GetArrayElementAtIndex(j);
                                
                                var mergedDescProp = mergedProp.FindPropertyRelative("description");
                                var mergedValueProp = mergedProp.FindPropertyRelative("modValue");
                                
                                Rect mergedRect = new Rect(currentRect.x + MERGED_INDENT, currentRect.y, 
                                    currentRect.width - MERGED_INDENT, MERGED_LINE_HEIGHT);
                                
                                string mergedValueText = FormatMergedMultiplierValue(mergedValueProp.floatValue);
                                
                                GUIStyle mergedStyle = new GUIStyle(EditorStyles.label);
                                mergedStyle.fontSize = 10;
                                mergedStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                                
                                float leftColumnWidth = 100f;
                                Rect leftRect = new Rect(mergedRect.x, mergedRect.y, leftColumnWidth, mergedRect.height);
                                Rect descRect = new Rect(mergedRect.x + leftColumnWidth + 5f, mergedRect.y, 
                                    mergedRect.width - leftColumnWidth - 5f, mergedRect.height);
                                
                                EditorGUI.LabelField(leftRect, $"({mergedValueText})", mergedStyle);
                                EditorGUI.LabelField(descRect, mergedDescProp.stringValue, mergedStyle);
                                
                                currentRect.y += MERGED_LINE_HEIGHT + 1f;
                            }
                            
                            var mainDescProp = modProp.FindPropertyRelative("description");
                            var mainValueProp = modProp.FindPropertyRelative("modValue");
                            var mainMergedProp = modProp.FindPropertyRelative("mergedMultipliers");
                            
                            float mainOriginalValue = mainValueProp.floatValue;
                            if (mainMergedProp != null && mainMergedProp.arraySize > 0)
                            {
                                for (int j = 0; j < mainMergedProp.arraySize; j++)
                                {
                                    var subProp = mainMergedProp.GetArrayElementAtIndex(j);
                                    var subValueProp = subProp.FindPropertyRelative("modValue");
                                    mainOriginalValue -= subValueProp.floatValue;
                                }
                            }
                            
                            Rect mainMergedRect = new Rect(currentRect.x + MERGED_INDENT, currentRect.y, 
                                currentRect.width - MERGED_INDENT, MERGED_LINE_HEIGHT);
                            
                            string mainOriginalValueText = FormatMergedMultiplierValue(mainOriginalValue);
                            
                            GUIStyle mainMergedStyle = new GUIStyle(EditorStyles.label);
                            mainMergedStyle.fontSize = 10;
                            mainMergedStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                            
                            float mainLeftColumnWidth = 100f;
                            Rect mainLeftRect = new Rect(mainMergedRect.x, mainMergedRect.y, mainLeftColumnWidth, mainMergedRect.height);
                            Rect mainDescRect = new Rect(mainMergedRect.x + mainLeftColumnWidth + 5f, mainMergedRect.y, 
                                mainMergedRect.width - mainLeftColumnWidth - 5f, mainMergedRect.height);
                            
                            EditorGUI.LabelField(mainLeftRect, $"({mainOriginalValueText})", mainMergedStyle);
                            EditorGUI.LabelField(mainDescRect, mainDescProp.stringValue, mainMergedStyle);
                            
                            currentRect.y += MERGED_LINE_HEIGHT + 1f;
                            
                            float intermediateValue = (debugValues != null && debugValueIndex < debugValues.Count) ? (float)debugValues[debugValueIndex] : 0f;
                            bool showResult = debugValues != null && debugValues.Count > 0;
                            
                            Rect mergedResultRect = new Rect(currentRect.x, currentRect.y, currentRect.width, MERGED_GROUP_LINE_HEIGHT);
                            string totalMergedValueText = FormatMergedMultiplierValue(mainValueProp.floatValue);
                            string mergedResultText = showResult ? $"{totalMergedValueText} (Merged) = {intermediateValue:F1}" : $"{totalMergedValueText} (Merged)";
                            
                            GUIStyle mergedResultStyle = new GUIStyle(EditorStyles.boldLabel);
                            mergedResultStyle.normal.textColor = new Color(0.8f, 1f, 0.8f);
                            mergedResultStyle.fontSize = 11;
                            
                            EditorGUI.LabelField(mergedResultRect, mergedResultText, mergedResultStyle);
                            currentRect.y += MERGED_GROUP_LINE_HEIGHT + SPACING;
                        }
                        else
                        {
                            float mainModHeight = EditorGUI.GetPropertyHeight(modProp);
                            Rect mainModRect = new Rect(currentRect.x, currentRect.y, currentRect.width, mainModHeight);
                            
                            float intermediateValue = (debugValues != null && debugValueIndex < debugValues.Count) ? (float)debugValues[debugValueIndex] : 0f;
                            bool showResult = debugValues != null && debugValues.Count > 0;
                            
                            FloatStatModifierPropertyDrawer.SetIntermediateResult(modProp.propertyPath, intermediateValue, showResult);
                            
                            EditorGUI.PropertyField(mainModRect, modProp, GUIContent.none, true);
                            currentRect.y += mainModHeight + SPACING;
                        }
                        
                        debugValueIndex++;
                    }
                }
            }
        }

        EditorGUI.EndProperty();
    }

    private string FormatMergedMultiplierValue(float value)
    {
        float percentage = value * 100f;
        string sign = percentage >= 0 ? "+" : "";
        return $"{sign}{percentage:F0}%";
    }

    private System.Collections.IList GetDebugIntermediateValues(object floatStat)
    {
#if UNITY_EDITOR
        if (floatStat == null) return null;
        
        var field = floatStat.GetType().GetField("debugIntermediateValues", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        if (field != null)
        {
            var values = field.GetValue(floatStat) as System.Collections.IList;
            return values;
        }
#endif
        return null;
    }

    private float GetModifiersSectionHeight(SerializedProperty property, System.Collections.IList debugValues)
    {
        float height = LINE_HEIGHT + SPACING; // Base value
        height += LINE_HEIGHT + SPACING; // Min value  
        height += LINE_HEIGHT + SPACING; // Max value

        var modsProp = property.FindPropertyRelative("floatStatMods");
        for (int i = 0; i < modsProp.arraySize; i++)
        {
            SerializedProperty modProp = modsProp.GetArrayElementAtIndex(i);
            
            var mergedMultipliersProp = modProp.FindPropertyRelative("mergedMultipliers");
            bool hasMergedMultipliers = mergedMultipliersProp != null && mergedMultipliersProp.arraySize > 0;

            if (hasMergedMultipliers)
            {
                for (int j = 0; j < mergedMultipliersProp.arraySize; j++)
                {
                    SerializedProperty mergedProp = mergedMultipliersProp.GetArrayElementAtIndex(j);
                    height += MERGED_LINE_HEIGHT + 1f;
                }
                height += MERGED_LINE_HEIGHT + 1f; // Main merged line
                height += MERGED_GROUP_LINE_HEIGHT + SPACING; // Merged result line
            }

            height += EditorGUI.GetPropertyHeight(modProp) + SPACING;
        }

        return height;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var modsProp = property.FindPropertyRelative("floatStatMods");
        
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
            var floatStat = fieldInfo.GetValue(property.serializedObject.targetObject);
            var debugValues = GetDebugIntermediateValues(floatStat);
            totalHeight += GetModifiersSectionHeight(property, debugValues);
        }

        return totalHeight;
    }
}

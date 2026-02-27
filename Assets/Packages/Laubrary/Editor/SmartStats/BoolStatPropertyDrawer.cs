using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(BoolStat), true)]
public class BoolStatPropertyDrawer : PropertyDrawer
{
    private const float LINE_HEIGHT = 18f;
    private const float SPACING = 2f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var baseValueProp = property.FindPropertyRelative("baseValue");
        var modsProp = property.FindPropertyRelative("boolStatMods");

        Rect currentRect = new Rect(position.x, position.y, position.width, LINE_HEIGHT);

        if (modsProp.arraySize == 0)
        {
            EditorGUI.PropertyField(currentRect, baseValueProp, new GUIContent(property.displayName));
        }
        else
        {
            bool totalValue = baseValueProp.boolValue;
            for (int i = 0; i < modsProp.arraySize; i++)
            {
                var modProp = modsProp.GetArrayElementAtIndex(i);
                var isPausedProp = modProp.FindPropertyRelative("isPaused");
                
                if (isPausedProp != null && !isPausedProp.boolValue)
                {
                    var modifierTypeProp = modProp.FindPropertyRelative("modifierType");
                    if (modifierTypeProp != null)
                    {
                        int modifierType = modifierTypeProp.intValue;
                        switch (modifierType)
                        {
                            case 0: // Flip
                                totalValue = !baseValueProp.boolValue;
                                break;
                            case 1: // AlwaysTrue
                                totalValue = true;
                                break;
                            case 2: // AlwaysFalse
                                totalValue = false;
                                break;
                        }
                    }
                }
            }

            EditorGUI.LabelField(currentRect, property.displayName, totalValue.ToString(), EditorStyles.boldLabel);
            currentRect.y += LINE_HEIGHT + SPACING;

            string foldoutKey = property.propertyPath + "_modifiers";
            bool foldout = EditorPrefs.GetBool(foldoutKey, true);
            bool newFoldout = EditorGUI.Foldout(currentRect, foldout, "Modifiers");
            
            if (newFoldout != foldout)
            {
                EditorPrefs.SetBool(foldoutKey, newFoldout);
            }
            currentRect.y += LINE_HEIGHT + SPACING;

            if (newFoldout)
            {
                EditorGUI.indentLevel++;
                
                EditorGUI.PropertyField(currentRect, baseValueProp, new GUIContent("Base Value"));
                currentRect.y += LINE_HEIGHT + SPACING;
                
                for (int i = 0; i < modsProp.arraySize; i++)
                {
                    SerializedProperty modProp = modsProp.GetArrayElementAtIndex(i);
                    float modHeight = EditorGUI.GetPropertyHeight(modProp);
                    currentRect.height = modHeight;
                    EditorGUI.PropertyField(currentRect, modProp, GUIContent.none, true);
                    currentRect.y += modHeight + SPACING;
                }
                EditorGUI.indentLevel--;
            }
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var modsProp = property.FindPropertyRelative("boolStatMods");
        
        if (modsProp.arraySize == 0)
        {
            return LINE_HEIGHT;
        }

        float height = LINE_HEIGHT + SPACING;
        height += LINE_HEIGHT + SPACING;

        string foldoutKey = property.propertyPath + "_modifiers";
        bool foldout = EditorPrefs.GetBool(foldoutKey, true);

        if (foldout)
        {
            height += LINE_HEIGHT + SPACING;
            
            for (int i = 0; i < modsProp.arraySize; i++)
            {
                SerializedProperty modProp = modsProp.GetArrayElementAtIndex(i);
                height += EditorGUI.GetPropertyHeight(modProp) + SPACING;
            }
        }

        return height;
    }
}

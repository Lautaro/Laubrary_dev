using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FloatStatModifier), true)]
public class FloatStatModifierPropertyDrawer : PropertyDrawer
{
    private const float MERGED_INDENT = 15f;
    private const float MERGED_LINE_HEIGHT = 14f;
    private const float PROGRESS_BAR_HEIGHT = 3f;

    private static System.Collections.Generic.Dictionary<string, float> intermediateResults = 
        new System.Collections.Generic.Dictionary<string, float>();
    private static System.Collections.Generic.Dictionary<string, bool> showResults = 
        new System.Collections.Generic.Dictionary<string, bool>();

    public static void SetIntermediateResult(string propertyPath, float value, bool show)
    {
        intermediateResults[propertyPath] = value;
        showResults[propertyPath] = show;
    }

    public float IntermediateResult { get; set; } = 0f;
    public bool ShowIntermediateResult { get; set; } = false;
    public bool IsMerged { get; set; } = false;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        
        string propertyPath = property.propertyPath;
        float intermediateValue = 0f;
        bool showResult = false;
        
        if (intermediateResults.ContainsKey(propertyPath))
        {
            intermediateValue = intermediateResults[propertyPath];
            showResult = showResults.ContainsKey(propertyPath) && showResults[propertyPath];
        }

        var descriptionProp = property.FindPropertyRelative("description");
        var modValueProp = property.FindPropertyRelative("modValue");
        var isPausedProp = property.FindPropertyRelative("isPaused");
        var isMergedProp = property.FindPropertyRelative("isMergedIntoAnother");
        var priorityProp = property.FindPropertyRelative("priority");
        
        var timerProp = property.FindPropertyRelative("timer");
        var timerModeProp = property.FindPropertyRelative("timerMode");
        
        bool isTimed = timerProp != null && timerModeProp != null && timerModeProp.intValue != 0;

        bool isMerged = isMergedProp != null && isMergedProp.boolValue;

        string modifierType = property.type;
        string valueText = FormatModifierValue(modValueProp.floatValue, modifierType);

        float valueWidth = 80f;
        float resultWidth = 80f;
        float pausedWidth = 60f;
        float spacing = 5f;

        Rect drawRect = position;
        
        if (isMerged)
        {
            drawRect.x += MERGED_INDENT;
            drawRect.width -= MERGED_INDENT;
            drawRect.height = MERGED_LINE_HEIGHT;
        }

        Rect mainRect = new Rect(drawRect.x, drawRect.y, drawRect.width, EditorGUIUtility.singleLineHeight);

        float leftColumnWidth = 100f;
        
        Rect leftRect = new Rect(mainRect.x, mainRect.y, leftColumnWidth, mainRect.height);
        Rect descRect = new Rect(mainRect.x + leftColumnWidth + spacing, mainRect.y, 
            mainRect.width - leftColumnWidth - spacing, mainRect.height);

        if (isMerged)
        {
            GUIStyle mergedStyle = new GUIStyle(EditorStyles.label);
            mergedStyle.fontSize = 10;
            mergedStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            
            EditorGUI.LabelField(leftRect, $"({valueText})", mergedStyle);
            EditorGUI.LabelField(descRect, descriptionProp.stringValue, mergedStyle);
        }
        else
        {
            string leftText = valueText;
            
            if (showResult && !isPausedProp.boolValue)
            {
                leftText = $"{valueText} = {intermediateValue:F1}";
            }
            
            int? priority = null;
            if (priorityProp != null && priorityProp.boxedValue != null)
            {
                priority = (int)priorityProp.boxedValue;
            }
            
            if (priority.HasValue && priority.Value != 0)
            {
                GUIStyle priorityStyle = new GUIStyle(EditorStyles.miniLabel);
                priorityStyle.normal.textColor = new Color(0.7f, 0.7f, 1f);
                priorityStyle.alignment = TextAnchor.MiddleRight;
                
                Rect priorityRect = new Rect(leftRect.xMax - 30f, leftRect.y, 30f, leftRect.height);
                EditorGUI.LabelField(priorityRect, $"[{priority.Value}]", priorityStyle);
                
                leftRect.width -= 32f;
            }
            
            EditorGUI.LabelField(leftRect, leftText, EditorStyles.boldLabel);
            EditorGUI.LabelField(descRect, descriptionProp.stringValue);
            
            if (isPausedProp.boolValue)
            {
                GUIStyle pausedStyle = new GUIStyle(EditorStyles.miniLabel);
                pausedStyle.normal.textColor = new Color(1f, 0.5f, 0.5f);
                EditorGUI.LabelField(descRect, "(Paused)", pausedStyle);
            }
        }

        if (isTimed && !isMerged)
        {
            var durationProp = timerProp.FindPropertyRelative("Duration");
            var startTimeProp = timerProp.FindPropertyRelative("StartTime");
            
            if (durationProp != null && startTimeProp != null)
            {
                float duration = durationProp.floatValue;
                float startTime = startTimeProp.floatValue;
                float elapsed = UnityEngine.Time.time - startTime;
                float progress = duration > 0 ? Mathf.Clamp01(elapsed / duration) : 0f;

                Rect progressBarRect = new Rect(mainRect.x, mainRect.yMax + 2f, mainRect.width, PROGRESS_BAR_HEIGHT);
                
                EditorGUI.DrawRect(progressBarRect, new Color(0.2f, 0.2f, 0.2f, 0.5f));
                
                Rect progressFillRect = new Rect(progressBarRect.x, progressBarRect.y, progressBarRect.width * progress, progressBarRect.height);
                EditorGUI.DrawRect(progressFillRect, new Color(0.3f, 0.7f, 1f, 0.8f));
            }
        }

        EditorGUI.EndProperty();
    }

    private string FormatModifierValue(float value, string modifierType)
    {
        if (modifierType.Contains("FloatMultiplierModifier"))
        {
            float percentage = value * 100f;
            string sign = percentage >= 0 ? "+" : "";
            return $"{sign}{percentage:F0}%";
        }
        else if (modifierType.Contains("FloatDividerModifier"))
        {
            return $"/{value:F1}";
        }
        else
        {
            string sign = value >= 0 ? "+" : "";
            return $"{sign}{value:F1}";
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var isMergedProp = property.FindPropertyRelative("isMergedIntoAnother");
        bool isMerged = isMergedProp != null && isMergedProp.boolValue;
        
        if (isMerged)
        {
            return MERGED_LINE_HEIGHT;
        }
        
        var durationProp = property.FindPropertyRelative("duration");
        var timerProp = property.FindPropertyRelative("timer");
        bool isTimed = durationProp != null && timerProp != null;
        
        if (isTimed)
        {
            return EditorGUIUtility.singleLineHeight + PROGRESS_BAR_HEIGHT + 2f;
        }
        
        return EditorGUIUtility.singleLineHeight;
    }
}

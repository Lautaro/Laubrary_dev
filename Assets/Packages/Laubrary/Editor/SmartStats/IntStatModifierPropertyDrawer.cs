using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(IntStatModifier), true)]
public class IntStatModifierPropertyDrawer : PropertyDrawer
{
    private const float PROGRESS_BAR_HEIGHT = 3f;

    private static System.Collections.Generic.Dictionary<string, int> intermediateResults =
        new System.Collections.Generic.Dictionary<string, int>();
    private static System.Collections.Generic.Dictionary<string, int> previousResults =
        new System.Collections.Generic.Dictionary<string, int>();
    private static System.Collections.Generic.Dictionary<string, bool> showResults =
        new System.Collections.Generic.Dictionary<string, bool>();

    /// <summary>Sets the previous and current intermediate values for a modifier, used to compute the resolved delta in the inspector.</summary>
    public static void SetIntermediateResult(string propertyPath, int previousValue, int value, bool show)
    {
        previousResults[propertyPath] = previousValue;
        intermediateResults[propertyPath] = value;
        showResults[propertyPath] = show;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var descriptionProp = property.FindPropertyRelative("description");
        var modValueProp = property.FindPropertyRelative("modValue");
        var isPausedProp = property.FindPropertyRelative("isPaused");
        var priorityProp = property.FindPropertyRelative("priority");
        var timerProp = property.FindPropertyRelative("timer");
        var timerModeProp = property.FindPropertyRelative("timerMode");

        bool isTimed = timerProp != null && timerModeProp != null && timerModeProp.intValue != 0;

        string propertyPath = property.propertyPath;
        int intermediateValue = 0;
        int previousValue = 0;
        bool showResult = false;

        if (intermediateResults.ContainsKey(propertyPath))
        {
            intermediateValue = intermediateResults[propertyPath];
            previousValue = previousResults.ContainsKey(propertyPath) ? previousResults[propertyPath] : 0;
            showResult = showResults.ContainsKey(propertyPath) && showResults[propertyPath];
        }

        string valueText = FormatModifierValue(modValueProp.intValue, property.type);

        float leftColumnWidth = 100f;
        float spacing = 5f;

        Rect mainRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        Rect leftRect = new Rect(mainRect.x, mainRect.y, leftColumnWidth, mainRect.height);
        Rect descRect = new Rect(mainRect.x + leftColumnWidth + spacing, mainRect.y,
            mainRect.width - leftColumnWidth - spacing, mainRect.height);

        string leftText = valueText;

        if (showResult && isPausedProp != null && !isPausedProp.boolValue)
        {
            bool isMultiplierOrDivider = property.type.Contains("Multiplier") || property.type.Contains("Divider");
            if (isMultiplierOrDivider)
            {
                int delta = intermediateValue - previousValue;
                string deltaSign = delta >= 0 ? "+" : "";
                leftText = $"{valueText} ({deltaSign}{delta})";
            }
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

        if (isPausedProp != null && isPausedProp.boolValue)
        {
            GUIStyle pausedStyle = new GUIStyle(EditorStyles.miniLabel);
            pausedStyle.normal.textColor = new Color(1f, 0.5f, 0.5f);
            EditorGUI.LabelField(descRect, "(Paused)", pausedStyle);
        }
        else
        {
            EditorGUI.LabelField(descRect, descriptionProp.stringValue);
        }

        if (isTimed)
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

    private string FormatModifierValue(int value, string modifierType)
    {
        if (modifierType.Contains("IntMultiplierModifier"))
        {
            string sign = value >= 0 ? "+" : "";
            return $"{sign}{value}%";
        }
        else if (modifierType.Contains("IntDividerModifier"))
        {
            return $"/{value}";
        }
        else
        {
            string sign = value >= 0 ? "+" : "";
            return $"{sign}{value}";
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var timerModeProp = property.FindPropertyRelative("timerMode");
        bool isTimed = timerModeProp != null && timerModeProp.intValue != 0;

        if (isTimed)
        {
            return EditorGUIUtility.singleLineHeight + PROGRESS_BAR_HEIGHT + 2f;
        }

        return EditorGUIUtility.singleLineHeight;
    }
}

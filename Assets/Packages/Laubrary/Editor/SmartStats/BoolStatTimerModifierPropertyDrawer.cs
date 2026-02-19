using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(BoolStatTimerModifier), true)]
public class BoolStatTimerModifierPropertyDrawer : PropertyDrawer
{
    private const float PROGRESS_BAR_HEIGHT = 3f;
    private static bool isUpdateCallbackRegistered = false;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            if (!isUpdateCallbackRegistered)
            {
                EditorApplication.update += OnEditorUpdate;
                isUpdateCallbackRegistered = true;
            }
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            if (isUpdateCallbackRegistered)
            {
                EditorApplication.update -= OnEditorUpdate;
                isUpdateCallbackRegistered = false;
            }
        }
    }

    private static void OnEditorUpdate()
    {
        if (EditorApplication.isPlaying)
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var descriptionProp = property.FindPropertyRelative("description");
        var modifierTypeProp = property.FindPropertyRelative("modifierType");
        var timerProp = property.FindPropertyRelative("timer");
        var isPausedProp = property.FindPropertyRelative("IsPaused");

        if (descriptionProp == null || modifierTypeProp == null || timerProp == null)
        {
            EditorGUI.LabelField(position, "BoolStatTimerModifier: Missing properties");
            EditorGUI.EndProperty();
            return;
        }

        var durationProp = timerProp.FindPropertyRelative("Duration");
        var startTimeProp = timerProp.FindPropertyRelative("StartTime");

        if (durationProp == null || startTimeProp == null)
        {
            EditorGUI.LabelField(position, "Timer: Missing Duration/StartTime");
            EditorGUI.EndProperty();
            return;
        }

        float duration = durationProp.floatValue;
        float startTime = startTimeProp.floatValue;
        float elapsed = Time.time - startTime;
        float remaining = Mathf.Max(0, duration - elapsed);
        float progress = duration > 0 ? Mathf.Clamp01((duration - remaining) / duration) : 0f;

        string modifierTypeText = ((BoolStatModifierBase.BoolStatModifierType)modifierTypeProp.intValue).ToString();

        float pausedWidth = 60f;
        float labelWidth = position.width * 0.35f;
        float valueWidth = position.width - labelWidth - 10 - (isPausedProp != null && isPausedProp.boolValue ? pausedWidth : 0);

        Rect mainRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        Rect descRect = new Rect(mainRect.x + 5, mainRect.y, labelWidth, mainRect.height);
        Rect valueRect = new Rect(mainRect.x + labelWidth + 10, mainRect.y, valueWidth, mainRect.height);
        Rect pausedRect = new Rect(mainRect.xMax - pausedWidth, mainRect.y, pausedWidth, mainRect.height);

        Color barColor = new Color(0.4f, 0.2f, 0.27f, 1f);
        Color borderColor = new Color(0.4f, 0.2f, 0.27f, 1f);
        
        Rect progressBarRect = new Rect(mainRect.x, mainRect.y, mainRect.width, mainRect.height);
        Rect fillRect = new Rect(progressBarRect.x, progressBarRect.y, progressBarRect.width * (1f - progress), progressBarRect.height);
        
        EditorGUI.DrawRect(fillRect, barColor);
        EditorGUI.DrawRect(new Rect(progressBarRect.x, progressBarRect.y, progressBarRect.width, 2f), borderColor);
        EditorGUI.DrawRect(new Rect(progressBarRect.x, progressBarRect.yMax - 2f, progressBarRect.width, 2f), borderColor);
        EditorGUI.DrawRect(new Rect(progressBarRect.x, progressBarRect.y, 2f, progressBarRect.height), borderColor);
        EditorGUI.DrawRect(new Rect(progressBarRect.xMax - 2f, progressBarRect.y, 2f, progressBarRect.height), borderColor);

        EditorGUI.LabelField(descRect, descriptionProp.stringValue);

        string valueText = $"{remaining:F1}s ({modifierTypeText})";
        GUIStyle boldStyle = new GUIStyle(EditorStyles.label);
        boldStyle.fontStyle = FontStyle.Bold;
        EditorGUI.LabelField(valueRect, valueText, boldStyle);

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

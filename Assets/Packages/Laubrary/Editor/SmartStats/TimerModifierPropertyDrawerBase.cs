using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared base for FloatStatTimerModifier and IntStatTimerModifier property drawers.
/// Handles the repaint callback, progress bar rendering, layout, and height.
/// Subclasses only need to implement FormatValue() to produce the value string.
/// </summary>
public abstract class TimerModifierPropertyDrawerBase : PropertyDrawer
{
    protected const float PROGRESS_BAR_HEIGHT = 3f;
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
        if (EditorApplication.isPlaying && Selection.activeGameObject != null)
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    /// <summary>Returns the formatted modifier value string (e.g. "+20.0" or "+5").</summary>
    protected abstract string FormatValue(SerializedProperty property);

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var descriptionProp = property.FindPropertyRelative("description");
        var timerProp = property.FindPropertyRelative("timer");

        if (descriptionProp == null || timerProp == null)
        {
            EditorGUI.LabelField(position, $"{property.type}: Missing properties", EditorStyles.boldLabel);
            EditorGUI.EndProperty();
            return;
        }

        var durationProp = timerProp.FindPropertyRelative("Duration");
        var startTimeProp = timerProp.FindPropertyRelative("StartTime");

        if (durationProp == null || startTimeProp == null)
        {
            EditorGUI.LabelField(position, "StatModifierTimer: Missing Duration/StartTime", EditorStyles.boldLabel);
            EditorGUI.EndProperty();
            return;
        }

        const float leftColumnWidth = 100f;
        const float spacing = 5f;

        Rect mainRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        Rect leftRect = new Rect(mainRect.x, mainRect.y, leftColumnWidth, mainRect.height);
        Rect descRect = new Rect(mainRect.x + leftColumnWidth + spacing, mainRect.y,
            mainRect.width - leftColumnWidth - spacing, mainRect.height);

        EditorGUI.LabelField(leftRect, $"{FormatValue(property)} [TIMER]", EditorStyles.boldLabel);
        EditorGUI.LabelField(descRect, descriptionProp.stringValue);

        float elapsed = Time.time - startTimeProp.floatValue;
        float progress = durationProp.floatValue > 0 ? Mathf.Clamp01(elapsed / durationProp.floatValue) : 0f;

        Rect barBgRect = new Rect(mainRect.x, mainRect.yMax + 2f, mainRect.width, PROGRESS_BAR_HEIGHT);
        EditorGUI.DrawRect(barBgRect, new Color(0.2f, 0.2f, 0.2f, 0.5f));
        EditorGUI.DrawRect(new Rect(barBgRect.x, barBgRect.y, barBgRect.width * progress, barBgRect.height),
            new Color(0.3f, 0.7f, 1f, 0.8f));

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight + PROGRESS_BAR_HEIGHT + 2f;
    }
}

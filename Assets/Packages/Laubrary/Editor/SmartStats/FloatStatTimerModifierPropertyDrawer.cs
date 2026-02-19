using Lautaro.Stats;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(FloatStatTimerModifier), true)]
public class FloatStatTimerModifierPropertyDrawer : PropertyDrawer
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
        if (EditorApplication.isPlaying && Selection.activeGameObject != null)
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        
        var descriptionProp = property.FindPropertyRelative("description");
        var modValueProp = property.FindPropertyRelative("modValue");
        var timerProp = property.FindPropertyRelative("timer");
        
        if (descriptionProp == null || modValueProp == null || timerProp == null)
        {
            EditorGUI.LabelField(position, "FloatStatTimerModifier: Missing properties", EditorStyles.boldLabel);
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
        
        string valueText = FormatModifierValue(modValueProp.floatValue);
        float leftColumnWidth = 100f;
        float spacing = 5f;
        
        Rect mainRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        Rect leftRect = new Rect(mainRect.x, mainRect.y, leftColumnWidth, mainRect.height);
        Rect descRect = new Rect(mainRect.x + leftColumnWidth + spacing, mainRect.y, 
            mainRect.width - leftColumnWidth - spacing, mainRect.height);
        
        string leftText = $"{valueText} [TIMER]";
        
        EditorGUI.LabelField(leftRect, leftText, EditorStyles.boldLabel);
        EditorGUI.LabelField(descRect, descriptionProp.stringValue);
        
        float duration = durationProp.floatValue;
        float startTime = startTimeProp.floatValue;
        float elapsed = UnityEngine.Time.time - startTime;
        float progress = duration > 0 ? Mathf.Clamp01(elapsed / duration) : 0f;
        
        Rect progressBarRect = new Rect(mainRect.x, mainRect.yMax + 2f, mainRect.width, PROGRESS_BAR_HEIGHT);
        EditorGUI.DrawRect(progressBarRect, new Color(0.2f, 0.2f, 0.2f, 0.5f));
        
        Rect progressFillRect = new Rect(progressBarRect.x, progressBarRect.y, progressBarRect.width * progress, progressBarRect.height);
        EditorGUI.DrawRect(progressFillRect, new Color(0.3f, 0.7f, 1f, 0.8f));
        
        EditorGUI.EndProperty();
    }
    
    private string FormatModifierValue(float value)
    {
        string sign = value >= 0 ? "+" : "";
        return $"{sign}{value:F1}";
    }
    
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight + PROGRESS_BAR_HEIGHT + 2f;
    }
}

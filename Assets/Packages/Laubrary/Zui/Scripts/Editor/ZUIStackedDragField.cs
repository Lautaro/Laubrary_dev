// ZUIStackedDragField.cs
// A compact "label above, value below" numeric field for packing several short fields into a narrow column
// (a repeated list-item box, a sidebar) — where ZUI.FloatField/Slider's wide inline "label : field" row
// doesn't fit. The LABEL ITSELF is the drag-scrub handle (click-drag horizontally to scrub, Unity's classic
// prefix-label-drag feel) rather than a separate grip icon — reuses ZUIDragField's own scrub math (delta.x *
// sensitivity, SetWantsMouseJumping) so the drag feel matches every other ZUI numeric control.
//
// Usage (typed control, for ZUIForm/ZUIRow):
//   row.Add(52f, ZUI.StackedFloat("Radius", () => r.radius, v => r.radius = v, 0f, 1f));
// Usage (plain call):
//   value = ZUI.StackedFloat("Radius", value, 52f);

using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    public static float StackedFloat(string label, float value, float width = 60f, float sensitivity = 0.05f)
    {
        GUILayout.BeginVertical(GUILayout.Width(width));
        Rect labelRect = GUILayoutUtility.GetRect(width, 14f, GUILayout.Width(width));
        value = ZUIStackedDragField.ScrubLabel(labelRect, label, value, sensitivity);
        float next = EditorGUILayout.FloatField(value, GUILayout.Width(width));
        GUILayout.EndVertical();
        return next;
    }

    public static int StackedInt(string label, int value, float width = 60f, float sensitivity = 0.2f)
    {
        GUILayout.BeginVertical(GUILayout.Width(width));
        Rect labelRect = GUILayoutUtility.GetRect(width, 14f, GUILayout.Width(width));
        float scrubbed = ZUIStackedDragField.ScrubLabel(labelRect, label, value, sensitivity);
        int next = EditorGUILayout.IntField(Mathf.RoundToInt(scrubbed), GUILayout.Width(width));
        GUILayout.EndVertical();
        return next;
    }

    // Typed-control factories (drop into ZUIForm/ZUIRow like ZUI.FloatField/Slider).
    public static ZUIStackedFloatControl StackedFloat(string label, System.Func<float> get, System.Action<float> set,
                                                        float? width = null, float sensitivity = 0.05f)
        => new ZUIStackedFloatControl(label, get, set, width, sensitivity);

    public static ZUIStackedIntControl StackedInt(string label, System.Func<int> get, System.Action<int> set,
                                                    float? width = null, float sensitivity = 0.2f)
        => new ZUIStackedIntControl(label, get, set, width, sensitivity);
}

public class ZUIStackedFloatControl : IZUIControl
{
    string _label;
    System.Func<float> _get;
    System.Action<float> _set;
    float _width;
    float _sensitivity;

    public ZUIStackedFloatControl(string label, System.Func<float> get, System.Action<float> set,
                                   float? width, float sensitivity)
    { _label = label; _get = get; _set = set; _width = width ?? 60f; _sensitivity = sensitivity; }

    public void Draw()
    {
        float val = _get();
        float next = ZUI.StackedFloat(_label, val, _width, _sensitivity);
        if (next != val) _set(next);
    }
}

public class ZUIStackedIntControl : IZUIControl
{
    string _label;
    System.Func<int> _get;
    System.Action<int> _set;
    float _width;
    float _sensitivity;

    public ZUIStackedIntControl(string label, System.Func<int> get, System.Action<int> set,
                                 float? width, float sensitivity)
    { _label = label; _get = get; _set = set; _width = width ?? 60f; _sensitivity = sensitivity; }

    public void Draw()
    {
        int val = _get();
        int next = ZUI.StackedInt(_label, val, _width, _sensitivity);
        if (next != val) _set(next);
    }
}

// Reusable internal drag-scrub-on-label logic — mirrors ZUIDragField's Scrub() math (see ZUIFormControls.cs)
// but the drag zone IS the label's own rect instead of a separate handle icon beside the field.
internal static class ZUIStackedDragField
{
    static readonly int s_hint = "ZUIStackedDragField".GetHashCode();

    public static float ScrubLabel(Rect labelRect, string label, float val, float sensitivity)
    {
        EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.SlideArrow);
        int id = GUIUtility.GetControlID(s_hint, FocusType.Passive, labelRect);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && labelRect.Contains(e.mousePosition))
                { GUIUtility.hotControl = id; e.Use(); EditorGUIUtility.SetWantsMouseJumping(1); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) { val += e.delta.x * sensitivity; GUI.changed = true; e.Use(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                { GUIUtility.hotControl = 0; e.Use(); EditorGUIUtility.SetWantsMouseJumping(0); }
                break;
        }
        if (e.type == EventType.Repaint)
            EditorStyles.miniLabel.Draw(labelRect, label, false, false, false, false);
        return val;
    }
}

// ZUIFlow.cs
// Layout helpers that bake in a bit of "landscape awareness": screens are wide, so vertical space is
// the precious axis, and very wide controls are hard to scan (long eye-travel between a control's left
// and right edge). Flow groups several narrow controls onto ONE row; Field caps a control's body width
// so a picker can't stretch across the whole window and bury what follows it.
//
//   using (ZUI.Flow())
//   {
//       ZUI.Field("Texture", 220f, () => def.texture = (Texture2D)EditorGUILayout.ObjectField(def.texture, typeof(Texture2D), false));
//       ZUI.Field("Tint",     48f, () => def.tint    = EditorGUILayout.ColorField(def.tint));
//   }

using System;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // A sensible default cap for a control body. Full-width should be a deliberate choice, not the
    // accident you get from ExpandWidth.
    public const float DefaultFieldWidth = 220f;

    /// <summary>Begin a row that groups narrow controls left-to-right (instead of one per row). Trailing
    /// space is pushed right so the next row's controls don't drift. Dispose ends the row.</summary>
    public static FlowScope Flow()
    {
        GUILayout.BeginHorizontal();
        return new FlowScope(true);
    }

    public readonly struct FlowScope : IDisposable
    {
        public FlowScope(bool _) { }
        public void Dispose() { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
    }

    /// <summary>A "label + width-capped body" cell for a Flow row. The body is fixed-width so it can't
    /// stretch; several Fields sit side by side and stay readable.</summary>
    public static void Field(string label, float bodyWidth, Action drawBody, float labelWidth = 0f)
    {
        if (!string.IsNullOrEmpty(label))
        {
            if (labelWidth <= 0f) labelWidth = EditorStyles.label.CalcSize(new GUIContent(label)).x + 4f;
            GUILayout.Label(label, GUILayout.Width(labelWidth));
        }
        GUILayout.BeginVertical(GUILayout.Width(Mathf.Max(1f, bodyWidth)));
        drawBody?.Invoke();
        GUILayout.EndVertical();
        GUILayout.Space(12f);
    }

    // Scrub accumulator so sub-pixel drag deltas add up to whole integer steps.
    static float _scrubAccum;
    static int   _scrubId = -1;

    /// <summary>The standard ZUI integer input: a compact typable field with a DRAGGABLE label — drag
    /// the label left/right to scrub the value (like Unity's own numeric fields). Returns the new value
    /// and sets GUI.changed when it moves, so an enclosing BeginChangeCheck sees edits. Drop it straight
    /// into a Flow row.</summary>
    public static int IntField(string label, int value, float bodyWidth = 52f,
                               int min = int.MinValue, int max = int.MaxValue, float sensitivity = 0.25f)
    {
        if (!string.IsNullOrEmpty(label))
        {
            float lw = EditorStyles.label.CalcSize(new GUIContent(label)).x + 4f;
            GUILayout.Label(label, GUILayout.Width(lw));
            var lr = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(lr, MouseCursor.SlideArrow);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && lr.Contains(e.mousePosition))
                    { GUIUtility.hotControl = id; _scrubId = id; _scrubAccum = 0f; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        _scrubAccum += e.delta.x * sensitivity;
                        int step = (int)_scrubAccum;
                        if (step != 0) { value = Mathf.Clamp(value + step, min, max); _scrubAccum -= step; GUI.changed = true; }
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; _scrubId = -1; e.Use(); }
                    break;
            }
        }
        int typed = EditorGUILayout.IntField(value, GUILayout.Width(Mathf.Max(1f, bodyWidth)));
        if (typed != value) { value = Mathf.Clamp(typed, min, max); GUI.changed = true; }
        GUILayout.Space(12f);
        return value;
    }
}

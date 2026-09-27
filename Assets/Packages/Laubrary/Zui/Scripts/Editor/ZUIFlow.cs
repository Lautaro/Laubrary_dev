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

    /// <summary>
    /// Places fixed-width controls left to right and starts a new row only when the next one would not fit — the layout
    /// for a window used wide, where vertical room is the scarce thing.
    ///
    /// Wrapping is decided from <paramref name="available"/>, a width the caller supplies, and never from the rects
    /// Unity hands back — because in IMGUI's layout pass those rects are placeholders, and deciding from them would put
    /// controls on different rows in the layout pass than in the paint pass, which corrupts the whole window's layout.
    /// Pass a width that is the same in both passes (for example the window's width less a remembered margin).
    ///
    ///   var row = new ZUI.WrapRow(available, indent: 20f, rowHeight: 20f, gap: 6f);
    ///   foreach (var w in widths) Draw(row.Next(w));
    /// </summary>
    public sealed class WrapRow
    {
        readonly float available, indent, rowHeight, gap;
        Rect row;
        float used;
        bool open;

        /// <summary>How many rows have been started.</summary>
        public int rows { get; private set; }

        public WrapRow(float available, float indent, float rowHeight, float gap)
        {
            this.available = Mathf.Max(1f, available - indent);
            this.indent = indent;
            this.rowHeight = rowHeight;
            this.gap = gap;
        }

        /// <summary>The rect for the next control of this width, on the current row if it fits, else on a new one.</summary>
        public Rect Next(float width)
        {
            float need = open && used > 0f ? gap + width : width;
            if (!open || used + need > available + 0.01f) { NewRow(); need = width; }
            var r = new Rect(row.x + used + (need - width), row.y, width, rowHeight);
            used += need;
            return r;
        }

        /// <summary>Forces the next control onto a fresh row.</summary>
        public void Break() { open = false; }

        void NewRow()
        {
            row = GUILayoutUtility.GetRect(10f, rowHeight, GUILayout.ExpandWidth(true));
            row.xMin += indent;
            used = 0f;
            open = true;
            rows++;
        }

        /// <summary>How many rows these widths would take at this available width, computed the same way as placing them.</summary>
        public static int CountRows(float available, float gap, System.Collections.Generic.IList<float> widths)
        {
            if (widths == null || widths.Count == 0) return 0;
            int rows = 1; float used = 0f;
            for (int i = 0; i < widths.Count; i++)
            {
                float need = used > 0f ? gap + widths[i] : widths[i];
                if (used + need > available + 0.01f && used > 0f) { rows++; used = widths[i]; }
                else used += need;
            }
            return rows;
        }

        /// <summary>The width these controls take on one row, gaps included.</summary>
        public static float OneRowWidth(float gap, System.Collections.Generic.IList<float> widths)
        {
            float w = 0f;
            for (int i = 0; i < widths.Count; i++) w += (i > 0 ? gap : 0f) + widths[i];
            return w;
        }
    }

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

    /// <summary>The float sibling of IntField: a compact typable float with a DRAGGABLE label. Sets
    /// GUI.changed when it moves. Drop it into a Flow row.</summary>
    public static float FloatField(string label, float value, float bodyWidth = 56f,
                                   float min = float.MinValue, float max = float.MaxValue, float sensitivity = 0.01f)
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
                    if (e.button == 0 && lr.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    { value = Mathf.Clamp(value + e.delta.x * sensitivity, min, max); GUI.changed = true; e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
        }
        float typed = EditorGUILayout.FloatField(value, GUILayout.Width(Mathf.Max(1f, bodyWidth)));
        if (!Mathf.Approximately(typed, value)) { value = Mathf.Clamp(typed, min, max); GUI.changed = true; }
        GUILayout.Space(12f);
        return value;
    }
}

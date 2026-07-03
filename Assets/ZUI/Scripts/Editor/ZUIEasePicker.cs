// ZUIEasePicker.cs
// Visual easing curve picker for ZUI. Shows each curve as a small preview alongside its name.
// Usage: ZUI.EasePicker(rect, currentEase) or ZUI.EasePicker(label, ref ease)

using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ===== Ease Picker API ====================================================

    /// <summary>Draws an ease picker button. Click opens a popup with curve previews.</summary>
    public static ZUIEaseCurve EasePicker(Rect rect, ZUIEaseCurve current)
    {
        string label = current.ToString();
        // Draw button with current ease name + small curve preview
        if (GUI.Button(rect, GUIContent.none, EditorStyles.popup))
        {
            PopupWindow.Show(rect, new EasePickerPopup(current, selected =>
            {
                _easePendingResult = selected;
                _easePendingHasResult = true;
            }));
        }

        // Draw curve preview in the left portion of the button
        if (Event.current.type == EventType.Repaint)
        {
            float previewW = Mathf.Min(28f, rect.width * 0.3f);
            var previewRect = new Rect(rect.x + 2f, rect.y + 2f, previewW, rect.height - 4f);
            DrawEaseCurvePreview(previewRect, current, new Color(0.5f, 0.8f, 1f, 0.9f), new Color(0.2f, 0.2f, 0.25f, 0.5f));
            // Label
            var labelRect = new Rect(rect.x + previewW + 4f, rect.y, rect.width - previewW - 18f, rect.height);
            GUI.Label(labelRect, label, EditorStyles.miniLabel);
        }

        // Return result from popup if pending
        if (_easePendingHasResult)
        {
            _easePendingHasResult = false;
            GUI.changed = true;
            return _easePendingResult;
        }
        return current;
    }

    /// <summary>Draws an ease picker with label. Layout version.</summary>
    public static ZUIEaseCurve EasePicker(string label, ZUIEaseCurve current, params GUILayoutOption[] options)
    {
        GUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(label))
            EditorGUILayout.LabelField(label, GUILayout.Width(58f));
        var rect = GUILayoutUtility.GetRect(80f, 18f, EditorStyles.popup, options);
        var result = EasePicker(rect, current);
        GUILayout.EndHorizontal();
        return result;
    }

    // Pending result from popup (popups close asynchronously)
    static ZUIEaseCurve _easePendingResult;
    static bool _easePendingHasResult;

    // ===== Curve Preview Drawing ==============================================

    internal static void DrawEaseCurvePreview(Rect rect, ZUIEaseCurve curve, Color curveColor, Color bgColor)
    {
        // Background
        EditorGUI.DrawRect(rect, bgColor);

        // Draw curve as polyline
        int steps = 20;
        var prev = GUI.color;
        for (int i = 0; i < steps; i++)
        {
            float t0 = (float)i / steps;
            float t1 = (float)(i + 1) / steps;
            float v0 = ZUIEasing.Evaluate(curve, t0);
            float v1 = ZUIEasing.Evaluate(curve, t1);

            // Map to rect (y inverted — 0 at bottom, 1 at top)
            float x0 = rect.x + t0 * rect.width;
            float x1 = rect.x + t1 * rect.width;
            float y0 = rect.yMax - Mathf.Clamp01(v0) * rect.height;
            float y1 = rect.yMax - Mathf.Clamp01(v1) * rect.height;

            // Draw line segment as a thin rect
            DrawLineSegment(x0, y0, x1, y1, curveColor, 1.5f);
        }
        GUI.color = prev;
    }

    static void DrawLineSegment(float x0, float y0, float x1, float y1, Color color, float thickness)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 0.01f) return;

        // Use a rotated rect via GL or approximate with EditorGUI.DrawRect
        // For simplicity: draw a rect along the segment direction
        // We'll approximate by drawing a 1px horizontal rect rotated via matrix
        var savedMatrix = GUI.matrix;
        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        GUIUtility.RotateAroundPivot(angle, new Vector2(cx, cy));
        var lineRect = new Rect(cx - len * 0.5f, cy - thickness * 0.5f, len, thickness);
        EditorGUI.DrawRect(lineRect, color);
        GUI.matrix = savedMatrix;
    }
}

// ===== Popup Window =========================================================

internal class EasePickerPopup : PopupWindowContent
{
    const int k_Columns = 3;
    const float k_CellW = 100f;
    const float k_CellH = 56f;
    const float k_CurveH = 32f;
    const float k_Pad = 4f;

    readonly ZUIEaseCurve _current;
    readonly System.Action<ZUIEaseCurve> _onSelect;
    static readonly ZUIEaseCurve[] k_AllCurves;

    static EasePickerPopup()
    {
        k_AllCurves = (ZUIEaseCurve[])System.Enum.GetValues(typeof(ZUIEaseCurve));
    }

    public EasePickerPopup(ZUIEaseCurve current, System.Action<ZUIEaseCurve> onSelect)
    {
        _current = current;
        _onSelect = onSelect;
    }

    public override Vector2 GetWindowSize()
    {
        int rows = Mathf.CeilToInt((float)k_AllCurves.Length / k_Columns);
        return new Vector2(k_Columns * (k_CellW + k_Pad) + k_Pad,
                           rows * (k_CellH + k_Pad) + k_Pad);
    }

    public override void OnGUI(Rect rect)
    {
        // Dark background
        EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.20f, 1f));

        for (int i = 0; i < k_AllCurves.Length; i++)
        {
            int col = i % k_Columns;
            int row = i / k_Columns;
            var cellRect = new Rect(
                k_Pad + col * (k_CellW + k_Pad),
                k_Pad + row * (k_CellH + k_Pad),
                k_CellW, k_CellH);

            bool isSelected = k_AllCurves[i] == _current;
            bool isHover = cellRect.Contains(Event.current.mousePosition);

            // Cell background
            Color cellBg = isSelected ? new Color(0.25f, 0.40f, 0.60f, 1f)
                         : isHover    ? new Color(0.22f, 0.22f, 0.28f, 1f)
                         :              new Color(0.18f, 0.18f, 0.22f, 1f);
            EditorGUI.DrawRect(cellRect, cellBg);

            // Curve preview
            var curveRect = new Rect(cellRect.x + 4f, cellRect.y + 2f, cellRect.width - 8f, k_CurveH);
            Color curveCol = isSelected ? new Color(0.7f, 0.9f, 1f, 1f) : new Color(0.5f, 0.75f, 1f, 0.8f);
            ZUI.DrawEaseCurvePreview(curveRect, k_AllCurves[i], curveCol, Color.clear);

            // Label
            var labelRect = new Rect(cellRect.x, cellRect.y + k_CurveH + 1f, cellRect.width, cellRect.height - k_CurveH - 1f);
            var labelStyle = isSelected ? EditorStyles.whiteBoldLabel : EditorStyles.miniLabel;
            GUI.Label(labelRect, FormatName(k_AllCurves[i]), labelStyle);

            // Click
            if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
            {
                _onSelect?.Invoke(k_AllCurves[i]);
                editorWindow.Close();
                Event.current.Use();
            }
        }

        // Repaint for hover
        if (Event.current.type == EventType.MouseMove)
            editorWindow.Repaint();
    }

    static string FormatName(ZUIEaseCurve curve)
    {
        // "EaseOutQuad" → "Out Quad", "Linear" → "Linear"
        string s = curve.ToString();
        if (s.StartsWith("EaseInOut")) return "In/Out " + s.Substring(9);
        if (s.StartsWith("EaseOut"))   return "Out " + s.Substring(7);
        if (s.StartsWith("EaseIn"))    return "In " + s.Substring(6);
        return s;
    }
}

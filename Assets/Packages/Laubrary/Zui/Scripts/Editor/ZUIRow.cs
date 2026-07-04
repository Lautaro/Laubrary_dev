// ZUIRow.cs
// Horizontal row layout helper for ZUI.
// Controls auto-size from their style definitions. Flexible() distributes remaining space.
//
// Usage:
//   using (var row = ZUI.HRow())
//   {
//       if (row.Button(editIcon, "ZoundBtnFlat")) { ... }
//       row.Toggle(ref muted, "M", "ZoundBtnFlatToggle");
//       row.Flexible();
//       if (row.Button("Knight Attack", "ZoundBtn")) { ... }
//       row.Flexible();
//       if (row.Button(removeIcon, "ZoundBtnFlat")) { ... }
//   }

using System;
using UnityEditor;
using UnityEngine;

public class ZUIRowScope : IDisposable
{
    bool _first;
    string _gapScale;

    internal ZUIRowScope(string gapScale = null)
    {
        _first = true;
        _gapScale = gapScale;
        GUILayout.BeginHorizontal();
    }

    void Gap()
    {
        if (_first) { _first = false; return; }
        if (!string.IsNullOrEmpty(_gapScale))
            ZUI.HorizontalSpace(_gapScale);
    }

    // ── Flexible space ──────────────────────────────────────────────────────

    public void Flexible()
    {
        GUILayout.FlexibleSpace();
        _first = true; // no gap after flexible
    }

    public void Space(float scale = 1f)
    {
        ZUI.HorizontalSpace(scale);
        _first = true;
    }

    // ── Button ──────────────────────────────────────────────────────────────

    public bool Button(string label, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    {
        Gap();
        return ZUI.Button(label, style, options);
    }

    public bool Button(GUIContent content, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    {
        Gap();
        return ZUI.Button(content, style, options);
    }

    // ── Toggle ──────────────────────────────────────────────────────────────

    public bool Toggle(bool value, string label, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    {
        Gap();
        return ZUI.Toggle(value, label, style, options);
    }

    public bool Toggle(bool value, GUIContent content, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    {
        Gap();
        return ZUI.Toggle(value, content, style, options);
    }

    public void Toggle(ref bool value, string label, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    {
        Gap();
        value = ZUI.Toggle(value, label, style, options);
    }

    public void Toggle(ref bool value, GUIContent content, string style = ZUI.Style.Default, params GUILayoutOption[] options)
    {
        Gap();
        value = ZUI.Toggle(value, content, style, options);
    }

    // ── Label ───────────────────────────────────────────────────────────────

    public void Label(string text, params GUILayoutOption[] options)
    {
        Gap();
        EditorGUILayout.LabelField(text, ZUI.RowLabelStyle, options);
    }

    // ── Slider ──────────────────────────────────────────────────────────────

    public float Slider(float value, float min, float max, string label = "", string style = ZUI.SliderStyle.Default, params GUILayoutOption[] options)
    {
        Gap();
        return ZUI.Slider(value, min, max, label, style, null, options);
    }

    public float MicroSlider(float value, float min, float max, string label = "", params GUILayoutOption[] options)
    {
        Gap();
        return ZUI.MicroSlider(value, min, max, label, ZUI.SliderStyle.Default, false, null, options);
    }

    // ── Dispose ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        GUILayout.EndHorizontal();
    }
}

// ── Factory method ──────────────────────────────────────────────────────────

public static partial class ZUI
{
    /// <summary>
    /// Creates a horizontal row scope. Controls auto-size. Use Flexible() for spacers.
    /// Gap scale controls automatic spacing between controls (null = no auto-gap).
    /// </summary>
    public static ZUIRowScope HRow(string gapScale = "H Control Gap")
    {
        return new ZUIRowScope(gapScale);
    }
}

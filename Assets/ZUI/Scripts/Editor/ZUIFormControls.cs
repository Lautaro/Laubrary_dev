// ZUIFormControls.cs
// Typed control objects for ZUI.Form. Value types use getter/setter delegates.
// Reference types can be passed directly.
//
// Each control implements IZUIControl which the Form system uses to draw.

using System;
using UnityEditor;
using UnityEngine;

// ── Interface ────────────────────────────────────────────────────────────────

public interface IZUIControl
{
    void Draw();
}

// ── Slider (float) ───────────────────────────────────────────────────────────

public class ZUISliderControl : IZUIControl
{
    Func<float> _get;
    Action<float> _set;
    float _min, _max;
    string _style;

    public ZUISliderControl(Func<float> get, Action<float> set, float min, float max, string style = null)
    {
        _get = get; _set = set; _min = min; _max = max; _style = style;
    }

    public void Draw()
    {
        float val = _get();
        float next;
        if (!string.IsNullOrEmpty(_style))
            next = ZUI.Slider(val, _min, _max, "", _style);
        else
            next = EditorGUILayout.Slider(val, _min, _max);
        if (next != val) _set(next);
    }
}

// ── IntSlider ────────────────────────────────────────────────────────────────

public class ZUIIntSliderControl : IZUIControl
{
    Func<int> _get;
    Action<int> _set;
    int _min, _max;

    public ZUIIntSliderControl(Func<int> get, Action<int> set, int min, int max)
    {
        _get = get; _set = set; _min = min; _max = max;
    }

    public void Draw()
    {
        int val = _get();
        int next = EditorGUILayout.IntSlider(val, _min, _max);
        if (next != val) _set(next);
    }
}

// ── Toggle (bool) ────────────────────────────────────────────────────────────

public class ZUIToggleControl : IZUIControl
{
    Func<bool> _get;
    Action<bool> _set;
    string _label;
    string _style;

    public ZUIToggleControl(Func<bool> get, Action<bool> set, string label = "", string style = null)
    {
        _get = get; _set = set; _label = label; _style = style;
    }

    public void Draw()
    {
        bool val = _get();
        bool next;
        if (!string.IsNullOrEmpty(_style))
            next = ZUI.Toggle(val, _label, _style);
        else if (!string.IsNullOrEmpty(_label))
            next = EditorGUILayout.Toggle(val);
        else
            next = EditorGUILayout.Toggle(val);
        if (next != val) _set(next);
    }
}

// ── FloatField ───────────────────────────────────────────────────────────────

public class ZUIFloatFieldControl : IZUIControl
{
    Func<float> _get;
    Action<float> _set;
    float? _width;

    public ZUIFloatFieldControl(Func<float> get, Action<float> set, float? width = null)
    {
        _get = get; _set = set; _width = width;
    }

    public void Draw()
    {
        float val = _get();
        float next = _width.HasValue
            ? EditorGUILayout.FloatField(val, GUILayout.Width(_width.Value))
            : EditorGUILayout.FloatField(val);
        if (next != val) _set(next);
    }
}

// ── IntField ─────────────────────────────────────────────────────────────────

public class ZUIIntFieldControl : IZUIControl
{
    Func<int> _get;
    Action<int> _set;
    float? _width;

    public ZUIIntFieldControl(Func<int> get, Action<int> set, float? width = null)
    {
        _get = get; _set = set; _width = width;
    }

    public void Draw()
    {
        int val = _get();
        int next = _width.HasValue
            ? EditorGUILayout.IntField(val, GUILayout.Width(_width.Value))
            : EditorGUILayout.IntField(val);
        if (next != val) _set(next);
    }
}

// ── ColorField ───────────────────────────────────────────────────────────────

public class ZUIColorFieldControl : IZUIControl
{
    Func<Color> _get;
    Action<Color> _set;

    public ZUIColorFieldControl(Func<Color> get, Action<Color> set)
    {
        _get = get; _set = set;
    }

    public void Draw()
    {
        Color val = _get();
        Color next = EditorGUILayout.ColorField(val);
        if (next != val) _set(next);
    }
}

// ── CycleButton ──────────────────────────────────────────────────────────────

public class ZUICycleButtonControl : IZUIControl
{
    Func<int> _get;
    Action<int> _set;
    string[] _labels;
    string _style;

    public ZUICycleButtonControl(Func<int> get, Action<int> set, string[] labels, string style = null)
    {
        _get = get; _set = set; _labels = labels;
        _style = style ?? ZUI.Style.Default;
    }

    public void Draw()
    {
        int val = _get();
        int next = ZUI.CycleButton(val, _labels, _style);
        if (next != val) _set(next);
    }
}

// ── Popup (dropdown) ─────────────────────────────────────────────────────────

public class ZUIPopupControl : IZUIControl
{
    Func<int> _get;
    Action<int> _set;
    string[] _options;

    public ZUIPopupControl(Func<int> get, Action<int> set, string[] options)
    {
        _get = get; _set = set; _options = options;
    }

    public void Draw()
    {
        int val = _get();
        int next = EditorGUILayout.Popup(val, _options);
        if (next != val) _set(next);
    }
}

// ── Palette Color Field ─────────────────────────────────────────────────────

public class ZUIPaletteColorControl : IZUIControl
{
    Func<ZUIColorRef> _get;
    Action<ZUIColorRef> _set;
    bool _paletteMode;
    bool _expanded;
    static readonly string k_ExpandedPref = "ZUI_PaletteColor_Expanded";

    public ZUIPaletteColorControl(Func<ZUIColorRef> get, Action<ZUIColorRef> set)
    {
        _get = get;
        _set = set;
        _paletteMode = get().IsPaletteRef;
        _expanded = EditorPrefs.GetBool(k_ExpandedPref, false);
    }

    public void Draw()
    {
        var current = _get();

        // Row 1: Palette toggle + color control
        GUILayout.BeginHorizontal();
        bool newPaletteMode = ZUI.Toggle(_paletteMode, "Palette", "Toggle", GUILayout.Width(60f));
        if (newPaletteMode != _paletteMode)
        {
            _paletteMode = newPaletteMode;
            if (!_paletteMode && current.IsPaletteRef)
            {
                // Switching to direct mode: resolve palette color to inline
                _set(new ZUIColorRef(current.Resolve(ZUI.ActiveSheet)));
            }
        }
        ZUI.HorizontalSpace("H Control Gap");

        if (!_paletteMode)
        {
            // Direct color mode
            Color next = EditorGUILayout.ColorField(current.color);
            if (next != current.color)
                _set(new ZUIColorRef(next));
        }
        else
        {
            // Show current palette selection
            DrawPalettePreview(current);
        }
        GUILayout.EndHorizontal();

        // Row 2: collapsible palette grid (palette mode only)
        if (_paletteMode)
            DrawPaletteGrid(current);
    }

    void DrawPalettePreview(ZUIColorRef current)
    {
        Color resolved = current.Resolve(ZUI.ActiveSheet);
        // Draw swatch
        var swatchRect = GUILayoutUtility.GetRect(24f, 16f, GUILayout.Width(24f));
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(swatchRect, resolved);

        // Draw palette name
        string display = current.IsPaletteRef ? $"{current.paletteRef} ({current.slot})" : "(none)";
        EditorGUILayout.LabelField(display, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
    }

    void DrawPaletteGrid(ZUIColorRef current)
    {
        var sheet = ZUI.ActiveSheet;
        if (sheet == null || sheet.palette == null || sheet.palette.Count == 0) return;

        // Expand/collapse toggle
        ZUI.VerticalSpace("V Section Rows");
        GUILayout.BeginHorizontal();
        string arrow = _expanded ? "\u25BC" : "\u25B6";
        if (GUILayout.Button($"{arrow} Palette Colors ({sheet.palette.Count})", EditorStyles.miniLabel))
        {
            _expanded = !_expanded;
            EditorPrefs.SetBool(k_ExpandedPref, _expanded);
        }
        GUILayout.EndHorizontal();

        if (!_expanded) return;

        ZUI.VerticalSpace("V Section Rows");

        // Grid of palette entries
        float swatchSize = 20f;
        float entryW = 80f;
        float availW = EditorGUIUtility.currentViewWidth - 40f;
        int cols = Mathf.Max(1, Mathf.FloorToInt(availW / entryW));

        int col = 0;
        GUILayout.BeginHorizontal();
        for (int i = 0; i < sheet.palette.Count; i++)
        {
            var entry = sheet.palette[i];
            bool isSelected = current.IsPaletteRef && current.paletteRef == entry.name;

            GUILayout.BeginVertical(GUILayout.Width(entryW));

            // Swatch
            var rect = GUILayoutUtility.GetRect(entryW - 4f, swatchSize);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, entry.color);
                if (isSelected)
                {
                    // Highlight border
                    float bw = 2f;
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, bw), Color.white);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - bw, rect.width, bw), Color.white);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, bw, rect.height), Color.white);
                    EditorGUI.DrawRect(new Rect(rect.xMax - bw, rect.y, bw, rect.height), Color.white);
                }
            }
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _set(new ZUIColorRef(entry.color, entry.name));
                GUI.changed = true;
                Event.current.Use();
            }

            // Name label
            EditorGUILayout.LabelField(entry.name, EditorStyles.centeredGreyMiniLabel);

            GUILayout.EndVertical();

            col++;
            if (col >= cols && i < sheet.palette.Count - 1)
            {
                col = 0;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }
        }
        GUILayout.EndHorizontal();
    }
}

// ── ZUI factory methods ──────────────────────────────────────────────────────

public static partial class ZUI
{
    // Value-type controls — getter/setter delegates

    public static ZUISliderControl Slider(Func<float> get, Action<float> set, float min, float max, string style = null)
        => new ZUISliderControl(get, set, min, max, style);

    public static ZUIIntSliderControl IntSlider(Func<int> get, Action<int> set, int min, int max)
        => new ZUIIntSliderControl(get, set, min, max);

    public static ZUIToggleControl Toggle(Func<bool> get, Action<bool> set, string label = "", string style = null)
        => new ZUIToggleControl(get, set, label, style);

    public static ZUIFloatFieldControl FloatField(Func<float> get, Action<float> set, float? width = null)
        => new ZUIFloatFieldControl(get, set, width);

    public static ZUIIntFieldControl IntField(Func<int> get, Action<int> set, float? width = null)
        => new ZUIIntFieldControl(get, set, width);

    public static ZUIColorFieldControl ColorField(Func<Color> get, Action<Color> set)
        => new ZUIColorFieldControl(get, set);

    public static ZUICycleButtonControl CycleButton(Func<int> get, Action<int> set, string[] labels, string style = null)
        => new ZUICycleButtonControl(get, set, labels, style);

    public static ZUIPopupControl Popup(Func<int> get, Action<int> set, string[] options)
        => new ZUIPopupControl(get, set, options);

    public static ZUIPaletteColorControl PaletteColorField(Func<ZUIColorRef> get, Action<ZUIColorRef> set)
        => new ZUIPaletteColorControl(get, set);
}

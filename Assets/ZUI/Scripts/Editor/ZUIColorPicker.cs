// ZUIColorPicker.cs
// Unified color control for ZUI. Handles both custom (direct) and palette colors.
//
// Usage:
//   bool changed = ZUI.ColorPicker(ref myColorRef);
//   bool changed = ZUI.ColorPicker(ref myColorRef, showAlpha: false);
//
// Layout:
//   [Custom|Palette] [ColorField]                     ← Custom mode
//   [Custom|Palette] [palette swatch with alpha]      ← Palette mode
//   ┌─palette grid (expandable below)─────────────┐   ← Palette mode, expanded
//   │ [Name] [P][H][S]  or  [Name] [7 slots]     │
//   └─────────────────────────────────────────────┘

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // State keyed by control ID
    static readonly Dictionary<int, bool> s_colorPickerExpanded = new Dictionary<int, bool>();
    static readonly Dictionary<int, int> s_colorPickerMode = new Dictionary<int, int>(); // 0=Custom, 1=Palette

    /// <summary>
    /// Draws a color picker with Custom/Palette mode toggle.
    /// Returns true if the value changed.
    /// </summary>
    public static bool ColorPicker(ref ZUIColorRef colorRef, bool showAlpha = true, params GUILayoutOption[] options)
        => ColorPicker(ref colorRef, null, showAlpha, options);

    /// <summary>
    /// Color picker with optional explicit palette. When paletteOverride is null, uses ActiveSheet's palette.
    /// </summary>
    public static bool ColorPicker(ref ZUIColorRef colorRef, System.Collections.Generic.List<ZUIPaletteColor> paletteOverride,
                                    bool showAlpha = true, params GUILayoutOption[] options)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        bool changed = false;

        // Initialize mode from colorRef state if not yet tracked
        if (!s_colorPickerMode.ContainsKey(id))
            s_colorPickerMode[id] = colorRef.IsPaletteRef ? 1 : 0;

        int mode = s_colorPickerMode[id];
        const float maxW = 260f;

        // Mode toggle
        GUILayout.BeginHorizontal(GUILayout.MaxWidth(maxW));

        int newMode = MiniRadio(mode, s_colorPickerModeLabels, "Toggle");
        if (newMode != mode)
        {
            s_colorPickerMode[id] = newMode;
            mode = newMode;

            if (mode == 0 && colorRef.IsPaletteRef)
            {
                // Switching to Custom — resolve palette to inline color
                colorRef = new ZUIColorRef(colorRef.Resolve(ActiveSheet));
                changed = true;
            }
            else if (mode == 1)
            {
                // Switching to Palette — expand the grid
                s_colorPickerExpanded[id] = true;
            }
        }

        HorizontalSpace("H Control Gap");

        if (mode == 0)
        {
            // Custom: Unity ColorField
            EditorGUI.BeginChangeCheck();
            colorRef.color = EditorGUILayout.ColorField(GUIContent.none, colorRef.color, true, showAlpha, false, GUILayout.Width(90f));
            if (EditorGUI.EndChangeCheck())
            {
                colorRef.paletteRef = "";
                changed = true;
            }
        }
        else
        {
            // Palette: swatch showing resolved color (non-editable)
            Color resolved = colorRef.Resolve(ActiveSheet);
            DrawPaletteSwatch(id, resolved, colorRef, showAlpha, options);
        }

        GUILayout.EndHorizontal();

        // Expandable palette grid below (always draw the vertical to keep Layout/Repaint consistent)
        s_colorPickerExpanded.TryGetValue(id, out bool expanded);
        if (expanded && mode == 1)
        {
            int gridResult = DrawPaletteGrid(ref colorRef, id, paletteOverride);
            if (gridResult > 0)
            {
                changed = true;
                // Single click = select but keep grid open to try colors
                // Double click = select and close
                if (gridResult >= 2)
                    s_colorPickerExpanded[id] = false;
            }
        }

        return changed;
    }

    static readonly string[] s_colorPickerModeLabels = { "Custom", "Palette" };

    static void DrawPaletteSwatch(int id, Color color, ZUIColorRef colorRef, bool showAlpha, GUILayoutOption[] options)
    {
        float h = EditorGUIUtility.singleLineHeight;
        var rect = GUILayoutUtility.GetRect(140f, h, GUILayout.MinWidth(140f), GUILayout.Height(h));

        if (Event.current.type == EventType.Repaint)
        {
            // Main color swatch
            float alphaStripH = showAlpha ? 3f : 0f;
            var colorRect = new Rect(rect.x, rect.y, rect.width, rect.height - alphaStripH);
            EditorGUI.DrawRect(colorRect, new Color(color.r, color.g, color.b, 1f));

            // Alpha strip at bottom (black/white to show alpha)
            if (showAlpha)
            {
                var alphaRect = new Rect(rect.x, rect.yMax - alphaStripH, rect.width, alphaStripH);
                // Checkerboard-ish: white background with alpha-blended color on top
                EditorGUI.DrawRect(alphaRect, Color.white);
                EditorGUI.DrawRect(alphaRect, new Color(0f, 0f, 0f, 1f - color.a));
            }

            // Border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.4f));
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.4f));

            // Label showing palette name + slot or autocolor name
            if (colorRef.IsPaletteRef)
            {
                string refName = colorRef.IsAutoColorRef ? colorRef.autoColorRef : colorRef.slot.ToString();
                string label = $"{colorRef.paletteRef} · {refName}";
                float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
                var labelColor = luminance > 0.45f ? Color.black : Color.white;
                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = labelColor },
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 9,
                };
                GUI.Label(colorRect, label, style);
            }
        }

        // Click to toggle palette expansion
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            s_colorPickerExpanded.TryGetValue(id, out bool exp);
            s_colorPickerExpanded[id] = !exp;
            Event.current.Use();
        }
    }

    // Returns: 0 = nothing, 1 = single click selection, 2 = double click selection
    static int DrawPaletteGrid(ref ZUIColorRef colorRef, int id,
                                System.Collections.Generic.List<ZUIPaletteColor> paletteOverride = null)
    {
        var palette = paletteOverride ?? ActiveSheet?.palette;
        if (palette == null || palette.Count == 0)
        {
            EditorGUILayout.LabelField("No palette colors defined.", EditorStyles.miniLabel);
            GUILayoutUtility.GetRect(1f, 1f);
            return 0;
        }

        int result = 0;
        VerticalSpace("V Control Gap");

        var bgRect = EditorGUILayout.BeginVertical();
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(bgRect, new Color(0.12f, 0.12f, 0.15f, 0.95f));

        const float swatchSize = 20f;
        const float nameW = 80f;
        const float rowH = 20f;

        foreach (var entry in palette)
        {
            bool isSelected = colorRef.IsPaletteRef && colorRef.paletteRef == entry.name;

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));

            // Name label
            var nameStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            EditorGUILayout.LabelField(entry.name, nameStyle, GUILayout.Width(nameW), GUILayout.Height(rowH));

            // Base color swatch
            {
                bool baseActive = isSelected && !colorRef.IsAutoColorRef;
                var baseRect = GUILayoutUtility.GetRect(swatchSize, rowH, GUILayout.Width(swatchSize), GUILayout.Height(rowH));
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(baseRect, entry.color);
                    if (baseActive) DrawSwatchBorder(baseRect);
                }
                if (Event.current.type == EventType.MouseDown && baseRect.Contains(Event.current.mousePosition))
                {
                    colorRef = new ZUIColorRef(entry.color, entry.name);
                    result = Event.current.clickCount >= 2 ? 2 : 1;
                    GUI.changed = true;
                    Event.current.Use();
                }
            }

            // Autocolor swatches
            if (entry.autoColors != null)
            {
                foreach (var ac in entry.autoColors)
                {
                    Color acColor  = ac.Resolve(entry.color);
                    bool  acActive = isSelected && colorRef.IsAutoColorRef && colorRef.autoColorRef == ac.name;
                    var   acRect   = GUILayoutUtility.GetRect(swatchSize, rowH, GUILayout.Width(swatchSize), GUILayout.Height(rowH));
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(acRect, acColor);
                        if (acActive) DrawSwatchBorder(acRect);
                    }
                    if (Event.current.type == EventType.MouseDown && acRect.Contains(Event.current.mousePosition))
                    {
                        colorRef = new ZUIColorRef(acColor, entry.name, ZUIPaletteSlot.Primary, ac.name);
                        result = Event.current.clickCount >= 2 ? 2 : 1;
                        GUI.changed = true;
                        Event.current.Use();
                    }
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Draw highlight on selected row (after EndHorizontal so GetLastRect works)
            if (isSelected && Event.current.type == EventType.Repaint)
            {
                var rowRect = GUILayoutUtility.GetLastRect();
                EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.08f));
            }
        }

        EditorGUILayout.EndVertical();

        return result;
    }

    static void DrawSwatchBorder(Rect r)
    {
        float b = 2f;
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, b), Color.white);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - b, r.width, b), Color.white);
        EditorGUI.DrawRect(new Rect(r.x, r.y, b, r.height), Color.white);
        EditorGUI.DrawRect(new Rect(r.xMax - b, r.y, b, r.height), Color.white);
    }
}

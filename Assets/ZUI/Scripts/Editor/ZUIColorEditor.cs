// ZUIColorEditor.cs
// Unified editor for ZUIColor (the top-level paint descriptor: solid, linear, radial, or fixed-edge).
//
// Usage (in style editor):
//   bool changed = ZUI.ColorEditor(color, onOpenStopEditor);
//   bool changed = ZUI.ColorEditor(color, onOpenStopEditor, allowGradient: false); // solid only
//
// Layout:
//   [Single|Gradient] toggle
//
//   Single mode:
//     [ZUI.ColorPicker for colorA]
//
//   Gradient mode:
//     [Linear|Radial|Fixed] sub-mode
//     Per-mode controls (angle, curve, shape, edge toggles)
//     [Gradient stop preview bar] → click opens stop editor via callback

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    static readonly string[] s_fillModeLabels = { "Single", "Gradient" };
    static readonly string[] s_gradientSubModeLabels = { "Linear", "Radial", "Fixed" };
    static readonly string[] s_gradientSubModeLabelsNoFixed = { "Linear", "Radial" };
    static readonly string[] s_radialShapeLabels = { "Ellipse", "Square", "Shape" };

    /// <summary>
    /// Draws a fill editor for a ZUIColor. Returns true if the value changed.
    /// </summary>
    /// <param name="g">The gradient to edit.</param>
    /// <param name="onOpenStopEditor">Callback to open the gradient stop editor popup, receiving the bar rect.</param>
    /// <param name="allowGradient">If false, only solid color mode is available.</param>
    /// <param name="hidePxEdge">If true, the Fixed/pixel-edge mode is hidden.</param>
    public static bool ColorEditor(ZUIColor g, Action<Rect> onOpenStopEditor = null,
                             bool allowGradient = true, bool hidePxEdge = false,
                             List<ZUIPaletteColor> paletteOverride = null)
    {
        bool changed = false;

        // ── Row 1 cascade: Single/Gradient → (ColorPicker | submode → shape) ──
        GUILayout.BeginHorizontal();

        if (allowGradient)
        {
            int mode = g.isGradient ? 1 : 0;
            EditorGUI.BeginChangeCheck();
            int newMode = MiniRadio(mode, s_fillModeLabels, "Toggle");
            if (EditorGUI.EndChangeCheck() && newMode != mode)
            {
                g.isGradient = newMode == 1;
                if (g.isGradient && (g.stops == null || g.stops.Count < 2))
                {
                    g.stops = new List<ZUIGradientStop>
                    {
                        new ZUIGradientStop(g.colorA, 0f, 0.5f),
                        new ZUIGradientStop(g.colorB, 1f, 0.5f),
                    };
                }
                g.Invalidate();
                changed = true;
            }
            HorizontalSpace("H Control Gap");
        }

        int subMode = g.isRadial ? 1 : (g.usePixelLength ? 2 : 0);

        if (g.isGradient)
        {
            // Sub-mode toggle on same row as Single/Gradient
            string[] subLabels = hidePxEdge ? s_gradientSubModeLabelsNoFixed : s_gradientSubModeLabels;
            EditorGUI.BeginChangeCheck();
            int newSubMode = MiniRadio(subMode, subLabels, "Toggle");
            if (EditorGUI.EndChangeCheck() && newSubMode != subMode)
            {
                g.isRadial       = newSubMode == 1;
                g.usePixelLength = newSubMode == 2;
                g.Invalidate();
                changed = true;
                subMode = newSubMode;
            }

            // Row 1 tail: Linear → Angle, Radial → shape picker, Fixed → Length.
            if (subMode == 0) // Linear: Angle slider after mode radio
            {
                HorizontalSpace("H Control Gap");
                EditorGUI.BeginChangeCheck();
                g.angle = MicroSlider(g.angle, 0f, 360f, "Angle", options: GUILayout.Width(140f));
                if (EditorGUI.EndChangeCheck()) { g.Invalidate(); changed = true; }
            }
            else if (subMode == 1) // Radial: shape picker after mode radio
            {
                HorizontalSpace("H Control Gap");
                int shape = MiniRadio(g.radialShape, s_radialShapeLabels, "Toggle");
                if (shape != g.radialShape) { g.radialShape = shape; g.Invalidate(); changed = true; }
            }
            else if (subMode == 2) // Fixed: Length slider after mode radio
            {
                HorizontalSpace("H Control Gap");
                EditorGUI.BeginChangeCheck();
                g.pixelLength = Mathf.Max(1, Mathf.RoundToInt(
                    MicroSlider(g.pixelLength, 1f, 64f, "Length", options: GUILayout.Width(140f))));
                if (EditorGUI.EndChangeCheck()) { g.Invalidate(); changed = true; }
            }
        }
        else
        {
            // Single mode: color picker on same row
            if (ColorPicker(ref g.colorA, paletteOverride))
            {
                g.Invalidate();
                changed = true;
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (g.isGradient)
        {
            VerticalSpace("V Control Gap");
            changed |= DrawFillGradientDetails(g, onOpenStopEditor, subMode);
        }

        return changed;
    }

    // Row 2+: stop bar and Fixed-only edge toggles.
    // Row-1 tail already carries Angle (Linear) / shape picker (Radial) / Length (Fixed).
    static bool DrawFillGradientDetails(ZUIColor g, Action<Rect> onOpenStopEditor, int subMode)
    {
        bool changed = false;

        EditorGUI.BeginChangeCheck();

        changed |= DrawFillStopBar(g, onOpenStopEditor);

        if (subMode == 2) // Fixed: edge toggles on a separate row below
        {
            VerticalSpace("V Control Gap");
            DrawFillFixedEdgeToggles(g);
        }

        if (EditorGUI.EndChangeCheck()) { g.Invalidate(); changed = true; }

        // Sync colorA/colorB from stops
        if (g.stops != null && g.stops.Count >= 2)
        {
            g.colorA = g.stops[0].color;
            g.colorB = g.stops[g.stops.Count - 1].color;
        }

        return changed;
    }

    static void DrawFillFixedEdgeToggles(ZUIColor g)
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Edges", RowLabelStyle, GUILayout.Width(36f));
        bool[] edgeValues =
        {
            (g.pixelEdges & ZUIPixelEdges.Left)   != 0,
            (g.pixelEdges & ZUIPixelEdges.Right)  != 0,
            (g.pixelEdges & ZUIPixelEdges.Bottom) != 0,
            (g.pixelEdges & ZUIPixelEdges.Top)    != 0,
        };
        GUIContent[] edgeLabels =
        {
            new GUIContent("\u2190", "Left edge"),
            new GUIContent("\u2192", "Right edge"),
            new GUIContent("\u2193", "Bottom edge"),
            new GUIContent("\u2191", "Top edge"),
        };
        edgeValues = ToggleRow(edgeValues, edgeLabels, "Toggle", GUILayout.Width(26f));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        g.pixelEdges = (edgeValues[0] ? ZUIPixelEdges.Left   : 0)
                     | (edgeValues[1] ? ZUIPixelEdges.Right  : 0)
                     | (edgeValues[2] ? ZUIPixelEdges.Bottom : 0)
                     | (edgeValues[3] ? ZUIPixelEdges.Top    : 0);
    }

    // Gradient preview bar — visualises the current gradient.
    // Click anywhere on it to open the stop editor popover (anchored to the bar's rect,
    // so the popover matches the bar's width). No stop markers are drawn here — the
    // popover's stop slider is the only place to read/edit stop positions.
    static bool DrawFillStopBar(ZUIColor g, Action<Rect> onOpenStopEditor)
    {
        bool changed = false;

        var barRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.Height(36f), GUILayout.ExpandWidth(true));

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f, 1f));

            // Preview bar — shows the real gradient including angle. Radial/pixel-edge
            // are flattened to a 1-D strip since we render into a horizontal rect.
            bool wasRadial = g.isRadial;
            bool wasPxLen  = g.usePixelLength;
            g.isRadial       = false;
            g.usePixelLength = false;
            g.Invalidate();
            var previewTex = g.GetOrBuildTexture(ActiveSheet);
            g.isRadial       = wasRadial;
            g.usePixelLength = wasPxLen;
            g.Invalidate();

            if (previewTex != null)
                GUI.DrawTexture(barRect, previewTex, ScaleMode.StretchToFill, true);

            // Top/bottom border
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.yMax - 1f, barRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
        }

        // Click → open stop editor popover anchored to the bar rect (same width).
        if (onOpenStopEditor != null &&
            Event.current.type == EventType.MouseDown && barRect.Contains(Event.current.mousePosition))
        {
            onOpenStopEditor(barRect);
            Event.current.Use();
        }

        return changed;
    }
}

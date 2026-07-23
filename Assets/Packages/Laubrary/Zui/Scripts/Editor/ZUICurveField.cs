// ZUICurveField.cs
// A foldable envelope curve field — a more capable replacement for EditorGUILayout.CurveField.
// Folded, it shows a small live curve thumbnail (click it to expand, like the native field);
// expanded, it shows the full ZUI.Envelope editor (the same one the multicontrol's "Curve" mode uses).
// It operates on the ZUI-native List<ZUIEnvelopePoint>, which is runtime-safe and evaluated with
// ZUIEnvelopeEvaluator — so the editor curve IS what the consumer evaluates (WYSIWYG).
//
// Usage:
//   changed = ZUI.CurveField("myKey", "Alpha over life", points, yMin: 0f, yMax: 1f);

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    class CurveFieldState
    {
        public ZUIEnvelopeDef def;
        public ZUIEnvelopeRuntime rt;
        public ZUIColorRef color;
        public int key;
    }

    static readonly Dictionary<List<ZUIEnvelopePoint>, CurveFieldState> s_curveField = new();
    static readonly Dictionary<string, bool> s_curveFieldExpanded = new();
    static int s_curveFieldNextKey = 8200;

    static CurveFieldState GetCurveFieldState(List<ZUIEnvelopePoint> pts)
    {
        if (!s_curveField.TryGetValue(pts, out var cs))
        {
            cs = new CurveFieldState
            {
                def = new ZUIEnvelopeDef { name = "ZUICurveField" },
                rt = new ZUIEnvelopeRuntime { showGrid = true, showValueLabels = true },
                color = new ZUIColorRef(new Color(0.5f, 0.9f, 1f)),
                key = s_curveFieldNextKey++,
            };
            s_curveField[pts] = cs;
        }
        return cs;
    }

    /// <summary>Foldable envelope curve field. Returns true if a point changed this frame.</summary>
    public static bool CurveField(string key, string label, List<ZUIEnvelopePoint> points,
                                  float yMin = 0f, float yMax = 1f, float height = 90f)
    {
        if (points == null) return false;
        if (points.Count == 0)
        {
            points.Add(new ZUIEnvelopePoint(0f, yMin));
            points.Add(new ZUIEnvelopePoint(1f, yMax));
        }

        // Captured up front so a click only takes effect NEXT frame — toggling the drawn control set mid-frame
        // would mismatch IMGUI's Layout/Repaint passes (control-count exception).
        bool expanded = s_curveFieldExpanded.TryGetValue(key, out var e) && e;
        bool changed = false;

        // Header: label · clickable thumbnail · caret
        GUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(label))
        {
            float lw = EditorGUIUtility.labelWidth > 1f ? EditorGUIUtility.labelWidth : 120f;
            GUILayout.Label(label, GUILayout.Width(lw));
        }
        Rect thumb = GUILayoutUtility.GetRect(80f, 18f, GUILayout.ExpandWidth(true), GUILayout.Height(18f));
        DrawCurveThumbnail(thumb, points, yMin, yMax);
        var ev = Event.current;
        if (ev.type == EventType.MouseDown && ev.button == 0 && thumb.Contains(ev.mousePosition))
        {
            s_curveFieldExpanded[key] = !expanded;
            ev.Use();
            GUI.changed = true;
        }
        EditorGUIUtility.AddCursorRect(thumb, MouseCursor.Link);
        GUILayout.Label(expanded ? "▼" : "▶", EditorStyles.miniLabel, GUILayout.Width(14f));
        if (GUILayout.Button(new GUIContent("★", "Load a saved shape, or save this one — built-in and project presets."),
                              EditorStyles.miniButton, GUILayout.Width(22f), GUILayout.Height(18f)))
            PopupWindow.Show(GUILayoutUtility.GetLastRect(), new ZUIEnvelopePresetPopup(points, yMin, yMax));
        GUILayout.EndHorizontal();

        if (expanded)
        {
            var cs = GetCurveFieldState(points);
            cs.rt.yMin = yMin;
            cs.rt.yMax = yMax;
            changed = ZUI.Envelope(points, cs.color, cs.def, cs.rt, 200f, height, cs.key);
            for (int i = 0; i < points.Count; i++)
                points[i].value = Mathf.Clamp(points[i].value, yMin, yMax);
        }
        return changed;
    }

    // A small non-interactive plot of the curve, sampled through the same evaluator the consumer uses.
    // internal so ZUIValueControl (same assembly) can reuse it for the multicontrol's folded Curve mode.
    internal static void DrawCurveThumbnail(Rect rect, List<ZUIEnvelopePoint> points, float yMin, float yMax)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.13f, 0.16f));
        if (Event.current.type != EventType.Repaint) return;

        const int steps = 40;
        var pts = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float v = ZUIEnvelopeEvaluator.Evaluate(points, t, yMax);
            float ny = Mathf.InverseLerp(yMin, yMax, v);
            float x = Mathf.Lerp(rect.x + 2f, rect.xMax - 2f, t);
            float y = Mathf.Lerp(rect.yMax - 2f, rect.y + 2f, Mathf.Clamp01(ny));
            pts[i] = new Vector3(x, y, 0f);
        }

        Handles.BeginGUI();
        var prev = Handles.color;
        Handles.color = new Color(0.5f, 0.9f, 1f);
        Handles.DrawAAPolyLine(2f, pts);
        Handles.color = prev;
        Handles.EndGUI();
    }
}

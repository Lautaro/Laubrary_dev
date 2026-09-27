// ZUIBandSliders.cs
// A row of vertical bars, one per value, each set by dragging its height — the shape of a step sequencer or a graphic
// equaliser. ZUI.BandSliders(rect, values, min, max, baseline, ...)
//
// Why this exists rather than a row of ZUI.SliderVertical: a vertical slider is a thin groove with a knob, so a row of them
// reads as a mixing desk, not as one shape made of bars; and each is its own control, so a sweep of the mouse across the
// row changes only the first. A list of values that are meant to be seen and shaped together (a step modifier's steps,
// an equaliser's bands) wants the bars themselves to be the control, and a drag across them to paint. Added to ZUI rather
// than drawn inside one window, so any tool with such a list gets the same control (first user: Zounds' Step modifier).

using UnityEditor;
using UnityEngine;
using ZuiRuntime;

public static partial class ZUI
{
    static int   bandDragId = -1;
    static int   bandLastIndex = -1;
    static float bandLastValue;

    /// <summary>
    /// Draws one bar per value across <paramref name="rect"/> and lets the user drag bars to new heights. Each bar is
    /// filled from <paramref name="baseline"/> towards its value, so a range that runs both sides of zero reads as bars
    /// going up and down from a middle line. Dragging across several bars paints them in one gesture, filling in any the
    /// mouse skipped over. Double-clicking a bar resets it to <paramref name="defaultValue"/> when one is given.
    ///
    /// Values outside [min, max] are drawn at the edge with a bright cap, and are only changed if the user drags that bar.
    ///
    /// Nothing is modified in place: returns true when a bar changed this event, with <paramref name="result"/> a changed
    /// copy — so the caller can record Undo before applying it, like every other ZUI control.
    /// </summary>
    public static bool BandSliders(Rect rect, float[] values, float min, float max, float baseline,
                                   out float[] result,
                                   string style = SliderStyle.Default,
                                   float? defaultValue = null,
                                   System.Func<int, string> tooltipFor = null)
    {
        result = values;
        int n = values != null ? values.Length : 0;
        if (n == 0 || max <= min) return false;

        var def = ActiveSheet?.FindSlider(style) ?? new ZUISliderDef();
        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        var ev = Event.current;
        float slot = rect.width / n;
        const float gap = 2f;

        int IndexAt(float x) => Mathf.Clamp((int)((x - rect.x) / slot), 0, n - 1);
        float ValueAt(float y) => Mathf.Clamp(Mathf.Lerp(max, min, Mathf.InverseLerp(rect.y, rect.yMax, y)), min, max);

        bool changed = false;
        float[] copy = null;
        void Set(int i, float v) {
            if (copy == null) copy = (float[])values.Clone();
            if (!Mathf.Approximately(copy[i], v)) { copy[i] = v; changed = true; }
        }

        switch (ev.type)
        {
            case EventType.MouseDown:
                if (ev.button == 0 && rect.Contains(ev.mousePosition)) {
                    int i = IndexAt(ev.mousePosition.x);
                    if (ev.clickCount == 2 && defaultValue.HasValue) Set(i, Mathf.Clamp(defaultValue.Value, min, max));
                    else {
                        GUIUtility.hotControl = id;
                        bandDragId = id;
                        bandLastIndex = i;
                        bandLastValue = ValueAt(ev.mousePosition.y);
                        Set(i, bandLastValue);
                    }
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id && bandDragId == id) {
                    int i = IndexAt(ev.mousePosition.x);
                    float v = ValueAt(ev.mousePosition.y);
                    // Fill in every bar between the last one touched and this one, so a quick sweep leaves no gaps.
                    int from = bandLastIndex < 0 ? i : bandLastIndex;
                    int step = i >= from ? 1 : -1;
                    for (int k = from; k != i + step; k += step) {
                        float t = i == from ? 1f : (float)(k - from) / (i - from);
                        Set(k, Mathf.Lerp(bandLastValue, v, t));
                    }
                    bandLastIndex = i;
                    bandLastValue = v;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; bandDragId = -1; bandLastIndex = -1; ev.Use(); }
                break;
            case EventType.Repaint: {
                float yOf(float v) => Mathf.Lerp(rect.yMax, rect.y, Mathf.InverseLerp(min, max, v));
                float yBase = yOf(Mathf.Clamp(baseline, min, max));
                bool dragging = GUIUtility.hotControl == id;
                int hovered = rect.Contains(ev.mousePosition) ? IndexAt(ev.mousePosition.x) : -1;
                for (int i = 0; i < n; i++) {
                    var bar = new Rect(rect.x + i * slot + gap * 0.5f, rect.y, Mathf.Max(1f, slot - gap), rect.height);
                    // The empty groove behind every bar, so each bar's full reach is visible even when it is low.
                    def.track?.DrawBackground(bar);
                    float v = values[i];
                    float yv = yOf(Mathf.Clamp(v, min, max));
                    var fill = new Rect(bar.x, Mathf.Min(yv, yBase), bar.width, Mathf.Max(1f, Mathf.Abs(yBase - yv)));
                    if (def.trackFill != null) def.trackFill.DrawBackground(fill);
                    else EditorGUI.DrawRect(fill, new Color(0.35f, 0.6f, 0.9f));
                    if (i == hovered || (dragging && i == bandLastIndex))
                        EditorGUI.DrawRect(bar, new Color(1f, 1f, 1f, 0.07f));
                    if (v > max || v < min)
                        EditorGUI.DrawRect(new Rect(bar.x, v > max ? bar.y : bar.yMax - 2f, bar.width, 2f), new Color(1f, 0.85f, 0.35f));
                }
                // The baseline across the whole band, so "zero" is one line rather than a guess per bar.
                if (baseline > min && baseline < max)
                    EditorGUI.DrawRect(new Rect(rect.x, yBase - 0.5f, rect.width, 1f), new Color(1f, 1f, 1f, 0.35f));
                break;
            }
        }

        // Per-bar hover text, when the caller has something to say about each bar.
        if (tooltipFor != null) {
            for (int i = 0; i < n; i++)
                GUI.Label(new Rect(rect.x + i * slot, rect.y, slot, rect.height), new GUIContent("", tooltipFor(i)), GUIStyle.none);
        }

        if (changed) { result = copy; GUI.changed = true; }
        return changed;
    }
}

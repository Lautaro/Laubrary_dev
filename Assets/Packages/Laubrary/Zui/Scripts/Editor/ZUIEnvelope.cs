// ZUIEnvelope.cs
// ZUI.Envelope control (IMGUI, editor-only).
//
// API shape follows ZUISlider: a named ZUIEnvelopeDef on the style sheet owns
// appearance (background, grid, handle variants per edit state, markers, curve
// thickness). Per-call runtime args supply domain, marker list, interaction
// flags, and callbacks. The curve color is supplied by the caller as a
// ZUIColorRef so consumers can reference a palette entry (volume, pitch, etc.).
//
// Per-point edit permissions live on ZUIEnvelopePoint.editState. The runtime
// arg `anchorsLocked` is a convenience: when true, first + last points are
// treated as NotEditable regardless of their own state.
//
// Markers are vertical lines the caller adds to ZUIEnvelopeRuntime.markers.
// LMB drags a single marker; RMB on a marker or in a tinted band drags all
// markers as a group, preserving their gaps (mirrors Zounds' trim handles).

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ── ZUIEnvelopeRuntime ───────────────────────────────────────────────────────
// Per-call envelope state: domain, markers, interaction flags, callbacks.
// Separate from ZUIEnvelopeDef (style).

public class ZUIEnvelopeRuntime
{
    // Domain
    public float xMin = 0f;
    public float xMax = 1f;
    public float yMin = 0f;
    public float yMax = 1f;

    /// <summary>Force first + last points to NotEditable regardless of their own editState.</summary>
    public bool anchorsLocked = false;

    // ── Legacy loop fields (kept as sugar) ───────────────────────────────────
    // When `markers` is null and `loopEnabled` is true, ZUI.Envelope auto-builds
    // a two-marker pair each frame from these fields and writes back into them
    // if the user drags. Consumers that want full control should set `markers`.
    public bool  loopEnabled = false;
    public float loopStart   = 0f;
    public float loopEnd     = 1f;

    /// <summary>Markers to render and allow dragging. Null is fine. Caller owns the list
    /// (writes back on drag). If null and loopEnabled, a legacy two-marker pair is
    /// auto-created each frame from loopStart/loopEnd.</summary>
    public List<ZUIEnvelopeMarker> markers;

    // Visual toggles (kept per-call — usually tied to the instance, not the style)
    public bool showGrid        = false;
    public bool showValueLabels = false;

    // Master interaction switch
    public bool editable           = true;
    public bool allowAddPoints     = true;
    public bool allowRemovePoints  = true;
    public bool allowExponentEdit  = true;
    public bool allowSegmentDrag   = true;
    public bool allowBoxSelect     = true;

    // Callbacks
    public Action onDragStarted;
    public Action onDragUpdated;
    public Action onMutated;
}

// ── ZUI.Envelope ─────────────────────────────────────────────────────────────
public static partial class ZUI
{
    static readonly Dictionary<int, EnvelopeState> s_envStates = new();

    class EnvelopeState
    {
        // Point drag
        public int dragPointIdx     = -1;
        public int dragLineIdx      = -1;    // second point of the segment being drag-moved
        public int dragExponentIdx  = -1;

        // Marker drag
        public int   dragMarkerIdx     = -1;  // single-marker LMB drag
        public bool  dragMarkerGroup;         // RMB group drag
        public float dragMarkerGrabOffset;    // mouseTime - markers[primary].time when group drag started
        public int   dragMarkerPrimary   = -1;
        public float[] dragMarkerOffsets;     // per-marker offset from primary at drag start

        // Box select
        public bool isBoxSelecting;
        public Vector2 boxStart;
        public readonly List<int> selected = new();

        // Repaint-only hover state
        public int hoverPointIdx  = -1;
        public int hoverLineIdx   = -1;  // second index of segment under cursor
        public int hoverMarkerIdx = -1;

        public void Reset()
        {
            dragPointIdx = dragLineIdx = dragExponentIdx = -1;
            dragMarkerIdx = dragMarkerPrimary = -1;
            dragMarkerGroup = false;
            dragMarkerOffsets = null;
            isBoxSelecting = false;
            selected.Clear();
            hoverPointIdx = hoverLineIdx = hoverMarkerIdx = -1;
        }
    }

    static EnvelopeState GetEnvState(int key)
    {
        if (!s_envStates.TryGetValue(key, out var s)) { s = new EnvelopeState(); s_envStates[key] = s; }
        return s;
    }

    // ── Entry points ─────────────────────────────────────────────────────────

    /// <summary>Layouted variant — resolves a Def by name and draws into a layout rect.</summary>
    public static bool Envelope(List<ZUIEnvelopePoint> points, ZUIColorRef curveColor,
                                string styleName, ZUIEnvelopeRuntime rt,
                                float minWidth = 100f, float height = 80f, int stateKey = 0,
                                params GUILayoutOption[] options)
    {
        var def = ActiveSheet != null ? ActiveSheet.FindEnvelope(styleName) : null;
        Rect rect = GUILayoutUtility.GetRect(minWidth, height, options);
        return Envelope(rect, points, curveColor, def, rt, stateKey);
    }

    /// <summary>Layouted variant — draws with an explicit Def.</summary>
    public static bool Envelope(List<ZUIEnvelopePoint> points, ZUIColorRef curveColor,
                                ZUIEnvelopeDef def, ZUIEnvelopeRuntime rt,
                                float minWidth = 100f, float height = 80f, int stateKey = 0,
                                params GUILayoutOption[] options)
    {
        Rect rect = GUILayoutUtility.GetRect(minWidth, height, options);
        return Envelope(rect, points, curveColor, def, rt, stateKey);
    }

    /// <summary>Rect variant — draws with an explicit Def into an explicit rect.</summary>
    public static bool Envelope(Rect rect, List<ZUIEnvelopePoint> points, ZUIColorRef curveColor,
                                ZUIEnvelopeDef def, ZUIEnvelopeRuntime rt, int stateKey = 0)
    {
        if (def == null) def = k_FallbackDef;
        if (rt  == null) rt  = k_FallbackRuntime;

        // ── Background ───────────────────────────────────────────────────────
        def.background.DrawRect(rect, def.ownerSheet);
        DrawEnvelopeBorder(rect, def.border, def.ownerSheet);

        if (points == null)
        {
            EditorGUI.LabelField(rect, "No envelope data", EditorStyles.centeredGreyMiniLabel);
            return false;
        }

        float xRange = rt.xMax - rt.xMin;
        float yRange = rt.yMax - rt.yMin;
        if (xRange <= 0f || yRange <= 0f) return false;

        // The curve, handles, grid, and markers draw inside this inset. Background
        // and border stay on the outer rect. This keeps edge handles fully visible
        // instead of overflowing past the border.
        Rect plotRect = new Rect(
            rect.x + def.paddingLeft,
            rect.y + def.paddingTop,
            Mathf.Max(0f, rect.width  - def.paddingLeft - def.paddingRight),
            Mathf.Max(0f, rect.height - def.paddingTop  - def.paddingBottom));

        // ── Grid ─────────────────────────────────────────────────────────────
        if (rt.showGrid && def.gridRows > 0)
        {
            Color g = def.gridColor.Resolve(def.ownerSheet);
            for (int i = 1; i < def.gridRows; i++)
            {
                float y = plotRect.y + plotRect.height * i / def.gridRows;
                EditorGUI.DrawRect(new Rect(plotRect.x, y, plotRect.width, 1f), g);
            }
        }

        Handles.BeginGUI();
        var prevColor = Handles.color;

        var state = GetEnvState(stateKey);

        // Resolve the marker list: explicit list on rt wins; otherwise the legacy
        // loop-fields produce a synthetic two-marker pair.
        var markers = ResolveMarkers(rt);

        // Hover detection runs first so curve + handles + markers can style themselves.
        UpdateHoverState(plotRect, points, rt, def, markers, state);

        // ── Curve ────────────────────────────────────────────────────────────
        Color curveCol = curveColor.Resolve(def.ownerSheet);
        DrawEnvelopeCurve(plotRect, points, rt, def, curveCol, state);

        // ── Marker fill bands (draw behind handles + markers) ───────────────
        DrawMarkerFillBands(plotRect, rt, def, markers);

        // ── Point handles ────────────────────────────────────────────────────
        DrawEnvelopePoints(plotRect, points, rt, def, state);

        // ── Curve ghost-preview (when allowAddPoints + hovering empty curve) ─
        DrawAddPointGhost(plotRect, points, rt, def, state);

        // ── Marker lines ─────────────────────────────────────────────────────
        DrawMarkers(plotRect, rt, def, markers, state);

        // ── Input ────────────────────────────────────────────────────────────
        bool dirty = false;
        if (rt.editable)
            dirty = HandleEnvelopeInput(plotRect, points, rt, def, markers, state);

        // Back-propagate marker moves into legacy loopStart/loopEnd so callers
        // that rely on those fields see changes without reading `rt.markers`.
        WriteBackLegacyLoopFields(rt, markers);

        // ── Value labels ─────────────────────────────────────────────────────
        if (rt.showValueLabels)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            EditorGUI.LabelField(new Rect(rect.x + 2f, rect.y + 2f, 60f, 14f),
                                 rt.yMax.ToString("F2"), EditorStyles.miniLabel);
            EditorGUI.LabelField(new Rect(rect.x + 2f, rect.y + rect.height - 14f, 60f, 14f),
                                 rt.yMin.ToString("F2"), EditorStyles.miniLabel);
            GUI.color = prev;
        }

        Handles.color = prevColor;
        Handles.EndGUI();
        return dirty;
    }

    // Fallbacks used when Def / Runtime args are null.
    static readonly ZUIEnvelopeDef     k_FallbackDef     = new ZUIEnvelopeDef { name = "__fallback__" };
    static readonly ZUIEnvelopeRuntime k_FallbackRuntime = new ZUIEnvelopeRuntime();

    // A reused synthetic list so we don't allocate every frame in the legacy-loop path.
    static readonly List<ZUIEnvelopeMarker> s_legacyMarkers = new List<ZUIEnvelopeMarker>(2);

    static List<ZUIEnvelopeMarker> ResolveMarkers(ZUIEnvelopeRuntime rt)
    {
        if (rt.markers != null) return rt.markers;
        if (!rt.loopEnabled)    return null;

        while (s_legacyMarkers.Count < 2) s_legacyMarkers.Add(new ZUIEnvelopeMarker());
        s_legacyMarkers[0].time             = rt.loopStart;
        s_legacyMarkers[0].editable         = true;
        s_legacyMarkers[0].clampToPrev      = false;
        s_legacyMarkers[0].fillBandFromPrev = false;
        s_legacyMarkers[1].time             = rt.loopEnd;
        s_legacyMarkers[1].editable         = true;
        s_legacyMarkers[1].clampToPrev      = true;
        s_legacyMarkers[1].fillBandFromPrev = true;
        return s_legacyMarkers;
    }

    static void WriteBackLegacyLoopFields(ZUIEnvelopeRuntime rt, List<ZUIEnvelopeMarker> markers)
    {
        if (rt.markers != null) return; // caller owns explicit list
        if (markers == null || markers.Count < 2) return;
        rt.loopStart = markers[0].time;
        rt.loopEnd   = markers[1].time;
    }

    static void DrawEnvelopeBorder(Rect rect, ZUIBorderDef border, ZUIStyleSheetAsset sheet)
    {
        if (border == null) return;
        if (Event.current.type != EventType.Repaint) return;
        var ew = border.edgeWidth;
        float bT = ew.Top, bR = ew.Right, bB = ew.Bottom, bL = ew.Left;
        if (bT <= 0f && bR <= 0f && bB <= 0f && bL <= 0f) return;
        Color bc = border.color.GetColorA(sheet);
        if (bc.a <= 0f) return;
        if (bT > 0f) EditorGUI.DrawRect(new Rect(rect.x,          rect.y,          rect.width, bT),          bc);
        if (bB > 0f) EditorGUI.DrawRect(new Rect(rect.x,          rect.yMax - bB,  rect.width, bB),          bc);
        if (bL > 0f) EditorGUI.DrawRect(new Rect(rect.x,          rect.y,          bL,         rect.height), bc);
        if (bR > 0f) EditorGUI.DrawRect(new Rect(rect.xMax - bR,  rect.y,          bR,         rect.height), bc);
    }

    // ── Edit state resolution ────────────────────────────────────────────────

    static ZUIEnvelopeEditState EffectiveState(int idx, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt)
    {
        if (rt.anchorsLocked && (idx == 0 || idx == points.Count - 1))
            return ZUIEnvelopeEditState.NotEditable;
        return points[idx].editState;
    }

    static bool CanMoveX(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.XEditable;
    static bool CanMoveY(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable || s == ZUIEnvelopeEditState.YEditable;
    static bool CanRemove(ZUIEnvelopeEditState s) => s == ZUIEnvelopeEditState.Editable;

    // ── Coordinate helpers ──────────────────────────────────────────────────

    static float TimeToX(float t, Rect rect, ZUIEnvelopeRuntime rt)
        => rect.x + (t - rt.xMin) / (rt.xMax - rt.xMin) * rect.width;

    static float XToTime(float x, Rect rect, ZUIEnvelopeRuntime rt)
        => rt.xMin + (x - rect.x) / rect.width * (rt.xMax - rt.xMin);

    static float ValueToY(float v, Rect rect, ZUIEnvelopeRuntime rt)
        => rect.y + rect.height - (v - rt.yMin) / (rt.yMax - rt.yMin) * rect.height;

    // ── Hover ────────────────────────────────────────────────────────────────

    static void UpdateHoverState(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt,
                                 ZUIEnvelopeDef def, List<ZUIEnvelopeMarker> markers, EnvelopeState state)
    {
        var e = Event.current;
        // Only update hover on events that carry a fresh mouse position. Repaint
        // and Layout both reuse the last non-mouse event's coordinates, so running
        // hover math on them would latch onto a stale position and never clear.
        if (e.type != EventType.MouseMove &&
            e.type != EventType.MouseDown &&
            e.type != EventType.MouseUp   &&
            e.type != EventType.MouseDrag) return;

        state.hoverPointIdx  = -1;
        state.hoverLineIdx   = -1;
        state.hoverMarkerIdx = -1;

        // Expand the accepted hover region by the maximum possible hit radius
        // so edge-point handles (at xMin/xMax) can still be hovered when the
        // mouse is in the padding zone just outside plotRect.
        float slop = Mathf.Max(def.editable.radius, def.xEditable.radius,
                               Mathf.Max(def.yEditable.radius, def.notEditable.radius)) + def.hitRadiusExtra;
        Rect hoverRect = new Rect(rect.x - slop, rect.y - slop,
                                   rect.width + slop * 2f, rect.height + slop * 2f);
        if (!hoverRect.Contains(e.mousePosition)) return;

        // Point hit first — HitPoint does its own distance test, so it correctly
        // matches a handle even when the mouse is just outside plotRect.
        int pHit = HitPoint(rect, points, rt, def, e.mousePosition);
        if (pHit >= 0)
        {
            if (EffectiveState(pHit, points, rt) != ZUIEnvelopeEditState.NotEditable)
                state.hoverPointIdx = pHit;
            return;
        }

        // Line / marker hits are meaningful only inside the plot area.
        if (!rect.Contains(e.mousePosition)) return;

        int mHit = HitMarker(rect, rt, def, markers, e.mousePosition);
        if (mHit >= 0)
        {
            if (markers != null && markers[mHit].editable)
                state.hoverMarkerIdx = mHit;
            return;
        }

        int lHit = HitLine(rect, points, rt, e.mousePosition);
        if (lHit > 0) state.hoverLineIdx = lHit;
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    static void DrawEnvelopeCurve(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt,
                                  ZUIEnvelopeDef def, Color color, EnvelopeState state)
    {
        if (points.Count == 0) return;

        float xRange = rt.xMax - rt.xMin;
        float yRange = rt.yMax - rt.yMin;
        int nEval = Mathf.Max(2, (int)(rect.width / 3f));

        Handles.color = color;
        var buf = new Vector3[nEval + 1];
        for (int i = 0; i <= nEval; i++)
        {
            float t = rt.xMin + xRange * i / nEval;
            float v = EvaluateEnvelope(points, t, rt.yMax);
            buf[i] = new Vector3(TimeToX(t, rect, rt), ValueToY(v, rect, rt), 0f);
        }
        Handles.DrawAAPolyLine(def.curveThickness, buf);

        // Only highlight the segment when Shift is held (segment-drag affordance).
        // Without Shift, we show a ghost add-point instead (see DrawAddPointGhost).
        int hoverLine = state.hoverLineIdx;
        if (hoverLine > 0 && hoverLine < points.Count && Event.current.shift)
        {
            var a = points[hoverLine - 1];
            var b = points[hoverLine];
            int segPx = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(TimeToX(b.time, rect, rt) - TimeToX(a.time, rect, rt)) / 2f));
            var seg = new Vector3[segPx + 1];
            for (int i = 0; i <= segPx; i++)
            {
                float t = Mathf.Lerp(a.time, b.time, (float)i / segPx);
                float v = EvaluateEnvelope(points, t, rt.yMax);
                seg[i] = new Vector3(TimeToX(t, rect, rt), ValueToY(v, rect, rt), 0f);
            }
            Color.RGBToHSV(color, out float h, out float s, out float vC);
            Handles.color = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.8f), Mathf.Min(vC * 1.4f, 1f));
            Handles.DrawAAPolyLine(def.curveHoverThickness, seg);
        }
    }

    static void DrawAddPointGhost(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt,
                                  ZUIEnvelopeDef def, EnvelopeState state)
    {
        // Only when: allowAddPoints, mouse is near the curve (lineHover set), Shift not held.
        if (!rt.allowAddPoints) return;
        if (Event.current.shift) return;
        if (state.hoverLineIdx <= 0) return;
        if (state.dragPointIdx >= 0 || state.dragLineIdx >= 0 || state.dragMarkerIdx >= 0 || state.dragMarkerGroup)
            return;

        var e = Event.current;
        if (e.type == EventType.Layout || e.type == EventType.Used) return;

        float t = XToTime(e.mousePosition.x, rect, rt);
        t = Mathf.Clamp(t, rt.xMin, rt.xMax);
        float v = EvaluateEnvelope(points, t, rt.yMax);
        float x = TimeToX(t, rect, rt);
        float y = ValueToY(v, rect, rt);

        var handle = def.GetHandle(ZUIEnvelopeEditState.Editable);
        Color fill = handle.fillColor.Resolve(def.ownerSheet);
        fill.a *= 0.5f;
        Handles.color = fill;
        Handles.DrawSolidDisc(new Vector3(x, y, 0f), Vector3.forward, handle.radius);
    }

    static void DrawEnvelopePoints(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt,
                                   ZUIEnvelopeDef def, EnvelopeState state)
    {
        Color selCol = def.selectedColor.Resolve(def.ownerSheet);

        for (int i = 0; i < points.Count; i++)
        {
            var p  = points[i];
            var es = EffectiveState(i, points, rt);
            var h  = def.GetHandle(es);

            float x = TimeToX(p.time,  rect, rt);
            float y = ValueToY(p.value, rect, rt);
            Vector3 c = new Vector3(x, y, 0f);

            bool isHover = state.hoverPointIdx == i || state.dragPointIdx == i;
            bool isSel   = state.selected.Contains(i) || state.dragPointIdx == i;

            // Visual radius grows on hover so there's clear feedback, while the
            // hit-test in HitPoint uses radius + def.hitRadiusExtra — so the
            // handle can be small on screen but still easy to grab.
            float visualR = isHover && es != ZUIEnvelopeEditState.NotEditable ? h.hoverRadius : h.radius;

            Color fill = isHover ? h.hoverFillColor.Resolve(def.ownerSheet) : h.fillColor.Resolve(def.ownerSheet);
            if (isSel) fill = selCol;

            if (h.borderWidth > 0f)
            {
                Handles.color = h.borderColor.Resolve(def.ownerSheet);
                Handles.DrawSolidDisc(c, Vector3.forward, visualR + h.borderWidth);
            }
            Handles.color = fill;
            Handles.DrawSolidDisc(c, Vector3.forward, visualR);

            if (isHover && !isSel && es != ZUIEnvelopeEditState.NotEditable)
            {
                Handles.color = selCol;
                Handles.DrawWireDisc(c, Vector3.forward, visualR + 1.5f);
            }
        }
    }

    static void DrawMarkerFillBands(Rect rect, ZUIEnvelopeRuntime rt, ZUIEnvelopeDef def,
                                    List<ZUIEnvelopeMarker> markers)
    {
        if (markers == null || markers.Count < 2) return;
        Color fill = def.markerColor.Resolve(def.ownerSheet);
        fill.a *= def.markerFillAlphaScale;
        for (int i = 1; i < markers.Count; i++)
        {
            var m = markers[i];
            if (!m.fillBandFromPrev) continue;
            float x0 = TimeToX(markers[i - 1].time, rect, rt);
            float x1 = TimeToX(m.time,              rect, rt);
            if (x1 <= x0) continue;
            EditorGUI.DrawRect(new Rect(x0, rect.y, x1 - x0, rect.height), fill);
        }
    }

    static void DrawMarkers(Rect rect, ZUIEnvelopeRuntime rt, ZUIEnvelopeDef def,
                            List<ZUIEnvelopeMarker> markers, EnvelopeState state)
    {
        if (markers == null || markers.Count == 0) return;

        Color baseCol  = def.markerColor.Resolve(def.ownerSheet);
        Color hoverCol = def.markerHoverColor.Resolve(def.ownerSheet);

        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (m.time < rt.xMin || m.time > rt.xMax) continue;

            bool isHover = m.editable && (state.hoverMarkerIdx == i ||
                                          state.dragMarkerIdx == i ||
                                          (state.dragMarkerGroup && state.dragMarkerOffsets != null));
            float thick  = isHover ? def.markerHoverThickness : def.markerThickness;
            Color col    = isHover ? hoverCol : baseCol;

            float x = TimeToX(m.time, rect, rt);
            EditorGUI.DrawRect(new Rect(x - thick * 0.5f, rect.y, thick, rect.height), col);
        }
    }

    // ── Evaluation ───────────────────────────────────────────────────────────

    static float EvaluateEnvelope(List<ZUIEnvelopePoint> points, float time, float fallback)
    {
        if (points.Count == 0) return fallback;
        if (points.Count == 1) return points[0].value;

        int idx = points.Count;
        for (int i = 0; i < points.Count; i++)
            if (points[i].time > time) { idx = i; break; }

        if (idx <= 0) return points[0].value;
        if (idx >= points.Count) return points[points.Count - 1].value;

        var a = points[idx - 1];
        var b = points[idx];
        float range = b.time - a.time;
        if (range <= 0f) return b.value;
        float t = (time - a.time) / range;
        return BendSegment(a.value, b.value, t, b.exponent);
    }

    /// <summary>
    /// Interpolate between two values with the canonical DAW-style bend:
    /// Lerp(a, b, Pow(t, exponent)). exponent == 1 → linear. exponent &lt; 1
    /// → log-style (fast rise, slow finish). exponent > 1 → exp-style
    /// (slow rise, fast finish). Matches the curve math used by REAPER,
    /// Ableton, Bitwig, FL Studio, Renoise, etc. — asymmetric by design;
    /// the peak deviation from linear sits closer to whichever endpoint
    /// the curve hugs, not at the midpoint.
    /// </summary>
    static float BendSegment(float aValue, float bValue, float t, float exponent)
    {
        if (exponent <= 0f) exponent = 0.000001f;
        return Mathf.Lerp(aValue, bValue, Mathf.Pow(t, exponent));
    }

    // ── Input ────────────────────────────────────────────────────────────────

    static bool HandleEnvelopeInput(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt,
                                    ZUIEnvelopeDef def, List<ZUIEnvelopeMarker> markers,
                                    EnvelopeState state)
    {
        Event e = Event.current;
        float xRange = rt.xMax - rt.xMin;
        float yRange = rt.yMax - rt.yMin;

        bool endDrag = e.type == EventType.MouseUp || e.type == EventType.Ignore || e.type == EventType.DragExited;

        // ── Active marker drag — single ──────────────────────────────────────
        if (state.dragMarkerIdx >= 0 && markers != null && state.dragMarkerIdx < markers.Count)
        {
            if (e.type == EventType.MouseDrag)
            {
                float t = Mathf.Clamp(XToTime(e.mousePosition.x, rect, rt), rt.xMin, rt.xMax);
                t = ClampMarker(markers, state.dragMarkerIdx, t, rt);
                markers[state.dragMarkerIdx].time = t;
                e.Use();
                rt.onDragUpdated?.Invoke();
                return true;
            }
            if (endDrag) state.dragMarkerIdx = -1;
            return false;
        }

        // ── Active marker drag — group (RMB) ────────────────────────────────
        if (state.dragMarkerGroup && markers != null && state.dragMarkerOffsets != null)
        {
            if (e.type == EventType.MouseDrag)
            {
                float mouseT = XToTime(e.mousePosition.x, rect, rt);
                float newPrimaryT = mouseT - state.dragMarkerGrabOffset;
                // Clamp translation so no marker leaves the domain.
                float minOff = 0f, maxOff = 0f;
                for (int i = 0; i < markers.Count; i++)
                {
                    float off = state.dragMarkerOffsets[i];
                    if (off < minOff) minOff = off;
                    if (off > maxOff) maxOff = off;
                }
                newPrimaryT = Mathf.Clamp(newPrimaryT, rt.xMin - minOff, rt.xMax - maxOff);
                for (int i = 0; i < markers.Count; i++)
                    markers[i].time = newPrimaryT + state.dragMarkerOffsets[i];
                e.Use();
                rt.onDragUpdated?.Invoke();
                return true;
            }
            if (endDrag || (e.button == 1 && e.type == EventType.MouseUp))
            {
                state.dragMarkerGroup = false;
                state.dragMarkerOffsets = null;
                state.dragMarkerPrimary = -1;
            }
            return false;
        }

        // ── Active point drag ────────────────────────────────────────────────
        if (state.dragPointIdx >= 0 && state.dragPointIdx < points.Count)
        {
            if (e.type == EventType.MouseDrag)
            {
                var p  = points[state.dragPointIdx];
                var es = EffectiveState(state.dragPointIdx, points, rt);

                float t = Mathf.Clamp(XToTime(e.mousePosition.x, rect, rt), rt.xMin, rt.xMax);
                float v = Mathf.Clamp(rt.yMin + yRange * (1f - (e.mousePosition.y - rect.y) / rect.height),
                                      rt.yMin, rt.yMax);
                if (state.dragPointIdx > 0)                 t = Mathf.Max(t, points[state.dragPointIdx - 1].time);
                if (state.dragPointIdx < points.Count - 1)  t = Mathf.Min(t, points[state.dragPointIdx + 1].time);

                if (CanMoveX(es)) p.time  = t;
                if (CanMoveY(es)) p.value = v;
                e.Use();
                rt.onDragUpdated?.Invoke();
                return true;
            }
            if (endDrag) state.dragPointIdx = -1;
            return false;
        }

        // ── Active segment drag ──────────────────────────────────────────────
        if (state.dragLineIdx > 0 && state.dragLineIdx < points.Count)
        {
            if (e.type == EventType.MouseDrag && e.shift)
            {
                MoveSegment(points, rt, state.dragLineIdx, xRange, yRange, rect, e);
                e.Use();
                rt.onDragUpdated?.Invoke();
                return true;
            }
            if (endDrag || !e.shift) state.dragLineIdx = -1;
            return false;
        }

        // ── Active exponent drag ─────────────────────────────────────────────
        if (state.dragExponentIdx > 0 && state.dragExponentIdx < points.Count)
        {
            if (e.type == EventType.MouseDrag && e.shift)
            {
                var p = points[state.dragExponentIdx];
                var prev = points[state.dragExponentIdx - 1];
                float mul = p.exponent <= 0f ? 0.000001f : Mathf.Sqrt(p.exponent);
                float dy = e.delta.y;
                if (p.value < prev.value) dy = -dy;
                p.exponent = Mathf.Max(0f, p.exponent + dy / rect.height * mul * 8f);
                e.Use();
                rt.onDragUpdated?.Invoke();
                return true;
            }
            if (endDrag || !e.shift) state.dragExponentIdx = -1;
            return false;
        }

        // ── Box select in-progress ───────────────────────────────────────────
        if (state.isBoxSelecting)
        {
            BoxSelectUpdate(rect, points, rt, def, state, e);
            if (endDrag) state.isBoxSelecting = false;
            return false;
        }

        // ── Multi-select drag ────────────────────────────────────────────────
        if (state.selected.Count > 1 && e.type == EventType.MouseDrag && e.button == 0)
        {
            rt.onDragStarted?.Invoke();
            MoveSelected(points, rt, state, xRange, yRange, rect, e);
            e.Use();
            rt.onDragUpdated?.Invoke();
            return true;
        }

        if (!rect.Contains(e.mousePosition)) return false;

        if (e.type == EventType.MouseDown)
        {
            // 1) Marker hit?
            int mHit = HitMarker(rect, rt, def, markers, e.mousePosition);
            if (mHit >= 0 && markers[mHit].editable)
            {
                if (e.button == 0)
                {
                    rt.onDragStarted?.Invoke();
                    state.dragMarkerIdx = mHit;
                    e.Use();
                    return false;
                }
                if (e.button == 1)
                {
                    StartMarkerGroupDrag(rt, markers, mHit, e.mousePosition, rect, state);
                    e.Use();
                    return false;
                }
            }

            // 1b) RMB inside a tinted fill band? → group drag as well.
            if (e.button == 1 && markers != null)
            {
                int bandEnd = HitFillBand(rect, rt, markers, e.mousePosition);
                if (bandEnd > 0)
                {
                    StartMarkerGroupDrag(rt, markers, bandEnd, e.mousePosition, rect, state);
                    e.Use();
                    return false;
                }
            }

            // 2) Existing point hit?
            int pointHit = HitPoint(rect, points, rt, def, e.mousePosition);
            if (pointHit >= 0)
            {
                var es = EffectiveState(pointHit, points, rt);
                if (es == ZUIEnvelopeEditState.NotEditable) return false;

                if (e.button == 0)
                {
                    if (e.clickCount == 2 && rt.allowRemovePoints && CanRemove(es))
                    {
                        rt.onDragStarted?.Invoke();
                        points.RemoveAt(pointHit);
                        state.selected.Clear();
                        e.Use();
                        rt.onMutated?.Invoke();
                        return true;
                    }
                    rt.onDragStarted?.Invoke();
                    state.dragPointIdx = pointHit;
                    if (!state.selected.Contains(pointHit))
                    {
                        state.selected.Clear();
                        state.selected.Add(pointHit);
                    }
                    GUI.FocusControl(null);
                    e.Use();
                }
                else if (e.button == 1 && e.shift && rt.allowExponentEdit && pointHit > 0)
                {
                    rt.onDragStarted?.Invoke();
                    state.dragExponentIdx = pointHit;
                    e.Use();
                }
                return false;
            }

            // 3) Line hit?
            int lineHit = HitLine(rect, points, rt, e.mousePosition);
            if (lineHit > 0)
            {
                if (e.shift && e.button == 0 && rt.allowSegmentDrag)
                {
                    rt.onDragStarted?.Invoke();
                    state.dragLineIdx = lineHit;
                    state.selected.Clear();
                    e.Use();
                    return false;
                }
                if (e.shift && e.button == 1 && rt.allowExponentEdit)
                {
                    rt.onDragStarted?.Invoke();
                    state.dragExponentIdx = lineHit;
                    state.selected.Clear();
                    e.Use();
                    return false;
                }
                if (e.button == 0 && rt.allowAddPoints)
                {
                    float t = XToTime(e.mousePosition.x, rect, rt);
                    float v = EvaluateEnvelope(points, t, rt.yMax);
                    rt.onDragStarted?.Invoke();
                    int insert = InsertSorted(points, new ZUIEnvelopePoint(t, v, 1f));
                    state.dragPointIdx = insert;
                    state.selected.Clear();
                    state.selected.Add(insert);
                    e.Use();
                    rt.onMutated?.Invoke();
                    return true;
                }
            }

            // 4) Empty area — start box select (LMB).
            if (e.button == 0 && rt.allowBoxSelect)
            {
                state.isBoxSelecting = true;
                state.boxStart = e.mousePosition;
                state.selected.Clear();
                GUI.FocusControl(null);
                e.Use();
            }
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete &&
            rt.allowRemovePoints && state.selected.Count > 0)
        {
            rt.onDragStarted?.Invoke();
            state.selected.Sort();
            for (int i = state.selected.Count - 1; i >= 0; i--)
            {
                int idx = state.selected[i];
                if (idx < 0 || idx >= points.Count) continue;
                if (!CanRemove(EffectiveState(idx, points, rt))) continue;
                points.RemoveAt(idx);
            }
            state.selected.Clear();
            e.Use();
            rt.onMutated?.Invoke();
            return true;
        }

        return false;
    }

    static void StartMarkerGroupDrag(ZUIEnvelopeRuntime rt, List<ZUIEnvelopeMarker> markers,
                                     int primary, Vector2 mouse, Rect rect, EnvelopeState state)
    {
        rt.onDragStarted?.Invoke();
        state.dragMarkerGroup   = true;
        state.dragMarkerPrimary = primary;
        state.dragMarkerGrabOffset = XToTime(mouse.x, rect, rt) - markers[primary].time;
        state.dragMarkerOffsets = new float[markers.Count];
        for (int i = 0; i < markers.Count; i++)
            state.dragMarkerOffsets[i] = markers[i].time - markers[primary].time;
    }

    static float ClampMarker(List<ZUIEnvelopeMarker> markers, int idx, float t, ZUIEnvelopeRuntime rt)
    {
        t = Mathf.Clamp(t, rt.xMin, rt.xMax);
        var m = markers[idx];
        if (m.clampToPrev && idx > 0) t = Mathf.Max(t, markers[idx - 1].time);
        // If the NEXT marker has clampToPrev, this one cannot cross it either.
        if (idx + 1 < markers.Count && markers[idx + 1].clampToPrev) t = Mathf.Min(t, markers[idx + 1].time);
        return t;
    }

    // ── Hit testing ──────────────────────────────────────────────────────────

    static int HitPoint(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt,
                        ZUIEnvelopeDef def, Vector2 mouse)
    {
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var h = def.GetHandle(EffectiveState(i, points, rt));
            float x = TimeToX(p.time,  rect, rt);
            float y = ValueToY(p.value, rect, rt);
            // Hit-test radius is intentionally larger than the visual radius so
            // small on-screen handles remain easy to grab. Separate knob on the
            // Def (hitRadiusExtra) lets the sheet tune this independently.
            float hitR = h.radius + def.hitRadiusExtra;
            if (Vector2.Distance(mouse, new Vector2(x, y)) <= hitR) return i;
        }
        return -1;
    }

    /// <summary>Returns the index of the SECOND point of the segment under the mouse, or -1.</summary>
    static int HitLine(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt, Vector2 mouse)
    {
        if (points.Count < 2) return -1;
        float t = XToTime(mouse.x, rect, rt);
        float v = EvaluateEnvelope(points, t, rt.yMax);
        float y = ValueToY(v, rect, rt);
        if (Mathf.Abs(mouse.y - y) > 6f) return -1;
        for (int i = 1; i < points.Count; i++)
            if (points[i].time >= t) return i;
        return -1;
    }

    static int HitMarker(Rect rect, ZUIEnvelopeRuntime rt, ZUIEnvelopeDef def,
                         List<ZUIEnvelopeMarker> markers, Vector2 mouse)
    {
        if (markers == null) return -1;
        float slop = def.markerThickness * 0.5f + def.markerHitPadding;
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (m.time < rt.xMin || m.time > rt.xMax) continue;
            float x = TimeToX(m.time, rect, rt);
            if (Mathf.Abs(mouse.x - x) <= slop && mouse.y >= rect.y && mouse.y <= rect.yMax)
                return i;
        }
        return -1;
    }

    /// <summary>Returns the end-marker index of the fill band under the mouse, or -1.</summary>
    static int HitFillBand(Rect rect, ZUIEnvelopeRuntime rt, List<ZUIEnvelopeMarker> markers, Vector2 mouse)
    {
        if (markers == null || markers.Count < 2) return -1;
        if (mouse.y < rect.y || mouse.y > rect.yMax) return -1;
        for (int i = 1; i < markers.Count; i++)
        {
            if (!markers[i].fillBandFromPrev) continue;
            float x0 = TimeToX(markers[i - 1].time, rect, rt);
            float x1 = TimeToX(markers[i].time,     rect, rt);
            if (mouse.x >= x0 && mouse.x <= x1) return i;
        }
        return -1;
    }

    // ── Mutation helpers ─────────────────────────────────────────────────────

    static int InsertSorted(List<ZUIEnvelopePoint> points, ZUIEnvelopePoint p)
    {
        int idx = points.Count;
        for (int i = 0; i < points.Count; i++)
            if (p.time < points[i].time) { idx = i; break; }
        points.Insert(idx, p);
        return idx;
    }

    static void MoveSegment(List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt, int endIdx,
                            float xRange, float yRange, Rect rect, Event e)
    {
        var a = points[endIdx - 1];
        var b = points[endIdx];
        var aState = EffectiveState(endIdx - 1, points, rt);
        var bState = EffectiveState(endIdx,     points, rt);

        float dt = xRange * e.delta.x / rect.width;
        float dv = -yRange * e.delta.y / rect.height;

        if (CanMoveX(aState) && CanMoveX(bState))
        {
            float aNewT = a.time + dt, bNewT = b.time + dt;
            if (endIdx - 1 > 0)             aNewT = Mathf.Max(aNewT, points[endIdx - 2].time);
            if (endIdx + 1 < points.Count)  bNewT = Mathf.Min(bNewT, points[endIdx + 1].time);
            a.time = Mathf.Clamp(aNewT, rt.xMin, rt.xMax);
            b.time = Mathf.Clamp(bNewT, rt.xMin, rt.xMax);
        }
        if (CanMoveY(aState)) a.value = Mathf.Clamp(a.value + dv, rt.yMin, rt.yMax);
        if (CanMoveY(bState)) b.value = Mathf.Clamp(b.value + dv, rt.yMin, rt.yMax);
    }

    static void MoveSelected(List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt, EnvelopeState state,
                             float xRange, float yRange, Rect rect, Event e)
    {
        float dt = xRange * e.delta.x / rect.width;
        float dv = -yRange * e.delta.y / rect.height;
        foreach (int idx in state.selected)
        {
            if (idx < 0 || idx >= points.Count) continue;
            var p = points[idx];
            var es = EffectiveState(idx, points, rt);
            if (CanMoveX(es)) p.time  = Mathf.Clamp(p.time  + dt, rt.xMin, rt.xMax);
            if (CanMoveY(es)) p.value = Mathf.Clamp(p.value + dv, rt.yMin, rt.yMax);
        }
        points.Sort((x, y) => x.time.CompareTo(y.time));
    }

    static void BoxSelectUpdate(Rect rect, List<ZUIEnvelopePoint> points, ZUIEnvelopeRuntime rt,
                                ZUIEnvelopeDef def, EnvelopeState state, Event e)
    {
        Vector2 cur = new Vector2(
            Mathf.Clamp(e.mousePosition.x, rect.x, rect.x + rect.width),
            Mathf.Clamp(e.mousePosition.y, rect.y, rect.y + rect.height));

        float minX = Mathf.Min(state.boxStart.x, cur.x);
        float maxX = Mathf.Max(state.boxStart.x, cur.x);
        float minY = Mathf.Min(state.boxStart.y, cur.y);
        float maxY = Mathf.Max(state.boxStart.y, cur.y);

        state.selected.Clear();
        for (int i = 0; i < points.Count; i++)
        {
            if (EffectiveState(i, points, rt) == ZUIEnvelopeEditState.NotEditable) continue;
            var p = points[i];
            float px = TimeToX(p.time, rect, rt);
            float py = ValueToY(p.value, rect, rt);
            if (px >= minX && px <= maxX && py >= minY && py <= maxY)
                state.selected.Add(i);
        }

        Handles.color = def.selectedColor.Resolve(def.ownerSheet);
        Handles.DrawSolidRectangleWithOutline(
            new Rect(minX, minY, maxX - minX, maxY - minY),
            new Color(1f, 1f, 1f, 0.1f), Handles.color);
    }

    /// <summary>Clears cached drag/selection/hover state for a given envelope key.</summary>
    public static void EnvelopeResetState(int stateKey)
    {
        if (s_envStates.TryGetValue(stateKey, out var s)) s.Reset();
    }
}

// ZUIValue2DControl.cs
// Editor drawer for a PAIR of ZUIValues (x, y) presented as one synchronized 2D-position control, instead of
// two separate sliders — dragging two independent 1D sliders to aim an XY position is exactly the ergonomics
// problem this exists to fix.
//
// Static mode: one draggable point in an XY plot, plus a Reset-to-default button. Two independent value
// displays are available around that dot — small "(x, y)" text beside it, and/or a compact numeric X/Y input
// block — both code-defaulted via Options and user-overridable per control from the "⋯" right-click menu.
// Curve mode ("animate over time"): several numbered points connected by lines, tracing a PATH through XY space.
// Unlike the 1D envelope editor (x-axis = time, y-axis = value), here BOTH axes are spatial (X value / Y value);
// time is not plotted at all — it's implicit in point ORDER, evenly divided across the overall lifetime as
// points are added/removed (point i sits at i/(count-1) of the timeline). Click empty plot space to append a
// new point; drag a point to move it; right-click a point to remove it (min 2 kept).
//
// Deliberately reuses the EXISTING ZUIValue/ZUIEnvelopePoint data model and ZUIEnvelopeEvaluator unchanged — x
// and y are two independent, perfectly ordinary ZUIValues (Static or Curve mode, kept in lockstep by this
// control). Any code that already evaluates a ZUIValue (BlastRenderer.Eval, ZUIValue.Evaluate, ...) needs zero
// changes to consume a field pair authored this way — this file is a pure editor-side UX layer.

using UnityEditor;
using UnityEngine;

public static class ZUIValue2DControl
{
    public struct Options
    {
        public float xMin, xMax, yMin, yMax;   // plot bounds (also becomes each ZUIValue's own yMin/yMax range)
        public Vector2? staticDefault;         // Reset-to target for Static mode; null = (0,0)
        public float plotSize;

        // Static-mode value display (Curve mode is unaffected — its own numbered points are a different
        // concern). Both are code-set DEFAULTS only — the user can override either one per-control via the
        // "⋯" right-click menu, and that override wins for the rest of the session (see FoldState below).
        public bool showValueText;         // small "(x, y)" text beside the dot
        public bool showNumericInputs;     // a "label Y / input Y / label X / input X" numeric block

        // Code-only (not exposed on the right-click menu, per explicit request — this is a layout call for
        // whoever's building the window, not a per-user preference): when numeric inputs are shown, stack
        // them ABOVE the plot instead of beside it. Default false = side by side (the natural fit, since the
        // numeric block and the plot are both roughly square).
        public bool stackInputsVertically;

        public static Options Default => new Options
        {
            xMin = -1f, xMax = 1f, yMin = -1f, yMax = 1f,
            staticDefault = null,
            plotSize = 140f,
            showValueText = false,
            showNumericInputs = true,   // matches this control's pre-existing always-on numeric fields
            stackInputsVertically = false,
        };

        public Options WithRange(float xLo, float xHi, float yLo, float yHi)
        { xMin = xLo; xMax = xHi; yMin = yLo; yMax = yHi; return this; }
        public Options WithDefault(Vector2 v) { staticDefault = v; return this; }
        public Options WithPlotSize(float s) { plotSize = s; return this; }
        /// <summary>Default (code-provided) visibility for the two Static-mode value displays. The user can
        /// still flip either one per-control via the right-click menu; this only sets what a fresh control
        /// starts as.</summary>
        public Options WithValueDisplay(bool showText, bool showNumericInputs)
        { this.showValueText = showText; this.showNumericInputs = showNumericInputs; return this; }
        /// <summary>Stack the numeric-input block above the plot instead of beside it. Code-only — not a
        /// user-facing menu toggle.</summary>
        public Options WithVerticalStack(bool vertical = true) { stackInputsVertically = vertical; return this; }
    }

    static readonly Color PointColor = new Color(0.4f, 0.85f, 1f);
    static readonly Color LineColor = new Color(0.4f, 0.85f, 1f, 0.7f);
    const float HitRadius = 8f;
    const float SidePanelWidth = 108f;
    const float NumericSquareWidth = 82f;

    // Folds to a one-line thumbnail by default (like the 1D envelope's curve thumbnail) — the full plotSize
    // plot is only worth the vertical space while you're actually aiming the point/path. Keyed by the X value
    // (x/y are always a matched pair from one Layer field, so X's identity is a stable key for the pair).
    // The two `?` overrides hold a per-control user choice from the right-click menu, once made — null means
    // "no override yet, use whatever Options.showValueText/showNumericInputs the calling code set."
    class FoldState { public bool expanded; public bool? showValueTextOverride; public bool? showNumericInputsOverride; }
    static readonly System.Collections.Generic.Dictionary<ZUIValue, FoldState> s_fold = new();
    static FoldState GetFold(ZUIValue x)
    {
        if (!s_fold.TryGetValue(x, out var st)) { st = new FoldState(); s_fold[x] = st; }
        return st;
    }

    public static void Draw(string label, ZUIValue x, ZUIValue y) => Draw(label, x, y, Options.Default);

    public static void Draw(string label, ZUIValue x, ZUIValue y, Options opts)
    {
        if (x == null || y == null) return;
        var fold = GetFold(x);

        if (!fold.expanded)
        {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label))
            {
                float lw = EditorGUIUtility.labelWidth > 1f ? EditorGUIUtility.labelWidth : 90f;
                EditorGUILayout.LabelField(label, GUILayout.Width(lw));
            }
            Rect thumb = GUILayoutUtility.GetRect(60f, 18f, GUILayout.ExpandWidth(true), GUILayout.Height(18f));
            DrawThumb(thumb, x, y, opts);
            EditorGUIUtility.AddCursorRect(thumb, MouseCursor.Link);
            var tev = Event.current;
            if (tev.type == EventType.MouseDown && tev.button == 0 && thumb.Contains(tev.mousePosition))
            {
                fold.expanded = true;
                tev.Use();
                GUI.changed = true;
            }
            GUILayout.Label("▶", EditorStyles.miniLabel, GUILayout.Width(14f));
            if (GUILayout.Button(new GUIContent("⋯", "Static point, or animate over time"), EditorStyles.miniButton, GUILayout.Width(24f), GUILayout.Height(18f)))
                ShowMenu(x, y, opts, fold);
            GUILayout.EndHorizontal();
            return;
        }

        // Expanded: a side panel (label, mode-specific info, mode/collapse buttons) sits LEFT of the value
        // display, both sharing the plot's own height — one horizontal box instead of a tall vertical stack.
        bool isCurve = x.mode == ZUIValue.Mode.Curve;   // x/y modes are always kept identical by this control
        bool showText = fold.showValueTextOverride ?? opts.showValueText;
        bool showInputs = fold.showNumericInputsOverride ?? opts.showNumericInputs;

        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(SidePanelWidth), GUILayout.Height(opts.plotSize));
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        if (isCurve) DrawCurveSidePanel(x, y, opts);
        else DrawStaticUtilityPanel(x, y, opts);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("⋯", "Static point, or animate over time"), EditorStyles.miniButton, GUILayout.Height(18f)))
            ShowMenu(x, y, opts, fold);
        if (GUILayout.Button("▲", EditorStyles.miniButton, GUILayout.Width(22f), GUILayout.Height(18f)))
            fold.expanded = false;
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        // Static mode's optional numeric-input block: two roughly-square blocks (the inputs, the plot) that
        // read well stacked either horizontally (default — side by side) or vertically (code opt-in), per
        // ui-layout-rules.md's "an x/y pair is a 2D-control case" framing applied one level up — this is that
        // same idea for the SUPPORTING numeric readout, not the drag target itself.
        bool drawInputs = !isCurve && showInputs;
        if (drawInputs && opts.stackInputsVertically)
        {
            GUILayout.BeginVertical();
            DrawNumericSquare(x, y, opts);
            DrawStaticPlot(x, y, opts, showText);
            GUILayout.EndVertical();
        }
        else
        {
            if (drawInputs) DrawNumericSquare(x, y, opts);
            if (isCurve) DrawCurvePlot(x, y, opts);
            else DrawStaticPlot(x, y, opts, showText);
        }

        GUILayout.EndHorizontal();
    }

    // The numeric-input block: "label Y / input Y / label X / input X" top to bottom, as a compact fixed-width
    // column — naturally close to square, which is exactly why it stacks well beside the (also roughly square)
    // plot. Static mode only; Curve mode's per-point values are edited on the plot itself.
    static void DrawNumericSquare(ZUIValue x, ZUIValue y, Options opts)
    {
        // Content-height-only (no FlexibleSpace padding) so this block's height is unambiguous whether it's
        // sitting beside the plot in a Height-bounded row, or stacked above it in an unbounded column.
        GUILayout.BeginVertical(GUILayout.Width(NumericSquareWidth));
        GUILayout.Label("Y", EditorStyles.miniLabel);
        y.staticValue = EditorGUILayout.FloatField(y.staticValue, GUILayout.Width(NumericSquareWidth - 4f));
        GUILayout.Space(4f);
        GUILayout.Label("X", EditorStyles.miniLabel);
        x.staticValue = EditorGUILayout.FloatField(x.staticValue, GUILayout.Width(NumericSquareWidth - 4f));
        GUILayout.EndVertical();
    }

    // One-line collapsed preview: the same XY plot, shrunk down — a single dot for Static, a mini traced path
    // for Curve — so the field is still glanceable at a glance without eating vertical space to show it.
    static void DrawThumb(Rect r, ZUIValue x, ZUIValue y, Options opts)
    {
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));

        bool isCurve = x.mode == ZUIValue.Mode.Curve && x.points.Count > 0 && x.points.Count == y.points.Count;
        if (isCurve)
        {
            int n = x.points.Count;
            Vector2 prev = default;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = ToPlot(r, new Vector2(x.points[i].value, y.points[i].value), opts);
                if (i > 0) { Handles.color = LineColor; Handles.DrawLine(prev, p); }
                prev = p;
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 p = ToPlot(r, new Vector2(x.points[i].value, y.points[i].value), opts);
                Handles.color = PointColor;
                Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0f), Vector3.forward, 2f);
            }
        }
        else
        {
            Vector2 p = ToPlot(r, new Vector2(x.staticValue, y.staticValue), opts);
            Handles.color = PointColor;
            Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0f), Vector3.forward, 3f);
        }
    }

    // Just the Reset button now — the X/Y numeric fields moved to DrawNumericSquare, shown independently per
    // Options.showNumericInputs / the right-click override, rather than being permanently glued to this panel.
    static void DrawStaticUtilityPanel(ZUIValue x, ZUIValue y, Options opts)
    {
        if (GUILayout.Button("Reset"))
        {
            Vector2 d = opts.staticDefault ?? Vector2.zero;
            x.staticValue = d.x; y.staticValue = d.y;
            GUI.changed = true;
        }
    }

    static void DrawStaticPlot(ZUIValue x, ZUIValue y, Options opts, bool showValueText)
    {
        Rect plot = GUILayoutUtility.GetRect(opts.plotSize, opts.plotSize, GUILayout.Width(opts.plotSize), GUILayout.Height(opts.plotSize));
        DrawPlotFrame(plot, opts);

        Vector2 screenPt = ToPlot(plot, new Vector2(x.staticValue, y.staticValue), opts);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (HandleDrag(id, plot, ref screenPt, opts))
        {
            Vector2 val = FromPlot(plot, screenPt, opts);
            x.staticValue = val.x; y.staticValue = val.y;
            GUI.changed = true;
        }
        DrawDot(screenPt, PointColor, 5f);

        if (showValueText && Event.current.type == EventType.Repaint)
            GUI.Label(new Rect(screenPt.x + 7f, screenPt.y - 7f, 90f, 14f),
                $"({x.staticValue:0.##}, {y.staticValue:0.##})", EditorStyles.miniLabel);
    }

    static void DrawCurveSidePanel(ZUIValue x, ZUIValue y, Options opts)
    {
        GUILayout.Label($"{x.points.Count} point(s)", EditorStyles.miniBoldLabel);
        GUILayout.Label("Click empty space to add, drag to move, right-click to remove (min 2).",
            EditorStyles.wordWrappedMiniLabel, GUILayout.Width(SidePanelWidth - 4f));
        if (GUILayout.Button("Reset"))
        {
            Vector2 d = opts.staticDefault ?? Vector2.zero;
            x.points.Clear(); y.points.Clear();
            x.points.Add(new ZUIEnvelopePoint(0f, d.x));
            y.points.Add(new ZUIEnvelopePoint(0f, d.y));
            x.points.Add(new ZUIEnvelopePoint(1f, d.x));
            y.points.Add(new ZUIEnvelopePoint(1f, d.y));
            GUI.changed = true;
        }
    }

    static void DrawCurvePlot(ZUIValue x, ZUIValue y, Options opts)
    {
        EnsurePairedCurveDefaults(x, y, opts);
        Rect plot = GUILayoutUtility.GetRect(opts.plotSize, opts.plotSize, GUILayout.Width(opts.plotSize), GUILayout.Height(opts.plotSize));
        DrawPlotFrame(plot, opts);

        int n = x.points.Count;
        var screenPts = new Vector2[n];
        for (int i = 0; i < n; i++) screenPts[i] = ToPlot(plot, new Vector2(x.points[i].value, y.points[i].value), opts);

        if (Event.current.type == EventType.Repaint)
        {
            Handles.color = LineColor;
            for (int i = 0; i < n - 1; i++) Handles.DrawLine(screenPts[i], screenPts[i + 1]);
        }

        int removeIdx = -1;
        bool anyHot = false;
        for (int i = 0; i < n; i++)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Vector2 p = screenPts[i];
            if (HandleDrag(id, plot, ref p, opts))
            {
                Vector2 val = FromPlot(plot, p, opts);
                x.points[i].value = val.x; y.points[i].value = val.y;
                GUI.changed = true;
            }
            if (GUIUtility.hotControl == id) anyHot = true;

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 1 && n > 2 &&
                new Rect(p.x - HitRadius, p.y - HitRadius, HitRadius * 2f, HitRadius * 2f).Contains(e.mousePosition))
            {
                removeIdx = i;
                e.Use();
            }

            DrawDot(p, PointColor, 5f);
            if (Event.current.type == EventType.Repaint)
                GUI.Label(new Rect(p.x + 6f, p.y - 15f, 22f, 16f), (i + 1).ToString(), EditorStyles.miniLabel);
        }

        if (removeIdx >= 0)
        {
            x.points.RemoveAt(removeIdx);
            y.points.RemoveAt(removeIdx);
            RenormalizeTimes(x, y);
            GUI.changed = true;
        }
        else
        {
            // Click empty plot space (not on an existing point, and not mid-drag) to append a new point there.
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !anyHot && plot.Contains(e.mousePosition))
            {
                bool onExisting = false;
                foreach (var p in screenPts)
                    if (new Rect(p.x - HitRadius, p.y - HitRadius, HitRadius * 2f, HitRadius * 2f).Contains(e.mousePosition)) { onExisting = true; break; }
                if (!onExisting)
                {
                    Vector2 val = FromPlot(plot, e.mousePosition, opts);
                    x.points.Add(new ZUIEnvelopePoint(0f, val.x));
                    y.points.Add(new ZUIEnvelopePoint(0f, val.y));
                    RenormalizeTimes(x, y);
                    GUI.changed = true;
                    e.Use();
                }
            }
        }
    }

    // Seed both ZUIValues with a matching 2-point path the first time Curve mode is entered (or if they've
    // somehow gone out of sync), and always keep each axis's own value-range pinned to this control's Options.
    static void EnsurePairedCurveDefaults(ZUIValue x, ZUIValue y, Options opts)
    {
        x.yMin = opts.xMin; x.yMax = opts.xMax;
        y.yMin = opts.yMin; y.yMax = opts.yMax;
        if (x.points.Count == 0 || x.points.Count != y.points.Count)
        {
            float sx = x.staticValue, sy = y.staticValue;
            x.points.Clear(); y.points.Clear();
            x.points.Add(new ZUIEnvelopePoint(0f, sx));
            y.points.Add(new ZUIEnvelopePoint(0f, sy));
            x.points.Add(new ZUIEnvelopePoint(1f, sx));
            y.points.Add(new ZUIEnvelopePoint(1f, sy));
        }
    }

    // Point order IS time order — appending always adds to the end, so re-deriving `time` from index keeps every
    // point evenly spaced across the overall lifetime with no separate per-point timing to author.
    static void RenormalizeTimes(ZUIValue x, ZUIValue y)
    {
        int n = x.points.Count;
        for (int i = 0; i < n; i++)
        {
            float tm = n > 1 ? i / (float)(n - 1) : 0f;
            x.points[i].time = tm;
            y.points[i].time = tm;
        }
    }

    static void ShowMenu(ZUIValue x, ZUIValue y, Options opts, FoldState fold)
    {
        var menu = new GenericMenu();
        bool isCurve = x.mode == ZUIValue.Mode.Curve;
        menu.AddItem(new GUIContent("Static (one point)"), !isCurve, () => { x.mode = ZUIValue.Mode.Static; y.mode = ZUIValue.Mode.Static; });
        menu.AddItem(new GUIContent("Animate over time (a path)"), isCurve, () =>
        {
            x.mode = ZUIValue.Mode.Curve; y.mode = ZUIValue.Mode.Curve;
            EnsurePairedCurveDefaults(x, y, opts);
        });

        // Per-control override of the code-set defaults (Options.showValueText/showNumericInputs) — sticks for
        // the rest of the session once touched (FoldState, same lifetime as the expand/collapse state above).
        menu.AddSeparator("");
        bool showText = fold.showValueTextOverride ?? opts.showValueText;
        bool showInputs = fold.showNumericInputsOverride ?? opts.showNumericInputs;
        menu.AddItem(new GUIContent("Show value as text"), showText, () => fold.showValueTextOverride = !showText);
        menu.AddItem(new GUIContent("Show value as numeric inputs"), showInputs, () => fold.showNumericInputsOverride = !showInputs);

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Copy value"), false, () => EditorGUIUtility.systemCopyBuffer = PairToClipboardString(x, y));
        if (TryPairFromClipboardString(EditorGUIUtility.systemCopyBuffer, out _))
            menu.AddItem(new GUIContent("Paste value"), false, () =>
            {
                if (TryPairFromClipboardString(EditorGUIUtility.systemCopyBuffer, out var pair))
                { x.CopyFrom(pair.x); y.CopyFrom(pair.y); GUI.changed = true; }
            });
        else
            menu.AddDisabledItem(new GUIContent("Paste value"));

        menu.ShowAsContext();
    }

    // Clipboard payload for the (x, y) PAIR — distinct prefix from ZUIValue.ToClipboardString's single-value
    // one, so a single multicont's copied value can never be silently paste-accepted here (or vice versa).
    [System.Serializable]
    class ClipboardPair { public ZUIValue x = new ZUIValue(); public ZUIValue y = new ZUIValue(); }
    const string PairClipboardPrefix = "ZUIVALUE2:";

    static string PairToClipboardString(ZUIValue x, ZUIValue y)
        => PairClipboardPrefix + JsonUtility.ToJson(new ClipboardPair { x = x, y = y });

    static bool TryPairFromClipboardString(string s, out ClipboardPair pair)
    {
        if (string.IsNullOrEmpty(s) || !s.StartsWith(PairClipboardPrefix)) { pair = null; return false; }
        try { pair = JsonUtility.FromJson<ClipboardPair>(s.Substring(PairClipboardPrefix.Length)); return pair != null; }
        catch { pair = null; return false; }
    }

    // ── plot geometry ──────────────────────────────────────────────────────
    static void DrawPlotFrame(Rect r, Options opts)
    {
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
        Handles.color = new Color(1f, 1f, 1f, 0.15f);
        Handles.DrawLine(new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin));
        Handles.DrawLine(new Vector3(r.xMax, r.yMin), new Vector3(r.xMax, r.yMax));
        Handles.DrawLine(new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax));
        Handles.DrawLine(new Vector3(r.xMin, r.yMax), new Vector3(r.xMin, r.yMin));

        Handles.color = new Color(1f, 1f, 1f, 0.3f);
        if (opts.xMin < 0f && opts.xMax > 0f)
        {
            float cx = ToPlot(r, new Vector2(0f, opts.yMin), opts).x;
            Handles.DrawLine(new Vector3(cx, r.yMin), new Vector3(cx, r.yMax));
        }
        if (opts.yMin < 0f && opts.yMax > 0f)
        {
            float cy = ToPlot(r, new Vector2(opts.xMin, 0f), opts).y;
            Handles.DrawLine(new Vector3(r.xMin, cy), new Vector3(r.xMax, cy));
        }
    }

    static void DrawDot(Vector2 p, Color c, float radius)
    {
        if (Event.current.type != EventType.Repaint) return;
        Handles.color = c;
        Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0f), Vector3.forward, radius);
        Handles.color = Color.black;
        Handles.DrawWireDisc(new Vector3(p.x, p.y, 0f), Vector3.forward, radius);
    }

    static Vector2 ToPlot(Rect r, Vector2 val, Options opts)
    {
        float tx = Mathf.InverseLerp(opts.xMin, opts.xMax, val.x);
        float ty = Mathf.InverseLerp(opts.yMin, opts.yMax, val.y);
        return new Vector2(r.xMin + tx * r.width, r.yMax - ty * r.height);   // Y-up, like the Pyre canvas itself
    }

    static Vector2 FromPlot(Rect r, Vector2 screen, Options opts)
    {
        float tx = Mathf.InverseLerp(r.xMin, r.xMax, screen.x);
        float ty = Mathf.InverseLerp(r.yMax, r.yMin, screen.y);
        return new Vector2(Mathf.Lerp(opts.xMin, opts.xMax, tx), Mathf.Lerp(opts.yMin, opts.yMax, ty));
    }

    // Standard IMGUI hot-control drag: mouse-down within HitRadius of the point claims it, drag moves it
    // (clamped to the plot rect), mouse-up releases. Returns true on any frame the point's screen position moved.
    static bool HandleDrag(int id, Rect plot, ref Vector2 screenPt, Options opts)
    {
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && new Rect(screenPt.x - HitRadius, screenPt.y - HitRadius, HitRadius * 2f, HitRadius * 2f).Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    e.Use();
                }
                return false;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    screenPt = new Vector2(Mathf.Clamp(e.mousePosition.x, plot.xMin, plot.xMax), Mathf.Clamp(e.mousePosition.y, plot.yMin, plot.yMax));
                    e.Use();
                    return true;
                }
                return false;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                return false;
            default:
                return false;
        }
    }
}

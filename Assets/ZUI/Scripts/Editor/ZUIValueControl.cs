// ZUIValueControl.cs
// Editor drawer for a ZUIValue. One labelled row whose body switches between a
// static Slider, a min↔max range, or the full Envelope curve editor (with
// Duration / Warmup / Initial / Cooldown fields). A "⋯" button opens a context
// menu to change mode and pick an external multiplier. When the value is dynamic
// (curve, range, or multiplied) a read-only live-computed readout is shown.
//
// Mirrors ZTracker's "static value OR curve, toggled" pattern, generalized to
// three modes + a host-supplied multiplier.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ZUIValueControl
{
    /// <summary>Per-field gating + range, passed in by the caller (later sourced from attributes).</summary>
    public struct Options
    {
        public bool allowStatic;
        public bool allowMinMax;
        public bool allowCurve;
        public float absMin;
        public float absMax;
        public string sliderStyle;
        public string[] multiplierIds; // selectable globals for the multiplier menu (null = none)

        public static Options Default => new Options
        {
            allowStatic = true,
            allowMinMax = true,
            allowCurve  = true,
            absMin = 0f,
            absMax = 10f,
            sliderStyle = ZUI.SliderStyle.Default,
            multiplierIds = null,
        };

        public Options WithRange(float lo, float hi) { absMin = lo; absMax = hi; return this; }
        public Options WithMultipliers(params string[] ids) { multiplierIds = ids; return this; }
    }

    // ── Per-value editor scaffolding (curve editor needs a persistent Def/Runtime/stateKey) ──
    class CurveState
    {
        public ZUIEnvelopeDef def;
        public ZUIEnvelopeRuntime rt;
        public ZUIColorRef color;
        public int key;
    }

    static readonly Dictionary<ZUIValue, CurveState> s_curve = new Dictionary<ZUIValue, CurveState>();
    static int s_nextKey = 7100;

    static CurveState GetCurveState(ZUIValue v)
    {
        if (!s_curve.TryGetValue(v, out var cs))
        {
            cs = new CurveState
            {
                def = new ZUIEnvelopeDef { name = "ZUIValue" },
                rt = new ZUIEnvelopeRuntime { showGrid = true, showValueLabels = true },
                color = new ZUIColorRef(new Color(0.4f, 0.85f, 1f)),
                key = s_nextKey++,
            };
            s_curve[v] = cs;
        }
        return cs;
    }

    public static void Draw(string label, ZUIValue v) => Draw(label, v, Options.Default);

    public static void Draw(string label, ZUIValue v, Options opts)
    {
        if (v == null) return;
        if (opts.sliderStyle == null) opts.sliderStyle = ZUI.SliderStyle.Default;

        GUILayout.BeginVertical();

        // ── Header row: label · body · ⋯ ─────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(label))
        {
            // Use the caller's shared label-column width (EditorGUIUtility.labelWidth) so labels align with
            // the other fields and aren't clipped; fall back to fitting the content if none was set.
            float lw = EditorGUIUtility.labelWidth > 1f
                ? EditorGUIUtility.labelWidth
                : Mathf.Max(ZUI.LabelWidthWide, ZUI.RowLabelStyle.CalcSize(new GUIContent(label)).x + 6f);
            EditorGUILayout.LabelField(label, ZUI.RowLabelStyle, GUILayout.Width(lw));
        }

        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        switch (v.mode)
        {
            case ZUIValue.Mode.Static:
                v.staticValue = ZUI.Slider(v.staticValue, opts.absMin, opts.absMax, "", opts.sliderStyle);
                break;
            case ZUIValue.Mode.MinMax:
            {
                float lo = v.min, hi = v.max;
                ZUI.MicroMinMax(ref lo, ref hi, opts.absMin, opts.absMax, "", opts.sliderStyle,
                                showInputFields: false,
                                labelMode: ZUI.MicroMinMaxLabelMode.LabelAndValues);
                v.min = lo; v.max = hi;
                break;
            }
            case ZUIValue.Mode.Curve:
                DrawCurveBody(v, opts);
                break;
        }
        GUILayout.EndVertical();

        if (GUILayout.Button(new GUIContent("⋯", "Configure value"), EditorStyles.miniButton, GUILayout.Width(24f), GUILayout.Height(18f)))
            ShowMenu(v, opts);
        GUILayout.EndHorizontal();

        // ── Live computed readout (only when the value actually varies) ───────
        if (v.IsDynamic)
        {
            float now = Application.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup;
            string readout;
            if (v.mode == ZUIValue.Mode.MinMax)
            {
                float m = v.Multiplier();
                readout = v.HasMultiplier
                    ? $"live: {v.min * m:0.###} – {v.max * m:0.###}   (range × {v.multiplierId} {m:0.##})"
                    : $"live: {v.min:0.###} – {v.max:0.###}  (random)";
            }
            else
            {
                float raw = v.EvaluateRaw(now);
                float val = raw * v.Multiplier();
                readout = v.HasMultiplier
                    ? $"live: {val:0.###}   ({raw:0.###} × {v.multiplierId} {v.Multiplier():0.##})"
                    : $"live: {val:0.###}";
            }
            EditorGUILayout.LabelField(readout, EditorStyles.miniLabel);
        }

        GUILayout.EndVertical();
    }

    static void DrawCurveBody(ZUIValue v, Options opts)
    {
        v.EnsureCurveDefaults();
        var cs = GetCurveState(v);
        cs.rt.yMin = v.yMin;
        cs.rt.yMax = v.yMax;

        ZUI.Envelope(v.points, cs.color, cs.def, cs.rt, 200f, 90f, cs.key);

        // Timing — free number fields (no artificial cap; a curve can run for any duration / warmup).
        GUILayout.BeginHorizontal();
        v.duration = NumField("Dur s", v.duration);
        v.warmup   = NumField("Warm s", v.warmup);
        bool loop = v.cooldown >= 0f;
        bool newLoop = GUILayout.Toggle(loop, "Loop", EditorStyles.miniButton, GUILayout.Width(44f));
        if (newLoop != loop) v.cooldown = newLoop ? 0f : -1f;
        if (newLoop) v.cooldown = Mathf.Max(0f, NumField("Cool s", v.cooldown));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Value range — the curve's min/max output, per controller (e.g. 0..600 for a long spawn interval).
        GUILayout.BeginHorizontal();
        GUILayout.Label("Value range", EditorStyles.miniLabel, GUILayout.Width(72f));
        v.yMin = NumField("min", v.yMin);
        v.yMax = NumField("max", v.yMax);
        if (v.yMax < v.yMin) v.yMax = v.yMin;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Keep all point values (and the warmup hold) inside the live [yMin..yMax]
        // domain. Shrinking the range under existing points would otherwise leave
        // them mapped outside the plot rect, and the envelope's line drawing isn't
        // clipped — so the curve spills across the window.
        ClampToRange(v);
    }

    static void ClampToRange(ZUIValue v)
    {
        for (int i = 0; i < v.points.Count; i++)
            v.points[i].value = Mathf.Clamp(v.points[i].value, v.yMin, v.yMax);
    }

    // Compact "label + free number field" — no min/max cap, so curve bounds are whatever the user types.
    // Uses a native FloatField so its prefix label is drag-scrubbable like any Unity numeric control.
    static float NumField(string label, float value, float labelW = 44f, float fieldW = 52f)
    {
        float prev = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = labelW;
        value = EditorGUILayout.FloatField(label, value, GUILayout.Width(labelW + fieldW));
        EditorGUIUtility.labelWidth = prev;
        return value;
    }

    static void ShowMenu(ZUIValue v, Options opts)
    {
        var menu = new GenericMenu();

        if (opts.allowStatic)
            menu.AddItem(new GUIContent("Mode/Static"), v.mode == ZUIValue.Mode.Static, () => v.mode = ZUIValue.Mode.Static);
        if (opts.allowMinMax)
            menu.AddItem(new GUIContent("Mode/Min-Max range"), v.mode == ZUIValue.Mode.MinMax, () => v.mode = ZUIValue.Mode.MinMax);
        if (opts.allowCurve)
            menu.AddItem(new GUIContent("Mode/Curve over time"), v.mode == ZUIValue.Mode.Curve, () =>
            {
                bool wasCurve = v.mode == ZUIValue.Mode.Curve;
                v.mode = ZUIValue.Mode.Curve;
                if (!wasCurve)
                {
                    // Seed the curve from the field's range + current value, so the points sit in a useful
                    // value range and the first point starts at the meaningful current value (not pinned to 0).
                    v.yMin = Mathf.Min(opts.absMin, opts.absMax);
                    v.yMax = Mathf.Max(opts.absMin, opts.absMax);
                    float start = Mathf.Clamp(v.staticValue, v.yMin, v.yMax);
                    v.points.Clear();
                    v.points.Add(new ZUIEnvelopePoint(0f, start));
                    v.points.Add(new ZUIEnvelopePoint(1f, start));
                }
                else v.EnsureCurveDefaults();
            });

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Multiplier/(none)"), !v.HasMultiplier, () => v.multiplierId = "");
        if (opts.multiplierIds != null)
            foreach (var id in opts.multiplierIds)
            {
                string captured = id;
                menu.AddItem(new GUIContent("Multiplier/" + id), v.multiplierId == id, () => v.multiplierId = captured);
            }

        menu.ShowAsContext();
    }
}

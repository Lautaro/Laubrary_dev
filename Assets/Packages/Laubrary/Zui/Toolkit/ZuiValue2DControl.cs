// ZuiValue2DControl — UI Toolkit counterpart of the IMGUI ZUIValue2DControl: a PAIR of ZUIValues
// (x, y) presented as one synchronized 2D-position control. Static mode: one draggable dot in an
// XY plot (+ optional "(x, y)" text and/or a compact numeric X/Y input block). Curve mode:
// numbered points tracing a PATH through XY space — both axes spatial, time implicit in point
// ORDER (evenly re-normalized across the lifetime on add/remove). Pure editor-side UX layer over
// the unchanged ZUIValue/ZUIEnvelopePoint data model, exactly like the IMGUI original.
//
// Clipboard payloads use the SAME "ZUIVALUE2:" prefix and JSON shape as the IMGUI control, so
// copy/paste interoperates across the two halves during the migration.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiValue2DControl : VisualElement
    {
        public class Options
        {
            public float xMin = -1f, xMax = 1f, yMin = -1f, yMax = 1f;
            public Vector2? staticDefault = null;   // Reset-to target; null = (0,0)
            public float plotSize = 140f;
            public bool showValueText = false;       // small "(x, y)" beside the dot (Static mode)
            public bool showNumericInputs = true;    // compact numeric X/Y block (Static mode)
            public bool stackInputsVertically = false;   // code-only layout call, not a menu item

            public Options WithRange(float xLo, float xHi, float yLo, float yHi)
            { xMin = xLo; xMax = xHi; yMin = yLo; yMax = yHi; return this; }
            public Options WithDefault(Vector2 v) { staticDefault = v; return this; }
            public Options WithPlotSize(float s) { plotSize = s; return this; }
            public Options WithValueDisplay(bool showText, bool showInputs)
            { showValueText = showText; showNumericInputs = showInputs; return this; }
            public Options WithVerticalStack(bool vertical = true) { stackInputsVertically = vertical; return this; }
        }

        static readonly Color PointColor = new Color(0.4f, 0.85f, 1f);
        static readonly Color LineColor = new Color(0.4f, 0.85f, 1f, 0.7f);
        const float HitRadius = 8f;
        const float SidePanelWidth = 108f;
        const float NumericFieldWidth = 78f;

        // Fold + per-control display overrides, keyed by the X value so they survive window rebuilds
        // (undo/redo) — the same session-state trick as the IMGUI control and ZuiValueControl.
        class FoldState { public bool expanded; public bool? showValueTextOverride; public bool? showNumericInputsOverride; }
        static readonly Dictionary<ZUIValue, FoldState> s_fold = new();
        static FoldState GetFold(ZUIValue x)
        {
            if (!s_fold.TryGetValue(x, out var st)) { st = new FoldState(); s_fold[x] = st; }
            return st;
        }

        readonly ZUIValue _x, _y;
        readonly Options _opt;
        readonly string _label;
        readonly string _tooltip;

        public Action OnBeforeMutate;
        public Action OnChanged;

        public ZuiValue2DControl(string label, ZUIValue x, ZUIValue y, Options options, string tooltip)
        {
            _x = x ?? throw new ArgumentNullException(nameof(x));
            _y = y ?? throw new ArgumentNullException(nameof(y));
            _opt = options ?? new Options();
            _label = label;
            _tooltip = tooltip;
            this.tooltip = tooltip;
            Build();
        }

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        void Build()
        {
            Clear();
            var fold = GetFold(_x);
            if (fold.expanded) BuildExpanded(fold); else BuildCollapsed(fold);
        }

        // ── collapsed: label · mini thumbnail · caret · ⋯ ───────────────────────────
        void BuildCollapsed(FoldState fold)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (!string.IsNullOrEmpty(_label))
            {
                var l = new Label(_label) { tooltip = _tooltip };
                l.AddToClassList("zui-field__label");
                row.Add(l);
            }
            var thumb = new Plot2D(this, thumbnail: true)
            { tooltip = "Click to expand the 2D editor." };
            thumb.style.width = 120f;
            thumb.style.height = 18f;
            thumb.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                fold.expanded = true;
                Build();
                e.StopPropagation();
            });
            row.Add(thumb);
            row.Add(Z.Text("▶", ZuiText.Small, "2D editor fold state."));
            row.Add(Z.Button("⋯", "Static point, or animate over time; value display; copy/paste.", ShowMenu).W(24f));
            Add(row);
        }

        // ── expanded: side panel · (numeric block) · plot ───────────────────────────
        void BuildExpanded(FoldState fold)
        {
            bool isCurve = _x.mode == ZUIValue.Mode.Curve;
            bool showInputs = !isCurve && (fold.showNumericInputsOverride ?? _opt.showNumericInputs);
            if (isCurve) EnsurePairedCurveDefaults();

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.alignItems = Align.FlexStart;

            // side panel
            var side = new VisualElement();
            side.style.width = SidePanelWidth;
            side.style.flexShrink = 0f;
            side.style.height = _opt.plotSize;
            var title = new Label(_label) { tooltip = _tooltip };
            title.AddToClassList("zui-box__title");
            side.Add(title);
            if (isCurve)
            {
                side.Add(Z.Text($"{_x.points.Count} point(s)", ZuiText.Small, "How many path points the animation has."));
                var hint = Z.Text("Click empty space to add, drag to move, right-click to remove (min 2).",
                    ZuiText.Subtle, "Path editing hints.");
                hint.style.whiteSpace = WhiteSpace.Normal;
                side.Add(hint);
            }
            side.Add(Z.Button("Reset", "Reset the value to this field's default.", () =>
            {
                Vector2 d = _opt.staticDefault ?? Vector2.zero;
                Mutate(() =>
                {
                    if (_x.mode == ZUIValue.Mode.Curve)
                    {
                        _x.points.Clear(); _y.points.Clear();
                        _x.points.Add(new ZUIEnvelopePoint(0f, d.x));
                        _y.points.Add(new ZUIEnvelopePoint(0f, d.y));
                        _x.points.Add(new ZUIEnvelopePoint(1f, d.x));
                        _y.points.Add(new ZUIEnvelopePoint(1f, d.y));
                    }
                    else { _x.staticValue = d.x; _y.staticValue = d.y; }
                });
                Build();
            }));
            side.Add(Z.Flexible());
            side.Add(Z.Row(
                Z.Button("⋯", "Static point, or animate over time; value display; copy/paste.", ShowMenu).W(24f),
                Z.Button("▲", "Collapse the 2D editor back to its one-line thumbnail.", () =>
                {
                    GetFold(_x).expanded = false;
                    Build();
                }).W(22f)));
            row.Add(side);

            var plot = new Plot2D(this, thumbnail: false) { tooltip = _tooltip };
            plot.style.width = _opt.plotSize;
            plot.style.height = _opt.plotSize;

            if (showInputs)
            {
                var numeric = BuildNumericBlock(plot);
                if (_opt.stackInputsVertically)
                {
                    var col = new VisualElement();
                    col.Add(numeric);
                    col.Add(plot);
                    row.Add(col);
                }
                else { row.Add(numeric); row.Add(plot); }
            }
            else row.Add(plot);

            Add(row);
        }

        VisualElement BuildNumericBlock(Plot2D plot)
        {
            var col = new VisualElement();
            col.style.width = NumericFieldWidth + 4f;
            col.style.flexShrink = 0f;
            col.Add(Z.Text("Y", ZuiText.Small, "The Y component."));
            var yField = Z.Float(_y.staticValue, "The Y component.", v =>
            {
                Mutate(() => _y.staticValue = v);
                plot.MarkDirtyRepaint();
            }, NumericFieldWidth);
            col.Add(yField);
            col.Add(Z.VSpace(4f));
            col.Add(Z.Text("X", ZuiText.Small, "The X component."));
            var xField = Z.Float(_x.staticValue, "The X component.", v =>
            {
                Mutate(() => _x.staticValue = v);
                plot.MarkDirtyRepaint();
            }, NumericFieldWidth);
            col.Add(xField);
            plot.OnPlotEdited += () =>
            {
                xField.SetValueWithoutNotify(_x.staticValue);
                yField.SetValueWithoutNotify(_y.staticValue);
            };
            return col;
        }

        // ── data helpers (identical semantics to the IMGUI control) ─────────────────
        void EnsurePairedCurveDefaults()
        {
            _x.yMin = _opt.xMin; _x.yMax = _opt.xMax;
            _y.yMin = _opt.yMin; _y.yMax = _opt.yMax;
            if (_x.points.Count == 0 || _x.points.Count != _y.points.Count)
            {
                float sx = _x.staticValue, sy = _y.staticValue;
                _x.points.Clear(); _y.points.Clear();
                _x.points.Add(new ZUIEnvelopePoint(0f, sx));
                _y.points.Add(new ZUIEnvelopePoint(0f, sy));
                _x.points.Add(new ZUIEnvelopePoint(1f, sx));
                _y.points.Add(new ZUIEnvelopePoint(1f, sy));
            }
        }

        void RenormalizeTimes()
        {
            int n = _x.points.Count;
            for (int i = 0; i < n; i++)
            {
                float tm = n > 1 ? i / (float)(n - 1) : 0f;
                _x.points[i].time = tm;
                _y.points[i].time = tm;
            }
        }

        // ── the ⋯ menu ──────────────────────────────────────────────────────────────
        void ShowMenu()
        {
            var fold = GetFold(_x);
            var menu = new GenericMenu();
            bool isCurve = _x.mode == ZUIValue.Mode.Curve;
            menu.AddItem(new GUIContent("Static (one point)"), !isCurve, () =>
            {
                Mutate(() => { _x.mode = ZUIValue.Mode.Static; _y.mode = ZUIValue.Mode.Static; });
                Build();
            });
            menu.AddItem(new GUIContent("Animate over time (a path)"), isCurve, () =>
            {
                Mutate(() =>
                {
                    _x.mode = ZUIValue.Mode.Curve; _y.mode = ZUIValue.Mode.Curve;
                    EnsurePairedCurveDefaults();
                });
                Build();
            });

            menu.AddSeparator("");
            bool showText = fold.showValueTextOverride ?? _opt.showValueText;
            bool showInputs = fold.showNumericInputsOverride ?? _opt.showNumericInputs;
            menu.AddItem(new GUIContent("Show value as text"), showText,
                () => { fold.showValueTextOverride = !showText; Build(); });
            menu.AddItem(new GUIContent("Show value as numeric inputs"), showInputs,
                () => { fold.showNumericInputsOverride = !showInputs; Build(); });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy value"), false,
                () => EditorGUIUtility.systemCopyBuffer = PairToClipboardString(_x, _y));
            if (TryPairFromClipboardString(EditorGUIUtility.systemCopyBuffer, out _))
                menu.AddItem(new GUIContent("Paste value"), false, () =>
                {
                    if (TryPairFromClipboardString(EditorGUIUtility.systemCopyBuffer, out var pair))
                        Mutate(() => { _x.CopyFrom(pair.x); _y.CopyFrom(pair.y); });
                    Build();
                });
            else
                menu.AddDisabledItem(new GUIContent("Paste value"));

            menu.ShowAsContext();
        }

        // Same prefix + JSON shape as the IMGUI ZUIValue2DControl — the two halves' clipboards interoperate.
        [Serializable]
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

        // ── the plot element (shared by thumbnail + full plot) ──────────────────────
        class Plot2D : VisualElement
        {
            readonly ZuiValue2DControl c;
            readonly bool thumb;
            int _dragIndex = -1;      // curve mode: point index; static mode: 0 while dragging the dot

            /// Raised after any plot-driven edit so sibling numeric fields can refresh.
            public event Action OnPlotEdited;

            public Plot2D(ZuiValue2DControl control, bool thumbnail)
            {
                c = control;
                thumb = thumbnail;
                AddToClassList("zui-envelope");
                generateVisualContent += Paint;
                if (!thumb)
                {
                    RegisterCallback<PointerDownEvent>(OnPointerDown);
                    RegisterCallback<PointerMoveEvent>(OnPointerMove);
                    RegisterCallback<PointerUpEvent>(OnPointerUp);
                }
            }

            bool IsCurve => c._x.mode == ZUIValue.Mode.Curve
                && c._x.points.Count > 0 && c._x.points.Count == c._y.points.Count;

            Vector2 ToPlot(Vector2 val)
            {
                var r = contentRect;
                float tx = Mathf.InverseLerp(c._opt.xMin, c._opt.xMax, val.x);
                float ty = Mathf.InverseLerp(c._opt.yMin, c._opt.yMax, val.y);
                return new Vector2(r.xMin + tx * r.width, r.yMax - ty * r.height);   // Y-up, like the Pyre canvas
            }

            Vector2 FromPlot(Vector2 local)
            {
                var r = contentRect;
                float tx = Mathf.InverseLerp(r.xMin, r.xMax, Mathf.Clamp(local.x, r.xMin, r.xMax));
                float ty = Mathf.InverseLerp(r.yMax, r.yMin, Mathf.Clamp(local.y, r.yMin, r.yMax));
                return new Vector2(
                    (float)Math.Round(Mathf.Lerp(c._opt.xMin, c._opt.xMax, tx), 5),
                    (float)Math.Round(Mathf.Lerp(c._opt.yMin, c._opt.yMax, ty), 5));
            }

            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f) || !(r.height > 4f)) return;
                var p = mgc.painter2D;

                // zero-axis crosshairs
                p.strokeColor = new Color(1f, 1f, 1f, 0.25f);
                p.lineWidth = 1f;
                if (c._opt.xMin < 0f && c._opt.xMax > 0f)
                {
                    float cx = ToPlot(new Vector2(0f, c._opt.yMin)).x;
                    p.BeginPath(); p.MoveTo(new Vector2(cx, r.yMin)); p.LineTo(new Vector2(cx, r.yMax)); p.Stroke();
                }
                if (c._opt.yMin < 0f && c._opt.yMax > 0f)
                {
                    float cy = ToPlot(new Vector2(c._opt.xMin, 0f)).y;
                    p.BeginPath(); p.MoveTo(new Vector2(r.xMin, cy)); p.LineTo(new Vector2(r.xMax, cy)); p.Stroke();
                }

                float dotR = thumb ? 2.5f : 5f;
                if (IsCurve)
                {
                    int n = c._x.points.Count;
                    if (n >= 2)
                    {
                        p.strokeColor = LineColor;
                        p.lineWidth = thumb ? 1f : 1.5f;
                        p.BeginPath();
                        p.MoveTo(ToPlot(new Vector2(c._x.points[0].value, c._y.points[0].value)));
                        for (int i = 1; i < n; i++)
                            p.LineTo(ToPlot(new Vector2(c._x.points[i].value, c._y.points[i].value)));
                        p.Stroke();
                    }
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 pt = ToPlot(new Vector2(c._x.points[i].value, c._y.points[i].value));
                        p.fillColor = PointColor;
                        p.BeginPath(); p.Arc(pt, dotR, 0f, 360f); p.Fill();
                        if (!thumb)
                            mgc.DrawText((i + 1).ToString(), new Vector2(pt.x + 6f, pt.y - 15f), 10f, Color.white);
                    }
                }
                else
                {
                    Vector2 pt = ToPlot(new Vector2(c._x.staticValue, c._y.staticValue));
                    p.fillColor = PointColor;
                    p.BeginPath(); p.Arc(pt, dotR + (thumb ? 0.5f : 0f), 0f, 360f); p.Fill();

                    var fold = GetFold(c._x);
                    bool showText = fold.showValueTextOverride ?? c._opt.showValueText;
                    if (!thumb && showText)
                        mgc.DrawText($"({c._x.staticValue:0.##}, {c._y.staticValue:0.##})",
                            new Vector2(pt.x + 7f, pt.y - 14f), 10f, new Color(1f, 1f, 1f, 0.85f));
                }
            }

            int FindPointNear(Vector2 local)
            {
                if (!IsCurve)
                {
                    Vector2 pt = ToPlot(new Vector2(c._x.staticValue, c._y.staticValue));
                    return Vector2.Distance(pt, local) <= HitRadius ? 0 : -1;
                }
                for (int i = 0; i < c._x.points.Count; i++)
                {
                    Vector2 pt = ToPlot(new Vector2(c._x.points[i].value, c._y.points[i].value));
                    if (Vector2.Distance(pt, local) <= HitRadius) return i;
                }
                return -1;
            }

            void OnPointerDown(PointerDownEvent e)
            {
                Vector2 local = e.localPosition;

                if (e.button == 1)
                {
                    if (!IsCurve || c._x.points.Count <= 2) return;
                    int hit = FindPointNear(local);
                    if (hit < 0) return;
                    c.Mutate(() =>
                    {
                        c._x.points.RemoveAt(hit);
                        c._y.points.RemoveAt(hit);
                        c.RenormalizeTimes();
                    });
                    MarkDirtyRepaint();
                    OnPlotEdited?.Invoke();
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;

                int point = FindPointNear(local);
                if (point >= 0)
                {
                    c.OnBeforeMutate?.Invoke();
                    _dragIndex = point;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                if (IsCurve)   // click empty plot space appends a new point there (curve mode only)
                {
                    Vector2 val = FromPlot(local);
                    c.Mutate(() =>
                    {
                        c._x.points.Add(new ZUIEnvelopePoint(0f, val.x));
                        c._y.points.Add(new ZUIEnvelopePoint(0f, val.y));
                        c.RenormalizeTimes();
                    });
                    _dragIndex = c._x.points.Count - 1;
                    this.CapturePointer(e.pointerId);
                    MarkDirtyRepaint();
                    OnPlotEdited?.Invoke();
                    e.StopPropagation();
                }
            }

            void OnPointerMove(PointerMoveEvent e)
            {
                if (_dragIndex < 0 || !this.HasPointerCapture(e.pointerId)) return;
                Vector2 val = FromPlot(e.localPosition);
                if (IsCurve && _dragIndex < c._x.points.Count)
                {
                    c._x.points[_dragIndex].value = val.x;
                    c._y.points[_dragIndex].value = val.y;
                }
                else
                {
                    c._x.staticValue = val.x;
                    c._y.staticValue = val.y;
                }
                c.OnChanged?.Invoke();
                MarkDirtyRepaint();
                OnPlotEdited?.Invoke();
                e.StopPropagation();
            }

            void OnPointerUp(PointerUpEvent e)
            {
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
                _dragIndex = -1;
            }
        }
    }
}

// ZuiValue2DControl — UI Toolkit counterpart of the IMGUI ZUIValue2DControl: an XY value presented
// as ONE synchronized 2D control instead of two sliders. Static mode: a draggable dot in an XY plot
// (+ optional "(x, y)" text and/or a numeric X/Y block). Curve mode: numbered points tracing a PATH
// through XY space — both axes spatial, time implicit in point ORDER.
//
// Works over TWO data shapes via Zui2DSource (2026-07-23): an animatable ZUIValue pair, or a PLAIN
// Vector2 (a get/set pair) for values that must never animate — Pyre's blast origin, a MetaBlob orb's
// position. The plain source reports SupportsAnimation=false, which hides the mode switch entirely
// so the control still looks and feels like the same ZUI 2D pad everywhere.
//
// RIGHT-CLICK is the single way into the mode / presentation / copy-paste menu, on every mode and
// anywhere on the control (label, thumbnail, plot, side panel alike). There is deliberately no "⋯"
// button — same rule as ZuiValueControl (2026-07-23 there, matched here 2026-09-02): one gesture,
// learned once, works on every value in every tool. Curve mode keeps its own right-click (remove the
// nearest point) inside the plot; that consumption stops propagation before it reaches this control's
// handler, so the menu stays one right-click away on the plot's empty space, the label or the side panel.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// Where a ZuiValue2DControl reads/writes its XY value. Two shipped implementations:
    /// <see cref="ZuiValuePairSource"/> (animatable ZUIValue pair) and <see cref="ZuiVector2Source"/>
    /// (a plain Vector2 accessor — no animation).
    public abstract class Zui2DSource
    {
        /// False hides the Static/Curve mode switch: this value can never animate.
        public abstract bool SupportsAnimation { get; }
        public abstract bool IsCurve { get; }
        public abstract void SetCurve(bool on, Vector2 fallback);
        public abstract Vector2 Static { get; set; }
        public abstract int PointCount { get; }
        public abstract Vector2 GetPoint(int i);
        public abstract void SetPoint(int i, Vector2 v);
        public abstract void AddPoint(Vector2 v);
        public abstract void RemovePoint(int i);
        /// Pin each axis's own value range to the control's plot bounds (curve sources only).
        public virtual void PinRange(float xMin, float xMax, float yMin, float yMax) { }
        /// Clipboard payload, or null when this source doesn't support copy/paste.
        public virtual string ToClipboard() => null;
        public virtual bool TryPaste(string s) => false;
        /// Corner smoothness for a curve path (0 = sharp polyline, 1 = Catmull-Rom through the points). No-op for
        /// non-animatable sources (a plain Vector2 has no curve).
        public virtual float Smoothness { get => 0f; set { } }
        /// Sample the (optionally smoothed) path at normalized u in [0,1] — the SAME evaluation the runtime traces,
        /// so a drawn curve matches what plays. Non-curve sources return their static value.
        public virtual Vector2 SamplePath(float u) => Static;
    }

    /// The animatable source: a matched pair of ZUIValues kept in lockstep (both Static or both Curve).
    public class ZuiValuePairSource : Zui2DSource
    {
        readonly ZUIValue _x, _y;
        public ZuiValuePairSource(ZUIValue x, ZUIValue y)
        {
            _x = x ?? throw new ArgumentNullException(nameof(x));
            _y = y ?? throw new ArgumentNullException(nameof(y));
        }
        public ZUIValue X => _x;
        public ZUIValue Y => _y;

        public override bool SupportsAnimation => true;
        public override bool IsCurve => _x.mode == ZUIValue.Mode.Curve
            && _x.points.Count > 0 && _x.points.Count == _y.points.Count;

        public override void SetCurve(bool on, Vector2 fallback)
        {
            _x.mode = _y.mode = on ? ZUIValue.Mode.Curve : ZUIValue.Mode.Static;
            if (on) EnsurePaired(fallback);
        }

        public override Vector2 Static
        {
            get => new Vector2(_x.staticValue, _y.staticValue);
            set { _x.staticValue = value.x; _y.staticValue = value.y; }
        }

        public override int PointCount => Mathf.Min(_x.points.Count, _y.points.Count);
        public override Vector2 GetPoint(int i) => new Vector2(_x.points[i].value, _y.points[i].value);
        public override void SetPoint(int i, Vector2 v) { _x.points[i].value = v.x; _y.points[i].value = v.y; }

        public override void AddPoint(Vector2 v)
        {
            _x.points.Add(new ZUIEnvelopePoint(0f, v.x));
            _y.points.Add(new ZUIEnvelopePoint(0f, v.y));
            Renormalize();
        }

        public override void RemovePoint(int i)
        {
            _x.points.RemoveAt(i);
            _y.points.RemoveAt(i);
            Renormalize();
        }

        public override void PinRange(float xMin, float xMax, float yMin, float yMax)
        {
            _x.yMin = xMin; _x.yMax = xMax;
            _y.yMin = yMin; _y.yMax = yMax;
        }

        // Both axes share one smoothness so the pair stays a single 2D spline (Catmull-Rom is separable: per-axis
        // smoothing with the SAME tension IS the 2D curve, since the points are evenly-timed 2D knots).
        public override float Smoothness
        {
            get => _x.smoothness;
            set { _x.smoothness = value; _y.smoothness = value; }
        }

        public override Vector2 SamplePath(float u)
        {
            float x = ZUIEnvelopeEvaluator.Evaluate(_x.points, u, _x.yMax, _x.smoothness);
            float y = ZUIEnvelopeEvaluator.Evaluate(_y.points, u, _y.yMax, _y.smoothness);
            // Mirror ZUIValue.EvaluateCurve's overshoot clamp so the drawn path matches the runtime one exactly.
            if (_x.smoothness > 0f) x = Mathf.Clamp(x, Mathf.Min(_x.yMin, _x.yMax), Mathf.Max(_x.yMin, _x.yMax));
            if (_y.smoothness > 0f) y = Mathf.Clamp(y, Mathf.Min(_y.yMin, _y.yMax), Mathf.Max(_y.yMin, _y.yMax));
            return new Vector2(x, y);
        }

        // ── keyed editing (a frame clock is attached — see Options.WithFrameClock) ─────────────────────
        // Keys are the curve's own points; a key's frame is its normalized time scaled onto the host's frame
        // count, so time stays a fraction of the animation (a frame-count change keeps every key proportional,
        // exactly like every other animated ZUIValue). Keyed edits never Renormalize: timing is authored here.

        public float KeyTime(int i) => _x.points[i].time;

        public static float FrameToTime(int frame, int frames) => frames > 1 ? frame / (float)(frames - 1) : 0f;
        public static int TimeToFrame(float t, int frames) => frames > 1 ? Mathf.RoundToInt(t * (frames - 1)) : 0;

        /// The key that sits on `frame` (nearest one if several round to it), or -1 when that frame has none.
        public int KeyAtFrame(int frame, int frames)
        {
            int best = -1;
            float bestD = float.MaxValue, target = FrameToTime(frame, frames);
            for (int i = 0; i < PointCount; i++)
            {
                if (TimeToFrame(_x.points[i].time, frames) != frame) continue;
                float d = Mathf.Abs(_x.points[i].time - target);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// Place the value at `frame`: moves the key already on that frame, otherwise inserts a new key there in
        /// time order. Returns the key's index.
        public int SetKeyAtFrame(int frame, int frames, Vector2 v)
        {
            int at = KeyAtFrame(frame, frames);
            if (at >= 0) { SetPoint(at, v); return at; }
            float t = FrameToTime(frame, frames);
            int idx = PointCount;
            for (int i = 0; i < PointCount; i++)
                if (_x.points[i].time > t) { idx = i; break; }
            _x.points.Insert(idx, new ZUIEnvelopePoint(t, v.x));
            _y.points.Insert(idx, new ZUIEnvelopePoint(t, v.y));
            return idx;
        }

        /// Delete one key, leaving every other key's timing untouched.
        public void RemoveKey(int i)
        {
            _x.points.RemoveAt(i);
            _y.points.RemoveAt(i);
        }

        // Point order IS time order — re-deriving `time` from index keeps points evenly spaced
        // across the lifetime with no separate per-point timing to author.
        void Renormalize()
        {
            int n = PointCount;
            for (int i = 0; i < n; i++)
            {
                float tm = n > 1 ? i / (float)(n - 1) : 0f;
                _x.points[i].time = tm;
                _y.points[i].time = tm;
            }
        }

        void EnsurePaired(Vector2 fallback)
        {
            if (_x.points.Count > 0 && _x.points.Count == _y.points.Count) return;
            Vector2 s = new Vector2(_x.staticValue, _y.staticValue);
            if (s == Vector2.zero) s = fallback;
            _x.points.Clear(); _y.points.Clear();
            _x.points.Add(new ZUIEnvelopePoint(0f, s.x));
            _y.points.Add(new ZUIEnvelopePoint(0f, s.y));
            _x.points.Add(new ZUIEnvelopePoint(1f, s.x));
            _y.points.Add(new ZUIEnvelopePoint(1f, s.y));
        }

        // Same prefix + JSON shape as the IMGUI ZUIValue2DControl — the two halves' clipboards interoperate.
        [Serializable]
        class ClipboardPair { public ZUIValue x = new ZUIValue(); public ZUIValue y = new ZUIValue(); }
        const string Prefix = "ZUIVALUE2:";

        public override string ToClipboard()
            => Prefix + JsonUtility.ToJson(new ClipboardPair { x = _x, y = _y });

        public override bool TryPaste(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith(Prefix)) return false;
            try
            {
                var pair = JsonUtility.FromJson<ClipboardPair>(s.Substring(Prefix.Length));
                if (pair == null) return false;
                _x.CopyFrom(pair.x); _y.CopyFrom(pair.y);
                return true;
            }
            catch { return false; }
        }

        public static bool CanPaste(string s) => !string.IsNullOrEmpty(s) && s.StartsWith(Prefix);
    }

    /// The non-animatable source: a plain Vector2 behind a get/set pair (Pyre's blast origin, a
    /// MetaBlob orb position). Never offers Curve mode.
    public class ZuiVector2Source : Zui2DSource
    {
        readonly Func<Vector2> _get;
        readonly Action<Vector2> _set;
        public ZuiVector2Source(Func<Vector2> get, Action<Vector2> set) { _get = get; _set = set; }

        public override bool SupportsAnimation => false;
        public override bool IsCurve => false;
        public override void SetCurve(bool on, Vector2 fallback) { }
        public override Vector2 Static { get => _get(); set => _set(value); }
        public override int PointCount => 0;
        public override Vector2 GetPoint(int i) => Vector2.zero;
        public override void SetPoint(int i, Vector2 v) { }
        public override void AddPoint(Vector2 v) { }
        public override void RemovePoint(int i) { }
    }

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
            public bool startExpanded = false;       // first-open state (per control instance key)
            public bool showSidePanel = true;        // the label / Reset column left of the plot
            /// Extra content appended into the side panel under the label (e.g. Pyre's origin α slider).
            public Func<VisualElement> sidePanelExtra = null;
            /// EditorPrefs suffix for the per-identity "2D pad vs Two sliders" presentation choice
            /// (right-click menu). Null → the control's label is used. Two pads sharing a label share it.
            public string prefKey = null;
            /// Per-axis names used to label the Two-sliders presentation. Default "X"/"Y" compose
            /// "&lt;label&gt; · X"; a real name (e.g. "Pitch") stands alone as that slider's own label.
            public string xLabel = "X";
            public string yLabel = "Y";
            /// Frame clock (optional, animatable pair sources only). With it, an ANIMATED value is edited as
            /// positions keyed at frames: the host's transport frame is the frame being edited, only keyed frames
            /// show a marker (labelled with the 1-based frame number the host's transport shows), and an unkeyed
            /// frame shows a ghost where the value actually is on that frame. Without it (or for a value that is
            /// a path over progress rather than time) the pad keeps the order-is-time path editor.
            public Func<int> clockFrameCount = null;
            public Func<int> clockCurrentFrame = null;   // 0-based
            public Action<int> clockSeek = null;          // optional: pressing a key moves the host's transport there

            public Options WithRange(float xLo, float xHi, float yLo, float yHi)
            { xMin = xLo; xMax = xHi; yMin = yLo; yMax = yHi; return this; }
            public Options WithDefault(Vector2 v) { staticDefault = v; return this; }
            public Options WithPlotSize(float s) { plotSize = s; return this; }
            public Options WithValueDisplay(bool showText, bool showInputs)
            { showValueText = showText; showNumericInputs = showInputs; return this; }
            public Options WithVerticalStack(bool vertical = true) { stackInputsVertically = vertical; return this; }
            public Options Expanded(bool on = true) { startExpanded = on; return this; }
            public Options WithoutSidePanel() { showSidePanel = false; return this; }
            public Options WithSidePanelExtra(Func<VisualElement> extra) { sidePanelExtra = extra; return this; }
            public Options WithPrefKey(string key) { prefKey = key; return this; }
            public Options WithAxisLabels(string x, string y) { xLabel = x; yLabel = y; return this; }
            public Options WithFrameClock(Func<int> frameCount, Func<int> currentFrame, Action<int> seek = null)
            { clockFrameCount = frameCount; clockCurrentFrame = currentFrame; clockSeek = seek; return this; }
        }

        static readonly Color PointColor = new Color(0.4f, 0.85f, 1f);
        static readonly Color LineColor = new Color(0.4f, 0.85f, 1f, 0.7f);
        const float HitRadius = 8f;
        const float SidePanelWidth = 108f;
        const float NumericFieldWidth = 78f;

        // Fold + per-control display overrides, keyed by the source's identity object so they survive
        // window rebuilds (undo/redo) — same session-state trick as ZuiValueControl.
        // zoom/center are pure VIEW state (the wheel zoom of the expanded plot): no Undo, kept per control identity.
        class FoldState
        {
            public bool expanded; public bool? showValueTextOverride; public bool? showNumericInputsOverride; public bool seeded;
            public float zoom = 1f; public Vector2 center;
        }
        const float MaxZoom = 16f;
        static readonly Dictionary<object, FoldState> s_fold = new();
        static FoldState GetFold(object key)
        {
            if (!s_fold.TryGetValue(key, out var st)) { st = new FoldState(); s_fold[key] = st; }
            return st;
        }

        readonly Zui2DSource _src;
        readonly object _key;
        readonly Options _opt;
        readonly string _label;
        readonly string _tooltip;

        // Presentation: false = the 2D pad (default), true = two stacked 1D value controls. Persisted per
        // control identity in EditorPrefs; only offered when the source exposes the two ZUIValues.
        bool _twoSliders;

        public Action OnBeforeMutate;
        public Action OnChanged;

        /// Animatable XY pair.
        public ZuiValue2DControl(string label, ZUIValue x, ZUIValue y, Options options, string tooltip)
            : this(label, new ZuiValuePairSource(x, y), x, options, tooltip) { }

        /// Any source. `stateKey` identifies this control for fold/display state across rebuilds —
        /// pass a stable object (the edited data instance).
        public ZuiValue2DControl(string label, Zui2DSource source, object stateKey, Options options, string tooltip)
        {
            _src = source ?? throw new ArgumentNullException(nameof(source));
            _key = stateKey ?? source;
            _opt = options ?? new Options();
            _label = label;
            _tooltip = tooltip;
            this.tooltip = tooltip;

            var fold = GetFold(_key);
            if (!fold.seeded) { fold.seeded = true; fold.expanded = _opt.startExpanded; }

            // The Two-sliders presentation binds two 1D ZuiValueControls to the pair's X/Y ZUIValues, so it
            // only exists for an animatable pair source (a plain Vector2 has no ZUIValues to bind); restore
            // the persisted choice when it applies. The right-click menu itself is unconditional below — a
            // plain Vector2 source still needs Reset/copy-paste even though it has no mode or presentation
            // choice to offer.
            if (_src is ZuiValuePairSource) _twoSliders = EditorPrefs.GetBool(PrefsKey, false);

            // RIGHT-CLICK ANYWHERE opens the menu (bubble phase, default): a curve-mode point removal inside
            // the plot (Plot2D.OnPointerDown) stops propagation when it actually consumes the click, which
            // keeps this handler from firing for that gesture without needing a separate suppress flag.
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                ShowMenu(this);
                e.StopPropagation();
            });

            Build();
        }

        string PrefsKey => "ZuiValue2D." + (string.IsNullOrEmpty(_opt.prefKey) ? (_label ?? "") : _opt.prefKey);

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        // ── keyed mode (frame clock attached + the value is animated) ──────────────────
        bool HasClock => _opt.clockFrameCount != null && _opt.clockCurrentFrame != null && _src is ZuiValuePairSource;
        bool Keyed => HasClock && _src.IsCurve;
        ZuiValuePairSource Pair => _src as ZuiValuePairSource;
        int ClockFrames => Mathf.Max(1, _opt.clockFrameCount());
        int ClockFrame => Mathf.Clamp(_opt.clockCurrentFrame(), 0, ClockFrames - 1);

        Label _status;   // keyed mode: "Frame 7: key" / "Frame 7: ghost" in the side panel, text-only updates

        void RefreshStatus()
        {
            if (_status == null || !Keyed) return;
            int f = ClockFrame;
            bool keyed = Pair.KeyAtFrame(f, ClockFrames) >= 0;
            _status.text = $"Frame {f + 1}: " + (keyed ? "key" : "ghost");
        }

        const string KeyedTip =
            "Set the transport to a frame, then click or drag in the pad to place the value on that frame — a " +
            "numbered dot is a key, labelled with its frame. A hollow ring is a GHOST: where the value is on the " +
            "current frame, worked out from the keys around it (before the first key / after the last it holds " +
            "that key). Press a key to jump to its frame and drag it; right-click a key to delete it. Keys are " +
            "stored as a fraction of the animation, so changing the frame count keeps them in proportion.";
        const string ZoomTip = " Mouse wheel zooms toward the cursor; middle-drag pans.";

        void ResetZoom()
        {
            var st = GetFold(_key);
            st.zoom = 1f;
            Build();
        }

        void Build()
        {
            Clear();
            var fold = GetFold(_key);
            if (_twoSliders && _src is ZuiValuePairSource pair) { BuildTwoSliders(pair); return; }
            if (fold.expanded) BuildExpanded(fold); else BuildCollapsed(fold);
        }

        // ── collapsed: label · mini thumbnail (right-click for the menu) ────────────
        void BuildCollapsed(FoldState fold)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            if (!string.IsNullOrEmpty(_label))
            {
                var l = new Label(_label)
                {
                    tooltip = (_tooltip + " ").TrimStart() + "Click to expand the 2D editor. Right-click to configure.",
                };
                l.AddToClassList("zui-field__label");
                l.AddToClassList("zui-fold-label");
                l.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    fold.expanded = true; Build(); e.StopPropagation();
                });
                row.Add(l);
            }
            var thumb = new Plot2D(this, thumbnail: true) { tooltip = "Click to expand the 2D editor. Right-click to configure." };
            thumb.style.width = 120f;
            thumb.style.height = 18f;
            thumb.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                fold.expanded = true; Build(); e.StopPropagation();
            });
            row.Add(thumb);
            Add(row);
        }

        // ── expanded: side panel · (numeric block) · plot ───────────────────────────
        void BuildExpanded(FoldState fold)
        {
            bool isCurve = _src.IsCurve;
            bool showInputs = !isCurve && (fold.showNumericInputsOverride ?? _opt.showNumericInputs);
            if (isCurve) _src.PinRange(_opt.xMin, _opt.xMax, _opt.yMin, _opt.yMax);

            // Assigned once the plot element exists (below); the side panel's smoothness slider repaints it live.
            Plot2D plotForRepaint = null;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.alignItems = Align.FlexStart;

            if (_opt.showSidePanel)
            {
                var side = new VisualElement();
                side.style.width = SidePanelWidth;
                side.style.flexShrink = 0f;
                side.style.minHeight = _opt.plotSize;

                var title = new Label(_label ?? "")
                {
                    tooltip = (_tooltip + " ").TrimStart() + "Click to collapse the 2D editor.",
                };
                title.AddToClassList("zui-box__title");
                title.AddToClassList("zui-fold-label");
                title.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    fold.expanded = false; Build(); e.StopPropagation();
                });
                side.Add(title);

                if (_opt.sidePanelExtra != null)
                {
                    var extra = _opt.sidePanelExtra();
                    if (extra != null) side.Add(extra);
                }

                _status = null;
                if (isCurve && Keyed)
                {
                    // One fixed single line whose TEXT follows the transport (never its geometry).
                    _status = Z.Text("", ZuiText.Small,
                        "Whether the transport's current frame has its own key, or shows a ghost worked out from " +
                        "the keys around it. " + KeyedTip);
                    _status.style.whiteSpace = WhiteSpace.NoWrap;
                    _status.style.overflow = Overflow.Hidden;
                    side.Add(_status);
                    RefreshStatus();
                }
                if (isCurve)
                {
                    if (!Keyed)
                    {
                        side.Add(Z.Text($"{_src.PointCount} point(s)", ZuiText.Small, "How many path points the animation has."));
                        var hint = Z.Text("Click empty space to add, drag to move, right-click to remove (min 2).",
                            ZuiText.Subtle, "Path editing hints.");
                        hint.style.whiteSpace = WhiteSpace.Normal;
                        side.Add(hint);
                    }
                    // Corner smoothness: 0 = sharp straight segments, 1 = a smooth Catmull-Rom curve through the
                    // points. Cheap smooth pathing, not precision editing; needs 3+ points to have a corner to round.
                    side.Add(Z.MicroSlider("Smooth", _src.Smoothness, 0f, 1f,
                        "Round the path's corners: 0 = sharp straight segments, 1 = a smooth curve through the " +
                        "points. Needs at least 3 points (2 points are always a straight line).",
                        v => { Mutate(() => _src.Smoothness = v); plotForRepaint?.MarkDirtyRepaint(); },
                        SidePanelWidth - 8f, showValue: true));
                }
                side.Add(Z.Button("Reset", "Reset this value to its default.", ResetToDefault));
                // No flexible spacer before this: the side panel stretches to whatever the numeric
                // block/plot beside it is tall, which left the button floating far below the rest of
                // the column (reported 2026-07-23). A tight column reads as one group.
                side.Add(Z.Button("▲", "Collapse the 2D editor back to its one-line thumbnail.",
                    () => { fold.expanded = false; Build(); }));
                row.Add(side);
            }

            string plotTip = ((_tooltip ?? "") + " ").TrimStart() + (isCurve && Keyed ? KeyedTip : "") + ZoomTip;
            var plot = new Plot2D(this, thumbnail: false) { tooltip = plotTip.Trim() };
            plot.style.width = _opt.plotSize;
            plot.style.height = _opt.plotSize;
            plotForRepaint = plot;

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

        void ResetToDefault()
        {
            Vector2 d = _opt.staticDefault ?? Vector2.zero;
            Mutate(() =>
            {
                if (_src.IsCurve)
                {
                    while (_src.PointCount > 0) _src.RemovePoint(_src.PointCount - 1);
                    _src.AddPoint(d);
                    _src.AddPoint(d);
                }
                else _src.Static = d;
            });
            Build();
        }

        VisualElement BuildNumericBlock(Plot2D plot)
        {
            var col = new VisualElement();
            col.style.width = NumericFieldWidth + 4f;
            col.style.flexShrink = 0f;
            col.Add(Z.Text("Y", ZuiText.Small, "The Y component."));
            var yField = Z.Float(_src.Static.y, "The Y component.", v =>
            {
                Mutate(() => _src.Static = new Vector2(_src.Static.x, v));
                plot.MarkDirtyRepaint();
            }, NumericFieldWidth);
            col.Add(yField);
            col.Add(Z.VSpace(4f));
            col.Add(Z.Text("X", ZuiText.Small, "The X component."));
            var xField = Z.Float(_src.Static.x, "The X component.", v =>
            {
                Mutate(() => _src.Static = new Vector2(v, _src.Static.y));
                plot.MarkDirtyRepaint();
            }, NumericFieldWidth);
            col.Add(xField);
            plot.OnPlotEdited += () =>
            {
                var s = _src.Static;
                xField.SetValueWithoutNotify(s.x);
                yField.SetValueWithoutNotify(s.y);
            };
            return col;
        }

        // ── the mode menu, a ZUI popover anchored to whatever was right-clicked ─────
        void ShowMenu(VisualElement anchor)
        {
            var fold = GetFold(_key);
            var menu = Z.Menu(anchor);
            bool isCurve = _src.IsCurve;   // curve/path has no single (x,y); the display-option toggles below are hidden for it

            // Presentation → 2D pad (default) or two stacked 1D sliders over the same X/Y ZUIValues. Only
            // offered for an animatable pair source — a plain Vector2 has no ZUIValues to bind a slider to.
            if (_src is ZuiValuePairSource)
            {
                menu.Radio(null, new[] { "2D pad", "Two sliders" }, _twoSliders ? 1 : 0,
                    "Show this value as one synchronized XY pad, or as two separate sliders.",
                    i => SetTwoSliders(i == 1), closeOnSelect: true);
                menu.Separator();
            }

            // Mode → one radio group (pick-one-then-close), only when the value can animate at all.
            if (_src.SupportsAnimation)
            {
                menu.Radio(null, new[] { "Static (one point)", HasClock ? "Animate (keys at frames)" : "Animate over time (a path)" },
                    isCurve ? 1 : 0,
                    HasClock
                        ? "Hold one static point, or place the value on chosen frames (keys) and let the frames between follow."
                        : "Hold one static point, or trace a path of points over the particle's life.",
                    i =>
                    {
                        Mutate(() => _src.SetCurve(i == 1, _opt.staticDefault ?? Vector2.zero));
                        fold.expanded = true;
                        Build();
                    }, closeOnSelect: true);
                menu.Separator();
            }

            // Display options ("as text" / "as numeric inputs") only affect the EXPANDED STATIC view — a curve/path
            // has no single (x, y) to show, so BOTH are HIDDEN in curve mode, where toggling them did nothing and
            // read as broken (2026-07-26 fix). They stay-open toggle rows now; flipping one also expands, otherwise
            // it silently appears to do nothing (reported 2026-07-23).
            if (!isCurve)
            {
                bool showText = fold.showValueTextOverride ?? _opt.showValueText;
                bool showInputs = fold.showNumericInputsOverride ?? _opt.showNumericInputs;
                menu.Toggle("Show value as text", "Draw a small (x, y) label beside the dot.", showText,
                    on => { fold.showValueTextOverride = on; fold.expanded = true; Build(); });
                menu.Toggle("Show value as numeric inputs", "Show a compact numeric X/Y block beside the plot.",
                    showInputs, on => { fold.showNumericInputsOverride = on; fold.expanded = true; Build(); });
            }

            menu.Separator();
            menu.Item("Reset to default", "Reset this value to its default.", ResetToDefault);
            if (fold.zoom > 1f)
                menu.Item("Reset zoom", "Zoom the pad back out to show the whole range.", ResetZoom);

            string payload = _src.ToClipboard();
            if (payload != null)
            {
                menu.Separator();
                bool canPaste = ZuiValuePairSource.CanPaste(EditorGUIUtility.systemCopyBuffer);
                menu.IconRow(
                    ("copy", "Copy this value to the clipboard.",
                        () => EditorGUIUtility.systemCopyBuffer = payload),
                    ("clipboard-text", canPaste ? "Paste a copied value from the clipboard." : "Paste (clipboard has no value).",
                        canPaste
                            ? () => { Mutate(() => _src.TryPaste(EditorGUIUtility.systemCopyBuffer)); Build(); }
                            : (Action)null));
            }

            menu.Show();
        }

        // ── alternate presentation: two stacked 1D controls over the same X/Y ZUIValues ─────────────
        void BuildTwoSliders(ZuiValuePairSource pair)
        {
            var col = new VisualElement();
            col.Add(BuildAxisControl(pair.X, AxisLabel(_opt.xLabel, "X"), _opt.xMin, _opt.xMax, _opt.staticDefault?.x));
            col.Add(Z.VSpace(2f));
            col.Add(BuildAxisControl(pair.Y, AxisLabel(_opt.yLabel, "Y"), _opt.yMin, _opt.yMax, _opt.staticDefault?.y));
            Add(col);
        }

        ZuiValueControl BuildAxisControl(ZUIValue v, string label, float axisMin, float axisMax, float? axisDefault)
        {
            // Translate the pad's Options to the 1D control: per-axis RANGE, and hide the curve chrome the pad
            // has no analog for (Dur/Warm/Loop + the Value-Range row) — WithoutCurveExtras also pins the
            // curve's Y-range to [min,max], matching the pad's own PinRange. MinMax mode is disabled so an
            // axis only ever holds a mode the pad understands (Static / Curve). Grow fills the pane like the
            // rest of ZUI's 1D controls do.
            var o = new ZuiValueControl.Options { allowMinMax = false };
            o.WithRange(axisMin, axisMax);
            o.WithoutCurveExtras();
            o.WithGrow();
            if (axisDefault.HasValue) o.WithDefault(axisDefault.Value);

            var c = new ZuiValueControl(label, v, o, _tooltip);
            c.OnBeforeMutate += () => OnBeforeMutate?.Invoke();
            c.OnChanged += () => OnChanged?.Invoke();
            return c;
        }

        // "Tilt" + default axis "X" → "Tilt · X"; a caller-supplied axis name ("Pitch") stands alone.
        string AxisLabel(string axisName, string defaultAxis)
        {
            if (string.IsNullOrEmpty(axisName)) axisName = defaultAxis;
            if (string.IsNullOrEmpty(_label)) return axisName;
            return axisName == defaultAxis ? _label + " · " + axisName : axisName;
        }

        void SetTwoSliders(bool on)
        {
            if (_twoSliders == on) return;
            _twoSliders = on;
            EditorPrefs.SetBool(PrefsKey, on);
            Build();
        }

        // ── the plot element (shared by thumbnail + full plot) ──────────────────────
        // The full plot can be ZOOMED (mouse wheel toward the cursor, middle-drag pans — the same feel as the
        // Tileset Builder / Lazor / DotGen canvases). Zoom only changes the VISIBLE window onto the value range;
        // every value written is still inside the declared range. The thumbnail always shows the whole range.
        class Plot2D : VisualElement
        {
            static readonly Color GhostColor = new Color(0.4f, 0.85f, 1f, 0.75f);
            static readonly Color KeyedPathColor = new Color(0.4f, 0.85f, 1f, 0.3f);
            static readonly Color CurrentRingColor = new Color(1f, 1f, 1f, 0.9f);
            static readonly Color DimLabelColor = new Color(1f, 1f, 1f, 0.55f);

            readonly ZuiValue2DControl c;
            readonly bool thumb;
            int _dragIndex = -1;      // curve mode: point index; static mode: 0 while dragging the dot
            bool _panning;
            Vector2 _panLast;
            int _lastFrame = int.MinValue;

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
                    RegisterCallback<WheelEvent>(OnWheel);
                }
                // A keyed pad follows the host's transport: repaint (ghost/highlight) and refresh the status line
                // whenever the frame changes, whether by playback, the scrubber, or a key press here.
                if (c.HasClock)
                    schedule.Execute(() =>
                    {
                        if (!c.Keyed) return;
                        int f = c.ClockFrame;
                        if (f == _lastFrame) return;
                        _lastFrame = f;
                        MarkDirtyRepaint();
                        if (!thumb) c.RefreshStatus();
                    }).Every(100);
            }

            bool IsCurve => c._src.IsCurve;
            bool Keyed => c.Keyed;

            // ── view window (value space) ──
            Rect FullRange => Rect.MinMaxRect(c._opt.xMin, c._opt.yMin, c._opt.xMax, c._opt.yMax);
            float Zoom => thumb ? 1f : Mathf.Clamp(GetFold(c._key).zoom, 1f, MaxZoom);

            Rect View()
            {
                var full = FullRange;
                float z = Zoom;
                if (z <= 1f) return full;
                var st = GetFold(c._key);
                float hw = full.width * 0.5f / z, hh = full.height * 0.5f / z;
                var ctr = new Vector2(Mathf.Clamp(st.center.x, full.xMin + hw, full.xMax - hw),
                                      Mathf.Clamp(st.center.y, full.yMin + hh, full.yMax - hh));
                st.center = ctr;
                return new Rect(ctr.x - hw, ctr.y - hh, hw * 2f, hh * 2f);
            }

            Vector2 ToPlot(Vector2 val)
            {
                var r = contentRect;
                var v = View();
                float tx = v.width != 0f ? (val.x - v.xMin) / v.width : 0.5f;
                float ty = v.height != 0f ? (val.y - v.yMin) / v.height : 0.5f;
                // Unzoomed, an out-of-range value pins to the border (the pad's long-standing behaviour); zoomed,
                // off-view points stay off-view and are simply not drawn.
                if (Zoom <= 1f) { tx = Mathf.Clamp01(tx); ty = Mathf.Clamp01(ty); }
                return new Vector2(r.xMin + tx * r.width, r.yMax - ty * r.height);   // Y-up, like the Pyre canvas
            }

            Vector2 FromPlot(Vector2 local)
            {
                var r = contentRect;
                var v = View();
                float tx = Mathf.InverseLerp(r.xMin, r.xMax, Mathf.Clamp(local.x, r.xMin, r.xMax));
                float ty = Mathf.InverseLerp(r.yMax, r.yMin, Mathf.Clamp(local.y, r.yMin, r.yMax));
                return new Vector2(
                    (float)Math.Round(Mathf.Lerp(v.xMin, v.xMax, tx), 5),
                    (float)Math.Round(Mathf.Lerp(v.yMin, v.yMax, ty), 5));
            }

            bool InPlot(Vector2 p) { var r = contentRect; return p.x >= r.xMin - 0.5f && p.x <= r.xMax + 0.5f && p.y >= r.yMin - 0.5f && p.y <= r.yMax + 0.5f; }

            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f) || !(r.height > 4f)) return;
                var p = mgc.painter2D;
                var v = View();

                p.strokeColor = new Color(1f, 1f, 1f, 0.25f);
                p.lineWidth = 1f;
                if (v.xMin < 0f && v.xMax > 0f)
                {
                    float cx = ToPlot(new Vector2(0f, v.yMin)).x;
                    p.BeginPath(); p.MoveTo(new Vector2(cx, r.yMin)); p.LineTo(new Vector2(cx, r.yMax)); p.Stroke();
                }
                if (v.yMin < 0f && v.yMax > 0f)
                {
                    float cy = ToPlot(new Vector2(v.xMin, 0f)).y;
                    p.BeginPath(); p.MoveTo(new Vector2(r.xMin, cy)); p.LineTo(new Vector2(r.xMax, cy)); p.Stroke();
                }

                float dotR = thumb ? 2.5f : 5f;
                if (IsCurve && Keyed) PaintKeyed(mgc, p, dotR);
                else if (IsCurve)
                {
                    int n = c._src.PointCount;
                    if (n >= 2)
                    {
                        p.strokeColor = LineColor;
                        p.lineWidth = thumb ? 1f : 1.5f;
                        if (c._src.Smoothness > 0f && n >= 3)
                        {
                            // Sample the SAME evaluation the runtime traces so the drawn path matches what plays.
                            int steps = Mathf.Clamp((n - 1) * 12, 24, 160);
                            StrokeClipped(p, s => ToPlot(c._src.SamplePath(s / (float)steps)), steps);
                        }
                        else
                            StrokeClipped(p, i => ToPlot(c._src.GetPoint(i)), n - 1);
                    }
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 pt = ToPlot(c._src.GetPoint(i));
                        if (!InPlot(pt)) continue;
                        p.fillColor = PointColor;
                        p.BeginPath(); p.Arc(pt, dotR, 0f, 360f); p.Fill();
                        if (!thumb)
                            mgc.DrawText((i + 1).ToString(), new Vector2(pt.x + 6f, pt.y - 15f), 10f, Color.white);
                    }
                }
                else
                {
                    Vector2 s = c._src.Static;
                    Vector2 pt = ToPlot(s);
                    if (InPlot(pt))
                    {
                        p.fillColor = PointColor;
                        p.BeginPath(); p.Arc(pt, dotR + (thumb ? 0.5f : 0f), 0f, 360f); p.Fill();

                        var fold = GetFold(c._key);
                        bool showText = fold.showValueTextOverride ?? c._opt.showValueText;
                        if (!thumb && showText)
                        {
                            string txt = $"({s.x:0.##}, {s.y:0.##})";
                            bool rightHalf = pt.x > r.xMin + r.width * 0.6f;
                            var tp = rightHalf ? new Vector2(pt.x - 7f - txt.Length * 5.5f, pt.y - 14f)
                                               : new Vector2(pt.x + 7f, pt.y - 14f);
                            mgc.DrawText(txt, tp, 10f, new Color(1f, 1f, 1f, 0.85f));
                        }
                    }
                }

                // Zoom readout, drawn inside the plot so it never takes layout space.
                if (!thumb && Zoom > 1f)
                    mgc.DrawText($"×{Zoom:0.#}", new Vector2(r.xMax - 44f, r.yMax - 24f), 18f, DimLabelColor);
            }

            // Keyed: a faint trace of what actually plays, a numbered dot per KEY only, the key on the current
            // frame ringed, and — when the current frame has no key — a hollow GHOST where the value is right now.
            void PaintKeyed(MeshGenerationContext mgc, Painter2D p, float dotR)
            {
                var pair = c.Pair;
                int frames = c.ClockFrames, cur = c.ClockFrame;
                int n = pair.PointCount;

                if (n >= 2)
                {
                    p.strokeColor = thumb ? LineColor : KeyedPathColor;
                    p.lineWidth = thumb ? 1f : 1.5f;
                    int steps = Mathf.Clamp(frames * 4, 32, 256);
                    StrokeClipped(p, s => ToPlot(pair.SamplePath(s / (float)steps)), steps);
                }

                int keyHere = pair.KeyAtFrame(cur, frames);
                for (int i = 0; i < n; i++)
                {
                    Vector2 pt = ToPlot(pair.GetPoint(i));
                    if (!InPlot(pt)) continue;
                    p.fillColor = PointColor;
                    p.BeginPath(); p.Arc(pt, dotR, 0f, 360f); p.Fill();
                    if (thumb) continue;
                    if (i == keyHere)
                    {
                        p.strokeColor = CurrentRingColor; p.lineWidth = 1.5f;
                        p.BeginPath(); p.Arc(pt, dotR + 3f, 0f, 360f); p.Stroke();
                    }
                    int f = ZuiValuePairSource.TimeToFrame(pair.KeyTime(i), frames);
                    mgc.DrawText((f + 1).ToString(), new Vector2(pt.x + 7f, pt.y - 24f), 20f, Color.white);
                }

                if (keyHere < 0)
                {
                    Vector2 g = ToPlot(pair.SamplePath(ZuiValuePairSource.FrameToTime(cur, frames)));
                    if (InPlot(g))
                    {
                        p.strokeColor = GhostColor; p.lineWidth = thumb ? 1f : 1.5f;
                        p.BeginPath(); p.Arc(g, dotR + (thumb ? 0f : 1f), 0f, 360f); p.Stroke();
                        if (!thumb)
                            mgc.DrawText((cur + 1).ToString(), new Vector2(g.x + 7f, g.y - 24f), 20f, DimLabelColor);
                    }
                }
            }

            // Stroke a polyline of count+1 plot-space points, breaking it wherever a point leaves the plot (only
            // possible while zoomed) so nothing draws outside the pad.
            void StrokeClipped(Painter2D p, Func<int, Vector2> pointAt, int count)
            {
                bool open = false;
                p.BeginPath();
                for (int i = 0; i <= count; i++)
                {
                    Vector2 q = pointAt(i);
                    if (!InPlot(q)) { open = false; continue; }
                    if (!open) { p.MoveTo(q); open = true; } else p.LineTo(q);
                }
                p.Stroke();
            }

            int FindPointNear(Vector2 local)
            {
                if (!IsCurve)
                    return Vector2.Distance(ToPlot(c._src.Static), local) <= HitRadius ? 0 : -1;
                for (int i = 0; i < c._src.PointCount; i++)
                {
                    Vector2 pt = ToPlot(c._src.GetPoint(i));
                    if (InPlot(pt) && Vector2.Distance(pt, local) <= HitRadius) return i;
                }
                return -1;
            }

            void OnWheel(WheelEvent e)
            {
                var st = GetFold(c._key);
                float old = Zoom;
                float z = Mathf.Clamp(old * (e.delta.y < 0 ? 1.25f : 0.8f), 1f, MaxZoom);
                if (z < 1.001f) z = 1f;
                if (!Mathf.Approximately(z, old))
                {
                    // Keep the value under the cursor under the cursor.
                    var r = contentRect;
                    var v = View();
                    Vector2 local = e.localMousePosition;
                    float u = Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width));
                    float w = Mathf.Clamp01((r.yMax - local.y) / Mathf.Max(1f, r.height));
                    var under = new Vector2(v.xMin + u * v.width, v.yMin + w * v.height);
                    var full = FullRange;
                    float hw = full.width * 0.5f / z, hh = full.height * 0.5f / z;
                    st.center = new Vector2(under.x - u * hw * 2f + hw, under.y - w * hh * 2f + hh);
                    st.zoom = z;
                    MarkDirtyRepaint();
                }
                e.StopPropagation();   // never let the surrounding scroll view scroll while zooming the pad
            }

            void OnPointerDown(PointerDownEvent e)
            {
                Vector2 local = e.localPosition;

                if (e.button == 2)   // middle-drag pans a zoomed pad
                {
                    if (Zoom <= 1f) return;
                    _panning = true;
                    _panLast = local;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                if (e.button == 1)
                {
                    if (!IsCurve) return;
                    int minPoints = Keyed ? 1 : 2;
                    if (c._src.PointCount <= minPoints) return;
                    int hit = FindPointNear(local);
                    if (hit < 0) return;
                    if (Keyed) c.Mutate(() => c.Pair.RemoveKey(hit));
                    else c.Mutate(() => c._src.RemovePoint(hit));
                    MarkDirtyRepaint();
                    c.RefreshStatus();
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
                    // Pressing a key jumps the host's transport to that key's frame, so the key being dragged is
                    // always the one on the frame being looked at.
                    if (IsCurve && Keyed && c._opt.clockSeek != null)
                        c._opt.clockSeek(ZuiValuePairSource.TimeToFrame(c.Pair.KeyTime(point), c.ClockFrames));
                    MarkDirtyRepaint();
                    e.StopPropagation();
                    return;
                }

                if (IsCurve && Keyed)   // empty space: place the value on the CURRENT frame (move its key or add one)
                {
                    Vector2 val = FromPlot(local);
                    int idx = -1;
                    c.Mutate(() => idx = c.Pair.SetKeyAtFrame(c.ClockFrame, c.ClockFrames, val));
                    _dragIndex = idx;
                    this.CapturePointer(e.pointerId);
                    MarkDirtyRepaint();
                    c.RefreshStatus();
                    OnPlotEdited?.Invoke();
                    e.StopPropagation();
                }
                else if (IsCurve)   // click empty plot space appends a new point there (path mode)
                {
                    Vector2 val = FromPlot(local);
                    c.Mutate(() => c._src.AddPoint(val));
                    _dragIndex = c._src.PointCount - 1;
                    this.CapturePointer(e.pointerId);
                    MarkDirtyRepaint();
                    OnPlotEdited?.Invoke();
                    e.StopPropagation();
                }
                else   // static: clicking anywhere moves the dot straight there, then keeps dragging
                {
                    c.OnBeforeMutate?.Invoke();
                    _dragIndex = 0;
                    c._src.Static = FromPlot(local);
                    c.OnChanged?.Invoke();
                    this.CapturePointer(e.pointerId);
                    MarkDirtyRepaint();
                    OnPlotEdited?.Invoke();
                    e.StopPropagation();
                }
            }

            void OnPointerMove(PointerMoveEvent e)
            {
                if (_panning && this.HasPointerCapture(e.pointerId))
                {
                    var r = contentRect;
                    var v = View();
                    Vector2 d = (Vector2)e.localPosition - _panLast;
                    _panLast = e.localPosition;
                    var st = GetFold(c._key);
                    st.center -= new Vector2(d.x * v.width / Mathf.Max(1f, r.width), -d.y * v.height / Mathf.Max(1f, r.height));
                    MarkDirtyRepaint();
                    e.StopPropagation();
                    return;
                }
                if (_dragIndex < 0 || !this.HasPointerCapture(e.pointerId)) return;
                Vector2 val = FromPlot(e.localPosition);
                if (IsCurve && _dragIndex < c._src.PointCount) c._src.SetPoint(_dragIndex, val);
                else c._src.Static = val;
                c.OnChanged?.Invoke();
                MarkDirtyRepaint();
                OnPlotEdited?.Invoke();
                e.StopPropagation();
            }

            void OnPointerUp(PointerUpEvent e)
            {
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
                _dragIndex = -1;
                _panning = false;
            }
        }
    }
}

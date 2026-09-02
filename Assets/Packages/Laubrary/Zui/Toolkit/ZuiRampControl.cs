// ZuiRampControl — the bespoke control for a colour ramp (IZuiRamp): ONE row holding a painted strip over a
// checkerboard, a marker lane under it (one thumb per stop, tinted with that stop's own colour), a compact "+"
// and — when the ramp offers blend modes — a segmented mode row. It replaces the vertical stack of near-identical
// "Stop N" cards ZuiReflect's generic List<> branch used to draw for a ramp, which was unusable at 10 stops.
//
// The strip is painted from the ramp's OWN Eval, so what it shows IS what the renderer paints — there is no second
// preview image to drift from it, and none is added (the editing surface IS the preview).
//
// Gestures, deliberately the same vocabulary as ZuiEnvelope so ZUI stays internally consistent:
//   * drag a marker           -> moves that stop's position, clamped strictly between its neighbours
//   * double-click the strip  -> inserts a stop THERE, carrying the colour the ramp already evaluates there, so
//                                inserting never changes how the ramp looks
//   * right-click a marker    -> removes that stop
//   * click a marker          -> a popover with its colour (the sanctioned ColorField island), its position, and
//                                a Remove button
//   * the "+" button          -> inserts at the middle; it is present ALWAYS, including at zero stops, because an
//                                empty ramp is legal (Pyre's soot ramp) and must never be auto-seeded — a blank
//                                surface with no create affordance would be a dead end.
//
// Undo contract, same as every ZUI control that mutates in place: OnBeforeMutate fires once per GESTURE (a drag,
// a colour pick, an insert) BEFORE the first mutation, OnChanged after every mutation.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiRampControl : VisualElement
    {
        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        readonly IZuiRamp _ramp;
        readonly Strip _strip;
        readonly ZuiSegmented _mode;
        readonly Button _library;

        // The width lesson from the deleted ZuiBandsControl: a strip sized for a narrow card gets CLIPPED at the
        // reflected card's column edge. This one starts at a readable minimum and takes whatever the pane gives it.
        public const float MinStripWidth = 150f;
        const float AddWidth = 22f;

        public ZuiRampControl(IZuiRamp ramp, string tooltip = null)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            AddToClassList("zui-ramp");
            AddToClassList("zui-row");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.FlexStart;
            style.flexGrow = 1f;
            style.flexShrink = 1f;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _strip = new Strip(this)
            {
                tooltip = "The ramp as the renderer paints it, over a checker so each stop's alpha reads. "
                        + "Drag a marker to move that stop; double-click the strip to insert one there; "
                        + "right-click a marker to remove it; click a marker for its colour and position.",
            };
            Add(_strip);

            var add = Z.Button("+", "Add a stop in the middle of the ramp, taking the colour the ramp already has "
                                  + "there. Double-click the strip instead to place one at an exact position.",
                () => Mutate(() => _ramp.Insert(0.5f, _ramp.Eval(0.5f)))).W(AddWidth);
            add.style.marginLeft = 6f;
            Add(add);

            // T-0205 — the SAME project gradient library Z.Gradient's "★" reaches, via ZuiRampGradientBridge:
            // "Load" replaces every stop with a saved gradient's colour keys (always exact — a Gradient has at
            // most 8 keys, this ramp has no upper bound); "Save" pushes the ramp's own stops out as a Gradient
            // (exact up to 8 stops, evenly subsampled beyond that — stated in the bridge's own file header).
            // This does NOT touch this ramp's data on its own; it only acts when the author presses one of these.
            _library = Z.Button("★", "This project's saved gradients — Load one into this ramp, or Save this "
                                  + "ramp's stops as a new one.", OpenLibrary).W(AddWidth);
            _library.style.marginLeft = 4f;
            Add(_library);

            // Two short options => Segmented, never a dropdown and never MiniRadio (ui-layout-rules: control choice).
            var names = _ramp.BlendModeNames;
            if (names != null && names.Length > 0)
            {
                _mode = Z.Segmented(Mathf.Clamp(_ramp.BlendMode, 0, names.Length - 1), names,
                    "How two neighbouring stops are mixed between them. This changes the COLOURS the ramp "
                  + "produces, not just how it is drawn.",
                    i => Mutate(() => _ramp.BlendMode = i));
                _mode.style.marginLeft = 6f;
                _mode.style.flexShrink = 0f;
                Add(_mode);
            }
        }

        /// Re-read the ramp (an undo, an external edit) — the strip and the mode row redraw from it.
        public void Refresh()
        {
            _mode?.SetOn(i => i == _ramp.BlendMode);
            _strip.MarkDirtyRepaint();
        }

        // ── mutation / Undo bookkeeping ──────────────────────────────────────────────────────────────────
        // One gesture = one Undo record: a drag calls BeginGesture once then Apply per move.
        bool _gestureOpen;
        void BeginGesture() { if (_gestureOpen) return; _gestureOpen = true; OnBeforeMutate?.Invoke(); }
        void EndGesture() => _gestureOpen = false;
        void Apply(Action edit) { edit(); _strip.MarkDirtyRepaint(); OnChanged?.Invoke(); }
        void Mutate(Action edit) { BeginGesture(); Apply(edit); EndGesture(); }

        // T-0205 — opens the SAME saved-gradient popup Z.Gradient's "★" opens. "Save" reads this ramp's current
        // stops (via the bridge, subsampled beyond 8 as stated in ZuiRampGradientBridge's header) and adds them
        // to the shared ZuiGradientPresetLibrary; picking a saved entry REPLACES every stop on this ramp — one
        // gesture, recorded through the normal Mutate() Undo wrapper, never applied silently.
        void OpenLibrary()
        {
            ZuiGradientPresetPopup.Show(_library,
                current: () => ZuiRampGradientBridge.ToGradient(_ramp),
                apply: g => Mutate(() => ZuiRampGradientBridge.ApplyGradient(_ramp, g)));
        }

        // ── the per-stop popover ─────────────────────────────────────────────────────────────────────────

        void OpenStopEditor(int i)
        {
            if (i < 0 || i >= _ramp.Count) return;
            ZuiPopover pop = null;
            pop = Z.Popover(_strip, panel =>
            {
                panel.style.flexDirection = FlexDirection.Row;
                panel.style.alignItems = Align.Center;
                string tip = $"Stop {i + 1} of {_ramp.Count}.";

                panel.Add(Z.Color(_ramp.GetColor(i),
                    tip + " Its colour — the alpha is the ramp's opacity at this position, not a separate key.",
                    c => Mutate(() => _ramp.SetColor(i, c)), 90f));

                // Bounded by the neighbours, so the stop can never jump past one and reorder the ramp under the
                // index this popover captured.
                float lo = i > 0 ? _ramp.GetPos(i - 1) : 0f;
                float hi = i + 1 < _ramp.Count ? _ramp.GetPos(i + 1) : 1f;
                if (hi < lo) { var swap = lo; lo = hi; hi = swap; }
                var pos = Z.MicroSlider("Pos", Mathf.Clamp(_ramp.GetPos(i), lo, hi), lo, hi,
                    tip + " Where it sits along the ramp, between its neighbours.",
                    v => Apply(() => _ramp.SetPos(i, v)), 120f, showValue: true, decimals: 3,
                    onBeforeMutate: BeginGesture);
                pos.style.marginLeft = 6f;
                pos.style.marginBottom = 0f;
                panel.Add(pos);

                var del = Z.Button("Remove stop", tip + " Delete it — the ramp blends straight between its "
                                                      + "neighbours afterwards.", () =>
                {
                    Mutate(() => _ramp.RemoveAt(i));
                    pop?.Close();
                });
                del.style.marginLeft = 6f;
                panel.Add(del);
            });
        }

        /// The painted strip + marker lane. Pointer logic lives here so the element's local coordinates ARE the
        /// ramp coordinates (t = x / width).
        sealed class Strip : VisualElement
        {
            const float StripH = 22f, LaneH = 10f, MarkerHalf = 5f, HitPad = 6f, DragSlop = 2f;
            readonly ZuiRampControl _o;
            int _drag = -1;
            bool _dragMoved;
            float _downX;

            public Strip(ZuiRampControl owner)
            {
                _o = owner;
                AddToClassList("zui-ramp__strip");
                style.minWidth = MinStripWidth;
                style.height = StripH + LaneH;
                style.flexGrow = 1f;
                style.flexShrink = 1f;
                generateVisualContent += OnGenerate;
                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
            }

            float X(float t) => Mathf.Clamp01(t) * contentRect.width;
            float T(float x) => Mathf.Clamp01(x / Mathf.Max(1f, contentRect.width));
            static bool InLane(float y) => y >= StripH - 3f;

            /// The stop whose marker sits nearest x, or -1 when none is within the grab pad. Ties go to the LATER
            /// marker, which is the one drawn on top.
            int MarkerAt(float x)
            {
                int best = -1; float bestD = HitPad;
                for (int i = 0; i < _o._ramp.Count; i++)
                {
                    float d = Mathf.Abs(X(_o._ramp.GetPos(i)) - x);
                    if (d <= bestD) { bestD = d; best = i; }
                }
                return best;
            }

            /// A stop may not cross its neighbours — that would reorder the ramp under every index the UI holds.
            float ClampBetweenNeighbours(int i, float t)
            {
                var r = _o._ramp;
                float lo = i > 0 ? r.GetPos(i - 1) : 0f;
                float hi = i + 1 < r.Count ? r.GetPos(i + 1) : 1f;
                return Mathf.Clamp(t, Mathf.Min(lo, hi), Mathf.Max(lo, hi));
            }

            void OnDown(PointerDownEvent e)
            {
                float x = e.localPosition.x, y = e.localPosition.y;

                // Right-click a marker removes it. (Anywhere else right-click is left alone, so a host context
                // menu still reaches the card.)
                if (e.button == 1)
                {
                    int hit = InLane(y) ? MarkerAt(x) : -1;
                    if (hit >= 0) { _o.Mutate(() => _o._ramp.RemoveAt(hit)); e.StopPropagation(); }
                    return;
                }
                if (e.button != 0) return;

                int m = InLane(y) ? MarkerAt(x) : -1;
                if (m >= 0)
                {
                    // Arm a drag but mutate NOTHING yet: a press that never moves is the "open its editor"
                    // gesture, and recording an Undo step for it would leave a no-op entry in the user's history.
                    _drag = m; _dragMoved = false; _downX = x;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }

                if (e.clickCount == 2)
                {
                    // Insert carrying the colour the ramp ALREADY evaluates there, so the picture does not change —
                    // the new stop is a handle on what was being drawn anyway. Then drag it straight away.
                    float t = T(x);
                    _o.BeginGesture();
                    int idx = -1;
                    _o.Apply(() => idx = _o._ramp.Insert(t, _o._ramp.Eval(t)));
                    _drag = idx; _dragMoved = true; _downX = x;
                    this.CapturePointer(e.pointerId);
                    e.StopPropagation();
                }
            }

            void OnMove(PointerMoveEvent e)
            {
                if (_drag < 0) return;
                float x = e.localPosition.x;
                if (!_dragMoved)
                {
                    if (Mathf.Abs(x - _downX) < DragSlop) return;   // still a click, not a drag
                    _dragMoved = true;
                    _o.BeginGesture();
                }
                int i = _drag;
                if (i < 0 || i >= _o._ramp.Count) return;
                float t = ClampBetweenNeighbours(i, T(x));
                _o.Apply(() => _o._ramp.SetPos(i, t));
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (_drag < 0) return;
                int i = _drag;
                bool moved = _dragMoved;
                _drag = -1; _dragMoved = false;
                this.ReleasePointer(e.pointerId);
                _o.EndGesture();
                MarkDirtyRepaint();
                if (!moved) _o.OpenStopEditor(i);
                e.StopPropagation();
            }

            void OnGenerate(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (r.width <= 1f) return;
                var p = mgc.painter2D;
                var ramp = _o._ramp;

                // Checker under the ramp so per-stop alpha reads; the same two greys Unity's colour fields use.
                const float cell = 6f;
                var dark = new Color(0.30f, 0.30f, 0.30f, 1f);
                var light = new Color(0.45f, 0.45f, 0.45f, 1f);
                for (float y = 0; y < StripH; y += cell)
                    for (float x = 0; x < r.width; x += cell)
                        Rect(p, x, y, Mathf.Min(cell, r.width - x), Mathf.Min(cell, StripH - y),
                            (((int)(x / cell) + (int)(y / cell)) & 1) == 0 ? dark : light);

                int n = ramp.Count;
                // An EMPTY ramp is legal and meaningful ("no colour at all"): show the bare checker rather than
                // inventing a stop. The always-present "+" beside the strip is the way in.
                if (n == 0) return;

                // One column per pixel, straight from the ramp's own Eval — this IS the renderer's ramp.
                float w = r.width;
                int cols = Mathf.Max(1, Mathf.CeilToInt(w));
                for (int c = 0; c < cols; c++)
                {
                    float x0 = c, x1 = Mathf.Min(c + 1f, w);
                    if (x1 <= x0) continue;
                    Rect(p, x0, 0f, x1 - x0, StripH, ramp.Eval((c + 0.5f) / w));
                }

                // A marker per stop, tinted with that stop's OWN colour so the lane is legible at a glance
                // (drawn opaque, or a low-alpha stop would leave an invisible handle).
                for (int i = 0; i < n; i++)
                {
                    float x = X(ramp.GetPos(i));
                    var c = ramp.GetColor(i);
                    bool hot = i == _drag;
                    p.fillColor = hot ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(c.r, c.g, c.b, 1f);
                    p.strokeColor = new Color(0f, 0f, 0f, 0.8f);
                    p.lineWidth = 1f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(x, StripH));
                    p.LineTo(new Vector2(x + MarkerHalf, StripH + LaneH - 1f));
                    p.LineTo(new Vector2(x - MarkerHalf, StripH + LaneH - 1f));
                    p.ClosePath();
                    p.Fill(); p.Stroke();
                }
            }

            static void Rect(Painter2D p, float x, float y, float w, float h, Color c)
            {
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y));
                p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h));
                p.ClosePath(); p.Fill();
            }
        }
    }
}

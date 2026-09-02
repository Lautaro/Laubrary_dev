// ZuiLanes — a multi-lane clock: N labelled bands on ONE shared, host-set ruler, with one playhead.
//
// The gap this fills: ZuiTimeline is by contract a single strip of CONSECUTIVE bands, so it can only ever
// express a CHAIN — the second band starts where the first one ended. A recipe whose parts overlap (a
// smash's sparks and its debris fire together, 0.00→0.50 alongside 0.15→0.95) cannot be drawn on it at all,
// and drawing it there makes an absolute "starts at 0.15 s" dial contradict its own picture. This control is
// the absolute answer: every lane is placed by its own START and END on the same ruler, so lanes that
// overlap are drawn overlapping, and a delay dial and the bar agree.
//
// Four behaviours are load-bearing rather than decorative; each one is a measured defect in something that
// did not have it (blueprint T-0123 §4/§7):
//   * the ruler's LENGTH comes from the host, never from the lanes — otherwise disabling one lane rescales
//     the ruler and every remaining band jumps sideways under the user;
//   * a disabled lane KEEPS its lane, drawn dim — removing it changes this surface's height and moves
//     whatever sits above it;
//   * the first and last tick numbers are pulled INSIDE the bar instead of centred on their tick, or
//     "0.00s" renders as ".00s" clipped against the left edge;
//   * a tick number that would land under the playhead's own readout is DROPPED, not drawn behind it.
//
// Two things this control deliberately does NOT do, because they belong to the host:
//   * it never owns the clock — the host sets the length and pushes the time in with SetTime (which does
//     NOT notify, so a play tick cannot feed itself back through the callback and stop its own playback);
//   * it never decides what a lane MEANS — a lane is just (label, start, end, colour, dim, tooltip), so
//     nothing Chunks-specific (or Pyre-specific) leaks in here.
//
// SetLanes/SetMarkers REPLACE the data on the live element; they never ask the host to rebuild it. That is
// what lets a host rebuild one card without the clock losing its playhead, its width, or its drag.
//
// Text is drawn with real Labels rather than painted, because Painter2D cannot draw text at all. They are
// absolutely positioned children — absolute on purpose: an absolutely-positioned child cannot resize its
// parent, so adding and moving them can never reflow the bar the user is aiming at. Painted colours are C#
// constants for the same reason ZuiTimeline's are: Painter2D cannot read a USS custom property, so only the
// ELEMENT-level look (bar fill, label colours) is in the shared sheet.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>One lane of a <see cref="ZuiLanes"/>: a band drawn from <see cref="Start"/> to
    /// <see cref="End"/> seconds on the shared ruler, named in the gutter. A lane whose owner is switched OFF
    /// is passed with <see cref="Dim"/> set rather than omitted — it keeps its row, drawn faint, so turning
    /// something off never changes the control's height.</summary>
    public readonly struct ZuiLane
    {
        /// Gutter text. A name, not a sentence — it is truncated with an ellipsis to the gutter's width.
        public readonly string Label;
        /// When this lane's band begins, in seconds on the host's ruler.
        public readonly float Start;
        /// When it ends. An END at or before START still paints a hairline band, so a zero-length lane reads
        /// as "present, instantaneous" rather than disappearing.
        public readonly float End;
        /// Draw faint: the lane exists but its owner is disabled. It still counts for nothing in the ruler's
        /// length — the HOST decides that — and it still occupies its row.
        public readonly bool Dim;
        /// The band's fill. Leave it at <c>default</c> (alpha 0) to take the control's own accent tone.
        public readonly Color Color;
        /// Shown while the pointer is over THIS band — the place to say what the lane is.
        public readonly string Tooltip;

        public ZuiLane(string label, float start, float end, bool dim = false, string tooltip = null)
            : this(label, start, end, default, dim, tooltip) { }

        public ZuiLane(string label, float start, float end, Color color, bool dim = false,
            string tooltip = null)
        {
            Label = label;
            Start = Mathf.Max(0f, start);
            End = Mathf.Max(Start, end);
            Dim = dim;
            Color = color;
            Tooltip = tooltip;
        }
    }

    /// <summary>A named instant on a <see cref="ZuiLanes"/> ruler — a cue, a hit frame, a beat. It draws as a
    /// line through every lane with a flag on the ruler, and its name takes precedence over the tick NUMBER
    /// it lands on, because a named moment says more than the second it happens at.</summary>
    public readonly struct ZuiLaneMarker
    {
        /// When it happens, in seconds on the host's ruler.
        public readonly float Time;
        /// Short text drawn on the ruler when it fits. A name, not a sentence.
        public readonly string Label;
        /// The flag's colour. Leave it at <c>default</c> (alpha 0) to take the control's own marker tone.
        public readonly Color Color;
        /// Shown while the pointer is over the marker's line.
        public readonly string Tooltip;

        public ZuiLaneMarker(float time, string label, string tooltip = null)
            : this(time, label, default, tooltip) { }

        public ZuiLaneMarker(float time, string label, Color color, string tooltip = null)
        {
            Time = Mathf.Max(0f, time);
            Label = label;
            Color = color;
            Tooltip = tooltip;
        }
    }

    /// <summary>A stack of <see cref="ZuiLane"/> bands over one shared ruler of <see cref="Length"/> seconds,
    /// with <see cref="ZuiLaneMarker"/> flags and a single draggable playhead. Click or drag anywhere to move
    /// the playhead. Reach for it via <c>Z.Lanes(...)</c>.</summary>
    public sealed class ZuiLanes : VisualElement
    {
        /// Fires on every USER-driven move of the playhead (click or drag), with the new time in SECONDS. It
        /// does NOT fire for <see cref="SetTime"/> — that is the whole point of that method.
        public Action<float> OnTimeChanged;

        const float LaneGap = 3f;
        const float RulerHeight = 14f;
        const float TickFontSize = 9f;
        const float MinWidth = 180f;
        // Reserved width for one ruler number ("0.00s" at 9pt, with air). Wider than the text so two numbers
        // are never printed touching; being a few points pessimistic drops one number, which is the intended
        // failure direction — see the drop rule in PlaceRuler.
        const float TickWidth = 44f;
        // The playhead's readout is bold and can read "10.5s", so it reserves a touch more than a tick.
        const float PlayWidth = 46f;
        // A band narrower than this cannot show its own edges apart; it is widened rather than vanishing.
        const float MinBandWidth = 2f;

        // Painted tones. Painter2D cannot read a USS variable, so these live here — the same compromise
        // ZuiTimeline makes. Element-level styling (the bar's fill, the gutter/tick text) is in ZuiToolkit.uss.
        static readonly Color TrackColor = new Color(0.14f, 0.15f, 0.18f, 1f);
        static readonly Color BandColor = new Color(0.35f, 0.63f, 1f, 1f);      // matches --zui-accent
        static readonly Color MarkerColor = new Color(1f, 0.85f, 0.3f, 0.85f);
        static readonly Color PlayheadCore = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color PlayheadEdge = new Color(0f, 0f, 0f, 0.75f);
        // A step from the 1/2/5 family, so the ruler never prints numbers closer together than they read.
        static readonly float[] Steps = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f };

        readonly VisualElement _body;      // gutter + bar, side by side
        readonly VisualElement _gutter;    // the lane names
        readonly VisualElement _bar;       // the painted lanes; its local x IS the time axis
        readonly VisualElement _ruler;     // the ruler row (a gutter-width pad + the numbers)
        readonly VisualElement _rulerLane; // the numbers themselves, aligned with _bar
        readonly Label _playLabel;         // the playhead's own readout, riding in the ruler

        readonly List<ZuiLane> _lanes = new List<ZuiLane>();
        readonly List<ZuiLaneMarker> _markers = new List<ZuiLaneMarker>();
        readonly List<Vector2> _spans = new List<Vector2>();   // x-ranges already taken on the ruler

        readonly float _laneHeight;
        readonly float _gutterWidth;

        float _length;      // what the host asked for; 0 = "derive it from the content"
        float _span = 1f;   // what the ruler actually measures — never 0, so nothing divides by it
        float _seconds;
        bool _dragging;
        float _laidOutWidth = -1f;

        /// The ruler's length in seconds — the host's number when it set one, otherwise the furthest lane end
        /// or marker (and finally 1, so an empty control still draws a ruler instead of nothing).
        public float Length => _span;

        /// How many lanes are drawn, disabled ones included.
        public int LaneCount => _lanes.Count;

        /// The playhead position in seconds, clamped into [0, Length]. Setting it NOTIFIES (UITK's `value`
        /// convention); use <see cref="SetTime"/> from a playback tick.
        public float Seconds
        {
            get => _seconds;
            set { if (Assign(value)) OnTimeChanged?.Invoke(_seconds); }
        }

        /// <summary>Move the playhead WITHOUT firing <see cref="OnTimeChanged"/> — what a host transport uses
        /// every frame, so a play tick that pushes the time in cannot loop back through the scrub callback and
        /// fight its own playback.</summary>
        public void SetTime(float seconds) => Assign(seconds);

        public ZuiLanes(float lengthSeconds, string tooltip, Action<float> onTimeChanged = null,
            float laneHeight = 16f, float gutterWidth = 96f)
        {
            OnTimeChanged = onTimeChanged;
            _laneHeight = Mathf.Max(6f, laneHeight);
            _gutterWidth = Mathf.Max(0f, gutterWidth);
            _length = Mathf.Max(0f, lengthSeconds);

            AddToClassList("zui-lanes");
            style.flexDirection = FlexDirection.Column;
            // flexGrow so it takes the WIDTH it is given in a row — but the height is capped in Recompute(),
            // because flex-grow is main-axis and this control's natural host is a COLUMN, where an uncapped
            // grow would eat every spare point and stretch a three-lane clock into a slab.
            style.flexGrow = 1f;
            style.flexShrink = 1f;
            style.minWidth = MinWidth;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _body = new VisualElement();
            _body.AddToClassList("zui-lanes__body");
            _body.style.flexDirection = FlexDirection.Row;
            _body.style.flexGrow = 0f;
            _body.style.flexShrink = 0f;
            Add(_body);

            _gutter = new VisualElement();
            _gutter.AddToClassList("zui-lanes__gutter");
            _gutter.style.flexDirection = FlexDirection.Column;
            _gutter.style.width = _gutterWidth;
            _gutter.style.flexGrow = 0f;
            _gutter.style.flexShrink = 0f;
            _gutter.style.overflow = Overflow.Hidden;
            _gutter.pickingMode = PickingMode.Ignore;   // the names are a legend, not a second hit target
            _body.Add(_gutter);

            _bar = new VisualElement();
            _bar.AddToClassList("zui-lanes__bar");
            _bar.style.flexGrow = 1f;
            _bar.style.flexShrink = 1f;
            _bar.style.overflow = Overflow.Hidden;
            _bar.generateVisualContent += Paint;
            _body.Add(_bar);

            _ruler = new VisualElement();
            _ruler.AddToClassList("zui-lanes__ruler");
            _ruler.style.flexDirection = FlexDirection.Row;
            _ruler.style.height = RulerHeight;
            _ruler.style.flexGrow = 0f;
            _ruler.style.flexShrink = 0f;
            _ruler.pickingMode = PickingMode.Ignore;
            Add(_ruler);

            // A pad exactly as wide as the gutter, so a number under x=0 sits under the BAR's zero and not
            // under the lane names.
            var pad = new VisualElement();
            pad.style.width = _gutterWidth;
            pad.style.flexGrow = 0f;
            pad.style.flexShrink = 0f;
            pad.pickingMode = PickingMode.Ignore;
            _ruler.Add(pad);

            _rulerLane = new VisualElement();
            _rulerLane.AddToClassList("zui-lanes__ruler-lane");
            _rulerLane.style.flexGrow = 1f;
            _rulerLane.style.flexShrink = 1f;
            _rulerLane.style.overflow = Overflow.Hidden;
            _rulerLane.pickingMode = PickingMode.Ignore;
            _ruler.Add(_rulerLane);

            _playLabel = new Label("0.00s") { pickingMode = PickingMode.Ignore };
            _playLabel.AddToClassList("zui-lanes__playhead-label");
            _playLabel.style.position = Position.Absolute;
            _playLabel.style.top = 0f;
            _playLabel.style.fontSize = TickFontSize;
            _playLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            _playLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _rulerLane.Add(_playLabel);

            // The gestures live on the ROOT, not on the bar, so a press anywhere in the control (the ruler
            // and the gutter included) scrubs — a ruler you cannot click reads as broken. x is always
            // resolved against the BAR, whatever element the pointer actually landed on.
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);

            Recompute();
        }

        // ── host-driven data ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Set the ruler's length in seconds. THE HOST OWNS THIS: computing it from the lanes would
        /// let a disabled lane rescale the ruler and slide every other band sideways. Pass 0 to fall back to
        /// the furthest lane end / marker.</summary>
        public void SetLength(float seconds)
        {
            _length = Mathf.Max(0f, seconds);
            Recompute();
        }

        /// <summary>Replace the lanes, in draw order, WITHOUT rebuilding the element — the playhead, the drag
        /// and the laid-out width all survive. Pass a disabled owner's lane with <c>Dim</c> set rather than
        /// dropping it, so the control keeps its height.</summary>
        public void SetLanes(IReadOnlyList<ZuiLane> lanes)
        {
            _lanes.Clear();
            if (lanes != null) for (int i = 0; i < lanes.Count; i++) _lanes.Add(lanes[i]);
            Recompute();
        }

        /// <inheritdoc cref="SetLanes(IReadOnlyList{ZuiLane})"/>
        public void SetLanes(params ZuiLane[] lanes) => SetLanes((IReadOnlyList<ZuiLane>)lanes);

        /// <summary>Replace the ruler's named instants, WITHOUT rebuilding the element.</summary>
        public void SetMarkers(IReadOnlyList<ZuiLaneMarker> markers)
        {
            _markers.Clear();
            if (markers != null) for (int i = 0; i < markers.Count; i++) _markers.Add(markers[i]);
            Recompute();
        }

        /// <inheritdoc cref="SetMarkers(IReadOnlyList{ZuiLaneMarker})"/>
        public void SetMarkers(params ZuiLaneMarker[] markers) => SetMarkers((IReadOnlyList<ZuiLaneMarker>)markers);

        // ── geometry ─────────────────────────────────────────────────────────────────────────────────────

        /// The ruler's measure, and the heights that follow from the lane count. Called for every data change;
        /// it never touches the element's identity, only its numbers.
        void Recompute()
        {
            float span = _length;
            if (span <= 0f)
            {
                for (int i = 0; i < _lanes.Count; i++) span = Mathf.Max(span, _lanes[i].End);
                for (int i = 0; i < _markers.Count; i++) span = Mathf.Max(span, _markers[i].Time);
            }
            _span = span > 0.0001f ? span : 1f;
            _seconds = Mathf.Clamp(_seconds, 0f, _span);

            float h = Mathf.Max(_laneHeight, _lanes.Count * (_laneHeight + LaneGap) - LaneGap);
            _body.style.height = h;
            _bar.style.height = h;
            _gutter.style.height = h;
            // Cap the height for the same reason ZuiTimeline does — see the flexGrow note in the constructor.
            style.maxHeight = h + RulerHeight + 2f;

            Relayout();
        }

        float BarWidth
        {
            get
            {
                float w = _bar.contentRect.width;
                return float.IsNaN(w) ? 0f : w;
            }
        }

        /// seconds → x. _span is never 0, so this never divides by zero even with no lanes at all.
        float X(float t) => Mathf.Clamp01(t / _span) * BarWidth;

        /// x → seconds.
        float T(float x) => Mathf.Clamp01(x / Mathf.Max(1f, BarWidth)) * _span;

        bool Assign(float s)
        {
            float v = Mathf.Clamp(s, 0f, _span);
            if (v == _seconds) return false;
            _seconds = v;
            PlaceRuler();          // the readout moved, so which numbers survive has changed
            _bar.MarkDirtyRepaint();
            return true;
        }

        // ── gestures (pointer-captured drag, the ZuiTimeline/ZuiRampControl idiom) ────────────────────────
        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            _dragging = true;
            this.CapturePointer(e.pointerId);
            Seconds = T(LocalX(e.position));
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!_dragging) return;
            Seconds = T(LocalX(e.position));
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            _dragging = false;
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        /// The pointer's x in the BAR's own space. Deliberately not `e.localPosition`: the event's target may
        /// be one of the absolutely-positioned band or marker overlays, whose local space starts at that
        /// band's own left edge — which would make a click near the end of the clock scrub to near its start.
        float LocalX(Vector2 panelPosition) => _bar.WorldToLocal(panelPosition).x;

        /// Every label position is a function of the bar's width, which is unknown until the panel lays out.
        /// The width GUARD is load-bearing, not an optimisation: GeometryChangedEvent bubbles, so the child
        /// labels this handler creates would re-enter it forever without it.
        void OnGeometry(GeometryChangedEvent _)
        {
            float w = BarWidth;
            if (Mathf.Abs(w - _laidOutWidth) < 0.5f) return;
            _laidOutWidth = w;
            Relayout();
        }

        void Relayout()
        {
            PlaceGutter();
            PlaceOverlays();
            PlaceRuler();
            _bar.MarkDirtyRepaint();
        }

        // ── labels (Painter2D cannot draw text, so these are real elements) ──────────────────────────────

        /// One name per lane, in the gutter. Truncated with an ellipsis rather than wrapping: a wrapped name
        /// would make its row taller than the band it belongs to and the two would stop lining up.
        void PlaceGutter()
        {
            _gutter.Clear();
            if (_gutterWidth <= 1f) return;

            for (int i = 0; i < _lanes.Count; i++)
            {
                var lane = _lanes[i];
                var l = new Label(lane.Label ?? string.Empty) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("zui-lanes__gutter-label");
                l.style.height = _laneHeight;
                l.style.marginBottom = i < _lanes.Count - 1 ? LaneGap : 0f;
                l.style.fontSize = TickFontSize;
                l.style.unityTextAlign = TextAnchor.MiddleLeft;
                l.style.overflow = Overflow.Hidden;
                l.style.whiteSpace = WhiteSpace.NoWrap;
                l.style.textOverflow = TextOverflow.Ellipsis;
                if (lane.Dim) l.style.opacity = 0.45f;
                _gutter.Add(l);
            }
        }

        /// Transparent hit overlays: one per band and one per marker, carrying the tooltips. They are pickable
        /// on purpose — it is how a hover knows which lane it is over — and scrubbing still works because the
        /// gesture handlers sit on the root and the pointer event bubbles up to them.
        void PlaceOverlays()
        {
            _bar.Clear();
            float w = BarWidth;
            if (w <= 1f) return;

            for (int i = 0; i < _lanes.Count; i++)
            {
                var lane = _lanes[i];
                float x0 = X(lane.Start);
                float bw = Mathf.Max(MinBandWidth, X(lane.End) - x0);
                var hit = new VisualElement();
                hit.style.position = Position.Absolute;
                hit.style.left = x0;
                hit.style.top = i * (_laneHeight + LaneGap);
                hit.style.width = bw;
                hit.style.height = _laneHeight;
                hit.tooltip = LaneTooltip(lane);
                _bar.Add(hit);
            }

            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                var hit = new VisualElement();
                hit.style.position = Position.Absolute;
                hit.style.left = X(m.Time) - 4f;
                hit.style.top = 0f;
                hit.style.bottom = 0f;
                hit.style.width = 9f;
                hit.tooltip = MarkerTooltip(m);
                _bar.Add(hit);   // after the bands, so a marker's own tooltip wins where they overlap
            }
        }

        string LaneTooltip(ZuiLane lane)
        {
            string when = $"{Format(lane.Start)} → {Format(lane.End)}";
            bool hasName = !string.IsNullOrEmpty(lane.Label);
            string head = hasName ? $"{lane.Label} — {when}" : when;
            if (lane.Dim) head += " (off)";
            return string.IsNullOrEmpty(lane.Tooltip) ? head : $"{head} — {lane.Tooltip}";
        }

        string MarkerTooltip(ZuiLaneMarker m)
        {
            bool hasName = !string.IsNullOrEmpty(m.Label);
            string head = hasName ? $"{m.Label} — {Format(m.Time)}" : Format(m.Time);
            return string.IsNullOrEmpty(m.Tooltip) ? head : $"{head} — {m.Tooltip}";
        }

        /// <summary>The ruler, rebuilt in PRECEDENCE order, each entry dropped outright if the space it wants
        /// is already taken: the playhead's own readout (the number actually being read) first, then the two
        /// edge numbers that say how long the clock is, then the named markers, and only then the interior
        /// tick numbers. A number printed on top of another number is worse than one number.</summary>
        void PlaceRuler()
        {
            _rulerLane.Clear();
            _rulerLane.Add(_playLabel);   // Clear() took it with the rest; it is persistent, so put it back
            _spans.Clear();

            float w = BarWidth;
            if (w <= 1f) { _playLabel.visible = false; return; }

            _playLabel.visible = true;
            _playLabel.text = Format(_seconds);
            float playLeft = Clamp(X(_seconds), PlayWidth, w);
            _playLabel.style.left = playLeft;
            _playLabel.style.width = PlayWidth;
            _spans.Add(new Vector2(playLeft, playLeft + PlayWidth));

            AddTick(0f, w, null, default(Color));
            AddTick(_span, w, null, default(Color));

            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                if (string.IsNullOrEmpty(m.Label)) continue;
                AddTick(m.Time, w, m.Label, m.Color.a > 0f ? m.Color : MarkerColor);
            }

            float step = NiceStep(_span, w);
            int n = Mathf.FloorToInt(_span / step + 0.0001f);
            for (int i = 1; i <= n; i++) AddTick(i * step, w, null, default(Color));
        }

        /// One ruler entry, or nothing at all if it would sit on something already placed.
        void AddTick(float time, float barW, string text, Color color)
        {
            float left = Clamp(X(time), TickWidth, barW);
            float right = left + TickWidth;
            for (int i = 0; i < _spans.Count; i++)
                if (right > _spans[i].x && left < _spans[i].y) return;   // taken — drop this one

            var l = new Label(text ?? Format(time)) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(text == null ? "zui-lanes__tick" : "zui-lanes__marker-label");
            l.style.position = Position.Absolute;
            l.style.left = left;
            l.style.top = 0f;
            l.style.width = TickWidth;
            l.style.fontSize = TickFontSize;
            l.style.unityTextAlign = TextAnchor.UpperCenter;
            l.style.overflow = Overflow.Hidden;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.textOverflow = TextOverflow.Ellipsis;
            if (color.a > 0f) l.style.color = color;
            _rulerLane.Add(l);
            _spans.Add(new Vector2(left, right));
        }

        /// A label of `width` centred on x, PULLED INSIDE the bar at both ends — centring the first and last
        /// numbers on their own ticks renders "0.00s" as ".00s" clipped against the edge.
        static float Clamp(float x, float width, float barW)
            => Mathf.Clamp(x - width * 0.5f, 0f, Mathf.Max(0f, barW - width));

        /// A step from the 1/2/5 family landing roughly one number per 70pt, so the ruler never prints
        /// numbers closer together than they can be read.
        static float NiceStep(float span, float width)
        {
            float raw = span / Mathf.Max(2f, width / 70f);
            for (int i = 0; i < Steps.Length; i++) if (raw <= Steps[i]) return Steps[i];
            return Mathf.Ceil(raw / 60f) * 60f;
        }

        static string Format(float seconds) => seconds >= 10f ? $"{seconds:0.#}s" : $"{seconds:0.00}s";

        // ── painting ─────────────────────────────────────────────────────────────────────────────────────
        void Paint(MeshGenerationContext mgc)
        {
            var r = _bar.contentRect;
            if (r.width <= 1f || r.height <= 1f) return;
            var p = mgc.painter2D;

            for (int i = 0; i < _lanes.Count; i++)
            {
                var lane = _lanes[i];
                float top = i * (_laneHeight + LaneGap);
                Fill(p, 0f, top, r.width, _laneHeight, TrackColor);

                float x0 = X(lane.Start);
                float bw = Mathf.Max(MinBandWidth, X(lane.End) - x0);
                var c = lane.Color.a > 0f ? lane.Color : BandColor;
                // A disabled lane keeps its place and its hue and loses its weight — dropping the lane would
                // change this control's height and move whatever sits above it.
                if (lane.Dim) c = new Color(c.r, c.g, c.b, 0.22f);
                Fill(p, x0, top, bw, _laneHeight, c);
            }

            // Markers run through every lane so the moment reads across the whole stack, not just one band.
            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                float mx = Mathf.Clamp(X(m.Time), 0f, r.width - 1f);
                var c = m.Color.a > 0f ? m.Color : MarkerColor;
                Fill(p, mx - 0.5f, 0f, 1f, r.height, c);
                Flag(p, mx, c);
            }

            // The playhead LAST, over everything. A dark 3pt stroke with a 1pt light core: a single light line
            // vanishes on a light band and a single dark one vanishes on a dark band, and a stack of lanes is
            // guaranteed to have both.
            float px = Mathf.Clamp(X(_seconds), 0f, r.width - 1f);
            Fill(p, px - 1.5f, 0f, 3f, r.height, PlayheadEdge);
            Fill(p, px - 0.5f, 0f, 1f, r.height, PlayheadCore);
            Flag(p, px, PlayheadCore);
        }

        /// The downward triangle at the top of a line — what tells the eye the line can be grabbed (playhead)
        /// or that it names a moment (marker).
        static void Flag(Painter2D p, float x, Color color)
        {
            const float hw = 4.5f, hh = 6f;
            p.fillColor = color;
            p.strokeColor = new Color(0f, 0f, 0f, 0.8f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(x - hw, 0f));
            p.LineTo(new Vector2(x + hw, 0f));
            p.LineTo(new Vector2(x, hh));
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }

        static void Fill(Painter2D p, float x, float y, float w, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h));
            p.ClosePath(); p.Fill();
        }
    }
}

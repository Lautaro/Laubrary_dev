// ZuiTimeline — a scrub bar for a LOOP made of consecutive, labelled REGIONS.
//
// The gap this fills: ZUI could scrub a normalized 0→1 value (a MicroSlider) and it could edit a curve
// (ZuiEnvelope), but it had nothing that says "this loop is 0.6 s of effect, then a 0.5 s gap with nothing
// playing — and you are HERE." A tool that owns a timebase had to spell that out as unrelated numeric
// dials sitting in different sections, which is exactly the confusion this control exists to end:
// the bar IS the explanation, because the bands are drawn to scale and the playhead is on the same ruler.
//
// (Do not confuse this with ZuiScrub — a false friend by name. ZuiScrub makes a NUMERIC FIELD draggable;
// it has nothing to do with a timeline.)
//
// Modelled deliberately on ZuiRampControl's Strip: a Painter2D-painted background whose element-local
// coordinates ARE the value's coordinates, click-to-position, pointer-captured drag. Same idiom, so a
// reader of one already knows the other.
//
// Two things this control deliberately does NOT do, because they belong to the host:
//   * it never owns the seconds — the host holds the clock and pushes it in (SetSecondsWithoutNotify
//     during playback, so a play tick cannot feed itself back through OnChanged and stop its own playback);
//   * it never decides what a region MEANS — a segment is just (name, seconds, colour, tooltip), so
//     nothing SpriteFx-specific (or Pyre-specific, or Chunks-specific) leaks in here.
//
// Text is drawn with real Labels rather than painted, because Painter2D cannot draw text at all. They are
// absolutely positioned children — absolute on purpose: an absolutely-positioned child cannot resize its
// parent, so adding and moving them can never reflow the bar the user is aiming at.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>One band of a <see cref="ZuiTimeline"/>: a stretch of the loop with a name, a length in
    /// seconds, a colour and its own tooltip. A ZERO-length segment is legal and common (an unconfigured
    /// gap) — it simply paints nothing, so a host never has to conditionally omit it.</summary>
    public readonly struct ZuiTimelineSegment
    {
        /// Short legend text drawn INSIDE the band when it is wide enough to hold it. A name, not a sentence.
        public readonly string Name;
        /// How long this band lasts. Clamped to >= 0; it takes a share of the bar's width proportional to the total.
        public readonly float Seconds;
        /// The band's fill. Pick colours that read apart at a glance — the band IS the legend.
        public readonly Color Color;
        /// Shown while the pointer is over THIS band — the place to explain what the region means.
        public readonly string Tooltip;

        public ZuiTimelineSegment(string name, float seconds, Color color, string tooltip = null)
        {
            Name = name;
            Seconds = Mathf.Max(0f, seconds);
            Color = color;
            Tooltip = tooltip;
        }
    }

    /// <summary>A horizontal scrub bar over a loop of <see cref="Total"/> seconds, split into consecutive
    /// coloured <see cref="ZuiTimelineSegment"/> bands, with a playhead, time ticks and numeric labels.
    /// Click anywhere to jump the playhead there; drag to scrub. Reach for it via <c>Z.Timeline(...)</c>.</summary>
    public sealed class ZuiTimeline : VisualElement
    {
        /// Fires on every user-driven change (click or drag), with the new position in SECONDS. It does NOT
        /// fire for <see cref="SetSecondsWithoutNotify"/> — that is the whole point of that method.
        public Action<float> OnChanged;

        // The tick lane under the bar. Small, but PERMANENTLY reserved: a ruler whose numbers appear and
        // disappear would move the bar itself, and the bar is the thing being aimed at.
        const float LaneHeight = 13f;
        const float TickFontSize = 9f;
        const float MinBarWidth = 160f;
        // Below this band width there is no room for a legible name, so the name is dropped rather than
        // clipped mid-word — the band's colour and its tooltip still identify it.
        const float MinBandForName = 34f;
        // Rough advance width of a character at TickFontSize. Used only to keep tick labels from OVERLAPPING;
        // being a few pixels pessimistic drops one label, which is the intended failure direction anyway.
        const float CharWidth = 5.4f;

        readonly VisualElement _bar;    // the painted band strip; its local x IS the time axis
        readonly VisualElement _lane;   // the tick/number lane beneath it
        readonly Label _playLabel;      // the playhead's own numeric readout, riding in the lane
        readonly List<Tick> _ticks = new List<Tick>();
        readonly string _baseTooltip;

        ZuiTimelineSegment[] _segments = Array.Empty<ZuiTimelineSegment>();
        float _total;
        float _seconds;
        bool _dragging;
        float _laidOutWidth = -1f;

        struct Tick { public Label El; public float Left, Right; }

        /// The loop's whole length — the sum of every segment's seconds. Zero until segments are set.
        public float Total => _total;

        /// The playhead position in seconds, clamped into [0, Total]. Setting it NOTIFIES (UITK's `value`
        /// convention); use <see cref="SetSecondsWithoutNotify"/> from a playback tick.
        public float Seconds
        {
            get => _seconds;
            set { if (Assign(value)) OnChanged?.Invoke(_seconds); }
        }

        /// Move the playhead without firing OnChanged — what a host's own clock uses, so a play tick that
        /// pushes the time in cannot loop back through the scrub callback.
        public void SetSecondsWithoutNotify(float s) => Assign(s);

        public ZuiTimeline(float seconds, string tooltip, Action<float> onChanged = null, float height = 22f)
        {
            _baseTooltip = tooltip;
            OnChanged = onChanged;

            AddToClassList("zui-timeline");
            style.flexDirection = FlexDirection.Column;
            // flexGrow so it takes the WIDTH it is given in a row — but capped in height, because flex-grow
            // is main-axis, and this control's natural host is a COLUMN, where an uncapped grow would eat any
            // spare vertical space and stretch a 22pt bar into a slab.
            style.flexGrow = 1f;
            style.flexShrink = 1f;
            style.minWidth = MinBarWidth;
            style.maxHeight = height + LaneHeight + 4f;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _bar = new VisualElement();
            _bar.AddToClassList("zui-timeline__bar");
            _bar.style.height = height;
            _bar.style.flexGrow = 0f;
            _bar.style.flexShrink = 0f;
            _bar.generateVisualContent += Paint;
            Add(_bar);

            _lane = new VisualElement();
            _lane.AddToClassList("zui-timeline__lane");
            _lane.style.height = LaneHeight;
            _lane.style.flexGrow = 0f;
            _lane.style.flexShrink = 0f;
            _lane.pickingMode = PickingMode.Ignore;   // the numbers are a ruler, not a second hit target
            Add(_lane);

            _playLabel = new Label("0.00s") { pickingMode = PickingMode.Ignore };
            _playLabel.style.position = Position.Absolute;
            _playLabel.style.top = 0f;
            _playLabel.style.fontSize = TickFontSize;
            _playLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            _playLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _playLabel.style.color = new Color(1f, 1f, 1f, 0.95f);
            _lane.Add(_playLabel);

            _seconds = Mathf.Max(0f, seconds);

            // The gestures live on the ROOT, not on the bar, so a press anywhere in the control (the tick
            // lane included) scrubs — a ruler you cannot click reads as broken. x is always resolved against
            // the BAR, whatever element the pointer actually landed on.
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);
        }

        /// <summary>Replace the bands. The total (and therefore the ruler) is derived from them, and the
        /// playhead is pulled back inside the new total if it fell outside it.</summary>
        public void SetSegments(params ZuiTimelineSegment[] segments)
        {
            _segments = segments ?? Array.Empty<ZuiTimelineSegment>();
            _total = 0f;
            for (int i = 0; i < _segments.Length; i++) _total += Mathf.Max(0f, _segments[i].Seconds);
            if (_seconds > _total) _seconds = _total;
            Relayout();
        }

        // ── position plumbing ────────────────────────────────────────────────────────────────────────────
        bool Assign(float s)
        {
            float v = _total > 0f ? Mathf.Clamp(s, 0f, _total) : 0f;
            if (v == _seconds) return false;
            _seconds = v;
            PlacePlayhead();
            _bar.MarkDirtyRepaint();
            return true;
        }

        float BarWidth
        {
            get
            {
                float w = _bar.contentRect.width;
                return float.IsNaN(w) ? 0f : w;
            }
        }

        /// seconds → x. Guarded against a zero total, which is a real state (a host whose every band is
        /// still 0) and must never divide.
        float X(float t) => _total > 0f ? Mathf.Clamp01(t / _total) * BarWidth : 0f;

        /// x → seconds.
        float T(float x) => _total > 0f ? Mathf.Clamp01(x / Mathf.Max(1f, BarWidth)) * _total : 0f;

        // ── gestures (pointer-captured drag, the ZuiRampControl.Strip idiom) ─────────────────────────────
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
        /// be one of the per-band tooltip overlays, whose local space starts at that band's own left edge —
        /// which would make a click near the end of the loop scrub to somewhere near its beginning.
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

        // ── labels (Painter2D cannot draw text, so these are real elements) ──────────────────────────────
        void Relayout()
        {
            PlaceBands();
            BuildTicks();
            PlacePlayhead();
            _bar.MarkDirtyRepaint();
        }

        /// One transparent overlay per band carrying that band's TOOLTIP, plus its name as a legend when the
        /// band is wide enough. The overlay is pickable on purpose — it is how a hover knows which region it
        /// is over — and the scrub still works because the gesture handlers sit on the root and the pointer
        /// event bubbles up to them.
        void PlaceBands()
        {
            _bar.Clear();
            if (BarWidth <= 1f || _total <= 0f) return;

            float t = 0f;
            for (int i = 0; i < _segments.Length; i++)
            {
                var seg = _segments[i];
                if (seg.Seconds <= 0f) continue;
                float x0 = X(t), x1 = X(t + seg.Seconds);
                t += seg.Seconds;
                float bw = x1 - x0;
                if (bw <= 0f) continue;

                var hit = new VisualElement();
                hit.style.position = Position.Absolute;
                hit.style.left = x0;
                hit.style.top = 0f;
                hit.style.bottom = 0f;
                hit.style.width = bw;
                hit.style.justifyContent = Justify.Center;
                hit.style.alignItems = Align.Center;
                hit.style.overflow = Overflow.Hidden;
                hit.tooltip = BandTooltip(seg);

                if (!string.IsNullOrEmpty(seg.Name) && bw >= MinBandForName)
                {
                    var l = new Label(seg.Name) { pickingMode = PickingMode.Ignore };
                    l.style.fontSize = TickFontSize + 1f;
                    l.style.color = ReadableOn(seg.Color);
                    l.style.unityTextAlign = TextAnchor.MiddleCenter;
                    hit.Add(l);
                }
                _bar.Add(hit);
            }
        }

        string BandTooltip(ZuiTimelineSegment seg)
        {
            bool hasName = !string.IsNullOrEmpty(seg.Name);
            bool hasTip = !string.IsNullOrEmpty(seg.Tooltip);
            if (hasName && hasTip) return $"{seg.Name} ({seg.Seconds:0.###} s) — {seg.Tooltip}";
            if (hasTip) return seg.Tooltip;
            if (hasName) return $"{seg.Name} — {seg.Seconds:0.###} s";
            return _baseTooltip;
        }

        /// The static half of the ruler: 0 at the left, the total at the right, and every band boundary in
        /// between. A label that would land on one already placed is DROPPED rather than drawn over it —
        /// two overlapping numbers are less readable than one number.
        void BuildTicks()
        {
            _lane.Clear();
            _ticks.Clear();
            _lane.Add(_playLabel);   // Clear() took it with the rest; it is persistent, so put it straight back

            float w = BarWidth;
            if (w <= 1f) return;

            if (_total <= 0f) { AddTick(w, 0f, "0.00s"); return; }

            AddTick(w, 0f, Format(0f));
            AddTick(w, w, Format(_total));
            float t = 0f;
            for (int i = 0; i < _segments.Length - 1; i++)
            {
                t += Mathf.Max(0f, _segments[i].Seconds);
                if (t <= 0f || t >= _total) continue;   // a zero-length band draws no boundary of its own
                AddTick(w, X(t), Format(t));
            }
        }

        void AddTick(float barW, float x, string text)
        {
            Span(text, x, barW, out float left, out float right);
            for (int i = 0; i < _ticks.Count; i++)
                if (right > _ticks[i].Left && left < _ticks[i].Right) return;   // would overlap — drop it

            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.position = Position.Absolute;
            l.style.left = left;
            l.style.top = 0f;
            l.style.width = right - left;
            l.style.fontSize = TickFontSize;
            l.style.unityTextAlign = TextAnchor.UpperCenter;
            l.style.color = new Color(1f, 1f, 1f, 0.42f);
            _lane.Add(l);
            _ticks.Add(new Tick { El = l, Left = left, Right = right });
        }

        /// The playhead's readout is the number actually being read, so it WINS every collision: any static
        /// tick it covers is hidden (visibility, never display — hiding must not move the surviving ones).
        void PlacePlayhead()
        {
            float w = BarWidth;
            if (w <= 1f || _total <= 0f) { _playLabel.visible = false; return; }
            _playLabel.visible = true;
            _playLabel.text = Format(_seconds);
            Span(_playLabel.text, X(_seconds), w, out float left, out float right);
            _playLabel.style.left = left;
            _playLabel.style.width = right - left;
            for (int i = 0; i < _ticks.Count; i++)
                _ticks[i].El.visible = !(right > _ticks[i].Left && left < _ticks[i].Right);
        }

        /// Where a label of this text, centred on x, ends up once clamped inside the bar.
        static void Span(string text, float x, float barW, out float left, out float right)
        {
            float tw = text.Length * CharWidth + 4f;
            left = Mathf.Clamp(x - tw * 0.5f, 0f, Mathf.Max(0f, barW - tw));
            right = left + tw;
        }

        static string Format(float seconds) => seconds >= 10f ? $"{seconds:0.#}s" : $"{seconds:0.00}s";

        /// Text drawn ON a band has to survive whatever colour the host picked for it, light or dark.
        static Color ReadableOn(Color band)
        {
            float lum = band.r * 0.299f + band.g * 0.587f + band.b * 0.114f;
            return lum > 0.55f ? new Color(0f, 0f, 0f, 0.75f) : new Color(1f, 1f, 1f, 0.72f);
        }

        // ── painting ─────────────────────────────────────────────────────────────────────────────────────
        void Paint(MeshGenerationContext mgc)
        {
            var r = _bar.contentRect;
            if (r.width <= 1f || r.height <= 1f) return;
            var p = mgc.painter2D;

            // No bands yet (or every band is zero-length): draw the empty track rather than nothing, so the
            // control still reads as a timeline waiting for a length instead of as a rendering failure.
            if (_total <= 0f)
            {
                Fill(p, 0f, 0f, r.width, r.height, new Color(0f, 0f, 0f, 0.35f));
                return;
            }

            float t = 0f;
            for (int i = 0; i < _segments.Length; i++)
            {
                var seg = _segments[i];
                if (seg.Seconds <= 0f) continue;
                float x0 = X(t), x1 = X(t + seg.Seconds);
                t += seg.Seconds;
                if (x1 <= x0) continue;
                Fill(p, x0, 0f, x1 - x0, r.height, seg.Color);
                // A hairline at the boundary so two similarly-toned bands still read as two.
                if (x0 > 0.5f) Fill(p, x0 - 0.5f, 0f, 1f, r.height, new Color(0f, 0f, 0f, 0.55f));
            }

            // Ticks rising from the bottom edge at 0 and at the end — the bar's half of the ruler, so the
            // numbers below have something to point at.
            float th = Mathf.Min(5f, r.height * 0.3f);
            Fill(p, 0f, r.height - th, 1f, th, new Color(1f, 1f, 1f, 0.35f));
            Fill(p, r.width - 1f, r.height - th, 1f, th, new Color(1f, 1f, 1f, 0.35f));

            // The playhead LAST, over everything. Drawn as a dark 3px stroke with a 1px light core: a single
            // light line vanishes on a light band and a single dark one vanishes on a dark band, and this bar
            // is guaranteed to have both.
            float px = Mathf.Clamp(X(_seconds), 0f, r.width - 1f);
            Fill(p, px - 1.5f, 0f, 3f, r.height, new Color(0f, 0f, 0f, 0.75f));
            Fill(p, px - 0.5f, 0f, 1f, r.height, new Color(1f, 1f, 1f, 0.95f));

            // A grab handle at the top, so the playhead advertises that it can be dragged.
            const float hw = 4.5f, hh = 6f;
            p.fillColor = new Color(1f, 1f, 1f, 0.95f);
            p.strokeColor = new Color(0f, 0f, 0f, 0.8f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(px - hw, 0f));
            p.LineTo(new Vector2(px + hw, 0f));
            p.LineTo(new Vector2(px, hh));
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

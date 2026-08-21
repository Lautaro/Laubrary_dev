// ZuiBandsControl — the bespoke control for a HARD band table (IZuiBands): ONE row, a "Bands" count MicroSlider
// beside a stepped strip. The strip is painted from the table's own Eval (alpha over a checker), so what it shows
// IS the quantised result the renderer paints — there is no separate preview to drift from it. Under the strip a
// marker per threshold: drag one to move that band's start (the table clamps it between its neighbours); click a
// band to open a popover with its colour (the sanctioned ColorField island) and its threshold as a MicroSlider.
// Changing the count resamples in place through IZuiBands.SetCount, so every edit is live.
//
// Undo contract, same as every ZUI control that mutates in place: OnBeforeMutate fires once per gesture (a drag,
// a slider change, a colour pick) BEFORE the first mutation, OnChanged after every mutation.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiBandsControl : VisualElement
    {
        public Action OnBeforeMutate;
        public Action OnChanged;

        readonly IZuiBands _bands;
        readonly ZuiMicroSlider _count;
        readonly Strip _strip;

        public const float CountWidth = 110f;
        public const float StripWidth = 220f;

        public ZuiBandsControl(IZuiBands bands, string tooltip)
        {
            _bands = bands;
            AddToClassList("zui-bands");
            AddToClassList("zui-row");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.FlexStart;
            style.flexShrink = 0;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _count = Z.MicroSlider("Bands", Mathf.Max(bands.MinCount, bands.Count), bands.MinCount, bands.MaxCount,
                "How many bands. Raising it splits the widest band in two (same colour, so recolour the new one); lowering it removes the narrowest band.",
                v => Mutate(() => _bands.SetCount(Mathf.RoundToInt(v))), CountWidth, showValue: true, decimals: 0,
                onBeforeMutate: () => OnBeforeMutate?.Invoke());
            Add(_count);

            _strip = new Strip(this);
            _strip.tooltip = "The bands as the renderer paints them. Drag a marker to move where a band starts; click a band to change its colour or type its threshold.";
            Add(_strip);
        }

        /// Re-read the table (an undo, an external edit) — the count slider and the strip redraw from it.
        public void Refresh()
        {
            _count.value = _bands.Count;
            _strip.MarkDirtyRepaint();
        }

        // One gesture = one Undo record; a drag calls BeginGesture once then Apply per move.
        bool _gestureOpen;
        void BeginGesture() { if (_gestureOpen) return; _gestureOpen = true; OnBeforeMutate?.Invoke(); }
        void EndGesture() => _gestureOpen = false;
        void Apply(Action edit) { edit(); _count.value = _bands.Count; _strip.MarkDirtyRepaint(); OnChanged?.Invoke(); }
        void Mutate(Action edit) { BeginGesture(); Apply(edit); EndGesture(); }

        void OpenBandEditor(int i)
        {
            Z.Popover(_strip, panel =>
            {
                panel.style.flexDirection = FlexDirection.Row;
                panel.style.alignItems = Align.Center;
                var tip = $"Band {i + 1} of {_bands.Count}.";
                var col = Z.Color(_bands.GetColor(i), "The band's colour. Its alpha is the band's opacity.",
                    c => Mutate(() => _bands.SetColor(i, c)), 90f);
                panel.Add(col);
                // Band 0 starts at 0 unless the table has a floor under it — then (and only then) its start is a dial.
                if (i > 0 || _bands.FirstIsFloor)
                {
                    float lo = i > 0 ? _bands.GetThreshold(i - 1) : 0f;
                    float hi = i + 1 < _bands.Count ? _bands.GetThreshold(i + 1) : 1f;
                    var thr = Z.MicroSlider("From", _bands.GetThreshold(i), lo, hi,
                        tip + " Where the band starts (its threshold in 0..1), between its neighbours.",
                        v => Apply(() => _bands.SetThreshold(i, v)), 120f, showValue: true, decimals: 3,
                        onBeforeMutate: BeginGesture);
                    thr.style.marginLeft = 6; thr.style.marginBottom = 0;
                    panel.Add(thr);
                }
                else panel.tooltip = tip + " The first band always starts at 0.";
            });
        }

        /// The painted strip + marker lane. Pointer logic lives here so the strip's local coordinates are the
        /// band coordinates (t = x / width).
        sealed class Strip : VisualElement
        {
            const float BandH = 18f, LaneH = 9f, MarkerHalf = 4f;
            readonly ZuiBandsControl _o;
            int _drag = -1;

            public Strip(ZuiBandsControl owner)
            {
                _o = owner;
                AddToClassList("zui-bands__strip");
                style.width = StripWidth; style.height = BandH + LaneH;
                style.flexShrink = 0; style.marginLeft = 6;
                generateVisualContent += OnGenerate;
                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
            }

            float X(float t) => Mathf.Clamp01(t) * contentRect.width;
            float T(float x) => Mathf.Clamp01(x / Mathf.Max(1f, contentRect.width));
            bool Draggable(int i) => i > 0 || _o._bands.FirstIsFloor;

            int MarkerAt(float x)
            {
                int best = -1; float bestD = MarkerHalf + 2f;
                for (int i = 0; i < _o._bands.Count; i++)
                {
                    if (!Draggable(i)) continue;
                    float d = Mathf.Abs(X(_o._bands.GetThreshold(i)) - x);
                    if (d <= bestD) { bestD = d; best = i; }   // ties go to the later marker (drawn on top)
                }
                return best;
            }

            int BandAt(float x)
            {
                float t = T(x); int i = 0;
                var b = _o._bands;
                while (i + 1 < b.Count && t >= b.GetThreshold(i + 1)) i++;
                return i;
            }

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0 || _o._bands.Count == 0) return;
                float x = e.localPosition.x, y = e.localPosition.y;
                int m = y >= BandH ? MarkerAt(x) : -1;
                if (m >= 0)
                {
                    _drag = m;
                    this.CapturePointer(e.pointerId);
                    _o.BeginGesture();
                    _o.Apply(() => _o._bands.SetThreshold(m, T(x)));
                }
                else _o.OpenBandEditor(BandAt(x));
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (_drag < 0) return;
                int i = _drag; float t = T(e.localPosition.x);
                _o.Apply(() => _o._bands.SetThreshold(i, t));
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (_drag < 0) return;
                _drag = -1;
                this.ReleasePointer(e.pointerId);
                _o.EndGesture();
                e.StopPropagation();
            }

            void OnGenerate(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (r.width <= 1f) return;
                var p = mgc.painter2D;
                var b = _o._bands;
                // checker under the bands so alpha reads; the same two greys Unity's colour fields use
                const float cell = 6f;
                var dark = new Color(0.30f, 0.30f, 0.30f, 1f); var light = new Color(0.45f, 0.45f, 0.45f, 1f);
                for (float y = 0; y < BandH; y += cell)
                    for (float x = 0; x < r.width; x += cell)
                        Rect(p, x, y, Mathf.Min(cell, r.width - x), Mathf.Min(cell, BandH - y),
                            (((int)(x / cell) + (int)(y / cell)) & 1) == 0 ? dark : light);
                int n = b.Count;
                if (n == 0) { Rect(p, 0, 0, r.width, BandH, new Color(1f, 1f, 1f, 0.5f)); return; }
                // the bands: band i from its threshold to the next (band 0 from 0 unless a floor sits under it)
                for (int i = 0; i < n; i++)
                {
                    float x0 = i == 0 && !b.FirstIsFloor ? 0f : X(b.GetThreshold(i));
                    float x1 = i + 1 < n ? X(b.GetThreshold(i + 1)) : r.width;
                    if (x1 <= x0) continue;
                    Rect(p, x0, 0, x1 - x0, BandH, b.GetColor(i));
                }
                // the markers: a small triangle under each draggable threshold, the dragged one filled brighter
                for (int i = 0; i < n; i++)
                {
                    if (!Draggable(i)) continue;
                    float x = X(b.GetThreshold(i));
                    bool hot = i == _drag;
                    p.fillColor = hot ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(0.92f, 0.92f, 0.92f, 1f);
                    p.strokeColor = new Color(0f, 0f, 0f, 0.8f); p.lineWidth = 1f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(x, BandH));
                    p.LineTo(new Vector2(x + MarkerHalf, BandH + LaneH - 1f));
                    p.LineTo(new Vector2(x - MarkerHalf, BandH + LaneH - 1f));
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

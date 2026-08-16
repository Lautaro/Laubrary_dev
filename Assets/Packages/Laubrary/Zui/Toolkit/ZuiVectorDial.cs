// ZuiVectorDial — the right-click context card for ZuiVectorEditor: a compact radial dial to point the
// arrow and (when the owning ZuiVectorEditorOptions.allowLength is true) adjust its length, plus the
// toggles the user asked for: show/hide input boxes, show/hide labels, angle display mode (local
// coordinates / degrees / radians), and an N-way quantize snap (Off / 4 / 8 / 16, via Cookbook2D's
// Dir4/Dir8/Dir16 — the same quantize math and "0°=up, clockwise" convention used project-wide).
//
// ZuiEnvelope's own right-click just deletes a point — there is no built-in settings-card mechanism on it
// to copy. This card is built from the general ZUI primitives instead: Z.Popover for the floating panel,
// plain ZUI controls (Z.MiniRadio/Z.ToggleButton/Z.Float) for the rows. See ZuiMenu for a persistent-toggle
// menu shape; this card is closer to a bespoke flyout than a menu, so it's built directly on Z.Popover.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Cookbook2D;

namespace Laubrary.Zui
{
    public static class ZuiVectorDial
    {
        // Display preferences are a per-EDITING-SESSION convenience, not asset data — shared across every
        // vector's card rather than stored per-instance, since "always show labels" is a user habit, not
        // something that belongs in the authored vector itself.
        static bool _showInputBoxes = true;
        static bool _showLabels = true;
        static ZuiVectorAngleMode _angleMode = ZuiVectorAngleMode.Degrees;
        static int _quantize = 0; // 0 = free, else 4 / 8 / 16

        public static void OpenCard(VisualElement anchor, ZuiVectorValue value, ZuiVectorEditorOptions opt,
            Action onChanged, Action onBeforeMutate)
        {
            Z.Popover(anchor, panel =>
            {
                var root = new VisualElement();
                root.style.paddingLeft = 10; root.style.paddingRight = 10;
                root.style.paddingTop = 8; root.style.paddingBottom = 8;
                root.style.width = 210;
                panel.Add(root);

                void Rebuild()
                {
                    root.Clear();

                    var dial = new Dial(value, opt, () => _quantize, () => _showLabels, onChanged, onBeforeMutate);
                    dial.style.alignSelf = Align.Center;
                    dial.style.marginBottom = 6;
                    root.Add(dial);

                    if (_showInputBoxes)
                        root.Add(BuildInputRow(value, opt, dial, onChanged, onBeforeMutate));

                    var sep = new VisualElement();
                    sep.style.height = 1;
                    sep.style.marginTop = 6; sep.style.marginBottom = 6;
                    sep.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
                    root.Add(sep);

                    root.Add(LabeledRow("Angle", Z.MiniRadio((int)_angleMode, new[] { "Local", "Deg", "Rad" },
                        "How the angle field below is expressed.",
                        i => { _angleMode = (ZuiVectorAngleMode)i; Rebuild(); }, wrap: false)));

                    root.Add(LabeledRow("Snap", Z.MiniRadio(QuantizeToIndex(_quantize), new[] { "Free", "4", "8", "16" },
                        "Quantize the dial to N evenly-spaced directions. Free = no snapping.",
                        i => { _quantize = IndexToQuantize(i); dial.MarkDirtyRepaint(); }, wrap: false)));

                    var toggles = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
                    toggles.Add(Z.ToggleButton("Input boxes", "Show numeric fields for direction/length.",
                        _showInputBoxes, v => { _showInputBoxes = v; Rebuild(); }).W(96));
                    toggles.Add(Z.ToggleButton("Labels", "Show axis labels on the dial.",
                        _showLabels, v => { _showLabels = v; dial.MarkDirtyRepaint(); }).W(96));
                    root.Add(toggles);
                }

                Rebuild();
            }, new ZuiPopover.Options { minWidth = 210 });
        }

        static VisualElement LabeledRow(string label, VisualElement control)
        {
            var row = Z.Field(label, null, control);
            row.style.marginTop = 3;
            return row;
        }

        static int QuantizeToIndex(int q) => q switch { 4 => 1, 8 => 2, 16 => 3, _ => 0 };
        static int IndexToQuantize(int i) => i switch { 1 => 4, 2 => 8, 3 => 16, _ => 0 };

        static VisualElement BuildInputRow(ZuiVectorValue value, ZuiVectorEditorOptions opt, Dial dial,
            Action onChanged, Action onBeforeMutate)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };

            if (_angleMode == ZuiVectorAngleMode.LocalCoordinates)
            {
                row.Add(Z.Field("X", "Direction X (local coordinates).",
                    Z.Float(value.direction.x, null, x =>
                    {
                        onBeforeMutate?.Invoke();
                        value.direction = new Vector2(x, value.direction.y).normalized;
                        dial.MarkDirtyRepaint(); onChanged?.Invoke();
                    })).W(70));
                row.Add(Z.Field("Y", "Direction Y (local coordinates).",
                    Z.Float(value.direction.y, null, y =>
                    {
                        onBeforeMutate?.Invoke();
                        value.direction = new Vector2(value.direction.x, y).normalized;
                        dial.MarkDirtyRepaint(); onChanged?.Invoke();
                    })).W(70));
            }
            else
            {
                float deg = DirectionToDeg(value.direction);
                float shown = _angleMode == ZuiVectorAngleMode.Radians ? deg * Mathf.Deg2Rad : deg;
                string label = _angleMode == ZuiVectorAngleMode.Radians ? "rad" : "deg";
                row.Add(Z.Field(label, "The vector's angle. 0 = up, increasing clockwise.",
                    Z.Float(shown, null, v =>
                    {
                        float d = _angleMode == ZuiVectorAngleMode.Radians ? v * Mathf.Rad2Deg : v;
                        onBeforeMutate?.Invoke();
                        value.direction = DegToDirection(d);
                        dial.MarkDirtyRepaint(); onChanged?.Invoke();
                    })).W(90));
            }

            if (opt.allowLength)
            {
                row.Add(Z.Field("len", "Vector length.",
                    Z.Float(value.length, null, l =>
                    {
                        onBeforeMutate?.Invoke();
                        value.length = Mathf.Max(0f, l);
                        dial.MarkDirtyRepaint(); onChanged?.Invoke();
                    })).W(70));
            }

            return row;
        }

        static float DirectionToDeg(Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return 0f;
            float deg = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        static Vector2 DegToDirection(float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        static float Quantized(float deg, int q)
        {
            if (q <= 0) return deg;
            float step = 360f / q;
            return Mathf.Round(deg / step) * step % 360f;
        }

        /// The dial itself: a small circle, drag around it to set angle (snapped live to the current
        /// quantize setting), drag radially to set length when the owning options allow it.
        class Dial : VisualElement
        {
            const float Size = 120f;
            const float Radius = Size * 0.5f - 16f;

            readonly ZuiVectorValue _value;
            readonly ZuiVectorEditorOptions _opt;
            readonly Func<int> _quantize;
            readonly Func<bool> _showLabels;
            readonly Action _onChanged;
            readonly Action _onBeforeMutate;
            bool _dragging;
            bool _gestureRecorded;

            public Dial(ZuiVectorValue value, ZuiVectorEditorOptions opt, Func<int> quantize, Func<bool> showLabels,
                Action onChanged, Action onBeforeMutate)
            {
                _value = value; _opt = opt; _quantize = quantize; _showLabels = showLabels;
                _onChanged = onChanged; _onBeforeMutate = onBeforeMutate;
                style.width = Size; style.height = Size;
                generateVisualContent += Paint;
                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
            }

            Vector2 Center => new Vector2(Size * 0.5f, Size * 0.5f);

            float CurrentDeg() => DirectionToDeg(_value.direction);

            void Paint(MeshGenerationContext mgc)
            {
                var painter = mgc.painter2D;
                Vector2 c = Center;

                painter.strokeColor = new Color(1f, 1f, 1f, 0.18f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.Arc(c, Radius, 0f, 360f);
                painter.Stroke();

                int q = _quantize();
                if (q > 0 && _showLabels())
                {
                    // Faint tick marks at each quantize step.
                    painter.strokeColor = new Color(1f, 1f, 1f, 0.25f);
                    for (int i = 0; i < q; i++)
                    {
                        float deg = i * 360f / q;
                        float rad = deg * Mathf.Deg2Rad;
                        Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
                        painter.BeginPath();
                        painter.MoveTo(c + dir * (Radius - 4f));
                        painter.LineTo(c + dir * (Radius + 4f));
                        painter.Stroke();
                    }
                }

                float showDeg = q > 0 ? Quantized(CurrentDeg(), q) : CurrentDeg();
                float showRad = showDeg * Mathf.Deg2Rad;
                Vector2 handleDir = new Vector2(Mathf.Sin(showRad), -Mathf.Cos(showRad));
                float len = _opt.allowLength ? Mathf.Clamp(Mathf.Max(0.02f, _value.length), 0.15f, 1.6f) * Radius * 0.85f : Radius * 0.85f;
                Vector2 tip = c + handleDir * len;

                painter.strokeColor = _opt.arrowColor;
                painter.lineWidth = _dragging ? 3f : 2f;
                painter.BeginPath();
                painter.MoveTo(c);
                painter.LineTo(tip);
                painter.Stroke();

                painter.fillColor = _opt.arrowColor;
                painter.BeginPath();
                painter.Arc(tip, 4f, 0f, 360f);
                painter.Fill();

                if (_showLabels())
                {
                    string txt = _angleMode == ZuiVectorAngleMode.Radians
                        ? $"{showRad:0.00} rad"
                        : $"{showDeg:0}°";
                    mgc.DrawText(txt, new Vector2(4f, Size - 14f), 10f, new Color(1f, 1f, 1f, 0.75f));
                }
            }

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0) return;
                if (!_gestureRecorded) { _gestureRecorded = true; _onBeforeMutate?.Invoke(); }
                _dragging = true;
                this.CapturePointer(e.pointerId);
                DragTo(e.localPosition);
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (!_dragging || !this.HasPointerCapture(e.pointerId)) return;
                DragTo(e.localPosition);
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
                _dragging = false;
                _gestureRecorded = false;
            }

            void DragTo(Vector2 local)
            {
                Vector2 delta = local - Center;
                if (delta.sqrMagnitude > 1e-6f)
                {
                    float deg = Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg;
                    if (deg < 0f) deg += 360f;
                    int q = _quantize();
                    if (q > 0) deg = Quantized(deg, q);
                    _value.direction = DegToDirection(deg);

                    if (_opt.allowLength)
                    {
                        float t = Mathf.Clamp01(delta.magnitude / (Radius * 0.85f));
                        _value.length = Mathf.Lerp(0.15f, 1.6f, t);
                    }
                }
                MarkDirtyRepaint();
                _onChanged?.Invoke();
            }
        }
    }
}

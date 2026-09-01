// ShaperMockFillControl — the "Shaper-native fill control" the design doc calls for at §C2/§I2 priority 2:
// NOT a reuse of ZuiFillControl, which is contractually bound to the OLD ZuiFill runtime type, not
// ShaperFillDef (see that file's own header comment and SHAPER-UI-VISION-AND-DESIGN.md §C2).
//
// DELIBERATELY KEPT MOCK-SCOPED rather than landed in shared ZUI (unlike ZuiBreadcrumb): the design doc's
// own J4 #2 leaves "a new ZuiShaperFillControl vs. a generalised Z.Fill<T> with an adapter" open for the
// project owner to decide — committing a shape to shared ZUI here would be guessing an answer this task
// was not asked to give. Promote or generalise it once that question actually gets decided.
//
// Mirrors ZuiFillControl's proven shape at a fraction of the surface (two kinds, not seven): a compact
// swatch + label header, right-click for the mode menu (the design doc's own "⋯" wording predates the
// toolkit-wide ⋯-deprecation — ui-layout-rules.md, 2026-08-05 — right-click is what every other ZUI
// control does today, so that is what this one does too), and a packed-row body that rebuilds on switch.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace ShaperMock.Editor
{
    public sealed class ShaperMockFillControl : VisualElement
    {
        readonly ShaperMockFill _fill;
        readonly string _label;
        readonly string _tooltip;
        readonly VisualElement _content;
        Swatch _swatch;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook (mirrors
        /// ZuiFillControl.OnBeforeMutate / Pyre's FillRow).
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        public ShaperMockFillControl(string label, ShaperMockFill fill, string tooltip)
        {
            _fill = fill ?? throw new ArgumentNullException(nameof(fill));
            _label = label;
            _tooltip = tooltip;
            AddToClassList("zui-fill");
            this.tooltip = string.IsNullOrEmpty(tooltip)
                ? "Right-click to choose the fill kind."
                : tooltip + "  (right-click to choose the fill kind)";

            _content = new VisualElement();
            Add(_content);
            RebuildAll();

            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                ShowMenu();
                e.StopPropagation();
            });
        }

        void Mutate(Action apply)
        {
            OnBeforeMutate?.Invoke();
            apply();
            _swatch?.Refresh();
            OnChanged?.Invoke();
        }

        void RebuildAll()
        {
            _content.Clear();
            _swatch = null;

            switch (_fill.kind)
            {
                case ShaperMockFillKind.Solid:
                {
                    var cf = Z.Color(_fill.solidColor, _tooltip,
                        c => Mutate(() => _fill.solidColor = c), 170f, showAlpha: true);
                    var row = Z.Row(FieldLabel(_label ?? "Fill"), cf);
                    row.style.alignItems = Align.FlexStart;
                    _content.Add(row);
                    break;
                }

                case ShaperMockFillKind.Gradient:
                {
                    if (ShaperMockFill.IsUnseeded(_fill.gradient)) _fill.gradient = ShaperMockFill.DefaultGradient();

                    _swatch = new Swatch(_fill, 40f,
                        "Live preview of the gradient — left is the ramp's start, right its end.");
                    var header = Z.Row(_swatch, FieldLabel(_label ?? "Fill"));
                    header.style.alignItems = Align.Center;
                    _content.Add(header);

                    var grad = Z.Gradient(_tooltip, () => _fill.gradient,
                        g => Mutate(() => _fill.gradient = g), 200f);
                    _content.Add(grad);

                    _content.Add(Z.MicroSlider("Angle", _fill.gradientAngleDegrees, 0f, 360f,
                        "Rotation of the gradient's axis across the shape, in degrees.",
                        v => Mutate(() => _fill.gradientAngleDegrees = v), 150f, decimals: 0));
                    break;
                }
            }
        }

        static Label FieldLabel(string text)
        {
            var l = new Label(text);
            l.AddToClassList("zui-field__label");
            l.style.marginTop = 3f;
            return l;
        }

        void ShowMenu()
        {
            Z.Menu(this)
                .Section("Fill kind")
                .Item("Solid colour", "A single flat colour.",
                    () => SetKind(ShaperMockFillKind.Solid), _fill.kind == ShaperMockFillKind.Solid)
                .Item("Gradient", "A colour ramp swept across the shape.",
                    () => SetKind(ShaperMockFillKind.Gradient), _fill.kind == ShaperMockFillKind.Gradient)
                .Show();
        }

        void SetKind(ShaperMockFillKind kind)
        {
            Mutate(() =>
            {
                _fill.kind = kind;
                if (kind == ShaperMockFillKind.Gradient && ShaperMockFill.IsUnseeded(_fill.gradient))
                    _fill.gradient = ShaperMockFill.DefaultGradient();
            });
            RebuildAll();
        }

        // ── live swatch (solid colour, or the gradient sampled left→right) ──────────────────────────
        sealed class Swatch : VisualElement
        {
            readonly ShaperMockFill _fill;

            public Swatch(ShaperMockFill fill, float size, string tip)
            {
                _fill = fill;
                tooltip = tip;
                style.width = size;
                style.height = size;
                style.flexShrink = 0f;
                style.marginRight = 6f;
                var bc = new Color(0f, 0f, 0f, 0.4f);
                style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1f;
                style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = bc;
                style.borderTopLeftRadius = style.borderTopRightRadius =
                    style.borderBottomLeftRadius = style.borderBottomRightRadius = 2f;
                generateVisualContent += Paint;
            }

            public void Refresh() => MarkDirtyRepaint();

            void Paint(MeshGenerationContext mgc)
            {
                var view = contentRect;
                if (view.width < 2f || view.height < 2f) return;
                var p = mgc.painter2D;

                if (_fill.kind == ShaperMockFillKind.Solid || _fill.gradient == null)
                {
                    Fill(p, view, _fill.kind == ShaperMockFillKind.Solid ? _fill.solidColor : Color.magenta);
                    return;
                }

                int steps = Mathf.Max(8, Mathf.RoundToInt(view.width));
                float stripW = view.width / steps;
                for (int i = 0; i < steps; i++)
                {
                    float t = steps <= 1 ? 0f : i / (float)(steps - 1);
                    Color c = _fill.gradient.Evaluate(t);
                    Fill(p, new Rect(view.x + i * stripW, view.y, stripW + 1f, view.height), c);
                }
            }

            static void Fill(Painter2D p, Rect r, Color c)
            {
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(new Vector2(r.x, r.y));
                p.LineTo(new Vector2(r.xMax, r.y));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.x, r.yMax));
                p.ClosePath();
                p.Fill();
            }
        }
    }
}

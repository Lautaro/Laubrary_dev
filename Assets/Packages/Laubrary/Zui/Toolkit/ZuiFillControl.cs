// ZuiFillControl — the UI Toolkit control that edits a ZuiFill: one labelled row whose compact face
// changes with the fill's mode, and a "⋯" button opening the same style of mode menu ZuiValueControl
// uses. Acts as a plain colour picker in Solid mode (alpha shown), and grows a gradient — plus a small
// Angle or Zoom field — in the spatial modes.
//
// Mirrors ZuiValueControl's conventions: an EXTERNAL label to the left (via FieldLabel) for every mode
// (ZuiFill has no MicroSlider-style inside-label case), a right-aligned ⋯ MenuButton, a GenericMenu of
// modes with the current one checked, and a full face rebuild on any mode switch. Operates on the SAME
// runtime ZuiFill the consumer owns; every edit fires OnBeforeMutate → apply → OnChanged (the Undo pair).
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiFillControl : VisualElement
    {
        /// Minimal layout options — mirrors the packed-row / grow surface of ZuiValueControl.Options.
        public class Options
        {
            public float controlWidth = 170f;   // width of the colour/gradient body (packed-row support)
            // Grow to fill the available width up to maxWidthFactor × controlWidth, instead of sitting at a
            // fixed width and leaving a wide row empty. Off by default so callers keep their exact layout.
            public bool grow = false;
            public float maxWidthFactor = 3.2f;

            public Options WithWidth(float w) { controlWidth = w; return this; }
            public Options WithGrow(float maxFactor = 2.4f) { grow = true; maxWidthFactor = maxFactor; return this; }
            public Options Clone() => (Options)MemberwiseClone();
        }

        readonly ZuiFill _fill;
        readonly Options _opt;
        readonly string _label;
        readonly string _tooltip;
        readonly VisualElement _content;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation (colour edits, gradient edits, angle/zoom drags, mode changes).
        public Action OnChanged;

        public ZuiFillControl(string label, ZuiFill fill, Options options, string tooltip)
        {
            _fill = fill ?? throw new ArgumentNullException(nameof(fill));
            _opt = options ?? new Options();
            _label = label;
            _tooltip = tooltip;
            this.tooltip = tooltip;

            if (_opt.grow)
            {
                style.flexGrow = 1f;
                style.flexShrink = 1f;
                style.minWidth = _opt.controlWidth;
                style.maxWidth = _opt.controlWidth * Mathf.Max(1f, _opt.maxWidthFactor);
            }

            _content = new VisualElement();
            Add(_content);

            RebuildAll();
        }

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        Button MenuButton() => Z.Button("⋯",
            "Fill mode — solid colour, over-life, or a spatial gradient (linear / radial / noise).",
            ShowMenu).W(24f);

        Label FieldLabel(string text)
        {
            var l = new Label(text) { tooltip = _tooltip };
            l.AddToClassList("zui-field__label");
            l.style.marginTop = 3f;
            return l;
        }

        // ── full rebuild (on construction and every mode switch) ─────────────────────────────
        void RebuildAll()
        {
            _content.Clear();

            switch (_fill.mode)
            {
                case ZuiFill.Mode.Solid:
                {
                    // Just a colour picker — alpha shown, filling the row. This IS the "acts as a plain
                    // colour field when fill isn't selected" case.
                    var cf = Z.Color(_fill.color, _tooltip,
                        c => Mutate(() => _fill.color = c), _opt.controlWidth, showAlpha: true);
                    AddHeaderRow(_label, cf);
                    break;
                }

                case ZuiFill.Mode.OverLife:
                    AddHeaderRow(_label, BuildGradient(_opt.controlWidth));
                    break;

                case ZuiFill.Mode.Linear:
                    AddHeaderRow(_label, GradientPlus(ExtraAngle()));
                    break;

                case ZuiFill.Mode.Radial:
                case ZuiFill.Mode.Noise:
                    AddHeaderRow(_label, GradientPlus(ExtraZoom()));
                    break;
            }
        }

        void AddHeaderRow(string label, VisualElement body)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.alignItems = Align.FlexStart;
            if (!string.IsNullOrEmpty(label)) row.Add(FieldLabel(label));
            if (_opt.grow) { body.style.flexGrow = 1f; body.style.flexShrink = 1f; }
            else body.style.flexShrink = 0f;
            row.Add(body);
            row.Add(MenuButton());
            _content.Add(row);
        }

        // A gradient field paired with a small extra numeric (Angle or Zoom) on the same body row.
        VisualElement GradientPlus(VisualElement extra)
        {
            var body = new VisualElement();
            body.AddToClassList("zui-row");
            // The gradient gives up the extra field's width; it grows to fill spare row space when grow is on.
            var grad = BuildGradient(Mathf.Max(80f, _opt.controlWidth - 74f));
            if (_opt.grow) { grad.style.flexGrow = 1f; grad.style.flexShrink = 1f; }
            body.Add(grad);
            body.Add(extra);
            return body;
        }

        GradientField BuildGradient(float width)
        {
            _fill.EnsureGradient();
            // get/set form re-reads the live gradient — the Undo-restored / paste-swapped instance shows up.
            return Z.Gradient(_tooltip,
                () => _fill.gradient,
                g => Mutate(() => _fill.gradient = g ?? ZuiFill.DefaultGradient()),
                width);
        }

        VisualElement ExtraAngle()
        {
            const string tip = "Rotation of the linear fill axis, in degrees.";
            var f = Z.Float(_fill.angleDeg, tip, v => Mutate(() => _fill.angleDeg = v), 42f);
            return Z.Field("Ang", tip, f);
        }

        VisualElement ExtraZoom()
        {
            const string tip = "Spatial scale of the fill — higher zooms the pattern in (min 0.05).";
            var f = Z.Float(_fill.zoom, tip, v => Mutate(() => _fill.zoom = Mathf.Max(0.05f, v)), 42f);
            return Z.Field("Zoom", tip, f);
        }

        // ── the ⋯ menu (mode only — ZuiFill carries no clipboard/multiplier surface) ──────────
        void ShowMenu()
        {
            var menu = new GenericMenu();
            AddModeItem(menu, "Solid colour", ZuiFill.Mode.Solid);
            AddModeItem(menu, "Over life", ZuiFill.Mode.OverLife);
            AddModeItem(menu, "Linear gradient", ZuiFill.Mode.Linear);
            AddModeItem(menu, "Radial gradient", ZuiFill.Mode.Radial);
            AddModeItem(menu, "Noise", ZuiFill.Mode.Noise);
            menu.ShowAsContext();
        }

        void AddModeItem(GenericMenu menu, string label, ZuiFill.Mode mode)
            => menu.AddItem(new GUIContent(label), _fill.mode == mode, () => SetMode(mode));

        void SetMode(ZuiFill.Mode mode)
        {
            Mutate(() =>
            {
                _fill.mode = mode;
                if (mode != ZuiFill.Mode.Solid) _fill.EnsureGradient();   // seed so the field is never blank
            });
            RebuildAll();
        }
    }
}

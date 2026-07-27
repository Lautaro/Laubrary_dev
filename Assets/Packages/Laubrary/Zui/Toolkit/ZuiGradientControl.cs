// ZuiGradientControl — the UI Toolkit control that edits a ZuiGradient (base Gradient + transform knobs).
// The top strip is a LIVE preview of the TRUE evaluated ramp: it is painted from ZuiGradient.ToLut(), the
// exact same LUT the palette-cycle shader consumes, so what you see is byte-for-byte what runs. Any knob
// change (or a base-gradient edit) re-bakes the strip, so reverse / hue / sat / brightness / contrast /
// quantise all show their real result immediately. Editor-only (uses UnityEditor's GradientField).
//
// COLLAPSE-WITH-PREVIEW (the shared behaviour every gradient group reuses): the preview strip is the
// always-visible fold HEADER — click it (or its caret) to collapse the base gradient + transform knobs to
// JUST that strip, exactly the way a ZUI envelope collapses to its preview thumbnail. This lives ONCE here,
// so every call site (Z.Gradient, ZuiReflect's ZuiGradient branch, ZuiFillControl's gradient modes) gains
// it without duplicating the fold logic. The fold plumbing is delegated to ZuiFoldCard (the shared caret +
// Clickable + per-instance persisted fold state) — the same primitive the modifier-card stacks fold with,
// so the fold survives window rebuilds and never drifts. Collapse can be turned off per call (collapsible:
// false) for a caller that wants the old flat, always-open layout.

using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiGradientControl : VisualElement
    {
        readonly ZuiGradient _g;
        readonly Image _preview;   // painted from ToLut() — the runtime-exact ramp; the ALWAYS-VISIBLE fold header
        Texture2D _lut;            // owned; re-baked on every change, destroyed on detach

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation.
        public Action OnChanged;

        /// <param name="collapsible">When true (default) the transform knobs + base gradient fold away beneath
        /// the always-visible preview strip (envelope-style), keyed per ZuiGradient instance so the fold sticks
        /// across rebuilds. Pass false for the old flat, always-open layout.</param>
        public ZuiGradientControl(ZuiGradient g, string tooltip = null, bool collapsible = true)
        {
            _g = g ?? throw new ArgumentNullException(nameof(g));
            AddToClassList("zui-gradient-control");
            style.flexDirection = FlexDirection.Column;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _preview = new Image
            {
                scaleMode = ScaleMode.StretchToFill,
                tooltip = "The gradient exactly as it evaluates at runtime — every transform applied."
                    + (collapsible ? "  Click to expand / collapse its controls." : ""),
            };
            _preview.style.height = 22;
            _preview.style.flexGrow = 1f;      // fill the header row's width (beside the fold caret) when stretched
            _preview.style.flexShrink = 1f;
            // A width FLOOR so the strip stays a real, usable ramp even when COLLAPSED inside a shrink-to-fit
            // parent (a Z.Field row): with the body's 200px controls hidden, nothing else would drive the width.
            // In a stretching parent (the fill control) flexGrow still fills the full width beyond this floor.
            _preview.style.minWidth = 120f;

            // The always-visible fold HEADER carries the preview strip; the BODY (base gradient + knobs) folds
            // away beneath it — mirroring how a ZUI envelope collapses to just its preview thumbnail. ZuiFoldCard
            // prepends the caret and wires the Clickable (a raw PointerDownEvent never fires for a header inside
            // a ScrollView — the same reason the box / section / card headers all use Clickable).
            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.style.marginBottom = 4;
            header.Add(_preview);
            Add(header);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Column;
            Add(body);

            var field = new GradientField { value = _g.gradient, tooltip = "Base gradient — the transforms below apply on top of it." };
            field.RegisterValueChangedCallback(e => Mutate(() => _g.gradient = e.newValue));
            body.Add(field);

            body.Add(Z.Toggle("Reverse", "Sample the gradient backwards (1-t).", _g.reverse, v => Mutate(() => _g.reverse = v)));
            AddSlider(body, "Hue",        _g.hueShift,    -1f, 1f, "Rotate the hue of the whole ramp (±1 = ±180°).", v => _g.hueShift = v);
            AddSlider(body, "Saturation", _g.saturation,   0f, 2f, "Multiply saturation across the ramp (1 = unchanged).",        v => _g.saturation = v);
            AddSlider(body, "Brightness", _g.brightness,   0f, 2f, "Multiply brightness across the ramp (1 = unchanged).",        v => _g.brightness = v);
            AddSlider(body, "Contrast",   _g.contrast,     0f, 2f, "Contrast around mid-grey (1 = unchanged).",                   v => _g.contrast = v);

            var q = Z.SliderInt(_g.quantiseSteps, 0, 16, "Snap the ramp to N discrete bands (0 = smooth) — the gradient Posterize.",
                                v => Mutate(() => _g.quantiseSteps = v), 200f);
            q.label = "Quantise";
            body.Add(q);

            body.Add(Z.Toggle("Cycle", "This ramp wants to colour-cycle (a ZuiPaletteCycle driver advances the phase at runtime).",
                              _g.cycle, v => Mutate(() => _g.cycle = v)));

            // Fold the body under the preview strip, keyed to this ZuiGradient so the state survives rebuilds.
            if (collapsible) ZuiFoldCard.Wire(_g, header, body);

            RefreshPreview();
            RegisterCallback<DetachFromPanelEvent>(_ => DisposeLut());
        }

        void AddSlider(VisualElement parent, string label, float value, float min, float max, string tip, Action<float> set)
        {
            var s = Z.Slider(value, min, max, tip, v => Mutate(() => set(v)), 200f);
            s.label = label;
            parent.Add(s);
        }

        void Mutate(Action apply)
        {
            OnBeforeMutate?.Invoke();
            apply();
            RefreshPreview();
            OnChanged?.Invoke();
        }

        void RefreshPreview()
        {
            DisposeLut();
            _lut = _g.ToLut(256);
            _lut.hideFlags = HideFlags.HideAndDontSave;
            _preview.image = _lut;
        }

        void DisposeLut()
        {
            if (_lut != null) { UnityEngine.Object.DestroyImmediate(_lut); _lut = null; }
        }
    }
}

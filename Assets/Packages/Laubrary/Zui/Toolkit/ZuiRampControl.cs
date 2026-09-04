// ZuiRampControl — the control for a colour ramp (IZuiRamp): one row holding Unity's own GradientField, the "★"
// project-library button and — when the ramp offers blend modes — a segmented mode row beside it, over a
// collapsible "Adjust" box when the ramp carries non-destructive knobs of its own (IZuiRamp.RampAdjust).
//
// The Adjust box is the SAME box a Fill's gradient has always had, drawn from the same knobs in the same order:
// a raw ramp holding the same colours now offers the same adjustments instead of a strictly poorer control just
// because of which type is holding it. A ramp with no knobs (a ZuiGradient, whose own editor draws the animatable
// set) draws no box, so nothing moved for the sites that never had one.
//
// The field is the SANCTIONED RAW ISLAND for a gradient. Unity's gradient popup is the editor everyone already
// knows (click under the bar to add a stop, drag it, pick its colour, alpha keys on top, presets, eyedropper); a
// bespoke stop editor re-teaches all of that and was, in the owner's words, cumbersome. So every ramp site and
// every gradient site opens the same familiar popup, and ZUI adds only what Unity's field cannot express: the
// saved-gradient library and the interpolation space.
//
// THE 8-STOP QUESTION, and why storage is not what the field shows. A UnityEngine.Gradient caps at 8 colour keys;
// an IZuiRamp does not (PyreRampPresets.Ember() ships 10). The cap is therefore paid at the LAST possible moment:
//   * DISPLAY subsamples to 8 (endpoints exact) purely to fill the field. Nothing is written, so an untouched
//     ramp — a Pyre preset, a 14-stop palette from the library — keeps every stop it has and renders exactly.
//   * Only a real EDIT in the popup writes back, and that necessarily collapses the ramp to what the field holds.
//     The field's tooltip says so before it happens, naming the count that will be lost.
// This is the deliberate trade the owner asked for (T-0223): the familiar editor everywhere, exactness preserved
// until the moment someone chooses to edit.
//
// An EMPTY ramp is legal and meaningful (Pyre's soot ramp means "no soot"), so it is never auto-seeded: it shows
// as a fully transparent bar — which is what it evaluates to — and stays empty until the author edits the field.
//
// Undo contract, same as every ZUI control that mutates in place: OnBeforeMutate fires once per gesture BEFORE the
// first mutation, OnChanged after every mutation.
using System;
using UnityEditor.UIElements;
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
        readonly VisualElement _row;     // field + "★" + blend space; the Adjust box sits under it
        readonly GradientField _field;
        readonly ZuiSegmented _mode;
        readonly Button _library;
        readonly string _tip;
        ZuiBox _adjust;                  // null when the ramp offers no Adjust knobs of its own
        Image _preview;                  // objective strip (the ramp WITH its knobs); only exists alongside _adjust
        Texture2D _previewTex;

        // Set while the ramp is being written FROM the field's own change event: pushing the value back into the
        // field mid-gesture would fight Unity's open popup (and re-enter this callback).
        bool _applying;

        const float BtnWidth = 22f;

        /// <param name="showLibrary">Draw this control's own "★" saved-gradient button. A host that already
        /// carries one for the same ramp (ZuiGradientEditor puts it on the Output row) passes false, so a
        /// gradient shows one library button, not two.</param>
        public ZuiRampControl(IZuiRamp ramp, string tooltip = null, bool showLibrary = true)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            _tip = string.IsNullOrEmpty(tooltip)
                ? "The colour ramp. Click it for Unity's gradient editor — click under the bar to add a stop, drag "
                + "to move it, and set its colour; the row of keys above the bar is opacity."
                : tooltip;

            AddToClassList("zui-ramp");
            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1f;
            style.flexShrink = 1f;

            // The ramp row itself (field + "★" + blend space). It used to be this control's own box; it became a
            // child the moment a ramp could also carry an Adjust box below it, and a ramp with no knobs still draws
            // exactly one row, so nothing moved for the sites that had none.
            _row = new VisualElement();
            _row.AddToClassList("zui-row");
            _row.style.flexDirection = FlexDirection.Row;
            _row.style.alignItems = Align.Center;
            // flexGrow 0 in a COLUMN parent: growth here would be VERTICAL, which padded the ramp row out with
            // empty space instead of widening it. The field inside still grows horizontally.
            _row.style.flexGrow = 0f;
            _row.style.flexShrink = 1f;
            _row.style.width = Length.Percent(100f);
            // Wrap, because a Pyre form's left pane is often narrower than field + "★" + a two-option segmented:
            // without this the blend-space row is drawn past the pane's edge and simply clipped, which is how it
            // read before. Wrapped, the space choice drops to a second line and stays reachable at any pane width.
            _row.style.flexWrap = Wrap.Wrap;
            Add(_row);

            _field = new GradientField();
            // A ramp legitimately takes the width of the pane it sits in — the same reason the painted strip did.
            // The shared sheet pins every .unity-base-field to flex-grow 0, so this opts out explicitly and tells
            // ZuiAudit that the stretch is intended rather than the usual accidental one.
            _field.AddToClassList("zui-audit-allow-stretch");
            _field.style.minWidth = 120f;   // a gradient bar reads at less width than the painted strip needed
            _field.style.height = 20f;
            _field.style.flexGrow = 1f;
            _field.style.flexShrink = 1f;
            _field.style.marginLeft = 0f;
            _field.style.marginRight = 0f;
            _field.RegisterValueChangedCallback(OnFieldChanged);
            _row.Add(_field);

            // T-0205 — the SAME project gradient library Z.Gradient's "★" reaches, via ZuiRampGradientBridge, which
            // trades in stop lists rather than an 8-key Gradient: saving keeps every stop the ramp has even though
            // the field beside it can only draw eight, and applying one brings all of them back. This does NOT touch
            // the ramp's data on its own; it only acts when the author presses one of these.
            if (showLibrary)
            {
                _library = Z.Button("★", "This project's saved gradients — apply one to this ramp (every stop, "
                                      + "whatever the count), or save this ramp's stops under a new name.",
                                    OpenLibrary).W(BtnWidth);
                _library.style.marginLeft = 4f;
                _row.Add(_library);
            }

            // Two short options => Segmented, never a dropdown and never MiniRadio (ui-layout-rules: control choice).
            // Unity's field cannot express this at all — a Gradient always blends in gamma — so it stays beside it.
            var names = _ramp.BlendModeNames;
            if (names != null && names.Length > 0)
            {
                _mode = Z.Segmented(Mathf.Clamp(_ramp.BlendMode, 0, names.Length - 1), names,
                    "How two neighbouring stops are mixed between them. This changes the COLOURS the ramp "
                  + "produces, not just how it is drawn.",
                    i => Mutate(() => _ramp.BlendMode = i));
                _mode.style.marginLeft = 6f;
                _mode.style.flexShrink = 0f;
                _row.Add(_mode);
            }

            BuildAdjust();

            SyncField();
        }

        // ── the Adjust box (T-0234) ──────────────────────────────────────────────────────────────────────
        // A raw ramp's knobs are the SAME knobs a Fill's gradient has always shown, so they are drawn as the same
        // collapsible "Adjust" box in the same order (ZuiGradientEditor.cs:85-124) — an author who has adjusted one
        // has already learned the other. They are plain floats here rather than ZuiGradient's per-life ZUIValues, so
        // they are MicroSliders rather than Z.Value rows; a ramp that offers none (a ZuiGradient, whose own editor
        // draws the animatable set) gets no box at all and this control stays the single row it was.
        void BuildAdjust()
        {
            var a = _ramp.RampAdjust;
            if (a == null) return;

            // The objective preview: the ramp as it RENDERS, knobs applied. Unity's field below shows the SOURCE
            // stops (what the popup edits) and cannot show a hue shift at all, so without this strip the knobs
            // would look inert — the same reason ZuiGradientEditor puts an Output strip over its own Source row.
            // Only a ramp WITH knobs gets one; a gradient's control already carries Output above this control.
            if (_preview == null)
            {
                _preview = new Image
                {
                    scaleMode = ScaleMode.StretchToFill,
                    tooltip = "The ramp exactly as it renders, with the Adjust knobs applied. The bar below is the "
                            + "SOURCE ramp you edit.",
                };
                _preview.style.height = 16f;
                _preview.style.minWidth = 120f;
                _preview.style.marginBottom = 3f;
                _preview.RegisterCallback<DetachFromPanelEvent>(_ => DisposePreview());
                // A host that merely HIDES this control detaches it and frees the texture; re-baking on attach is
                // what stops the strip coming back permanently blank after a fold-away.
                _preview.RegisterCallback<AttachToPanelEvent>(_ => RefreshPreview());
                Insert(0, _preview);
            }
            RefreshPreview();

            _adjust = Z.Box("Adjust",
                "Non-destructive adjustments applied on top of the ramp above. The stops themselves are never "
              + "rewritten — turn a knob back and the exact original ramp returns.");

            _adjust.Add(Z.HGroup(
                Z.MicroSlider("Hue", a.hueShift, -1f, 1f,
                    "Rotate the hue of every colour in the ramp (±1 = ±180°).",
                    v => Mutate(() => a.hueShift = v), prefsKey: "ramp.hue"),
                Z.MicroSlider("Saturation", a.saturation, 0f, 2f,
                    "Multiply how colourful the ramp is (1 = unchanged, 0 = grey).",
                    v => Mutate(() => a.saturation = v), prefsKey: "ramp.sat"),
                Z.MicroSlider("Brightness", a.brightness, 0f, 2f,
                    "Multiply how bright the ramp is (1 = unchanged).",
                    v => Mutate(() => a.brightness = v), prefsKey: "ramp.bri")));

            _adjust.Add(Z.HGroup(
                Z.MicroSlider("Contrast", a.contrast, 0f, 2f,
                    "Push the ramp's colours away from mid-grey (1 = unchanged, above 1 = harder edges between them).",
                    v => Mutate(() => a.contrast = v), prefsKey: "ramp.con"),
                Z.MicroSlider("Phase", a.phase, 0f, 2f,
                    "Scroll the ramp along its own length. 0→1 runs it forward, 1→2 runs it back mirrored, so 2 lands "
                  + "exactly where 0 did and an animated phase never jumps at the wrap.",
                    v => Mutate(() => a.phase = v), prefsKey: "ramp.phase")));

            _adjust.Add(Z.HGroup(
                Z.MicroSlider("Quantise", a.quantiseSteps, 0, 16,
                    "Snap the ramp to N flat bands instead of a smooth blend (0 = smooth).",
                    v => Mutate(() => a.quantiseSteps = Mathf.RoundToInt(v)), decimals: 0, prefsKey: "ramp.quantise"),
                Z.Toggle("Cycle", "Mark this ramp as wanting to colour-cycle — a driver advances its phase at "
                                + "runtime. A still frame looks the same either way.",
                    a.cycle, v => Mutate(() => a.cycle = v)),
                Z.Toggle("Reverse", "Read the ramp end-to-start, so the hot core colour lands where the cold edge was.",
                    a.reverse, v => Mutate(() => a.reverse = v))));

            Add(_adjust);
        }

        /// Re-read the ramp (an undo, an external edit) — the field, the mode row and the Adjust knobs redraw from
        /// it. The Adjust box is rebuilt rather than written back into: its controls own their displayed value and
        /// pushing one in would fire the change callback, turning a refresh into an edit and an Undo into a new
        /// Undo record.
        public void Refresh()
        {
            _mode?.SetOn(i => i == _ramp.BlendMode);
            if (_adjust != null) { Remove(_adjust); _adjust = null; BuildAdjust(); }
            SyncField();
        }

        // ── mutation / Undo bookkeeping ──────────────────────────────────────────────────────────────────
        // One gesture = one Undo record: a drag calls BeginGesture once then Apply per move.
        bool _gestureOpen;
        void BeginGesture() { if (_gestureOpen) return; _gestureOpen = true; OnBeforeMutate?.Invoke(); }
        void EndGesture() => _gestureOpen = false;
        void Apply(Action edit) { edit(); SyncField(); OnChanged?.Invoke(); }
        void Mutate(Action edit) { BeginGesture(); Apply(edit); EndGesture(); }

        // ── the GradientField, and the one place the 8-key cap is paid ───────────────────────────────────

        /// Push the ramp's CURRENT stops into the field without notifying (so this never re-enters the change
        /// callback), and restate the tooltip — which has to name the live stop count to be worth reading.
        void SyncField()
        {
            if (_applying) return;
            _field.SetValueWithoutNotify(BuildDisplayGradient());
            _field.tooltip = FieldTooltip();
            RefreshPreview();
        }

        /// Re-bake the objective preview strip from the ramp's OWN evaluation, so the picture cannot drift from
        /// what the renderer reads. Cheap (256×1) and only exists on a ramp that has knobs at all.
        void RefreshPreview()
        {
            if (_preview == null) return;
            DisposePreview();
            var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[256];
            for (int i = 0; i < 256; i++) px[i] = _ramp.Eval(i / 255f);
            tex.SetPixels(px);
            tex.Apply();
            _previewTex = tex;
            _preview.image = tex;
        }

        void DisposePreview()
        {
            if (_previewTex != null) { UnityEngine.Object.DestroyImmediate(_previewTex); _previewTex = null; }
            if (_preview != null) _preview.image = null;
        }

        /// DISPLAY only: exact up to 8 stops, evenly subsampled beyond with the endpoints kept. The ramp itself is
        /// not touched, so a 14-stop preset stays 14 stops behind an 8-key picture until someone edits it.
        Gradient BuildDisplayGradient()
        {
            var g = ZuiRampGradientBridge.ToGradient(_ramp);
            if (g != null) return g;

            // Empty ramp: a fully transparent bar, which is exactly what it evaluates to. Editing the field is the
            // way in; until then nothing is written and the ramp stays legitimately empty.
            var empty = new Gradient();
            empty.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 1f) });
            return empty;
        }

        string FieldTooltip()
        {
            int n = _ramp.Count;
            if (n > ZuiGradient.MaxGradientKeys)
                return _tip + $" Unity's editor holds {ZuiGradient.MaxGradientKeys} stops; this gradient was "
                     + $"simplified from {n} for the picture above. The ramp still has all {n} and renders with "
                     + $"them — changing anything here replaces them with the {ZuiGradient.MaxGradientKeys} you see.";
            if (n == 0)
                return _tip + " This ramp is EMPTY (no colour at all, which is a legal state) — editing the field "
                     + "is what gives it stops.";
            return _tip;
        }

        void OnFieldChanged(ChangeEvent<Gradient> e)
        {
            if (e.newValue == null) return;
            _applying = true;
            try
            {
                // ApplyGradient keeps the ramp's own blend space (a UnityEngine.Gradient carries none), so editing
                // colours never silently re-blends a Linear-Light ramp into sRGB.
                Mutate(() => ZuiRampGradientBridge.ApplyGradient(_ramp, e.newValue));
            }
            finally { _applying = false; }
            _field.tooltip = FieldTooltip();
        }

        // T-0205 — opens the SAME saved-gradient popup Z.Gradient's "★" opens. "Save" reads this ramp's current
        // stops through the bridge (every stop, no subsampling) into the shared ZuiGradientPresetLibrary; picking a
        // saved entry REPLACES every stop on this ramp — one gesture, recorded through the normal Mutate() Undo
        // wrapper, never applied silently. This is the route by which a ramp gets MORE than eight stops.
        void OpenLibrary()
        {
            ZuiGradientPresetPopup.Show(_library,
                current: () => ZuiRampGradientBridge.ToZuiGradient(_ramp),
                apply: g => Mutate(() =>
                {
                    ZuiRampGradientBridge.ApplyZuiGradient(_ramp, g);
                    _mode?.SetOn(i => i == _ramp.BlendMode);
                }));
        }
    }
}

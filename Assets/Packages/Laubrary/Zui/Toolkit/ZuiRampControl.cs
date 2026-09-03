// ZuiRampControl — the control for a colour ramp (IZuiRamp): ONE row holding Unity's own GradientField, the "★"
// project-library button, and — when the ramp offers blend modes — a segmented mode row beside it.
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
        readonly GradientField _field;
        readonly ZuiSegmented _mode;
        readonly Button _library;
        readonly string _tip;

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
            AddToClassList("zui-row");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexGrow = 1f;
            style.flexShrink = 1f;
            // Wrap, because a Pyre form's left pane is often narrower than field + "★" + a two-option segmented:
            // without this the blend-space row is drawn past the pane's edge and simply clipped, which is how it
            // read before. Wrapped, the space choice drops to a second line and stays reachable at any pane width.
            style.flexWrap = Wrap.Wrap;

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
            Add(_field);

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
                Add(_library);
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
                Add(_mode);
            }

            SyncField();
        }

        /// Re-read the ramp (an undo, an external edit) — the field and the mode row redraw from it.
        public void Refresh()
        {
            _mode?.SetOn(i => i == _ramp.BlendMode);
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

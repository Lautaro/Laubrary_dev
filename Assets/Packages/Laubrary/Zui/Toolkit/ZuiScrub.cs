// ZuiScrub — makes a bare numeric field (IntegerField / FloatField) drag-adjustable while keeping full
// keyboard editing, by scrubbing from a dedicated DRAG ZONE that has no text input to fight over pointer
// capture.
//
// Why a zone, not an in-field "arm" (the previous design, now removed):
//  On pointer-down a field's inner `unity-text-input` takes POINTER CAPTURE for text selection, and capture
//  redirects EVERY subsequent PointerMove to the capturing element only. A field-level move handler therefore
//  never sees the drag. The old scheme — arm on down, claim the drag on the move that crosses a 4px threshold,
//  then steal capture — could not fire while the text input held capture, so the drag never engaged in
//  practice (proven dead by a synthetic pointer test; it was the real cause of "inputs only accept keyboard").
//  The fix is to scrub from an element that OWNS capture from the very first event:
//   • Attach(field)          — overlays a slim, invisible GRIP on the field's LEFT edge (a fallback that makes
//                              EVERY numeric field draggable, even one with no external label). The rest of the
//                              field stays a normal click-to-edit text box.
//   • AttachToLabel(zone, f) — turns an existing element (a Zui.Field label — Unity's own native drag idiom, a
//                              field's label being where numeric drag has always lived) into the drag zone.
//                              Zui.Field wires this automatically for the numeric fields it wraps.
//
// Both drive the field through its normal `value` setter, so every RegisterValueChangedCallback the call site
// wired (Undo hooks, dirtying, live preview) fires unchanged — the scrubber adds no parallel mutation path.
// Keyboard entry in the field body is untouched (a zone covers only the label or the left grip, never the
// editable area). Drag modifiers: Shift = GENTLE/fine (a small change per pixel, for dialling in precise
// small values), Ctrl = coarse/fast — the project-wide convention, matching ZuiMicroSlider's Shift-fine drag.
// (Changed 2026-07-26 from the old Shift ×10 / Alt ×0.1 so Shift means "gentle" everywhere.) Float writes
// round to 5 decimals to match the toolkit, while the running accumulator stays unrounded so slow drags never
// lose sub-steps.
//
// A zone CAPTURES the pointer on down and StopPropagation()s it, so all moves route to it wherever the cursor
// travels and a plain synthetic PointerDown/Move/Up on the zone drives it — no reliance on hover state or
// panel-level capture subtleties. All coordinates use e.position (panel space).
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiScrub
    {
        // Int scrub speed: one whole unit per this many horizontal pixels (× the modifier).
        const float IntPixelsPerUnit = 3f;

        // Drag modifiers. Shift = GENTLE/fine (a tenth of the normal value-per-pixel, for precise small
        // values); Ctrl = coarse/fast (×10). Shift-means-gentle is the project-wide convention (see the
        // file header), matching ZuiMicroSlider's Shift-fine drag.
        const float FineModifier = 0.1f;
        const float CoarseModifier = 10f;

        // Width of the invisible left-edge grip strip the bare-field fallback overlays (px).
        const float GripWidth = 6f;

        // ── grip fallback: every bare Int/Float field gets a left-edge drag strip ─────────

        /// Attach scrub-dragging to an IntegerField via a slim grip overlaid on its left edge. `low`/`high`,
        /// when given, clamp the scrubbed value (the field's own onChanged clamps still run on top — this just
        /// keeps the drag inside a range the factory already knows). Keyboard entry is untouched.
        public static void Attach(IntegerField field, int? low = null, int? high = null)
        {
            if (field == null) return;
            var grip = MakeGrip();
            field.hierarchy.Add(grip);
            AttachZone(grip, () => field.value,
                v => field.value = (int)Math.Round(v, MidpointRounding.AwayFromZero),
                AdvanceInt, ToD(low), ToD(high));
        }

        /// Attach scrub-dragging to a FloatField via a left-edge grip. `low`/`high` clamp as in the int
        /// overload. The per-pixel step is adaptive to the value's magnitude, so 0.05 scrubs finely and 500
        /// scrubs coarsely.
        public static void Attach(FloatField field, float? low = null, float? high = null)
        {
            if (field == null) return;
            var grip = MakeGrip();
            field.hierarchy.Add(grip);
            AttachZone(grip, () => field.value,
                v => field.value = (float)Math.Round(v, 5),
                AdvanceFloat, ToD(low), ToD(high));
        }

        // ── label drag: an arbitrary zone (a Zui.Field label) scrubs the field ───────────

        /// Make `dragZone` (typically a Zui.Field label) scrub an IntegerField — Unity's native "drag the
        /// label" idiom. Same sensitivity/clamps as the grip fallback; both can coexist on one field.
        public static void AttachToLabel(VisualElement dragZone, IntegerField field, int? low = null, int? high = null)
        {
            if (dragZone == null || field == null) return;
            AttachZone(dragZone, () => field.value,
                v => field.value = (int)Math.Round(v, MidpointRounding.AwayFromZero),
                AdvanceInt, ToD(low), ToD(high));
        }

        /// Make `dragZone` scrub a FloatField.
        public static void AttachToLabel(VisualElement dragZone, FloatField field, float? low = null, float? high = null)
        {
            if (dragZone == null || field == null) return;
            AttachZone(dragZone, () => field.value,
                v => field.value = (float)Math.Round(v, 5),
                AdvanceFloat, ToD(low), ToD(high));
        }

        /// Convenience for Zui.Field: wire the label to whatever numeric field the wrapped control IS. Only
        /// a DIRECT IntegerField/FloatField is wired — a control that merely CONTAINS one (a Slider / SliderInt
        /// embeds its own numeric input field) is deliberately skipped, so a slider's own Zui.Field label never
        /// becomes a rival scrub zone. Every bare-field case that wants label-drag (Seed, First frame, Every N
        /// frames, ZuiReflect int/float bodies) is a direct Int/Float, so the direct check covers them all.
        public static void AttachToLabel(VisualElement dragZone, VisualElement control)
        {
            if (dragZone == null || control == null) return;
            if (control is IntegerField i) AttachToLabel(dragZone, i);
            else if (control is FloatField f) AttachToLabel(dragZone, f);
        }

        // ── shared wiring ────────────────────────────────────────────────────────────────
        static double? ToD<T>(T? v) where T : struct => v.HasValue ? Convert.ToDouble(v.Value) : (double?)null;

        static void AttachZone(VisualElement zone, Func<double> read, Action<double> write,
            Func<double, float, float, double> advance, double? low, double? high)
        {
            zone.AddManipulator(new ZoneScrubManipulator(read, write, advance, low, high));
            ApplyCursor(zone);
        }

        static VisualElement MakeGrip()
        {
            var grip = new VisualElement { tooltip = "Drag to adjust. Hold Shift for fine (gentle) steps, Ctrl for coarse." };
            grip.style.position = Position.Absolute;
            grip.style.left = 0f;
            grip.style.top = 0f;
            grip.style.bottom = 0f;
            grip.style.width = GripWidth;
            grip.pickingMode = PickingMode.Position;   // invisible but pickable — the drag affordance
            return grip;
        }

        // ── stepping strategies (unchanged) ──────────────────────────────────────────────
        static double AdvanceInt(double v, float dxPixels, float modifier)
            => v + dxPixels * modifier / IntPixelsPerUnit;

        static double AdvanceFloat(double v, float dxPixels, float modifier)
        {
            // Adaptive step: max(0.01, 10^floor(log10(max(0.01,|v|))) * 0.01) per pixel — roughly a
            // hundredth of the value's own magnitude, floored at 0.01 so tiny values stay editable.
            double mag = Math.Max(0.01, Math.Abs(v));
            double step = Math.Max(0.01, Math.Pow(10, Math.Floor(Math.Log10(mag))) * 0.01);
            return v + dxPixels * step * modifier;
        }

        // ── the zone manipulator: capture on down, scrub on move, release on up ──────────
        class ZoneScrubManipulator : Manipulator
        {
            readonly Func<double> _read;
            readonly Action<double> _write;
            readonly Func<double, float, float, double> _advance;
            readonly double? _low, _high;

            bool _dragging;
            int _pointer = -1;
            int _undoGroup = -1;      // one scrub is one Undo step — see ZuiUndoGesture
            float _lastX;             // panel-space x at the previous applied move
            double _value;            // unrounded running value so slow drags never lose sub-steps

            public ZoneScrubManipulator(Func<double> read, Action<double> write,
                Func<double, float, float, double> advance, double? low, double? high)
            {
                _read = read; _write = write; _advance = advance; _low = low; _high = high;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0) return;
                // Capture immediately: the zone owns the drag from the first event (no text input to lose
                // capture to), so every following move reaches us wherever the cursor goes.
                _dragging = true;
                _pointer = e.pointerId;
                _value = _read();
                _lastX = e.position.x;
                _undoGroup = ZuiUndoGesture.Begin();
                target.CapturePointer(e.pointerId);
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (!_dragging || e.pointerId != _pointer) return;
                float dx = e.position.x - _lastX;
                _lastX = e.position.x;
                float modifier = e.shiftKey ? FineModifier : e.ctrlKey ? CoarseModifier : 1f;
                _value = _advance(_value, dx, modifier);
                if (_low.HasValue && _value < _low.Value) _value = _low.Value;
                if (_high.HasValue && _value > _high.Value) _value = _high.Value;
                _write(_value);      // goes through the field's value setter → existing onChanged fires
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (e.pointerId != _pointer) return;
                if (_dragging && target.HasPointerCapture(e.pointerId)) target.ReleasePointer(e.pointerId);
                _dragging = false;
                _pointer = -1;
                EndGesture();
                e.StopPropagation();
            }

            void OnCaptureOut(PointerCaptureOutEvent e)
            {
                // Capture lost for any reason (panel change, another element grabbed it) — end cleanly.
                _dragging = false;
                _pointer = -1;
                EndGesture();
            }

            void EndGesture()
            {
                ZuiUndoGesture.End(_undoGroup);
                _undoGroup = -1;
            }
        }

        // ── slide cursor (the hover affordance) ───────────────────────────────────────
        // The system "slide-arrow" numeric-drag cursor lives behind Cursor.defaultCursorId, which is
        // internal — style.cursor's public surface only accepts a texture cursor, and this toolkit's one
        // stylesheet is off-limits here — so it is set via reflection, cached, and degrades to no special
        // cursor if the internal member ever moves. Applied to the drag ZONE only (the grip / the label), so
        // the field's editable body keeps its own text I-beam.
        static bool s_cursorResolved;
        static StyleCursor s_slideCursor;
        static bool s_cursorOk;

        static void ApplyCursor(VisualElement zone)
        {
            if (zone == null || !ResolveCursor()) return;
            zone.style.cursor = s_slideCursor;
        }

        static bool ResolveCursor()
        {
            if (s_cursorResolved) return s_cursorOk;
            s_cursorResolved = true;
            try
            {
                var t = typeof(UnityEngine.UIElements.Cursor);
                object boxed = new UnityEngine.UIElements.Cursor();
                var prop = t.GetProperty("defaultCursorId",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                    prop.SetValue(boxed, (int)MouseCursor.SlideArrow);
                else
                {
                    var fld = t.GetField("defaultCursorId", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fld == null) return false;
                    fld.SetValue(boxed, (int)MouseCursor.SlideArrow);
                }
                s_slideCursor = new StyleCursor((UnityEngine.UIElements.Cursor)boxed);
                s_cursorOk = true;
            }
            catch { s_cursorOk = false; }
            return s_cursorOk;
        }
    }
}

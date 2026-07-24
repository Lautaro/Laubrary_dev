// ZuiScrub — makes a bare numeric field (IntegerField / FloatField) scrub-draggable while keeping
// full keyboard editing. Fixes the toolkit-wide complaint that "numeric inputs that are not sliders …
// can only be typed, not click-dragged": a numeric entry that isn't a Slider had no drag affordance at
// all (Unity's own drag lives on a field's LABEL, and these fields are built bare — the label is the
// separate Zui.Field wrap, not the BaseField's built-in one).
//
// Mechanism (a custom Manipulator, NOT Unity's internal FieldMouseDragger<T> — that and its
// IValueField<T> are internal in this Unity version, 6000.3):
//  • A PLAIN CLICK (press+release with < DragThreshold px of horizontal travel) is left completely
//    alone — the callbacks never capture or stop the event on press, so the text input focuses, places
//    its caret, select-all-on-focus fires, and typing/Enter/Tab all behave exactly as before.
//  • PRESS-THEN-DRAG (≥ DragThreshold px horizontal before release) turns into a scrub: the manipulator
//    steals pointer capture from the text input (which cancels its own text-selection drag), blurs the
//    field so no caret fights the drag, and moves the value continuously with horizontal motion.
//  • Disambiguation is by horizontal travel only, watched from the press point — so a click that
//    wobbles a pixel still counts as a click.
//
// The callbacks are registered TrickleDown so the field pre-empts its own text-input child: on the move
// that crosses the threshold, the field sees the event first, captures, and StopPropagation()s it before
// the text input can treat it as a selection drag. All coordinates use e.position (panel space) — never
// localPosition — because the event target switches from the text input to the field the instant capture
// moves, and localPosition would jump with it.
//
// Value writes go through the field's normal `value` setter, so every RegisterValueChangedCallback /
// onChanged pipeline the call site already wired (Undo.RecordObject hooks, dirtying, live preview) fires
// unchanged — the scrubber adds no parallel mutation path. Float writes are rounded to 5 decimals to
// match the rest of the toolkit; the sub-pixel accumulator stays unrounded so slow drags don't stick.
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
        /// Horizontal pixels of travel from the press point before a press becomes a scrub instead of a
        /// click. Below this it is a plain click (focus + caret); at or above it, a drag.
        public const float DragThreshold = 4f;

        // Int scrub speed: one whole unit per this many horizontal pixels (× the modifier).
        const float IntPixelsPerUnit = 3f;

        /// Attach scrub-dragging to an IntegerField. `low`/`high`, when given, clamp the scrubbed value
        /// (the field's own onChanged clamps still run on top — this just keeps the drag inside a range
        /// the factory already knows). Keyboard entry is untouched.
        public static void Attach(IntegerField field, int? low = null, int? high = null)
        {
            if (field == null) return;
            double? lo = low.HasValue ? low.Value : (double?)null;
            double? hi = high.HasValue ? high.Value : (double?)null;
            field.AddManipulator(new ScrubManipulator(
                read: () => field.value,
                write: v => field.value = (int)Math.Round(v, MidpointRounding.AwayFromZero),
                advance: AdvanceInt,
                low: lo, high: hi));
            ApplyCursor(field);
        }

        /// Attach scrub-dragging to a FloatField. `low`/`high` clamp as in the int overload. The per-pixel
        /// step is adaptive to the value's magnitude, so 0.05 scrubs finely and 500 scrubs coarsely.
        public static void Attach(FloatField field, float? low = null, float? high = null)
        {
            if (field == null) return;
            double? lo = low.HasValue ? low.Value : (double?)null;
            double? hi = high.HasValue ? high.Value : (double?)null;
            field.AddManipulator(new ScrubManipulator(
                read: () => field.value,
                write: v => field.value = (float)Math.Round(v, 5),
                advance: AdvanceFloat,
                low: lo, high: hi));
            ApplyCursor(field);
        }

        // ── stepping strategies ──────────────────────────────────────────────────────
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

        // ── the manipulator ───────────────────────────────────────────────────────────
        class ScrubManipulator : Manipulator
        {
            readonly Func<double> _read;
            readonly Action<double> _write;
            readonly Func<double, float, float, double> _advance;
            readonly double? _low, _high;

            bool _armed;               // pointer is down on the field, watching for a drag
            bool _dragging;            // the drag threshold has been crossed — now scrubbing
            int _pointer = -1;
            float _startX;             // panel-space x at press (the click/drag disambiguation origin)
            float _lastX;             // panel-space x at the previous applied move
            double _value;            // unrounded running value so slow drags never lose sub-steps

            public ScrubManipulator(Func<double> read, Action<double> write,
                Func<double, float, float, double> advance, double? low, double? high)
            {
                _read = read; _write = write; _advance = advance; _low = low; _high = high;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                // TrickleDown: the field must see the event before its text-input child so it can claim a
                // drag before the input starts a text selection.
                target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0) return;
                // Arm only — do NOT capture or stop here, so a plain click still focuses the text input
                // and drops a caret. The drag is claimed later, on the move that crosses the threshold.
                _armed = true;
                _dragging = false;
                _pointer = e.pointerId;
                _startX = e.position.x;
            }

            void OnMove(PointerMoveEvent e)
            {
                if (!_armed || e.pointerId != _pointer) return;

                if (!_dragging)
                {
                    if (Mathf.Abs(e.position.x - _startX) < DragThreshold) return;
                    // Threshold crossed: become a scrub. Steal capture from the text input (cancels its
                    // selection drag), blur so the caret stops fighting, and count the travel so far.
                    _dragging = true;
                    _value = _read();
                    _lastX = _startX;
                    target.CapturePointer(e.pointerId);
                    BlurField();
                }

                float dx = e.position.x - _lastX;
                _lastX = e.position.x;
                float modifier = e.shiftKey ? 10f : e.altKey ? 0.1f : 1f;
                _value = _advance(_value, dx, modifier);
                if (_low.HasValue && _value < _low.Value) _value = _low.Value;
                if (_high.HasValue && _value > _high.Value) _value = _high.Value;
                _write(_value);      // goes through the field's value setter → existing onChanged fires
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (e.pointerId != _pointer) return;
                if (_dragging)
                {
                    if (target.HasPointerCapture(e.pointerId)) target.ReleasePointer(e.pointerId);
                    e.StopPropagation();   // a scrub is not a click — don't let the input re-focus on release
                }
                _armed = false;
                _dragging = false;
                _pointer = -1;
            }

            void OnCaptureOut(PointerCaptureOutEvent e)
            {
                // Capture lost for any reason (panel change, another element grabbed it) — end cleanly.
                _armed = false;
                _dragging = false;
                _pointer = -1;
            }

            void BlurField()
            {
                var focused = target.focusController?.focusedElement as VisualElement;
                if (focused != null && (focused == target || target.Contains(focused)))
                    focused.Blur();
            }
        }

        // ── slide cursor (the hover affordance) ───────────────────────────────────────
        // The system "slide-arrow" numeric-drag cursor lives behind Cursor.defaultCursorId, which is
        // internal — style.cursor's public surface only accepts a texture cursor, and this toolkit's one
        // stylesheet is off-limits here — so it is set via reflection, cached, and degrades to no special
        // cursor if the internal member ever moves.
        static bool s_cursorResolved;
        static StyleCursor s_slideCursor;
        static bool s_cursorOk;

        static void ApplyCursor(VisualElement field)
        {
            if (!ResolveCursor()) return;
            field.style.cursor = s_slideCursor;
            // cursor is not an inherited USS property, so the text-input child needs it set too — it
            // covers most of the field's visible area and would otherwise keep its own text I-beam.
            var input = field.Q("unity-text-input");
            if (input != null) input.style.cursor = s_slideCursor;
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

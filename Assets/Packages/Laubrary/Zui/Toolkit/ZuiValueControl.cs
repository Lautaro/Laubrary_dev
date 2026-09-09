// ZuiValueControl — UI Toolkit counterpart of the IMGUI ZUIValueControl: one labelled row editing
// a ZUIValue, whose body switches between a static Slider, a min↔max range, or the full envelope
// curve editor. Operates on the SAME runtime ZUIValue data the IMGUI control edits — nothing about
// assets or play-mode evaluation changes.
//
// RIGHT-CLICK is the single way into the mode / multiplier / copy-paste menu, on every mode and
// anywhere on the control. There is deliberately no "⋯" button: one gesture, learned once, works on
// every value in every tool, instead of a button that has to be found, placed and kept from
// crowding a packed row. The only elements that keep their own right-click are real editing
// surfaces — an envelope removes a point, a step sequencer clears a step.
//
// Curve-mode layout (2026-07-23, per user direction): the LABEL is the fold toggle — click it to
// expand/collapse. Expanded = label, the full envelope below (NO thumbnail), then the optional
// per-point numeric inputs row. The menu gains "Show point values" / "Show numeric inputs" /
// "Inputs for selected points only".
//
// COLLAPSED, an animated value is a MicroSlider with a curve badge (2026-09-07): the same filled
// track every static dial uses, its fill and readout showing the value AT THE CURRENT FRAME, the
// label inside the track, and an 18px thumbnail of the shape at the right end. A bare thin line in
// a 150x18 box — what this used to draw — reads as Unity's native curve field and tells an author
// nothing about what the dial is worth right now. The track is deliberately NOT draggable (an
// animated value has no single value to drag to); clicking it, or the badge, opens the editor. The
// three envelope modes (Curve, Steps, Oscillation) all fold to this same row.
//
// Not yet ported from the IMGUI version (tracked in CHANGELOG/zui.md): the "★" envelope-preset
// popup (ZUIEnvelopePresetPopup is IMGUI and lives in ZUI.Editor).
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiValueControl : VisualElement
    {
        /// Per-field gating + range — mirrors the IMGUI ZUIValueControl.Options surface.
        public class Options
        {
            public bool allowStatic = true;
            public bool allowMinMax = true;
            public bool allowCurve = true;
            public bool allowSteps = true;   // Steps = a step-sequencer mode, a peer of the others
            // Oscillation = a sine between two envelopes. OPT-IN, unlike its peers: a host whose own evaluator
            // does not understand the mode would silently flatten it to the static value, which is worse than
            // not offering it. A host that CAN evaluate it turns it on (SpriteFx does).
            public bool allowOscillation = false;
            // Offer the "Cycles" envelope generator (a sawtooth for a value that WRAPS — a rotation angle, a
            // hue). Off by default: it only makes sense where the field's value loops, so a field opts in.
            public bool cyclic = false;
            public float absMin = 0f;
            public float absMax = 10f;
            public string[] multiplierIds = null;
            public float? staticDefault = null;   // double-click reset target for the Static slider
            public bool hideCurveTiming = false;  // hide the Duration / Warmup / Loop row
            public bool hideCurveRange = false;   // hide the Value-Range row AND pin Y range to [absMin, absMax]
            public bool hideLiveReadout = false;
            public float controlWidth = 170f;     // width of the slider/range/envelope body (packed-row support)
            // Grow to fill available horizontal space (up to maxWidthFactor × controlWidth), instead of
            // sitting at a fixed width and leaving the rest of a wide row empty. Off by default so tools that
            // haven't opted in keep their exact layout; Pyre turns it on.
            public bool grow = false;
            public float maxWidthFactor = 3.2f;   // how far a grown control may widen past its compact width
            // Static (MicroSlider) mode only: draw the field's LABEL inside the slider track (the point of a
            // MicroSlider) instead of as a separate label to its left. MinMax/Curve modes always use an
            // external label — they aren't MicroSliders. Default true; set false to keep the label outside.
            public bool sliderLabelInside = true;
            // Decimal places to show/snap to. -1 = float-noise cleanup only. 0 = INTEGER (a discrete value —
            // arm count, frame count — that can't be fractional): the slider snaps to whole numbers and shows
            // no decimals, and MinMax mode uses integer fields.
            public int decimals = -1;
            // Draw vertical frame-boundary markers in the curve editor at each animation frame's position,
            // labelled with the frame index (labels auto-thin when dense). 0 = off. Set it to the spec's frame
            // count so an author can see exactly which frame each part of an over-life envelope lands on.
            public int frameCount = 0;
            // INDEX markers — the frame-marker mechanism reused for a curve whose X axis is a particle INDEX
            // (not time), e.g. scale-by-index. > 1 draws vertical lines labelled 0,1,2,… by index (same line/
            // label/thinning code as the frame markers) and SUPPRESSES the frame lines (an index-mapped curve
            // never shows frames). 0 (default) = off, so every other control keeps its frame-marker behaviour.
            public int indexMarkerCount = 0;
            // Where the host's playhead is, as a FRAME INDEX into frameCount. Optional: it only feeds the
            // FOLDED state, which shows the animated value at that frame the way a MicroSlider shows a static
            // one. Null (the default, and what every existing caller passes) falls back to frame 0, so a host
            // that has no clock still gets a filled track rather than a bare line — nothing has to change to
            // compile. Polled, not pushed: the fold refreshes itself a few times a second while it is visible.
            public Func<int> currentFrame = null;
            // Optional axis captions drawn on the envelope (X along the bottom, Y up the left). Null (default) =
            // none, so every existing Val/curve is visually unchanged.
            public string xAxisLabel = null;
            public string yAxisLabel = null;
            // Optional Y-axis colour legend for the curve editor: maps a curve output VALUE to the colour it
            // resolves to (a gradient / palette lookup the curve is driving). Non-null draws a vertical colour
            // strip up the envelope's Y axis and tints each point's handle with its evaluated colour — so an
            // author sees which colour each value produces. Null (default) = unchanged. The func receives values
            // in the curve's own [yMin..yMax] range, so a caller must map from the same range its curve edits.
            public Func<float, Color> yColorFor = null;

            public Options WithRange(float lo, float hi) { absMin = lo; absMax = hi; return this; }
            public Options WithGrow(float maxFactor = 2.4f) { grow = true; maxWidthFactor = maxFactor; return this; }
            public Options WithCyclic() { cyclic = true; return this; }
            public Options WithMultipliers(params string[] ids) { multiplierIds = ids; return this; }
            public Options WithDefault(float value) { staticDefault = value; return this; }
            public Options WithoutCurveExtras() { hideCurveTiming = true; hideCurveRange = true; return this; }
            public Options WithoutLiveReadout() { hideLiveReadout = true; return this; }
            public Options WithWidth(float w) { controlWidth = w; return this; }
            public Options WithFrameLines(int frames) { frameCount = frames; return this; }
            public Options WithCurrentFrame(Func<int> frame) { currentFrame = frame; return this; }
            public Options WithIndexMarkers(int count) { indexMarkerCount = count; return this; }
            public Options WithAxisLabels(string x, string y) { xAxisLabel = x; yAxisLabel = y; return this; }
            public Options WithYColor(Func<float, Color> map) { yColorFor = map; return this; }

            public Options Clone() => (Options)MemberwiseClone();
        }

        // Curve-mode UI state keyed by the ZUIValue instance so it survives window rebuilds
        // (undo/redo rebuilds the whole tree) — the same trick the IMGUI control uses.
        class CurveUiState
        {
            public bool expanded;
            public bool showValues;
            public bool showInputs;
            public bool inputsSelectedOnly;
            public int oscEdit;   // Oscillation mode: which of Floor / Ceiling / Rate the editor is bound to
        }
        static readonly Dictionary<ZUIValue, CurveUiState> s_state = new();
        static CurveUiState GetState(ZUIValue v)
        {
            if (!s_state.TryGetValue(v, out var st)) { st = new CurveUiState(); s_state[v] = st; }
            return st;
        }

        readonly ZUIValue _v;
        readonly Options _opt;
        readonly string _label;
        readonly string _tooltip;
        readonly VisualElement _content;
        readonly Label _readout;

        ZuiEnvelope _env;
        VisualElement _inputsHost;
        readonly List<(int index, FloatField field)> _inputFields = new();

        /// The value being edited. Read-only access for a host laying the control out: how much room it
        /// needs depends on its MODE — Static and Min-Max are one slider row, while the envelope modes draw a
        /// curve and open to a full-width editor — and a host packing controls into a flowing row has no
        /// other way to tell those apart.
        public ZUIValue Value => _v;

        /// True when this control is RIGHT NOW drawing a curve rather than a single row, so a host packing a
        /// flow knows it earns a line of its own instead of being squeezed beside a radio with its label left
        /// stranded. An envelope-mode value that is FOLDED is not curve-shaped: since 2026-09-07 it collapses
        /// to a MicroSlider-sized row, and a slider given a whole 100%-wide line is the space-economy waste
        /// the fold exists to avoid. Folding therefore re-decides the layout, exactly as a mode switch does —
        /// which is why both raise ModeChanged.
        public bool IsCurveShaped =>
            _v != null && (_v.mode == ZUIValue.Mode.Curve || _v.mode == ZUIValue.Mode.Steps ||
                           _v.mode == ZUIValue.Mode.Oscillation) && GetState(_v).expanded;

        /// Fires when the room this control deserves changes — a MODE switch (Static ⇄ Envelope ⇄ Steps ⇄ …)
        /// or a FOLD toggle. A host laying these out in a flowing row needs it: either one rebuilds only the
        /// control itself, so nothing else would ever revisit that decision. Without it, switching a value to
        /// Envelope leaves the new curve squeezed into the slider-sized slot it used to occupy.
        public Action ModeChanged;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation (slider drags, curve edits, mode/multiplier changes, paste).
        public Action OnChanged;

        public ZuiValueControl(string label, ZUIValue v, Options options, string tooltip)
        {
            _v = v ?? throw new ArgumentNullException(nameof(v));
            _opt = options ?? new Options();
            _label = label;
            AddToClassList("zui-value");   // row-level class → uniform bottom spacing (see ZuiToolkit.uss)
            _tooltip = tooltip;
            // No ⋯ button — the config menu opens on right-click; hint it on the row tooltip so it's discoverable.
            this.tooltip = string.IsNullOrEmpty(tooltip) ? "Right-click to configure." : tooltip + "  (right-click to configure)";

            if (_opt.grow)
            {
                // Fill available width up to a cap, so a control uses the horizontal space a wide pane offers
                // instead of leaving it empty — and packed pairs share the row and reflow when it's narrow.
                style.flexGrow = 1f;
                style.flexShrink = 1f;
                style.minWidth = _opt.controlWidth;
                style.maxWidth = _opt.controlWidth * Mathf.Max(1f, _opt.maxWidthFactor);
            }

            _content = new VisualElement();
            Add(_content);

            _readout = Z.Text("", ZuiText.Small,
                "Live evaluated value (wall-clock time) — shown only while the value is dynamic.");
            Add(_readout);
            _readout.schedule.Execute(UpdateReadout).Every(250);

            RebuildAll();
            UpdateReadout();

            // RIGHT-CLICK ANYWHERE on the control opens the mode menu. This is the ONE route — there is no
            // button — so it has to work on every mode's body, not just on the label or the empty space
            // beside it. A user who right-clicks the slider they are looking at must get the menu; being
            // told to aim at the label instead is the same failure as having no affordance at all.
            //
            // TrickleDown, so it beats children that own their own right-click: a MicroSlider's display-options
            // menu, a Min-Max slider's manipulators. Those swallowed the event in BUBBLE phase, which is
            // exactly how a value switched to Static or Min-Max became a value that could never go back.
            //
            // The one exception is a real EDITING surface, where right-click already means something: an
            // envelope removes a point, a step sequencer clears a step. Those keep their gesture — the menu
            // is still one right-click away on the header or the margin around them.
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                if (OwnsRightClick(e.target as VisualElement)) return;
                ShowMenu(this);
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        Label FieldLabel(string text)
        {
            var l = new Label(text) { tooltip = _tooltip };
            l.AddToClassList("zui-field__label");
            l.style.marginTop = 3f;
            return l;
        }

        /// The EXPANDED header's label, which is also the fold toggle — one implementation for all three
        /// envelope modes, which had three copies of it that could (and did) drift apart.
        Label FoldLabel(CurveUiState st, string text, string what)
        {
            var l = new Label(text)
            {
                tooltip = (_tooltip + " ").TrimStart() + "Click to collapse the " + what + ".",
            };
            l.AddToClassList("zui-field__label");
            l.AddToClassList("zui-fold-label");
            l.style.marginTop = 3f;
            l.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                st.expanded = !st.expanded;
                RebuildAll();
                // A fold changes how wide this control deserves to be (see IsCurveShaped), and only the host
                // can act on that — collapsing without telling it leaves a slider-sized row on a 100% line.
                ModeChanged?.Invoke();
                e.StopPropagation();
            });
            return l;
        }

        // ── full rebuild (mode switches, fold toggles, display-option toggles) ──────
        void RebuildAll()
        {
            _content.Clear();
            _env = null;
            _inputsHost = null;
            _inputFields.Clear();

            switch (_v.mode)
            {
                case ZUIValue.Mode.Static:
                {
                    // MicroSlider: label AND value sit inside the filled track (the whole point of a
                    // MicroSlider), the fill is the handle — the old-ZUI look, half a vanilla Slider's height.
                    // Label inside by default, so no external label; sliderLabelInside=false keeps it outside.
                    bool inside = _opt.sliderLabelInside;
                    var slider = Z.MicroSlider(inside ? _label : "", _v.staticValue, _opt.absMin, _opt.absMax,
                        _tooltip, val => Mutate(() => _v.staticValue = val), _opt.controlWidth, showValue: true,
                        defaultValue: _opt.staticDefault, decimals: _opt.decimals);
                    // The caption is inside the track, so a long one has nowhere to go but under the value.
                    // Reflected hosts name a dial after its FIELD, and nobody sized the control for that name.
                    if (inside) slider.FitCaption(_opt.controlWidth);
                    AddHeaderRow(inside ? null : _label, slider);
                    break;
                }
                case ZUIValue.Mode.MinMax:
                    AddHeaderRow(_label, Z.MinMax(_v.min, _v.max, _opt.absMin, _opt.absMax, _tooltip,
                        (lo, hi) => Mutate(() => { _v.min = lo; _v.max = hi; }),
                        Mathf.Max(60f, _opt.controlWidth - 90f), _opt.decimals == 0));
                    break;
                case ZUIValue.Mode.Curve:
                    BuildCurve();
                    break;
                case ZUIValue.Mode.Steps:
                    BuildSteps();
                    break;
                case ZUIValue.Mode.Oscillation:
                    BuildOscillation();
                    break;
            }
        }

        void AddHeaderRow(string label, VisualElement body)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.alignItems = Align.FlexStart;
            if (!string.IsNullOrEmpty(label)) row.Add(FieldLabel(label));
            // T-0192 — this method only ever builds the Static (a bare Z.MicroSlider) or MinMax body: a
            // scalar value display, not a curve graph. `_opt.grow` exists for the CURVE/Steps/Oscillation
            // envelope bodies (BuildCurve/BuildSteps/BuildOscillation, which size themselves directly and
            // never call this method) — applying it here too used to let a solo Static Val balloon to fill
            // its row exactly like a bare Dial()'d MicroSlider must never do (PM by-eye, shaper_3col.png:
            // Rotation/Width/Specular/Thickness/Height Δ/Quantise/Extent ƒ all stretching while their
            // row-mates stayed at the ~150-170px norm). A Static/MinMax body now always sits at its own
            // explicit width, the same as Dial()'s bare MicroSlider.
            body.style.flexGrow = 0f;
            body.style.flexShrink = 0f;
            // The body is the row's last child and the control carries its own row margins: the zui-row child gap
            // after it would only widen this control past its controlWidth and add a second bottom margin, so a
            // value control packed beside plain MicroSliders would sit 6 px out of grid and 5 px taller than its row.
            body.style.marginRight = 0f;
            body.style.marginBottom = 0f;
            row.style.marginBottom = 0f;
            row.Add(body);
            _content.Add(row);
        }

        // ── the folded state, shared by Curve / Steps / Oscillation ─────────────────
        // Layout, left to right, inside ONE "zui-row":
        //   [ ZuiMicroSlider track — label inside left, value-at-current-frame inside right ][ 18px badge ]
        // The track is a REAL Z.MicroSlider (same element, same classes, same painted gradient fill), not a
        // look-alike, so it can never drift from the static dials it sits beside. It is picking-Ignore, which
        // is what makes it non-draggable AND hands its clicks to the row: an animated value has no one value
        // to drag to, so a draggable-looking track that silently discarded the drag would be a worse lie than
        // the bare line this replaces. The badge carries the shape, so nothing is hidden by the fold.
        const float BadgeSize = 18f;   // == the .zui-microslider track height, so the row stays one line
        const float BadgeGap = 3f;

        VisualElement BuildFoldedRow(CurveUiState st, string what)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.alignItems = Align.Center;
            // The control owns its own bottom margin (.zui-value), and this row is its only child: a second
            // margin here would put a folded value 4px out of grid with the static sliders beside it.
            row.style.marginBottom = 0f;

            string modeWord = _v.mode == ZUIValue.Mode.Steps ? "Stepped"
                            : _v.mode == ZUIValue.Mode.Oscillation ? "Oscillating" : "Animated";
            row.tooltip = (_tooltip + " ").TrimStart() + modeWord +
                          ": the track shows this value at the current frame and cannot be dragged. " +
                          "Click to open the " + what + ".";

            FoldedRange(out float lo, out float hi);
            var track = Z.MicroSlider(_label ?? "", FoldedValue(), lo, hi, row.tooltip, _ => { },
                Mathf.Max(60f, _opt.controlWidth - BadgeSize - BadgeGap), showValue: true,
                defaultValue: null, decimals: _opt.decimals,
                // Its own EditorPrefs namespace: sharing the caption-keyed one would let a "Numeric input"
                // toggled on a same-named STATIC slider put a dead input field inside this read-only track.
                prefsKey: "zuivalue.folded");
            track.pickingMode = PickingMode.Ignore;
            // ZuiMicroSlider's ctor appends "Drag to set; Shift = fine" to whatever it is given. That is true of
            // every other MicroSlider and false of this one, so replace it rather than let a stale promise ride.
            track.tooltip = row.tooltip;
            // T-0192: a folded value is a scalar readout, so it sits at its explicit width exactly like the
            // Static/MinMax bodies in AddHeaderRow — never grown to fill the row. The whole point of the fold
            // is that it reads as one of the sliders beside it, which a 3.2x-wide one would not.
            track.style.flexGrow = 0f;
            track.style.flexShrink = 0f;
            track.style.marginRight = 0f;
            track.style.marginBottom = 0f;
            row.Add(track);

            var badge = new ModeThumb(_v, FoldedPlayheadNorm, lo, hi) { tooltip = row.tooltip };
            badge.style.width = BadgeSize;
            badge.style.height = BadgeSize;
            badge.style.flexGrow = 0f;
            badge.style.flexShrink = 0f;
            badge.style.marginLeft = BadgeGap;
            // ".zui-row > *" hands every child a 6px right gap; the badge is this row's LAST child, so that gap
            // would only widen the control past its controlWidth — the same reason AddHeaderRow zeroes it.
            badge.style.marginRight = 0f;
            badge.style.marginBottom = 0f;
            row.Add(badge);

            // One handler for the whole row: the track ignores picking, and the badge lets its pointer-down
            // bubble here, so both "click the track" and "click the badge" land on the same expand.
            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                st.expanded = true;
                RebuildAll();
                ModeChanged?.Invoke();   // the editor that just opened wants the whole line (see IsCurveShaped)
                e.StopPropagation();
            });

            // Follow the host's playhead while folded. Polled at the readout's cadence rather than pushed,
            // so a host only has to hand over a Func<int> and never has to notify anything.
            if (_opt.currentFrame != null)
                row.schedule.Execute(() =>
                {
                    track.value = FoldedValue();
                    badge.MarkDirtyRepaint();
                }).Every(200);

            return row;
        }

        /// The track's scale. Normally the field's own declared range, so a folded dial fills to the same
        /// place a static one beside it would — widened only if the author pushed the curve's value range
        /// past it, which would otherwise pin the fill at one end and read as a stuck value.
        void FoldedRange(out float lo, out float hi)
        {
            lo = Mathf.Min(_opt.absMin, _opt.absMax);
            hi = Mathf.Max(_opt.absMin, _opt.absMax);
            if (_v.mode != ZUIValue.Mode.Static && _v.mode != ZUIValue.Mode.MinMax)
            {
                lo = Mathf.Min(lo, Mathf.Min(_v.yMin, _v.yMax));
                hi = Mathf.Max(hi, Mathf.Max(_v.yMin, _v.yMax));
            }
            if (hi - lo < 1e-4f) hi = lo + 1f;
        }

        /// Where the host's playhead sits along the envelope, 0..1. Frame i of N maps to i/(N-1) — the same
        /// mapping the frame lines on the expanded envelope are drawn at, so the fill and the ticks agree.
        /// No frame source (or a single-frame spec) means frame 0, which is a real, honest reading.
        float FoldedPlayheadNorm()
        {
            if (_opt.currentFrame == null || _opt.frameCount <= 1) return 0f;
            return Mathf.Clamp01(_opt.currentFrame() / (float)(_opt.frameCount - 1));
        }

        float FoldedValue() => SampleAtNorm(_v, FoldedPlayheadNorm());

        /// One sampler for the folded track AND its badge, so the number in the track can never disagree with
        /// the shape drawn beside it.
        static float SampleAtNorm(ZUIValue v, float t) => v.mode switch
        {
            ZUIValue.Mode.Curve => v.EvaluateCurveAtNorm(t),
            ZUIValue.Mode.Steps => v.EvaluateStepsAtNorm(t),
            ZUIValue.Mode.Oscillation => v.EvaluateOscillationAtNorm(t),
            _ => v.staticValue,
        };

        /// True when the clicked element is an editing surface whose own right-click gesture must survive —
        /// an envelope (right-click removes a point) or a step sequencer. Everything else hands its
        /// right-click to the mode menu.
        static bool OwnsRightClick(VisualElement target)
        {
            for (var e = target; e != null; e = e.parent)
            {
                if (e is ZuiEnvelope || e is ZuiStepSequencer) return true;
                if (e is ZuiValueControl) return false;   // reached our own root — nothing claimed it
            }
            return false;
        }

        // ── curve mode ──────────────────────────────────────────────────────────────
        void BuildCurve()
        {
            _v.EnsureCurveDefaults();
            if (_opt.hideCurveRange)
            {
                _v.yMin = Mathf.Min(_opt.absMin, _opt.absMax);
                _v.yMax = Mathf.Max(_opt.absMin, _opt.absMax);
            }
            var st = GetState(_v);

            // Collapsed: the whole row is ONE MicroSlider-shaped track (the label sits INSIDE it, so no
            // separate fold label is drawn) plus the shape badge. Clicking either expands.
            if (!st.expanded) { _content.Add(BuildFoldedRow(st, "curve editor")); return; }

            // Header: the label IS the fold toggle (mockup: plain label, no caret — hover affordance via USS).
            // The config menu opens on RIGHT-CLICK (wired in the ctor), so there's no ⋯ button on the row.
            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.Add(FoldLabel(st, _label ?? "Curve", "curve editor"));
            _content.Add(header);

            // Index markers (a particle-index-mapped curve) take priority over frame lines: when indexMarkerCount
            // is set the verticals are labelled by index and the frame lines are suppressed. Default (0) leaves
            // the frame-marker behaviour exactly as before.
            bool indexMode = _opt.indexMarkerCount > 1;
            var envOptions = new ZuiEnvelopeOptions
            {
                xMin = 0f, xMax = 1f,
                yMin = _v.yMin, yMax = _v.yMax,
                curveColor = new Color(0.4f, 0.85f, 1f),
                showValueLabels = st.showValues,
                showFrameLines = indexMode || _opt.frameCount > 1,
                frameCount = indexMode ? _opt.indexMarkerCount : _opt.frameCount,
                markerMode = indexMode ? ZuiEnvelopeOptions.MarkerMode.Index : ZuiEnvelopeOptions.MarkerMode.Frame,
                xAxisLabel = _opt.xAxisLabel,
                yAxisLabel = _opt.yAxisLabel,
                yColorFor = _opt.yColorFor,
            };
            // Mockup layout: the envelope fills the available width (taller than the old 90px), with
            // the optional numeric-inputs COLUMN standing to its right.
            _env = new ZuiEnvelope(_v.points, envOptions, _tooltip, 200f, 140f);
            _env.style.width = StyleKeyword.Auto;
            _env.style.flexGrow = 1f;
            _env.style.flexShrink = 1f;
            _env.OnBeforeMutate += () => OnBeforeMutate?.Invoke();
            _env.OnChanged += () =>
            {
                OnChanged?.Invoke();
                SyncInputFields();
            };
            _env.OnSelectionChanged += () => { if (st.showInputs && st.inputsSelectedOnly) RebuildInputsRow(); };

            var envRow = new VisualElement();
            envRow.style.flexDirection = FlexDirection.Row;
            envRow.style.alignItems = Align.Stretch;
            envRow.Add(_env);
            _inputsHost = new VisualElement();
            _inputsHost.style.flexShrink = 0f;
            _inputsHost.style.marginLeft = 4f;
            envRow.Add(_inputsHost);
            _content.Add(envRow);
            if (st.showInputs) RebuildInputsRow();

            if (!_opt.hideCurveTiming) AddTimingRow();

            if (!_opt.hideCurveRange)
            {
                var range = new VisualElement();
                range.AddToClassList("zui-row");
                range.Add(Z.Text("Value range", ZuiText.Small, "The curve's min/max output values."));
                range.Add(Z.Field("min", "The curve's minimum output value.",
                    Z.Float(_v.yMin, "The curve's minimum output value.", val => Mutate(() =>
                    {
                        _v.yMin = val;
                        if (_v.yMax < _v.yMin) _v.yMax = _v.yMin;
                        envOptions.yMin = _v.yMin; envOptions.yMax = _v.yMax;
                        ClampPointsToRange(); _env?.Refresh(); SyncInputFields();
                    }), 46f)));
                range.Add(Z.Field("max", "The curve's maximum output value.",
                    Z.Float(_v.yMax, "The curve's maximum output value.", val => Mutate(() =>
                    {
                        _v.yMax = Mathf.Max(val, _v.yMin);
                        envOptions.yMin = _v.yMin; envOptions.yMax = _v.yMax;
                        ClampPointsToRange(); _env?.Refresh(); SyncInputFields();
                    }), 46f)));
                _content.Add(range);
            }
        }

        // Shared timing row (Dur / Warm / Loop / Cool) — used by BOTH Envelope and Steps (same duration/warmup/
        // cooldown fields).
        void AddTimingRow()
        {
            var timing = new VisualElement();
            timing.AddToClassList("zui-row");
            timing.Add(Z.Field("Dur s", "Seconds one playthrough takes.",
                Z.Float(_v.duration, "Seconds one playthrough takes.",
                    val => Mutate(() => _v.duration = val), 46f)));
            timing.Add(Z.Field("Warm s", "Seconds before it starts (holds its first value).",
                Z.Float(_v.warmup, "Seconds before it starts (holds its first value).",
                    val => Mutate(() => _v.warmup = val), 46f)));
            bool loop = _v.cooldown >= 0f;
            VisualElement coolField = null;
            timing.Add(Z.Toggle("Loop", "Restart after a cooldown pause instead of holding the final value.",
                loop, on =>
                {
                    Mutate(() => _v.cooldown = on ? 0f : -1f);
                    coolField?.Shown(on);
                }));
            coolField = Z.Field("Cool s", "Pause after a playthrough before looping.",
                Z.Float(Mathf.Max(0f, _v.cooldown), "Pause after a playthrough before looping.",
                    val => Mutate(() => _v.cooldown = Mathf.Max(0f, val)), 46f));
            coolField.Shown(loop);
            timing.Add(coolField);
            _content.Add(timing);
        }

        // ── steps mode (a step sequencer) ─────────────────────────────────────────────
        void BuildSteps()
        {
            _v.EnsureStepsDefaults();
            if (_opt.hideCurveRange)
            {
                _v.yMin = Mathf.Min(_opt.absMin, _opt.absMax);
                _v.yMax = Mathf.Max(_opt.absMin, _opt.absMax);
            }

            // Steps folds exactly like its two envelope peers. It used to be the odd one out — always fully
            // drawn, a sequencer block deep enough to push everything below it off the pane, with no way to put
            // it away — while Curve and Oscillation both collapsed to a row. All three are "an animated value"
            // to an author, so all three now read the same folded and cost the same vertical space.
            var st = GetState(_v);
            if (!st.expanded) { _content.Add(BuildFoldedRow(st, "step sequencer")); return; }

            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.Add(FoldLabel(st, _label ?? "Steps", "step sequencer"));
            _content.Add(header);

            var seq = new ZuiStepSequencer(_v.steps, _v.yMin, _v.yMax);
            seq.style.flexGrow = 1f;
            seq.OnBeforeMutate += () => OnBeforeMutate?.Invoke();
            seq.OnChanged += () => OnChanged?.Invoke();
            _content.Add(seq);

            _content.Add(Z.MicroSlider("Sections", _v.steps.Count, 2f, 32f,
                "How many held sections divide the envelope.",
                v => Mutate(() => { _v.SetStepCount(Mathf.RoundToInt(v)); seq.Refresh(); }), 200f, decimals: 0));

            if (!_opt.hideCurveTiming) AddTimingRow();
            if (!_opt.hideCurveRange) AddStepsRange(seq);
        }

        void AddStepsRange(ZuiStepSequencer seq)
        {
            var range = new VisualElement();
            range.AddToClassList("zui-row");
            range.Add(Z.Text("Value range", ZuiText.Small, "The steps' min/max output values."));
            range.Add(Z.Field("min", "The minimum output value.",
                Z.Float(_v.yMin, "The minimum output value.", val => Mutate(() =>
                {
                    _v.yMin = val;
                    if (_v.yMax < _v.yMin) _v.yMax = _v.yMin;
                    ClampStepsToRange(); seq.SetRange(_v.yMin, _v.yMax);
                }), 46f)));
            range.Add(Z.Field("max", "The maximum output value.",
                Z.Float(_v.yMax, "The maximum output value.", val => Mutate(() =>
                {
                    _v.yMax = Mathf.Max(val, _v.yMin);
                    ClampStepsToRange(); seq.SetRange(_v.yMin, _v.yMax);
                }), 46f)));
            _content.Add(range);
        }

        // ── oscillation mode (a sine carrier between two envelopes) ───────────────────
        // The carrier is fixed — the three things an author shapes are the FLOOR the wave dips to, the CEILING
        // it peaks at, and the RATE it swings at, each a full envelope over the same playthrough. One envelope
        // editor is shown at a time (picked by a segmented switch) with the resolved wave always visible above
        // it, rather than three stacked editors: vertical space is the scarce resource and the thing an author
        // is actually judging is the RESULT, not three curves side by side.
        void BuildOscillation()
        {
            if (_opt.hideCurveRange)
            {
                _v.yMin = Mathf.Min(_opt.absMin, _opt.absMax);
                _v.yMax = Mathf.Max(_opt.absMin, _opt.absMax);
            }
            _v.EnsureOscillationDefaults();
            var st = GetState(_v);

            if (!st.expanded) { _content.Add(BuildFoldedRow(st, "oscillation editor")); return; }

            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.Add(FoldLabel(st, _label ?? "Oscillation", "oscillation editor"));
            _content.Add(header);

            var preview = new OscThumb(_v)
            {
                tooltip = "The wave this value actually produces: the sine carrier travelling between the Floor " +
                          "and Ceiling envelopes at the Rate envelope's speed. Updates as you drag.",
            };
            preview.style.height = 48f;
            preview.style.flexGrow = 1f;
            _content.Add(preview);

            int edit = Mathf.Clamp(st.oscEdit, 0, 2);
            _content.Add(Z.Segmented(edit, new[] { "Floor", "Ceiling", "Rate" },
                "Which of the wave's three envelopes the editor below edits: the value it dips to at every " +
                "trough, the value it peaks at, or how many full swings it makes across one playthrough.",
                i => { st.oscEdit = i; RebuildAll(); }));

            var pts = edit == 0 ? _v.oscMin : edit == 1 ? _v.oscMax : _v.oscRate;
            bool rate = edit == 2;
            var envOptions = new ZuiEnvelopeOptions
            {
                xMin = 0f, xMax = 1f,
                yMin = rate ? 0f : Mathf.Min(_v.yMin, _v.yMax),
                yMax = rate ? Mathf.Max(0.1f, _v.oscRateMax) : Mathf.Max(_v.yMin, _v.yMax),
                curveColor = rate ? new Color(1f, 0.76f, 0.35f) : new Color(0.4f, 0.85f, 1f),
                showFrameLines = _opt.frameCount > 1,
                frameCount = _opt.frameCount,
                xAxisLabel = _opt.xAxisLabel,
                yAxisLabel = rate ? "cycles" : _opt.yAxisLabel,
            };
            string envTip = rate
                ? "How many full swings the wave makes per playthrough, over the playthrough. A rising rate " +
                  "accelerates the wobble; the phase is integrated, so changing the rate never jumps the wave."
                : (edit == 0
                    ? "The value the wave dips to at every trough, over the playthrough."
                    : "The value the wave peaks at, over the playthrough.");
            _env = new ZuiEnvelope(pts, envOptions, envTip, 200f, 120f);
            _env.style.width = StyleKeyword.Auto;
            _env.style.flexGrow = 1f;
            _env.style.flexShrink = 1f;
            _env.OnBeforeMutate += () => OnBeforeMutate?.Invoke();
            _env.OnChanged += () => { OnChanged?.Invoke(); preview.MarkDirtyRepaint(); };
            _content.Add(_env);

            if (rate)
                _content.Add(Z.MicroSlider("Fastest", _v.oscRateMax, 1f, 32f,
                    "Ceiling of the Rate editor above — the most swings per playthrough it can be dragged to. " +
                    "Raise it for a buzz, lower it for fine control over a slow sway.",
                    v => Mutate(() =>
                    {
                        _v.oscRateMax = v;
                        envOptions.yMax = Mathf.Max(0.1f, _v.oscRateMax);
                        ClampPoints(_v.oscRate, envOptions.yMin, envOptions.yMax);
                        _env?.Refresh();
                        preview.MarkDirtyRepaint();
                    }), _opt.controlWidth, decimals: 0));

            if (!_opt.hideCurveTiming) AddTimingRow();
            if (!_opt.hideCurveRange) AddOscRange(preview, envOptions, rate);
        }

        // The band's own output range — shared by the Floor and Ceiling envelopes, so widening it widens both.
        void AddOscRange(OscThumb preview, ZuiEnvelopeOptions envOptions, bool editingRate)
        {
            var range = new VisualElement();
            range.AddToClassList("zui-row");
            range.Add(Z.Text("Value range", ZuiText.Small, "The lowest and highest values the wave may reach."));
            range.Add(Z.Field("min", "The lowest value the wave may reach.",
                Z.Float(_v.yMin, "The lowest value the wave may reach.", val => Mutate(() =>
                {
                    _v.yMin = val;
                    if (_v.yMax < _v.yMin) _v.yMax = _v.yMin;
                    ApplyOscRange(preview, envOptions, editingRate);
                }), 46f)));
            range.Add(Z.Field("max", "The highest value the wave may reach.",
                Z.Float(_v.yMax, "The highest value the wave may reach.", val => Mutate(() =>
                {
                    _v.yMax = Mathf.Max(val, _v.yMin);
                    ApplyOscRange(preview, envOptions, editingRate);
                }), 46f)));
            _content.Add(range);
        }

        void ApplyOscRange(OscThumb preview, ZuiEnvelopeOptions envOptions, bool editingRate)
        {
            float lo = Mathf.Min(_v.yMin, _v.yMax), hi = Mathf.Max(_v.yMin, _v.yMax);
            ClampPoints(_v.oscMin, lo, hi);
            ClampPoints(_v.oscMax, lo, hi);
            if (!editingRate) { envOptions.yMin = lo; envOptions.yMax = hi; }
            _env?.Refresh();
            preview.MarkDirtyRepaint();
        }

        static void ClampPoints(List<ZUIEnvelopePoint> pts, float lo, float hi)
        {
            if (pts == null) return;
            for (int i = 0; i < pts.Count; i++) pts[i].value = Mathf.Clamp(pts[i].value, lo, hi);
        }

        void ClampStepsToRange()
        {
            for (int i = 0; i < _v.steps.Count; i++)
                _v.steps[i] = Mathf.Clamp(_v.steps[i], Mathf.Min(_v.yMin, _v.yMax), Mathf.Max(_v.yMin, _v.yMax));
        }

        // The per-point numeric inputs — a vertical COLUMN to the envelope's right (mockup), one
        // compact field per shown point, kept in sync with envelope drags; "selected only" narrows
        // it to the envelope's current selection.
        void RebuildInputsRow()
        {
            if (_inputsHost == null) return;
            _inputsHost.Clear();
            _inputFields.Clear();
            var st = GetState(_v);

            bool any = false;
            for (int i = 0; i < _v.points.Count; i++)
            {
                if (st.inputsSelectedOnly && (_env == null || !ContainsIndex(_env.Selected, i))) continue;
                any = true;
                int idx = i;
                var f = new FloatField
                {
                    value = (float)Math.Round(_v.points[idx].value, 5),
                    tooltip = $"Point {idx + 1}'s value.",
                };
                f.style.width = 52f;
                f.style.marginBottom = 2f;
                // Scrub-draggable like every other numeric entry; clamp the drag to the curve's own range
                // (the onChanged clamp below still runs on top).
                ZuiScrub.Attach(f, Mathf.Min(_v.yMin, _v.yMax), Mathf.Max(_v.yMin, _v.yMax));
                f.RegisterValueChangedCallback(e => Mutate(() =>
                {
                    _v.points[idx].value = Mathf.Clamp(e.newValue,
                        Mathf.Min(_v.yMin, _v.yMax), Mathf.Max(_v.yMin, _v.yMax));
                    _env?.Refresh();
                }));
                _inputFields.Add((idx, f));
                _inputsHost.Add(f);
            }
            if (!any && st.inputsSelectedOnly)
            {
                var hint = Z.Text("select\npoints", ZuiText.Subtle,
                    "Inputs are set to show for selected points only — box-select points in the envelope to edit them numerically.");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.width = 52f;
                _inputsHost.Add(hint);
            }
        }

        static bool ContainsIndex(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }

        void SyncInputFields()
        {
            var st = GetState(_v);
            if (!st.showInputs) return;
            // point count may have changed (insert/remove) — rebuild; otherwise just refresh values.
            bool stale = false;
            foreach (var (index, _) in _inputFields)
                if (index >= _v.points.Count) { stale = true; break; }
            int expected = 0;
            for (int i = 0; i < _v.points.Count; i++)
                if (!st.inputsSelectedOnly || (_env != null && ContainsIndex(_env.Selected, i))) expected++;
            if (stale || expected != _inputFields.Count) { RebuildInputsRow(); return; }
            foreach (var (index, field) in _inputFields)
                field.SetValueWithoutNotify((float)Math.Round(_v.points[index].value, 5));
        }

        void ClampPointsToRange()
        {
            for (int i = 0; i < _v.points.Count; i++)
                _v.points[i].value = Mathf.Clamp(_v.points[i].value, _v.yMin, _v.yMax);
        }

        // ── live readout ────────────────────────────────────────────────────────────
        void UpdateReadout()
        {
            bool show = _v.IsDynamic && !_opt.hideLiveReadout;
            _readout.Shown(show);
            if (!show) return;

            float now = Application.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup;
            if (_v.mode == ZUIValue.Mode.MinMax)
            {
                float m = _v.Multiplier();
                _readout.text = _v.HasMultiplier
                    ? $"live: {_v.min * m:0.###} – {_v.max * m:0.###}   (range × {_v.multiplierId} {m:0.##})"
                    : $"live: {_v.min:0.###} – {_v.max:0.###}  (random)";
            }
            else
            {
                float raw = _v.EvaluateRaw(now);
                float val = raw * _v.Multiplier();
                _readout.text = _v.HasMultiplier
                    ? $"live: {val:0.###}   ({raw:0.###} × {_v.multiplierId} {_v.Multiplier():0.##})"
                    : $"live: {val:0.###}";
            }
        }

        // ── the ⋯ menu (mode / display / multiplier / copy-paste), a ZUI popover anchored to the ⋯ button ──
        void ShowMenu(VisualElement anchor)
        {
            bool hasMultiplier = _opt.multiplierIds != null && _opt.multiplierIds.Length > 0;
            var menu = Z.Menu(anchor);

            // Mode → one radio group (pick-one-then-close). Only the allowed modes appear, same as the old
            // conditional AddItem set; a "Mode" section heading stands in for the old "Mode/" submenu prefix,
            // shown only when a multiplier section also exists (else the radio needs no heading of its own).
            var modes = new System.Collections.Generic.List<ZUIValue.Mode>();
            var modeLabels = new System.Collections.Generic.List<string>();
            if (_opt.allowStatic) { modes.Add(ZUIValue.Mode.Static); modeLabels.Add("Static"); }
            if (_opt.allowMinMax) { modes.Add(ZUIValue.Mode.MinMax); modeLabels.Add("Min-Max range"); }
            if (_opt.allowCurve) { modes.Add(ZUIValue.Mode.Curve); modeLabels.Add("Envelope"); }
            if (_opt.allowSteps) { modes.Add(ZUIValue.Mode.Steps); modeLabels.Add("Steps"); }
            if (_opt.allowOscillation) { modes.Add(ZUIValue.Mode.Oscillation); modeLabels.Add("Oscillation"); }
            int modeSel = modes.IndexOf(_v.mode);
            if (hasMultiplier) menu.Section("Mode");
            // T-0321 — the sentence is composed from the modes ACTUALLY offered. It used to be a fixed string
            // ending "…or an Oscillation — a sine swinging between two envelopes at an animatable rate", while
            // Oscillation is opt-in per host (`_opt.allowOscillation`, default false): measured live on a Shaper
            // dial, the menu showed four buttons and its tooltip described five. A tooltip has to read for the
            // state it is in — naming a mode the control cannot reach is a hunt for an affordance that is not
            // there.
            var modeBlurbs = new System.Collections.Generic.Dictionary<ZUIValue.Mode, string>
            {
                { ZUIValue.Mode.Static, "a Static point" },
                { ZUIValue.Mode.MinMax, "a random Min-Max range" },
                { ZUIValue.Mode.Curve, "an Envelope over time" },
                { ZUIValue.Mode.Steps, "held Steps" },
                { ZUIValue.Mode.Oscillation,
                    "an Oscillation — a sine swinging between two envelopes at an animatable rate" },
            };
            var offered = modes.ConvertAll(m => modeBlurbs[m]);
            string modeTip = offered.Count == 0
                ? "How this value is produced."
                : "How this value is produced: " + (offered.Count == 1
                    ? offered[0]
                    : string.Join(", ", offered.GetRange(0, offered.Count - 1))
                      + ", or " + offered[offered.Count - 1])
                  + ".";
            menu.Radio(null, modeLabels.ToArray(), modeSel, modeTip, i => SetMode(modes[i]), closeOnSelect: true);

            // Curve-display options → persistent toggle rows (stay open so several can be flipped in one visit).
            if (_v.mode == ZUIValue.Mode.Curve)
            {
                var st = GetState(_v);
                menu.Separator();
                // The three curve-display toggles, stacked HORIZONTALLY on one row (short names), instead of three
                // full-width stacked rows — a menu is a content-sized card, so a wide row just widens it.
                menu.Custom((body, _) =>
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.marginTop = 2f; row.style.marginBottom = 2f;
                    VisualElement Tog(string label, string tip, bool on, Action<bool> ch)
                    {
                        var tg = Z.ToggleButton(label, tip, on, ch);
                        tg.style.marginRight = 3f;
                        return tg;
                    }
                    // Each of these can force the curve OPEN, which changes the room it deserves — so they take
                    // the same RebuildAll + ModeChanged pair the fold toggle does.
                    void Reopen() { RebuildAll(); ModeChanged?.Invoke(); }
                    row.Add(Tog("Values", "Draw each point's value on the curve.",
                        st.showValues, on => { st.showValues = on; if (!st.expanded) st.expanded = true; Reopen(); }));
                    row.Add(Tog("Inputs", "Show a column of numeric fields for the points beside the curve.",
                        st.showInputs, on => { st.showInputs = on; if (!st.expanded) st.expanded = true; Reopen(); }));
                    row.Add(Tog("Inputs for selected", "Narrow the numeric-inputs column to just the box-selected points.",
                        st.inputsSelectedOnly, on => { st.inputsSelectedOnly = on; if (!st.expanded) st.expanded = true; st.showInputs = true; Reopen(); }));
                    body.Add(row);
                });

                // Envelope SHAPE tools — generators (write the points) + the visual shape picker. All operate on
                // the point list, so they live under Envelope mode only. Each opens a popover anchored to the ⋯
                // button once the menu closes; edits route through the Undo hook.
                Action shapeApplied = () => { OnChanged?.Invoke(); RebuildAll(); UpdateReadout(); };
                menu.Separator();
                menu.Section("Shape");
                menu.Item("Oscillation…", "Generate a wave between two values (start / end / reps / smoothness).",
                    () => ZuiEnvelopeGenerators.OpenOscillation(anchor, _v, _opt.absMin, _opt.absMax,
                        () => OnBeforeMutate?.Invoke(), shapeApplied));
                if (_opt.cyclic)
                    menu.Item("Cycles…", "Generate a sawtooth for a wrapping value (a rotation, a hue).",
                        () => ZuiEnvelopeGenerators.OpenCycles(anchor, _v, _opt.absMin, _opt.absMax,
                            () => OnBeforeMutate?.Invoke(), shapeApplied));
                // The recall grid is INLINE in the menu (not a "Recall shape…" button that opens yet another
                // panel) — the thumbnails ARE the recall control, so clicking one applies it immediately.
                menu.Custom((body, close) =>
                    body.Add(ZuiShapeBrowser.BuildBrowser(_v, () => OnBeforeMutate?.Invoke(), shapeApplied, close)));
            }

            // Steps SHAPE persistence — the same inline thumbnail picker, in its step-sequence flavour: click a
            // built-in / saved bar pattern to apply it, or ＋ Save the current one. Lives under Steps mode only.
            if (_v.mode == ZUIValue.Mode.Steps)
            {
                Action stepsApplied = () => { OnChanged?.Invoke(); RebuildAll(); UpdateReadout(); };
                menu.Separator();
                menu.Section("Shape");
                menu.Custom((body, close) =>
                    body.Add(ZuiShapeBrowser.BuildBrowser(_v, () => OnBeforeMutate?.Invoke(), stepsApplied, close)));
            }

            // Multiplier → its own labelled section + a radio ("(none)" + each id) that stays open as a live setting.
            if (hasMultiplier)
            {
                menu.Separator();
                menu.Section("Multiplier");
                var multOptions = new string[_opt.multiplierIds.Length + 1];
                multOptions[0] = "(none)";
                for (int i = 0; i < _opt.multiplierIds.Length; i++) multOptions[i + 1] = _opt.multiplierIds[i];
                int multSel = _v.HasMultiplier ? System.Array.IndexOf(_opt.multiplierIds, _v.multiplierId) + 1 : 0;
                if (multSel < 0) multSel = 0;
                menu.Radio(null, multOptions, multSel,
                    "Scale this value live by a named multiplier from the host, or (none) for no scaling.",
                    i => Mutate(() => _v.multiplierId = i == 0 ? "" : _opt.multiplierIds[i - 1]));
            }

            // Copy / paste — compact icon buttons, side by side (no text needed).
            menu.Separator();
            bool canPaste = ZUIValue.TryFromClipboardString(EditorGUIUtility.systemCopyBuffer, out _);
            menu.IconRow(
                ("copy", "Copy this value (mode + all mode data) to the clipboard.",
                    () => EditorGUIUtility.systemCopyBuffer = _v.ToClipboardString()),
                ("clipboard-text", canPaste ? "Paste a copied value from the clipboard." : "Paste (clipboard has no value).",
                    canPaste
                        ? () =>
                        {
                            if (ZUIValue.TryFromClipboardString(EditorGUIUtility.systemCopyBuffer, out var parsed))
                                Mutate(() => _v.CopyFrom(parsed));
                            RebuildAll();
                            UpdateReadout();
                            // A paste can bring a different MODE with it, so the host has to re-decide the
                            // width exactly as it would after a mode switch.
                            ModeChanged?.Invoke();
                        }
                        : (Action)null));

            menu.Show();
        }

        void SetMode(ZUIValue.Mode mode)
        {
            Mutate(() =>
            {
                bool wasCurve = _v.mode == ZUIValue.Mode.Curve;
                _v.mode = mode;
                if (mode == ZUIValue.Mode.Curve && !wasCurve)
                {
                    // Seed the curve from the field's range + current value so points start somewhere useful.
                    _v.yMin = Mathf.Min(_opt.absMin, _opt.absMax);
                    _v.yMax = Mathf.Max(_opt.absMin, _opt.absMax);
                    float start = Mathf.Clamp(_v.staticValue, _v.yMin, _v.yMax);
                    _v.points.Clear();
                    _v.points.Add(new ZUIEnvelopePoint(0f, start));
                    _v.points.Add(new ZUIEnvelopePoint(1f, start));
                }
                else if (mode == ZUIValue.Mode.Curve) _v.EnsureCurveDefaults();
                else if (mode == ZUIValue.Mode.Steps)
                {
                    _v.yMin = Mathf.Min(_opt.absMin, _opt.absMax);
                    _v.yMax = Mathf.Max(_opt.absMin, _opt.absMax);
                    _v.EnsureStepsDefaults();
                }
                else if (mode == ZUIValue.Mode.Oscillation)
                {
                    _v.yMin = Mathf.Min(_opt.absMin, _opt.absMax);
                    _v.yMax = Mathf.Max(_opt.absMin, _opt.absMax);
                    _v.EnsureOscillationDefaults();
                }
            });
            RebuildAll();
            UpdateReadout();
            ModeChanged?.Invoke();
        }

        // ── live oscillation preview: the two bounds as faint rails, the resolved wave on top ────────
        class OscThumb : VisualElement
        {
            readonly ZUIValue _v;

            public OscThumb(ZUIValue v)
            {
                _v = v;
                AddToClassList("zui-envelope");
                style.height = 48f;
                generateVisualContent += Paint;
            }

            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f)) return;
                float lo = Mathf.Min(_v.yMin, _v.yMax), hi = Mathf.Max(_v.yMin, _v.yMax);
                if (hi - lo < 1e-4f) hi = lo + 1f;
                var painter = mgc.painter2D;

                // The band's rails first, dim — they are context for the wave, not the subject.
                Rail(painter, r, lo, hi, _v.oscMin, new Color(0.45f, 0.55f, 0.65f, 0.55f));
                Rail(painter, r, lo, hi, _v.oscMax, new Color(0.45f, 0.55f, 0.65f, 0.55f));

                painter.strokeColor = new Color(0.4f, 0.85f, 1f);
                painter.lineWidth = 1.3f;
                painter.BeginPath();
                // Dense enough that a fast carrier reads as a wave rather than as aliased noise.
                int samples = Mathf.Clamp(Mathf.RoundToInt(r.width * 2f), 96, 512);
                for (int s = 0; s <= samples; s++)
                {
                    float t = s / (float)samples;
                    float ty = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, _v.EvaluateOscillationAtNorm(t)));
                    var p = new Vector2(r.x + 2f + t * (r.width - 4f), r.yMax - 2f - ty * (r.height - 4f));
                    if (s == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
            }

            static void Rail(Painter2D painter, Rect r, float lo, float hi,
                             List<ZUIEnvelopePoint> pts, Color color)
            {
                if (pts == null || pts.Count == 0) return;
                painter.strokeColor = color;
                painter.lineWidth = 1f;
                painter.BeginPath();
                const int Samples = 48;
                for (int s = 0; s <= Samples; s++)
                {
                    float t = s / (float)Samples;
                    float ty = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, ZUIEnvelopeEvaluator.Evaluate(pts, t, hi)));
                    var p = new Vector2(r.x + 2f + t * (r.width - 4f), r.yMax - 2f - ty * (r.height - 4f));
                    if (s == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
            }
        }

        // ── the folded row's shape badge ────────────────────────────────────────────
        // An 18px square live thumbnail of whatever shape the value is in — an envelope's curve, a step
        // sequence's staircase, an oscillation's wave — with the playhead marked on it. It is the one thing
        // the folded track cannot say (a track shows a value, not a shape), and it is the affordance that
        // tells an author this dial is animated at all. Deliberately small: it is a badge on a slider, not a
        // graph, and the graph is one click away.
        class ModeThumb : VisualElement
        {
            readonly ZUIValue _v;
            readonly Func<float> _playhead;
            readonly float _lo, _hi;   // the TRACK's scale, handed in so the shape and the readout agree

            public ModeThumb(ZUIValue v, Func<float> playhead, float lo, float hi)
            {
                _v = v;
                _playhead = playhead;
                _lo = lo; _hi = hi;
                // The envelope's own painted surface (it IS a miniature of the editor it opens) plus the
                // badge class, which rounds it to the MicroSlider's corner radius so the pair reads as one
                // control rather than a slider with a square box stuck on the end.
                AddToClassList("zui-envelope");
                AddToClassList("zui-value__badge");
                style.width = 18f;
                style.height = 18f;
                generateVisualContent += Paint;
            }

            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f)) return;
                float lo = _lo, hi = _hi;
                if (hi - lo < 1e-4f) hi = lo + 1f;
                var painter = mgc.painter2D;

                // Playhead first, so the shape draws over it — it is context, like the oscillation rails.
                float ph = _playhead != null ? Mathf.Clamp01(_playhead()) : 0f;
                if (ph > 0f)
                {
                    painter.strokeColor = new Color(1f, 1f, 1f, 0.28f);
                    painter.lineWidth = 1f;
                    painter.BeginPath();
                    float x = r.x + 2f + ph * (r.width - 4f);
                    painter.MoveTo(new Vector2(x, r.y + 1f));
                    painter.LineTo(new Vector2(x, r.yMax - 1f));
                    painter.Stroke();
                }

                painter.strokeColor = new Color(0.4f, 0.85f, 1f);
                painter.lineWidth = 1.2f;
                painter.BeginPath();
                // Steps need the extra samples to keep their risers vertical at this size; a curve is smooth
                // and an oscillation is redrawn from its own evaluator either way.
                int samples = _v.mode == ZUIValue.Mode.Steps ? 96 : 40;
                for (int s = 0; s <= samples; s++)
                {
                    float t = s / (float)samples;
                    float ty = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, SampleAtNorm(_v, t)));
                    var p = new Vector2(r.x + 2f + t * (r.width - 4f), r.yMax - 2f - ty * (r.height - 4f));
                    if (s == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
            }
        }
    }
}

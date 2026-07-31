// ZuiValueControl — UI Toolkit counterpart of the IMGUI ZUIValueControl: one labelled row editing
// a ZUIValue, whose body switches between a static Slider, a min↔max range, or the full envelope
// curve editor (with Duration/Warmup/Loop and Value-Range fields). A "⋯" button opens the same
// mode / multiplier / copy-paste menu. Operates on the SAME runtime ZUIValue data the IMGUI
// control edits — nothing about assets or play-mode evaluation changes.
//
// Curve-mode layout (2026-07-23, per user direction): the LABEL is the fold toggle ("▶/▼ Label" —
// click it to expand/collapse). Collapsed = label · live thumbnail · ⋯. Expanded = label · ⋯ on
// one row, the full envelope below (NO thumbnail), then the optional per-point numeric inputs row,
// then the timing/range rows. The ⋯ menu gains "Show point values" / "Show numeric inputs" /
// "Inputs for selected points only".
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
            this.tooltip = tooltip;

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

            // RIGHT-CLICK anywhere on the control opens the same ⋯ config menu — the ⋯ button becomes a shortcut,
            // not the only way in. Registered on the row itself in the BUBBLE phase, so a child that owns its own
            // right-click (a MicroSlider's display-options menu) still wins on ITSELF (it stops the event before it
            // reaches here); a right-click on the label / body / empty space bubbles up and opens this menu.
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                ShowMenu(this);
                e.StopPropagation();
            });
        }

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        Button MenuButton()
        {
            Button btn = null;
            btn = Z.Button("⋯", "Configure value — mode" +
                (_opt.multiplierIds != null && _opt.multiplierIds.Length > 0 ? ", multiplier" : "") +
                (_v.mode == ZUIValue.Mode.Curve ? ", value display" : "") +
                ", copy/paste.", () => ShowMenu(btn)).W(24f);
            return btn;
        }

        Label FieldLabel(string text)
        {
            var l = new Label(text) { tooltip = _tooltip };
            l.AddToClassList("zui-field__label");
            l.style.marginTop = 3f;
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

            // Header: the label IS the fold toggle (mockup: plain label, no caret — hover affordance
            // via USS); ⋯ sits right next to it (expanded) or after the thumbnail (collapsed).
            var header = new VisualElement();
            header.AddToClassList("zui-row");

            var foldLabel = new Label(_label ?? "Curve")
            {
                tooltip = (_tooltip + " ").TrimStart() + "Click to " + (st.expanded ? "collapse" : "expand") + " the curve editor.",
            };
            foldLabel.AddToClassList("zui-field__label");
            foldLabel.AddToClassList("zui-fold-label");
            foldLabel.style.marginTop = 3f;
            foldLabel.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                st.expanded = !st.expanded;
                RebuildAll();
                e.StopPropagation();
            });
            header.Add(foldLabel);

            if (!st.expanded)
            {
                var thumb = new CurveThumb(_v) { tooltip = "Click to expand the curve editor." };
                thumb.style.width = Mathf.Max(60f, _opt.controlWidth - 20f);
                // A collapsed thumbnail should fill the row's spare width too, not sit narrow with the rest
                // empty — same grow rule as the sliders, capped so it never runs infinitely wide.
                if (_opt.grow)
                {
                    thumb.style.flexGrow = 1f; thumb.style.flexShrink = 1f;
                    thumb.style.maxWidth = _opt.controlWidth * Mathf.Max(1f, _opt.maxWidthFactor);
                }
                thumb.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    st.expanded = true;
                    RebuildAll();
                    e.StopPropagation();
                });
                header.Add(thumb);
                header.Add(MenuButton());
                _content.Add(header);
                return;
            }

            header.Add(MenuButton());
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

            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.Add(FieldLabel(_label ?? "Steps"));
            header.Add(Z.Flexible());
            header.Add(MenuButton());
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
            int modeSel = modes.IndexOf(_v.mode);
            if (hasMultiplier) menu.Section("Mode");
            menu.Radio(null, modeLabels.ToArray(), modeSel,
                "How this value is produced: a Static point, a random Min-Max range, or a Curve over time.",
                i => SetMode(modes[i]), closeOnSelect: true);

            // Curve-display options → persistent toggle rows (stay open so several can be flipped in one visit).
            if (_v.mode == ZUIValue.Mode.Curve)
            {
                var st = GetState(_v);
                menu.Separator();
                menu.Toggle("Show point values", "Draw each point's value on the curve.", st.showValues,
                    on => { st.showValues = on; if (!st.expanded) st.expanded = true; RebuildAll(); });
                menu.Toggle("Show numeric inputs", "Show a column of numeric fields for the points beside the curve.",
                    st.showInputs, on => { st.showInputs = on; if (!st.expanded) st.expanded = true; RebuildAll(); });
                menu.Toggle("Inputs for selected points only",
                    "Narrow the numeric-inputs column to just the points currently box-selected in the curve.",
                    st.inputsSelectedOnly,
                    on => { st.inputsSelectedOnly = on; if (!st.expanded) st.expanded = true; st.showInputs = true; RebuildAll(); });

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
                menu.Item("Recall shape…", "Pick a built-in or saved shape, or save the current one.",
                    () => ZuiEnvelopePresetPopover.Open(anchor, _v, () => OnBeforeMutate?.Invoke(), shapeApplied));
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
            });
            RebuildAll();
            UpdateReadout();
        }

        // ── tiny live curve thumbnail (the folded-state header) ─────────────────────
        class CurveThumb : VisualElement
        {
            readonly ZUIValue _v;

            public CurveThumb(ZUIValue v)
            {
                _v = v;
                AddToClassList("zui-envelope");
                style.width = 150f;
                style.height = 18f;
                generateVisualContent += Paint;
            }

            void Paint(MeshGenerationContext mgc)
            {
                var r = contentRect;
                if (!(r.width > 4f) || _v.points.Count == 0) return;
                var painter = mgc.painter2D;
                painter.strokeColor = new Color(0.4f, 0.85f, 1f);
                painter.lineWidth = 1.2f;
                painter.BeginPath();
                const int Samples = 40;
                for (int s = 0; s <= Samples; s++)
                {
                    float t = s / (float)Samples;
                    float v = ZUIEnvelopeEvaluator.Evaluate(_v.points, t, _v.yMax);
                    float ty = Mathf.InverseLerp(_v.yMin, _v.yMax, v);
                    var p = new Vector2(r.x + 2f + t * (r.width - 4f), r.yMax - 2f - ty * (r.height - 4f));
                    if (s == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
            }
        }
    }
}

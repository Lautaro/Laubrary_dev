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
            public float maxWidthFactor = 2.4f;
            // Static (MicroSlider) mode only: draw the field's LABEL inside the slider track (the point of a
            // MicroSlider) instead of as a separate label to its left. MinMax/Curve modes always use an
            // external label — they aren't MicroSliders. Default true; set false to keep the label outside.
            public bool sliderLabelInside = true;

            public Options WithRange(float lo, float hi) { absMin = lo; absMax = hi; return this; }
            public Options WithGrow(float maxFactor = 2.4f) { grow = true; maxWidthFactor = maxFactor; return this; }
            public Options WithMultipliers(params string[] ids) { multiplierIds = ids; return this; }
            public Options WithDefault(float value) { staticDefault = value; return this; }
            public Options WithoutCurveExtras() { hideCurveTiming = true; hideCurveRange = true; return this; }
            public Options WithoutLiveReadout() { hideLiveReadout = true; return this; }
            public Options WithWidth(float w) { controlWidth = w; return this; }

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
        }

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        Button MenuButton() => Z.Button("⋯", "Configure value — mode" +
            (_opt.multiplierIds != null && _opt.multiplierIds.Length > 0 ? ", multiplier" : "") +
            (_v.mode == ZUIValue.Mode.Curve ? ", value display" : "") +
            ", copy/paste.", ShowMenu).W(24f);

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
                        defaultValue: _opt.staticDefault);
                    AddHeaderRow(inside ? null : _label, slider);
                    break;
                }
                case ZUIValue.Mode.MinMax:
                    AddHeaderRow(_label, Z.MinMax(_v.min, _v.max, _opt.absMin, _opt.absMax, _tooltip,
                        (lo, hi) => Mutate(() => { _v.min = lo; _v.max = hi; }),
                        Mathf.Max(60f, _opt.controlWidth - 90f)));
                    break;
                case ZUIValue.Mode.Curve:
                    BuildCurve();
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

            var envOptions = new ZuiEnvelopeOptions
            {
                xMin = 0f, xMax = 1f,
                yMin = _v.yMin, yMax = _v.yMax,
                curveColor = new Color(0.4f, 0.85f, 1f),
                showValueLabels = st.showValues,
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

            if (!_opt.hideCurveTiming)
            {
                var timing = new VisualElement();
                timing.AddToClassList("zui-row");
                timing.Add(Z.Field("Dur s", "Seconds one playthrough of the curve takes.",
                    Z.Float(_v.duration, "Seconds one playthrough of the curve takes.",
                        val => Mutate(() => _v.duration = val), 46f)));
                timing.Add(Z.Field("Warm s", "Seconds before the curve starts (holds its first value).",
                    Z.Float(_v.warmup, "Seconds before the curve starts (holds its first value).",
                        val => Mutate(() => _v.warmup = val), 46f)));
                bool loop = _v.cooldown >= 0f;
                VisualElement coolField = null;
                timing.Add(Z.Toggle("Loop", "Restart the curve after a cooldown pause instead of holding its final value.",
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

        // ── the ⋯ menu (mode / display / multiplier / copy-paste) ───────────────────
        void ShowMenu()
        {
            var menu = new GenericMenu();
            bool hasMultiplier = _opt.multiplierIds != null && _opt.multiplierIds.Length > 0;
            string modePrefix = hasMultiplier ? "Mode/" : "";

            if (_opt.allowStatic)
                menu.AddItem(new GUIContent(modePrefix + "Static"), _v.mode == ZUIValue.Mode.Static,
                    () => SetMode(ZUIValue.Mode.Static));
            if (_opt.allowMinMax)
                menu.AddItem(new GUIContent(modePrefix + "Min-Max range"), _v.mode == ZUIValue.Mode.MinMax,
                    () => SetMode(ZUIValue.Mode.MinMax));
            if (_opt.allowCurve)
                menu.AddItem(new GUIContent(modePrefix + "Curve over time"), _v.mode == ZUIValue.Mode.Curve,
                    () => SetMode(ZUIValue.Mode.Curve));

            if (_v.mode == ZUIValue.Mode.Curve)
            {
                var st = GetState(_v);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Show point values"), st.showValues,
                    () => { st.showValues = !st.showValues; if (!st.expanded) st.expanded = true; RebuildAll(); });
                menu.AddItem(new GUIContent("Show numeric inputs"), st.showInputs,
                    () => { st.showInputs = !st.showInputs; if (!st.expanded) st.expanded = true; RebuildAll(); });
                menu.AddItem(new GUIContent("Inputs for selected points only"), st.inputsSelectedOnly,
                    () => { st.inputsSelectedOnly = !st.inputsSelectedOnly; if (!st.expanded) st.expanded = true; st.showInputs = true; RebuildAll(); });
            }

            if (hasMultiplier)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Multiplier/(none)"), !_v.HasMultiplier,
                    () => Mutate(() => _v.multiplierId = ""));
                foreach (var id in _opt.multiplierIds)
                {
                    string captured = id;
                    menu.AddItem(new GUIContent("Multiplier/" + id), _v.multiplierId == id,
                        () => Mutate(() => _v.multiplierId = captured));
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy value"), false,
                () => EditorGUIUtility.systemCopyBuffer = _v.ToClipboardString());
            if (ZUIValue.TryFromClipboardString(EditorGUIUtility.systemCopyBuffer, out _))
                menu.AddItem(new GUIContent("Paste value"), false, () =>
                {
                    if (ZUIValue.TryFromClipboardString(EditorGUIUtility.systemCopyBuffer, out var parsed))
                        Mutate(() => _v.CopyFrom(parsed));
                    RebuildAll();
                    UpdateReadout();
                });
            else
                menu.AddDisabledItem(new GUIContent("Paste value"));

            menu.ShowAsContext();
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

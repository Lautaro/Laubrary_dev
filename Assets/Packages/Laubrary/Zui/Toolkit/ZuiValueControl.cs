// ZuiValueControl — UI Toolkit counterpart of the IMGUI ZUIValueControl: one labelled row editing
// a ZUIValue, whose body switches between a static Slider, a min↔max range, or the full envelope
// curve editor (with Duration/Warmup/Loop and Value-Range fields). A "⋯" button opens the same
// mode / multiplier / copy-paste menu. Operates on the SAME runtime ZUIValue data the IMGUI
// control edits — nothing about assets or play-mode evaluation changes.
//
// Not yet ported from the IMGUI version (tracked in CHANGELOG/zui.md): the "★" envelope-preset
// popup (ZUIEnvelopePresetPopup is IMGUI and lives in ZUI.Editor — wire it up when presets are
// migrated rather than coupling the new assembly to the old one now).
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

            public Options WithRange(float lo, float hi) { absMin = lo; absMax = hi; return this; }
            public Options WithMultipliers(params string[] ids) { multiplierIds = ids; return this; }
            public Options WithDefault(float value) { staticDefault = value; return this; }
            public Options WithoutCurveExtras() { hideCurveTiming = true; hideCurveRange = true; return this; }
            public Options WithoutLiveReadout() { hideLiveReadout = true; return this; }
            public Options WithWidth(float w) { controlWidth = w; return this; }

            public Options Clone() => (Options)MemberwiseClone();
        }

        // Curve fold state keyed by the ZUIValue instance so it survives window rebuilds (undo/redo
        // rebuilds the whole tree) — the same trick the IMGUI control uses for its session state.
        static readonly Dictionary<ZUIValue, bool> s_expanded = new();

        readonly ZUIValue _v;
        readonly Options _opt;
        readonly string _tooltip;
        readonly VisualElement _body;
        readonly Label _readout;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation (slider drags, curve edits, mode/multiplier changes, paste).
        public Action OnChanged;

        public ZuiValueControl(string label, ZUIValue v, Options options, string tooltip)
        {
            _v = v ?? throw new ArgumentNullException(nameof(v));
            _opt = options ?? new Options();
            _tooltip = tooltip;
            this.tooltip = tooltip;

            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.alignItems = Align.FlexStart;
            if (!string.IsNullOrEmpty(label))
            {
                var l = new Label(label) { tooltip = tooltip };
                l.AddToClassList("zui-field__label");
                l.style.marginTop = 3f;
                row.Add(l);
            }

            _body = new VisualElement();
            _body.style.flexShrink = 0f;
            row.Add(_body);

            var menuButton = Z.Button("⋯", "Configure value — mode" +
                (_opt.multiplierIds != null && _opt.multiplierIds.Length > 0 ? ", multiplier" : "") +
                ", copy/paste.", ShowMenu).W(24f);
            row.Add(menuButton);
            Add(row);

            _readout = Z.Text("", ZuiText.Small,
                "Live evaluated value (wall-clock time) — shown only while the value is dynamic.");
            Add(_readout);
            _readout.schedule.Execute(UpdateReadout).Every(250);

            RebuildBody();
            UpdateReadout();
        }

        void Mutate(Action apply) { OnBeforeMutate?.Invoke(); apply(); OnChanged?.Invoke(); }

        // ── body per mode ───────────────────────────────────────────────────────────
        void RebuildBody()
        {
            _body.Clear();
            switch (_v.mode)
            {
                case ZUIValue.Mode.Static:
                {
                    var slider = Z.Slider(_v.staticValue, _opt.absMin, _opt.absMax, _tooltip,
                        val => Mutate(() => _v.staticValue = val), _opt.controlWidth);
                    if (_opt.staticDefault.HasValue)
                        slider.RegisterCallback<PointerDownEvent>(e =>
                        {
                            if (e.clickCount != 2) return;
                            Mutate(() => _v.staticValue = _opt.staticDefault.Value);
                            slider.SetValueWithoutNotify(_v.staticValue);
                        });
                    _body.Add(slider);
                    break;
                }
                case ZUIValue.Mode.MinMax:
                    _body.Add(Z.MinMax(_v.min, _v.max, _opt.absMin, _opt.absMax, _tooltip,
                        (lo, hi) => Mutate(() => { _v.min = lo; _v.max = hi; }),
                        Mathf.Max(60f, _opt.controlWidth - 90f)));
                    break;
                case ZUIValue.Mode.Curve:
                    BuildCurveBody();
                    break;
            }
        }

        void BuildCurveBody()
        {
            _v.EnsureCurveDefaults();
            if (_opt.hideCurveRange)
            {
                _v.yMin = Mathf.Min(_opt.absMin, _opt.absMax);
                _v.yMax = Mathf.Max(_opt.absMin, _opt.absMax);
            }

            bool expanded = s_expanded.TryGetValue(_v, out var e) && e;

            // Folded header: live thumbnail (click to expand) + caret.
            var header = new VisualElement();
            header.AddToClassList("zui-row");
            var thumb = new CurveThumb(_v) { tooltip = "Click to " + (expanded ? "collapse" : "expand") + " the curve editor." };
            thumb.style.width = Mathf.Max(60f, _opt.controlWidth - 20f);
            thumb.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (ev.button != 0) return;
                s_expanded[_v] = !expanded;
                RebuildBody();
                ev.StopPropagation();
            });
            header.Add(thumb);
            header.Add(Z.Text(expanded ? "▼" : "▶", ZuiText.Small, "Curve editor fold state."));
            _body.Add(header);

            if (!expanded) return;

            var envOptions = new ZuiEnvelopeOptions
            {
                xMin = 0f, xMax = 1f,
                yMin = _v.yMin, yMax = _v.yMax,
                curveColor = new Color(0.4f, 0.85f, 1f),
            };
            var env = new ZuiEnvelope(_v.points, envOptions, _tooltip, 220f, 90f);
            env.OnBeforeMutate += () => OnBeforeMutate?.Invoke();
            env.OnChanged += () => { OnChanged?.Invoke(); thumb.MarkDirtyRepaint(); };
            _body.Add(env);

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
                var loopToggle = Z.Toggle("Loop", "Restart the curve after a cooldown pause instead of holding its final value.",
                    loop, on =>
                    {
                        Mutate(() => _v.cooldown = on ? 0f : -1f);
                        coolField?.Shown(on);
                    });
                timing.Add(loopToggle);
                coolField = Z.Field("Cool s", "Pause after a playthrough before looping.",
                    Z.Float(Mathf.Max(0f, _v.cooldown), "Pause after a playthrough before looping.",
                        val => Mutate(() => _v.cooldown = Mathf.Max(0f, val)), 46f));
                coolField.Shown(loop);
                timing.Add(coolField);
                _body.Add(timing);
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
                        ClampPointsToRange(); env.Refresh(); thumb.MarkDirtyRepaint();
                    }), 46f)));
                range.Add(Z.Field("max", "The curve's maximum output value.",
                    Z.Float(_v.yMax, "The curve's maximum output value.", val => Mutate(() =>
                    {
                        _v.yMax = Mathf.Max(val, _v.yMin);
                        envOptions.yMin = _v.yMin; envOptions.yMax = _v.yMax;
                        ClampPointsToRange(); env.Refresh(); thumb.MarkDirtyRepaint();
                    }), 46f)));
                _body.Add(range);
            }
            else
            {
                envOptions.yMin = _v.yMin;
                envOptions.yMax = _v.yMax;
            }
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

        // ── the ⋯ menu (mode / multiplier / copy-paste) ─────────────────────────────
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
                    RebuildBody();
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
            RebuildBody();
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

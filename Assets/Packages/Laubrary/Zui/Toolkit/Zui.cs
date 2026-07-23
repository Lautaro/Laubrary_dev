// Zui — the UI Toolkit half of ZUI (retained-mode VisualElement controls; the IMGUI half is the
// global-namespace ZUI class, kept fully working while tools migrate one by one).
//
// Design rules carried over from the IMGUI era (see the laubrary skill's ui-layout-rules.md):
//  • every control factory REQUIRES a tooltip parameter — no bare labels, by construction;
//  • no control is left to stretch: BaseFields get flex-grow:0 from ZuiToolkit.uss, and factories
//    set an explicit default width the caller can override with .W(px);
//  • labels pair with controls via Zui.Field (a FieldWrap), sidestepping BaseField's inconsistent
//    built-in label widths;
//  • drag-computed numeric values are rounded to 5 decimals at the source so float noise never
//    reaches a display.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public enum ZuiText { Body, Section, Small, Subtle }

    // NOTE the class is `Z`, not `Zui` — a class named the same as its containing namespace's last
    // segment (Laubrary.Zui.Zui) gets shadowed by the namespace inside every sibling Laubrary.*
    // namespace, making `Zui.Button(...)` unresolvable exactly where it's most used.
    public static class Z
    {
        // ── stylesheet ──────────────────────────────────────────────────────────────
        static StyleSheet _sheet;

        /// The shared ZuiToolkit.uss, located by search so the path works both in this dev host
        /// (Assets/Packages/Laubrary/...) and in consumers (Packages/com.lautaro.arino.laubrary/...).
        public static StyleSheet Sheet
        {
            get
            {
                if (_sheet != null) return _sheet;
                foreach (string guid in AssetDatabase.FindAssets("ZuiToolkit t:StyleSheet"))
                {
                    var s = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                    if (s != null) { _sheet = s; break; }
                }
                if (_sheet == null) Debug.LogError("[Zui] ZuiToolkit.uss not found in the project.");
                return _sheet;
            }
        }

        /// Attach the shared stylesheet + root class to a window/popup root. ZuiWindow does this
        /// automatically; call it yourself only for roots Zui doesn't own (popups, inspectors).
        public static void Attach(VisualElement root)
        {
            var s = Sheet;
            if (s != null && !root.styleSheets.Contains(s)) root.styleSheets.Add(s);
            root.AddToClassList("zui-root");
        }

        // ── containers ──────────────────────────────────────────────────────────────

        public static VisualElement Row(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            foreach (var c in children) if (c != null) row.Add(c);
            return row;
        }

        public static VisualElement Column(params VisualElement[] children)
        {
            var col = new VisualElement();
            foreach (var c in children) if (c != null) col.Add(c);
            return col;
        }

        /// A framed section. The tooltip (if any) renders as a "?" hover icon on the title's own
        /// row — never below the content it explains (ui-layout-rules: help sits on the header).
        public static VisualElement Box(string title, string tooltip, params VisualElement[] children)
        {
            var box = new VisualElement();
            box.AddToClassList("zui-box");
            if (!string.IsNullOrEmpty(title))
            {
                var titleRow = new VisualElement();
                titleRow.AddToClassList("zui-box__titlerow");
                var t = new Label(title);
                t.AddToClassList("zui-box__title");
                if (!string.IsNullOrEmpty(tooltip)) t.tooltip = tooltip;
                titleRow.Add(t);
                if (!string.IsNullOrEmpty(tooltip))
                {
                    var spacer = new VisualElement();
                    spacer.style.flexGrow = 1f;
                    titleRow.Add(spacer);
                    titleRow.Add(HelpIcon(tooltip));
                }
                box.Add(titleRow);
            }
            foreach (var c in children) if (c != null) box.Add(c);
            return box;
        }

        public static VisualElement HSpace(float px = 8f)
        {
            var v = new VisualElement();
            v.style.width = px;
            v.style.flexShrink = 0f;
            return v;
        }

        public static VisualElement VSpace(float px = 6f)
        {
            var v = new VisualElement();
            v.style.height = px;
            v.style.flexShrink = 0f;
            return v;
        }

        public static VisualElement Flexible()
        {
            var v = new VisualElement();
            v.style.flexGrow = 1f;
            return v;
        }

        // ── text ────────────────────────────────────────────────────────────────────

        public static Label Text(string text, ZuiText style = ZuiText.Body, string tooltip = null)
        {
            var l = new Label(text);
            switch (style)
            {
                case ZuiText.Section: l.AddToClassList("zui-text--section"); break;
                case ZuiText.Small: l.AddToClassList("zui-text--small"); break;
                case ZuiText.Subtle: l.AddToClassList("zui-text--subtle"); break;
            }
            if (!string.IsNullOrEmpty(tooltip)) l.tooltip = tooltip;
            return l;
        }

        /// A small "?" glyph whose hover shows the full explanation.
        public static Label HelpIcon(string tooltip)
        {
            var l = new Label("?");
            l.AddToClassList("zui-help");
            l.tooltip = tooltip;
            return l;
        }

        /// Label + control pair with the label sized to its own text (the FieldWrap pattern).
        public static VisualElement Field(string label, string tooltip, VisualElement control)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("zui-field");
            var l = new Label(label);
            l.AddToClassList("zui-field__label");
            l.tooltip = tooltip;
            wrap.Add(l);
            if (string.IsNullOrEmpty(control.tooltip)) control.tooltip = tooltip;
            wrap.Add(control);
            return wrap;
        }

        // ── controls (tooltip is always a required parameter) ───────────────────────

        public static Button Button(string label, string tooltip, Action onClick)
        {
            var b = new Button(onClick) { text = label, tooltip = tooltip };
            return b;
        }

        public static Toggle Toggle(string label, string tooltip, bool value, Action<bool> onChanged)
        {
            var t = new Toggle(label) { value = value, tooltip = tooltip };
            t.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return t;
        }

        /// Slider with an inline numeric input. Values are rounded to 5 decimals at the source so
        /// float noise from drag interpolation never reaches the display or the stored value.
        public static Slider Slider(float value, float min, float max, string tooltip,
            Action<float> onChanged, float width = 170f)
        {
            var s = new Slider(min, max) { value = value, tooltip = tooltip, showInputField = true };
            s.style.width = width;
            s.RegisterValueChangedCallback(e =>
            {
                float r = (float)Math.Round(e.newValue, 5);
                s.SetValueWithoutNotify(r);
                onChanged?.Invoke(r);
            });
            return s;
        }

        public static SliderInt SliderInt(int value, int min, int max, string tooltip,
            Action<int> onChanged, float width = 170f)
        {
            var s = new SliderInt(min, max) { value = value, tooltip = tooltip, showInputField = true };
            s.style.width = width;
            s.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return s;
        }

        public static FloatField Float(float value, string tooltip, Action<float> onChanged, float width = 60f)
        {
            var f = new FloatField { value = value, tooltip = tooltip };
            f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        public static TextField TextInput(string value, string tooltip, Action<string> onChanged, float width = 200f)
        {
            var f = new TextField { value = value, tooltip = tooltip };
            f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        public static ObjectField Object<T>(T value, string tooltip, Action<T> onChanged,
            float width = 200f, bool allowSceneObjects = false) where T : UnityEngine.Object
        {
            var f = new ObjectField
            {
                objectType = typeof(T),
                value = value,
                tooltip = tooltip,
                allowSceneObjects = allowSceneObjects
            };
            f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue as T));
            return f;
        }

        /// A row of mutually-exclusive mini buttons (the MiniRadio pattern). Returns the row;
        /// selection state is kept in the buttons' classes.
        public static VisualElement MiniRadio(int index, string[] options, string tooltip, Action<int> onChanged)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("zui-radio");
            var buttons = new Button[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                buttons[i] = new Button(() =>
                {
                    for (int b = 0; b < buttons.Length; b++)
                        buttons[b].EnableInClassList("zui-radio__on", b == idx);
                    onChanged?.Invoke(idx);
                })
                { text = options[i], tooltip = tooltip };
                buttons[i].EnableInClassList("zui-radio__on", i == index);
                if (i == 0) buttons[i].AddToClassList("zui-radio__first");
                if (i == options.Length - 1) buttons[i].AddToClassList("zui-radio__last");
                row.Add(buttons[i]);
            }
            return row;
        }

        /// One compact button that cycles through the options on each click — the CycleButton
        /// pattern, for when a MiniRadio row would be too wide for the space.
        public static Button CycleButton(int index, string[] options, string tooltip, Action<int> onChanged)
        {
            int current = Mathf.Clamp(index, 0, options.Length - 1);
            Button b = null;
            b = new Button(() =>
            {
                current = (current + 1) % options.Length;
                b.text = options[current];
                onChanged?.Invoke(current);
            })
            { text = options[current], tooltip = tooltip };
            return b;
        }

        public static DropdownField Dropdown(int index, List<string> choices, string tooltip,
            Action<int> onChanged, float width = 140f)
        {
            var d = new DropdownField(choices, Mathf.Clamp(index, 0, choices.Count - 1)) { tooltip = tooltip };
            d.style.width = width;
            d.RegisterValueChangedCallback(e => onChanged?.Invoke(choices.IndexOf(e.newValue)));
            return d;
        }

        public static EnumField EnumDropdown<T>(T value, string tooltip, Action<T> onChanged,
            float width = 140f) where T : Enum
        {
            var d = new EnumField(value) { tooltip = tooltip };
            d.style.width = width;
            d.RegisterValueChangedCallback(e => onChanged?.Invoke((T)e.newValue));
            return d;
        }

        public static IntegerField Int(int value, string tooltip, Action<int> onChanged, float width = 60f)
        {
            var f = new IntegerField { value = value, tooltip = tooltip };
            f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// The control type IMGUI's tooltip audits kept catching bare — here the tooltip is required
        /// like everywhere else.
        public static ColorField Color(Color value, string tooltip, Action<Color> onChanged,
            float width = 60f, bool showAlpha = true, bool hdr = false)
        {
            var f = new ColorField { value = value, tooltip = tooltip, showAlpha = showAlpha, hdr = hdr };
            f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// A min/max range: numeric low field + MinMaxSlider + numeric high field, kept in sync
        /// (the SliderRange pattern). Rounded to 5 decimals like every slider.
        public static VisualElement MinMax(float low, float high, float min, float max, string tooltip,
            Action<float, float> onChanged, float sliderWidth = 130f)
        {
            var slider = new MinMaxSlider(low, high, min, max) { tooltip = tooltip };
            slider.style.width = sliderWidth;
            var lowField = new FloatField { value = low, tooltip = tooltip };
            lowField.style.width = 42f;
            var highField = new FloatField { value = high, tooltip = tooltip };
            highField.style.width = 42f;

            void Commit(float lo, float hi, bool fromSlider)
            {
                lo = (float)Math.Round(Mathf.Clamp(lo, min, max), 5);
                hi = (float)Math.Round(Mathf.Clamp(hi, lo, max), 5);
                if (fromSlider) { lowField.SetValueWithoutNotify(lo); highField.SetValueWithoutNotify(hi); }
                else slider.SetValueWithoutNotify(new Vector2(lo, hi));
                onChanged?.Invoke(lo, hi);
            }
            slider.RegisterValueChangedCallback(e => Commit(e.newValue.x, e.newValue.y, true));
            lowField.RegisterValueChangedCallback(e => Commit(e.newValue, slider.maxValue, false));
            highField.RegisterValueChangedCallback(e => Commit(slider.minValue, e.newValue, false));

            return Row(lowField, slider, highField);
        }

        /// A collapsible framed section — the FoldoutBox pattern. The tooltip lands on the Foldout
        /// AND its internal disclosure Toggle (which does not inherit it — a known UI Toolkit trap).
        public static Foldout Foldout(string title, string tooltip, bool open, params VisualElement[] children)
        {
            var f = new Foldout { text = title, value = open, tooltip = tooltip };
            f.AddToClassList("zui-box");
            var disclosure = f.Q<Toggle>();
            if (disclosure != null) disclosure.tooltip = tooltip;
            foreach (var c in children) if (c != null) f.Add(c);
            return f;
        }

        public static HelpBox Help(string text, HelpBoxMessageType type = HelpBoxMessageType.Info)
            => new HelpBox(text, type);

        /// Square drag-pad for a plain Vector2 (the PositionPad pattern — NOT for animatable values).
        public static ZuiPad Pad(Vector2 value, Rect range, string tooltip, Action<Vector2> onChanged,
            float size = 56f, bool flipY = true)
        {
            var pad = new ZuiPad(value, range, tooltip, size, flipY);
            pad.OnChanged += v => onChanged?.Invoke(v);
            return pad;
        }

        /// Labelled ZUIValue editor (Static / MinMax / Curve modes with the ⋯ config menu) — the
        /// ValRow workhorse. Pass `onBeforeMutate` to record Undo on the owning asset.
        public static ZuiValueControl Value(string label, ZUIValue v, ZuiValueControl.Options options,
            string tooltip, Action onChanged, Action onBeforeMutate = null)
        {
            var c = new ZuiValueControl(label, v, options, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            return c;
        }

        /// DAW-style multi-point envelope editor over a caller-owned List&lt;ZUIEnvelopePoint&gt;
        /// (the same runtime data ZUI.Envelope edits). Pass `onBeforeMutate` to record Undo on the
        /// owning asset — it fires once per gesture, before the first mutation.
        public static ZuiEnvelope Envelope(List<ZUIEnvelopePoint> points, ZuiEnvelopeOptions options,
            string tooltip, Action onChanged, Action onBeforeMutate = null,
            float width = 220f, float height = 80f)
        {
            var env = new ZuiEnvelope(points, options, tooltip, width, height);
            if (onChanged != null) env.OnChanged += onChanged;
            if (onBeforeMutate != null) env.OnBeforeMutate += onBeforeMutate;
            return env;
        }
    }

    public static class ZuiExtensions
    {
        /// Explicit width — the sanctioned way to size a control (never leave one to stretch).
        public static T W<T>(this T ve, float width) where T : VisualElement
        {
            ve.style.width = width;
            ve.style.flexGrow = 0f;
            ve.style.flexShrink = 0f;
            return ve;
        }

        /// Explicit height.
        public static T H<T>(this T ve, float height) where T : VisualElement
        {
            ve.style.height = height;
            return ve;
        }

        /// Show/hide without removing from the tree (retained-mode conditional sections).
        public static T Shown<T>(this T ve, bool visible) where T : VisualElement
        {
            ve.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            return ve;
        }
    }
}

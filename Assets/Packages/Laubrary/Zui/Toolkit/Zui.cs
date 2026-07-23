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

        /// A masonry column layout: items are dealt round-robin into `n` equal-width columns, each an
        /// INDEPENDENT vertical stack. This is the retained-mode answer to pairing controls in a Row —
        /// a Row couples heights (a tall expanded envelope stretches its neighbour and shoves the next
        /// row down), whereas each column here flows on its own, so a control never grows or moves
        /// because the thing beside it expanded. Two consecutive items land in adjacent columns (left,
        /// right, left, …), preserving the side-by-side pairing when heights match while decoupling it
        /// when they don't. Each item is stretched to its column's full width and keeps its natural
        /// height (any horizontal flexGrow it carried for Row-mode is cleared).
        public static VisualElement Columns(int n, params VisualElement[] items)
        {
            if (n < 1) n = 1;
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            var cols = new VisualElement[n];
            for (int i = 0; i < n; i++)
            {
                var col = new VisualElement();
                col.style.flexDirection = FlexDirection.Column;
                col.style.flexGrow = 1f;
                col.style.flexShrink = 1f;
                col.style.flexBasis = 0f;                 // equal width regardless of content
                col.style.minWidth = 0f;                  // let it shrink; content wraps inside
                if (i > 0) col.style.marginLeft = 6f;     // gutter between columns
                cols[i] = col;
                root.Add(col);
            }
            int placed = 0;
            foreach (var it in items)
            {
                if (it == null) continue;
                it.style.flexGrow = 0f;                   // in a column, flexGrow would grow HEIGHT
                it.style.flexShrink = 0f;
                it.style.alignSelf = Align.Stretch;       // fill the column width
                it.style.marginBottom = 2f;
                cols[placed % n].Add(it);
                placed++;
            }
            return root;
        }

        /// A titled, COLLAPSIBLE section — the header itself is the toggle. Children added to the
        /// returned element go inside the section body. Prefer this over a bare
        /// `Z.Text(.., ZuiText.Section, ..)` heading whenever the heading names a block of controls:
        /// a label can only sit beside its controls, a section owns them and can fold them away.
        /// `stateKey` defaults to the title; pass one explicitly if two sections share a title.
        public static ZuiSection Section(string title, string tooltip, string stateKey = null)
            => new ZuiSection(title, tooltip, stateKey);

        /// A framed section. The tooltip (if any) renders as a "?" hover icon on the title's own
        /// row — never below the content it explains (ui-layout-rules: help sits on the header).
        /// A titled box folds when its title row is clicked (see ZuiBox).
        public static ZuiBox Box(string title, string tooltip, params VisualElement[] children)
        {
            var box = new ZuiBox(title, tooltip);
            foreach (var c in children) if (c != null) box.Add(c);
            return box;
        }

        /// Box with an explicit fold-state key — for repeated boxes that share a title (one per list row),
        /// which would otherwise all fold together.
        public static ZuiBox BoxKeyed(string title, string tooltip, string stateKey,
            params VisualElement[] children)
        {
            var box = new ZuiBox(title, tooltip, stateKey);
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

        /// A thin horizontal rule that divides one run of controls from the next INSIDE a section or box —
        /// the lightweight grouping between a full sub-box (too heavy for a couple of rows) and nothing at
        /// all (a long flat column with no visual structure). An optional label sits on the line, so a
        /// divider can also name the group it introduces.
        public static VisualElement Divider(string label = null, string tooltip = null)
        {
            var d = new VisualElement();
            d.AddToClassList("zui-divider");
            if (!string.IsNullOrEmpty(label))
            {
                d.AddToClassList("zui-divider--labelled");
                var l = new Label(label) { tooltip = tooltip };
                l.AddToClassList("zui-divider__label");
                d.Add(l);
            }
            return d;
        }

        // ── text ────────────────────────────────────────────────────────────────────

        public static Label Text(string text, ZuiText style = ZuiText.Body, string tooltip = null)
        {
            // A Section heading is a ZuiSectionLabel: still a Label to every call site, but clicking it
            // folds the block it names (see ZuiSectionLabel). Every heading already written across the
            // tools became collapsible through this one line.
            if (style == ZuiText.Section) return new ZuiSectionLabel(text, tooltip);

            var l = new Label(text);
            switch (style)
            {
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

        /// Which look the DEFAULT-variant factories draw. `Vanilla` = Unity's own controls skinned with USS
        /// (the migration default); `Custom` = the Painter2D-drawn old-ZUI look (MicroSlider fill-track,
        /// button-toggles). Flip this once to roll every tool over; the explicit Z.MicroSlider / Z.ToggleButton
        /// factories always draw the custom look regardless, and a plain Z.Slider always draws vanilla — this
        /// only steers the wrappers that offer both (currently none but the door is open, per the roll-out plan).
        public enum Variant { Vanilla, Custom }
        public static Variant DefaultVariant = Variant.Vanilla;

        public static Button Button(string label, string tooltip, Action onClick)
        {
            var b = new Button(onClick) { text = label, tooltip = tooltip };
            return b;
        }

        /// The old-ZUI MicroSlider: a filled track whose fill is the value, label+value inside, no thumb.
        /// Half the height of a vanilla Slider and needs no separate value field.
        public static ZuiMicroSlider MicroSlider(string label, float value, float min, float max,
            string tooltip, Action<float> onChanged, float width = 150f, bool showValue = true,
            float? defaultValue = null, int decimals = -1)
        {
            var s = new ZuiMicroSlider(label, value, min, max, tooltip, onChanged, showValue, defaultValue,
                decimals: decimals);
            s.style.width = width;
            return s;
        }

        /// A button that latches on/off (the old-ZUI button toggle), instead of a checkbox with a tick.
        public static ZuiToggleButton ToggleButton(string label, string tooltip, bool value, Action<bool> onChanged)
            => new ZuiToggleButton(label, tooltip, value, onChanged);

        /// A joined row of buttons, single-select (radio look, custom-drawn) — the themed twin of MiniRadio.
        public static ZuiSegmented Segmented(int selected, string[] labels, string tooltip, Action<int> onChanged)
            => ZuiSegmented.Radio(selected, labels, tooltip, onChanged);

        /// A joined row of independently-latching segments — the right control for a flag set.
        public static ZuiSegmented SegmentedMulti(Func<int, bool> isOn, string[] labels, string tooltip,
            Action<int, bool> onToggled)
            => ZuiSegmented.Multi(isOn, labels, tooltip, onToggled);

        public static Toggle Toggle(string label, string tooltip, bool value, Action<bool> onChanged)
        {
            var t = new Toggle(label) { value = value, tooltip = tooltip };
            t.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return t;
        }

        /// Slider with an inline numeric input (pass showInput:false for a bare track — for values
        /// whose exact number doesn't matter, e.g. a preview-only opacity). Values are rounded to 5
        /// decimals at the source so float noise from drag interpolation never reaches the display
        /// or the stored value.
        public static Slider Slider(float value, float min, float max, string tooltip,
            Action<float> onChanged, float width = 170f, bool showInput = true)
        {
            var s = new Slider(min, max) { value = value, tooltip = tooltip, showInputField = showInput };
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
        /// `wrap` lets a long option set fold onto a second line instead of running off the side of a narrow
        /// pane. Off by default, because a wrapped radio loses its single-pill look and most sets are short;
        /// turn it on for the ones that genuinely grow over time (Pyre's shape picker gains a shape now and
        /// then, and each one pushed the row further past the edge).
        public static VisualElement MiniRadio(int index, string[] options, string tooltip, Action<int> onChanged,
            bool wrap = false)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("zui-radio");
            if (wrap) row.AddToClassList("zui-radio--wrap");
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

        /// Vertical variant of MiniRadio — a stacked column of mutually-exclusive buttons, for when
        /// the radio sits beside a square control (a pad, a picker) and should match its height.
        public static VisualElement MiniRadioVertical(int index, string[] options, string tooltip,
            Action<int> onChanged, float width = 70f, float totalHeight = 0f)
        {
            var col = new VisualElement { tooltip = tooltip };
            col.AddToClassList("zui-radio");
            col.style.flexDirection = FlexDirection.Column;
            col.style.width = width;
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
                if (totalHeight > 0f) buttons[i].style.height = totalHeight / options.Length;
                buttons[i].style.marginTop = 0f;
                buttons[i].style.marginBottom = 0f;
                col.Add(buttons[i]);
            }
            return col;
        }

        /// Label stacked ABOVE a compact slider — the SliderStacked pattern for tightly packed rows
        /// where side-by-side label+slider would be too wide.
        public static VisualElement Stacked(string label, string tooltip, float value, float min, float max,
            Action<float> onChanged, float width, bool isInt = false)
        {
            var col = new VisualElement();
            col.style.width = width;
            col.style.flexShrink = 0f;
            col.Add(Text(label, ZuiText.Small, tooltip));
            if (isInt)
            {
                var s = new SliderInt(Mathf.RoundToInt(min), Mathf.RoundToInt(max))
                { value = Mathf.RoundToInt(value), tooltip = tooltip };
                s.style.width = width;
                s.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
                col.Add(s);
            }
            else
            {
                var s = new Slider(min, max) { value = value, tooltip = tooltip };
                s.style.width = width;
                s.RegisterValueChangedCallback(e =>
                {
                    float r = (float)Math.Round(e.newValue, 5);
                    s.SetValueWithoutNotify(r);
                    onChanged?.Invoke(r);
                });
                col.Add(s);
            }
            return col;
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

        /// A plain AnimationCurve field. Distinct from Z.Envelope: an envelope is ZUI's own draggable
        /// multi-point editor over a List&lt;ZUIEnvelopePoint&gt;, whereas this edits a real UnityEngine
        /// AnimationCurve through Unity's own curve editor — which is what a runtime type already storing an
        /// AnimationCurve (ChunkSpec's size/alpha-over-life) actually needs. Width and height are paired:
        /// widening one without the other just makes a curve clumsier to read, not more useful.
        public static CurveField Curve(AnimationCurve value, string tooltip, Action<AnimationCurve> onChanged,
            float width = 180f, float height = 24f)
        {
            var f = new CurveField { value = value, tooltip = tooltip };
            f.style.width = width;
            f.style.height = height;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// A plain Gradient field (Unity's own gradient editor), sized rather than left to stretch.
        public static GradientField Gradient(Gradient value, string tooltip, Action<Gradient> onChanged,
            float width = 180f, float height = 20f)
        {
            var f = new GradientField { value = value, tooltip = tooltip };
            f.style.width = width;
            f.style.height = height;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// A min/max range: numeric low field + MinMaxSlider + numeric high field, kept in sync
        /// (the SliderRange pattern). Rounded to 5 decimals like every slider.
        public static VisualElement MinMax(float low, float high, float min, float max, string tooltip,
            Action<float, float> onChanged, float sliderWidth = 130f, bool isInt = false)
        {
            var slider = new MinMaxSlider(low, high, min, max) { tooltip = tooltip };
            slider.style.width = sliderWidth;
            // isInt: the flanking numeric fields are IntegerFields and both handle+field snap to whole
            // numbers — for a discrete range (a frame window) that can never be fractional.
            BaseField<int> lowFieldI = isInt ? new IntegerField { value = Mathf.RoundToInt(low), tooltip = tooltip } : null;
            BaseField<int> highFieldI = isInt ? new IntegerField { value = Mathf.RoundToInt(high), tooltip = tooltip } : null;
            var lowField = isInt ? null : new FloatField { value = low, tooltip = tooltip };
            var highField = isInt ? null : new FloatField { value = high, tooltip = tooltip };
            (isInt ? (VisualElement)lowFieldI : lowField).style.width = 42f;
            (isInt ? (VisualElement)highFieldI : highField).style.width = 42f;

            void Commit(float lo, float hi, bool fromSlider)
            {
                lo = isInt ? Mathf.Round(lo) : (float)Math.Round(Mathf.Clamp(lo, min, max), 5);
                hi = isInt ? Mathf.Round(hi) : (float)Math.Round(Mathf.Clamp(hi, lo, max), 5);
                lo = Mathf.Clamp(lo, min, max); hi = Mathf.Clamp(hi, lo, max);
                if (fromSlider)
                {
                    if (isInt) { lowFieldI.SetValueWithoutNotify(Mathf.RoundToInt(lo)); highFieldI.SetValueWithoutNotify(Mathf.RoundToInt(hi)); }
                    else { lowField.SetValueWithoutNotify(lo); highField.SetValueWithoutNotify(hi); }
                }
                else slider.SetValueWithoutNotify(new Vector2(lo, hi));
                onChanged?.Invoke(lo, hi);
            }
            slider.RegisterValueChangedCallback(e => Commit(e.newValue.x, e.newValue.y, true));
            if (isInt)
            {
                lowFieldI.RegisterValueChangedCallback(e => Commit(e.newValue, slider.maxValue, false));
                highFieldI.RegisterValueChangedCallback(e => Commit(slider.minValue, e.newValue, false));
                return Row(lowFieldI, slider, highFieldI);
            }
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

        /// Synchronized XY pair of ZUIValues as ONE 2D control (drag a dot / trace a path) — the
        /// 2D analog of Z.Value. Pass `onBeforeMutate` to record Undo on the owning asset.
        public static ZuiValue2DControl Value2D(string label, ZUIValue x, ZUIValue y,
            ZuiValue2DControl.Options options, string tooltip, Action onChanged, Action onBeforeMutate = null)
        {
            var c = new ZuiValue2DControl(label, x, y, options, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            return c;
        }

        /// The SAME 2D control over a PLAIN Vector2 (no animation, no mode switch) — for values that
        /// must never animate (a pivot, an authored placement). `stateKey` should be the edited data
        /// instance so fold/display state survives window rebuilds.
        public static ZuiValue2DControl Vector2Field(string label, Func<Vector2> get, Action<Vector2> set,
            object stateKey, ZuiValue2DControl.Options options, string tooltip,
            Action onChanged, Action onBeforeMutate = null)
        {
            var c = new ZuiValue2DControl(label, new ZuiVector2Source(get, set), stateKey, options, tooltip);
            if (onChanged != null) c.OnChanged += onChanged;
            if (onBeforeMutate != null) c.OnBeforeMutate += onBeforeMutate;
            return c;
        }

        /// A [SerializeReference] polymorphic field: a fold header naming the current concrete type, with a
        /// button that switches it, over a body the CALLER fills with that type's own fields (so a child can
        /// get a better control than Unity's default — a clip-name dropdown, an asset picker). Subscribe to
        /// OnTypeChanged and rebuild: switching the type changes which fields exist.
        public static ZuiManagedRef ManagedRef(SerializedProperty property, string title, string tooltip)
            => new ZuiManagedRef(property, title, tooltip);

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

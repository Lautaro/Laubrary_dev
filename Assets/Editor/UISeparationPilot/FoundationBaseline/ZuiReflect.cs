// ZuiReflect — build controls for fields discovered by REFLECTION, rather than known at compile time.
// This is what a data-driven inspector needs (Laubrary.Rulesets renders every GameRule subclass this
// way) and it closes three of the IMGUI half's standing `// ZUI-GAP:` markers at once, because UI
// Toolkit solves natively what IMGUI could not:
//   • a typed object field whose type is only known at runtime — `ObjectField.objectType` is a plain
//     property, so no compile-time generic parameter is needed (the IMGUI ZUI.ObjectField<T> could
//     not express this, which is why those call sites stayed raw EditorGUILayout);
//   • an enum for a runtime Enum value — rendered as WRAPPED MINI-RADIOS (and a [Flags] enum as a
//     multi-select segmented row), per the toolkit rule that an enum is a radio/segmented, never a
//     native EnumField dropdown — no compile-time generic needed for either;
//   • boxed list rows / cards — plain `Z.Box`.
//
// Everything here still obeys the toolkit's rules: a tooltip on every control, explicit widths, and a
// single mutation contract (onBeforeChange fires once before a change, onChanged after).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace Laubrary.Zui.FoundationBaseline
{
    using Z = Laubrary.Zui.FoundationFactoryBaseline.Z;
    using ZuiText = Laubrary.Zui.FoundationFactoryBaseline.ZuiText;
    public static class ZuiReflect
    {
        /// A field whose type is only known at runtime (`ObjectField.objectType` is settable, so unlike
        /// the IMGUI half this needs no compile-time generic).
        public static ObjectField ObjectByType(Type objectType, UnityEngine.Object value, string tooltip,
            Action<UnityEngine.Object> onChanged, float width = 200f, bool allowSceneObjects = true)
        {
            var f = new ObjectField
            {
                objectType = objectType,
                value = value,
                tooltip = tooltip,
                allowSceneObjects = allowSceneObjects,
            };
            f.style.width = width;
            f.RegisterValueChangedCallback(e => onChanged?.Invoke(e.newValue));
            return f;
        }

        /// A reflected enum as WRAPPED MINI-RADIOS — never a native EnumField dropdown (ui-layout-rules:
        /// an enum is a radio/segmented). A [Flags] enum instead becomes a multi-select segmented row over
        /// its single-bit members, so several flags can light at once. `onChanged` gets the new Enum value.
        public static VisualElement EnumControl(Enum value, string tooltip, Action<Enum> onChanged)
        {
            var t = value.GetType();
            string[] names = Enum.GetNames(t);
            Array values = Enum.GetValues(t);
            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++) labels[i] = ObjectNames.NicifyVariableName(names[i]);

            if (Attribute.IsDefined(t, typeof(FlagsAttribute)))
            {
                // Single-bit members only (skip 0 = "None" and any pre-combined masks); toggle bits on the value.
                var bits = new List<long>();
                var bitLabels = new List<string>();
                for (int i = 0; i < values.Length; i++)
                {
                    long bv = Convert.ToInt64(values.GetValue(i));
                    if (bv != 0 && (bv & (bv - 1)) == 0) { bits.Add(bv); bitLabels.Add(labels[i]); }
                }
                long cur = Convert.ToInt64(value);
                return Z.SegmentedMulti(i => (cur & bits[i]) != 0, bitLabels.ToArray(), tooltip,
                    (i, on) => { cur = on ? (cur | bits[i]) : (cur & ~bits[i]); onChanged?.Invoke((Enum)Enum.ToObject(t, cur)); });
            }

            int sel = 0;
            for (int i = 0; i < values.Length; i++) if (values.GetValue(i).Equals(value)) { sel = i; break; }

            // A short set is a Z.Segmented, a long one wraps as mini-radios — the same split every hand-written
            // ZUI window already makes (ui-layout-rules: "Segmented for short 2–3 single-line sets", and "the
            // same KIND of value should use the same control everywhere"). Before this, a three-option enum
            // drawn by hand and the same enum drawn by reflection looked like two different controls in one
            // window, which reads as an oversight even when each choice is defensible on its own.
            if (values.Length <= 3)
                return Z.Segmented(sel, labels, tooltip, i => onChanged?.Invoke((Enum)values.GetValue(i)));

            return Z.MiniRadio(sel, labels, tooltip, i => onChanged?.Invoke((Enum)values.GetValue(i)), wrap: true);
        }

        /// A compact X/Y pair. Deliberately NOT the 2D pad: a reflected Vector2 is just as likely to be a
        /// min/max or a size as a spatial position, and a drag-pad would misrepresent those. Use
        /// `Z.Vector2Field` explicitly where a field really is a position.
        public static VisualElement Vector2Row(string label, Vector2 value, string tooltip,
            Action<Vector2> onChanged, float fieldWidth = 60f)
        {
            var x = Z.Float(value.x, tooltip + " (X)", v => onChanged?.Invoke(new Vector2(v, value.y)), fieldWidth);
            var y = Z.Float(value.y, tooltip + " (Y)", v => onChanged?.Invoke(new Vector2(value.x, v)), fieldWidth);
            return Z.Field(label, tooltip, Z.Row(Z.Field("X", tooltip + " (X)", x), Z.Field("Y", tooltip + " (Y)", y)));
        }

        public static VisualElement Vector2IntRow(string label, Vector2Int value, string tooltip,
            Action<Vector2Int> onChanged, float fieldWidth = 60f)
        {
            var x = Z.Int(value.x, tooltip + " (X)", v => onChanged?.Invoke(new Vector2Int(v, value.y)), fieldWidth);
            var y = Z.Int(value.y, tooltip + " (Y)", v => onChanged?.Invoke(new Vector2Int(value.x, v)), fieldWidth);
            return Z.Field(label, tooltip, Z.Row(Z.Field("X", tooltip + " (X)", x), Z.Field("Y", tooltip + " (Y)", y)));
        }

        /// Options for <see cref="BuildFields"/> / <see cref="BuildField"/>.
        public class Options
        {
            /// Fires once before a mutation — the Undo.RecordObject hook.
            public Action OnBeforeChange;
            /// Fires after every mutation.
            public Action OnChanged;
            /// Rebuild the surrounding UI (used when a list gains/loses elements).
            public Action OnStructureChanged;
            /// Tooltip for a field; default explains the field's own name + type.
            public Func<FieldInfo, string> TooltipFor;
            /// Skip a field entirely.
            public Func<FieldInfo, bool> Skip;
            /// A duck-typed "wrapper holding a float" (e.g. ZUIValue's staticValue) — return the property
            /// to edit, or null. Lets a host expose such wrappers as plain floats without this file
            /// referencing the wrapper's type.
            public Func<Type, PropertyInfo> FloatWrapperProperty;
            /// Per-field customisation of a reflected ZUIValue's control options, called AFTER the [Range]
            /// bounds are applied. The hook a host needs to disable modes that are wrong in its context —
            /// e.g. a baked-renderer modifier param evaluates Min-Max to ONE frame-invariant constant, so its
            /// host hides that mode (allowMinMax = false) and the meaningless runtime Duration/Warmup/Loop
            /// row (hideCurveTiming = true) instead of offering controls that do nothing.
            public Action<FieldInfo, ZuiValueControl.Options> ConfigureValue;
            /// What a double-click on this field's slider should restore, when the field's own initializer is
            /// not the whole story. Returning null (or leaving this null) falls back to the initializer read
            /// off a fresh owner, which is the right answer almost everywhere. The exception is a field whose
            /// registered default depends on the CONTEXT the object is used in — DotGen's Grid is 8x7 on a root
            /// generator and 4x4 on a child, one type serving two roles — where a reset to the type's own
            /// number would quietly be the wrong number.
            public Func<FieldInfo, float?> DefaultFor;
            public float ControlWidth = 150f;
        }

        /// Every public, serializable, non-hidden instance field of `owner`'s type, base-first.
        public static FieldInfo[] FieldsOf(Type t)
        {
            if (s_fieldCache.TryGetValue(t, out var arr)) return arr;
            var list = new List<FieldInfo>();
            var chain = new List<Type>();
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType) chain.Add(cur);
            chain.Reverse();
            foreach (var ct in chain)
                foreach (var f in ct.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized) continue;
                    if (Attribute.IsDefined(f, typeof(HideInInspector))) continue;
                    list.Add(f);
                }
            arr = list.ToArray();
            s_fieldCache[t] = arr;
            return arr;
        }

        static readonly Dictionary<Type, FieldInfo[]> s_fieldCache = new();

        // ── field-initializer defaults (double-click reset) ──────────────────────────
        //
        // A ZuiMicroSlider offers double-click-to-reset only when it was TOLD what to reset to, and a
        // reflected field has nowhere to declare that: `public float radius = 0.35f;` is the author's
        // statement of intent, but it lives in IL, not in an attribute. So read it back off a fresh
        // instance of the owner's type — one throwaway object per type, kept for the session — and hand
        // the number to the slider. Nothing else about the reflected control changes: the reset routes
        // through the same onBeforeMutate/onChanged pair as a drag, so a host that already records Undo
        // gets one undo step and one re-evaluate for free, with no per-tool wiring.

        static readonly Dictionary<Type, object> s_defaultProbe = new();

        /// A throwaway instance of `t` whose fields still hold their initializers, or null when one cannot
        /// be made SAFELY. Deliberately conservative — a missing default costs a reset, a bad `new` costs a
        /// broken inspector:
        ///   • a UnityEngine.Object (ScriptableObject/MonoBehaviour) is never constructed with `new` — it
        ///     needs its own factory and would run OnEnable on a phantom object;
        ///   • an abstract or open generic type cannot be constructed at all — for a [SerializeReference]
        ///     slot the caller passes the CONCRETE runtime type of the assigned instance, which can be;
        ///   • a type with no parameterless constructor, or one that throws, yields null rather than an error.
        static object DefaultProbe(Type t)
        {
            if (t == null) return null;
            if (s_defaultProbe.TryGetValue(t, out var cached)) return cached;
            object made = null;
            bool constructible = !typeof(UnityEngine.Object).IsAssignableFrom(t)
                                 && !t.IsAbstract && !t.ContainsGenericParameters
                                 && (t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null);
            if (constructible)
            {
                try { made = Activator.CreateInstance(t); }
                catch { made = null; }
            }
            s_defaultProbe[t] = made;
            return made;
        }

        /// The number `field` holds on a fresh `ownerType` — what a double-click on its slider restores.
        /// Null when no probe could be made or the field is not a plain float/int.
        static float? DefaultNumberOf(Type ownerType, FieldInfo field)
        {
            var probe = DefaultProbe(ownerType);
            if (probe == null) return null;
            try
            {
                object dv = field.GetValue(probe);
                if (dv is float f) return f;
                if (dv is int i) return i;
                return null;
            }
            catch { return null; }
        }

        /// The same, one level deeper: the float inside a duck-typed wrapper field (a Rulesets RuleParam's
        /// `staticValue`) on a fresh owner. Null when either level is unavailable.
        static float? DefaultWrappedNumberOf(Type ownerType, FieldInfo field, PropertyInfo wrapperProp)
        {
            var probe = DefaultProbe(ownerType);
            if (probe == null || wrapperProp == null) return null;
            try
            {
                object wrapper = field.GetValue(probe);
                if (wrapper == null) return null;
                object dv = wrapperProp.GetValue(wrapper);
                return dv is float f ? f : (float?)null;
            }
            catch { return null; }
        }

        /// Build a control for every field of `owner`, appended to `root`.
        public static void BuildFields(VisualElement root, object owner, Options opt)
        {
            if (owner == null) return;
            var fields = FieldsOf(owner.GetType());

            // A [ZUIPair2D] X field swallows its Y partner into one 2D control, so the partner must not also
            // be drawn on its own further down the card. Collected first, because the Y field can be declared
            // before the X one and a single forward pass would already have drawn it.
            HashSet<string> consumed = null;
            foreach (var f in fields)
            {
                var pair = PairAttributeOf(f);
                if (pair == null) continue;
                if (FindField(fields, pair.YField) == null) continue;   // bad name → both draw normally
                (consumed ??= new HashSet<string>()).Add(pair.YField);
            }

            foreach (var f in fields)
            {
                if (consumed != null && consumed.Contains(f.Name)) continue;
                if (!VisibleNow(owner, f, fields)) continue;
                if (opt?.Skip != null && opt.Skip(f)) continue;
                var ve = BuildField(owner, f, opt);
                if (ve != null) root.Add(ve);
            }
        }

        /// Whether a [ZUIShowIf] field applies to the owner's CURRENT state. Fails OPEN — an attribute
        /// naming a field that no longer exists shows the control rather than silently deleting it from the
        /// UI, because a stale dial is a visible problem and a missing one is not.
        static bool VisibleNow(object owner, FieldInfo f, FieldInfo[] all)
        {
            var show = (ZUIShowIfAttribute)Attribute.GetCustomAttribute(f, typeof(ZUIShowIfAttribute));
            if (show == null) return true;
            var gate = FindField(all, show.Field);
            if (gate == null) return true;
            string cur = gate.GetValue(owner)?.ToString();
            foreach (var v in show.Values)
                if (string.Equals(v, cur, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// Field names that some [ZUIShowIf] on this type reads — the ones whose edit has to redraw the card.
        static readonly Dictionary<Type, HashSet<string>> s_gateCache = new();
        static bool IsGate(Type t, string fieldName)
        {
            if (!s_gateCache.TryGetValue(t, out var gates))
            {
                gates = new HashSet<string>();
                foreach (var f in FieldsOf(t))
                {
                    var show = (ZUIShowIfAttribute)Attribute.GetCustomAttribute(f, typeof(ZUIShowIfAttribute));
                    if (show != null && !string.IsNullOrEmpty(show.Field)) gates.Add(show.Field);
                }
                s_gateCache[t] = gates;
            }
            return gates.Contains(fieldName);
        }

        /// The per-axis name for a paired field: the trailing "X"/"Y" of "Offset X" carries no information
        /// once the control is labelled "Offset", but a real axis name ("Pitch") is worth keeping.
        static string AxisLabel(string nicified, string fallback)
        {
            if (string.IsNullOrEmpty(nicified)) return fallback;
            int sp = nicified.LastIndexOf(' ');
            string last = sp >= 0 ? nicified.Substring(sp + 1) : nicified;
            return last.Length <= 2 ? fallback : last;
        }

        /// "Replacements" → "Replacement". Only the trivial plural; anything irregular keeps its own name,
        /// which reads no worse than the list label did.
        static string Singular(string plural)
            => !string.IsNullOrEmpty(plural) && plural.Length > 1 && plural.EndsWith("s")
                ? plural.Substring(0, plural.Length - 1) : plural;

        /// Build an object's fields as a FLOWING row — short controls sit beside each other, and a control
        /// that draws a curve or a plot takes a line of its own. The shared implementation of the
        /// space-economy rule, so a nested list element and a top-level effect card lay out the same way.
        public static void FlowFields(VisualElement host, object owner, Options opt)
        {
            var flow = new VisualElement();
            flow.style.flexDirection = FlexDirection.Row;
            flow.style.flexWrap = Wrap.Wrap;
            flow.style.alignItems = Align.FlexStart;
            BuildFields(flow, owner, opt);

            void ApplyWidths()
            {
                foreach (var child in flow.Children())
                    child.style.flexBasis = IsWideControl(child)
                        ? new StyleLength(Length.Percent(100f))
                        : new StyleLength(StyleKeyword.Auto);
            }
            ApplyWidths();

            // Re-decide when a value CHANGES MODE. How much room a control deserves depends on its mode, and
            // a mode switch rebuilds only that control — so a value switched to Envelope kept the
            // slider-sized slot it had, leaving four envelopes crushed side by side and undraggable. The
            // widths are re-applied rather than the card rebuilt, so the switch costs nothing and does not
            // throw away scroll position.
            foreach (var child in flow.Children())
            {
                var val = child as ZuiValueControl ?? child.Q<ZuiValueControl>();
                if (val != null) val.ModeChanged += ApplyWidths;
            }

            host.Add(flow);
        }

        /// A control that earns a whole line: one drawing a curve or a plot rather than sitting on a row like
        /// a slider. An animatable value counts only in its ENVELOPE modes — in Static or Min-Max it is one
        /// slider row and must flow like anything else.
        public static bool IsWideControl(VisualElement e)
        {
            var val = e as ZuiValueControl ?? e.Q<ZuiValueControl>();
            if (val != null) return val.IsCurveShaped;
            // A ramp strip is wide by nature — leave it out of this list and it gets squeezed into the flow beside
            // a slider, which is exactly the clipping the deleted bands control had to be re-fitted for.
            return e is ZuiValue2DControl || e is ZuiGradientControl || e is ZuiRampControl ||
                   e.Q<ZuiValue2DControl>() != null || e.Q<ZuiGradientControl>() != null ||
                   e.Q<ZuiRampControl>() != null;
        }

        static ZUIPair2DAttribute PairAttributeOf(FieldInfo f)
            => (ZUIPair2DAttribute)Attribute.GetCustomAttribute(f, typeof(ZUIPair2DAttribute));

        static FieldInfo FindField(FieldInfo[] fields, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var f in fields) if (f.Name == name) return f;
            return null;
        }

        [ThreadStatic] static int _nestDepth;   // recursion guard for nested plain-class fields

        /// Build one control for `field` on `owner`, or null when the type isn't renderable.
        public static VisualElement BuildField(object owner, FieldInfo field, Options opt)
        {
            opt ??= new Options();
            string nice = ObjectNames.NicifyVariableName(field.Name);
            // A field's own [Tooltip] is the AUTHORED description of what it does — always better than a
            // generated sentence that only restates the label, so it outranks the fallback.
            string tip = opt.TooltipFor?.Invoke(field) ?? TooltipAttributeOf(field)
                         ?? $"{nice} — a {PrettyTypeName(field.FieldType)} value on {owner.GetType().Name}.";
            object v = field.GetValue(owner);
            var range = (RangeAttribute)Attribute.GetCustomAttribute(field, typeof(RangeAttribute));
            var t = field.FieldType;

            void Set(object nv)
            {
                opt.OnBeforeChange?.Invoke();
                field.SetValue(owner, nv);
                opt.OnChanged?.Invoke();
                // A field some [ZUIShowIf] reads decides which other fields apply, so editing it has to
                // redraw the card — otherwise the dials it just made relevant stay hidden until something
                // unrelated happens to rebuild, which reads as the switch not working.
                if (IsGate(owner.GetType(), field.Name)) opt.OnStructureChanged?.Invoke();
            }

            // A hue in degrees gets a colour swatch beside its slider: pick a colour, the field takes its hue.
            // Typing "25" for orange is a conversion the author has to do in their head with nothing on screen
            // to check it against.
            if (t == typeof(float) && Attribute.IsDefined(field, typeof(ZUIHueAttribute)))
            {
                float hue = (float)v;
                var slider = Z.MicroSlider(nice, hue, range?.min ?? 0f, range?.max ?? 360f, tip,
                    nv => Set(nv), opt.ControlWidth, showValue: true,
                    defaultValue: DefaultNumberOf(owner.GetType(), field));
                var swatch = Z.Color(Color.HSVToRGB(Mathf.Repeat(hue, 360f) / 360f, 1f, 1f),
                    tip + "  •  Click the swatch to choose a colour, or use the EYEDROPPER beside it to " +
                    "sample one from anywhere on screen — including the sprite in the preview above. Only " +
                    "the hue is taken.",
                    c => { Color.RGBToHSV(c, out float h, out _, out _); Set(h * 360f); }, 60f);
                swatch.style.flexShrink = 0f;
                var row = Z.Row(slider, swatch);
                row.style.flexShrink = 0f;
                return row;
            }

            if (t == typeof(float))
                // Bounded (a [Range] attr = min/max in the call) → a MicroSlider, per the toolkit rule that a
                // ranged scalar is a MicroSlider (label + value INSIDE the track), never a plain Slider + a
                // separate numeric field. Unbounded → a scrub Float wrapped in a Z.Field for its external label.
                // The MicroSlider carries its OWN caption, so it is NOT wrapped in a Z.Field (that prints twice).
                // `defaultValue` is the field's own initializer, read off a fresh owner (see DefaultProbe) —
                // it is what makes double-click-to-reset work on a reflected dial at all.
                return range != null
                    ? (VisualElement)Z.MicroSlider(nice, (float)v, range.min, range.max, tip,
                        nv => Set(nv), opt.ControlWidth, showValue: true,
                        defaultValue: opt.DefaultFor?.Invoke(field) ?? DefaultNumberOf(owner.GetType(), field))
                    : Z.Field(nice, tip, Z.Float((float)v, tip, nv => Set(nv), 80f));

            if (t == typeof(int))
                // Bounded int → a whole-number MicroSlider (decimals 0, rounded at the setter), matching how the
                // hand-written windows render a bounded count (Pyre deliberately uses a MicroSlider, not a
                // thumbed SliderInt, for a count). Unbounded → a scrub Int wrapped in a Z.Field for its label.
                return range != null
                    ? (VisualElement)Z.MicroSlider(nice, (int)v, range.min, range.max, tip,
                        nv => Set(Mathf.RoundToInt(nv)), opt.ControlWidth, showValue: true,
                        defaultValue: opt.DefaultFor?.Invoke(field) ?? DefaultNumberOf(owner.GetType(), field),
                        decimals: 0)
                    : Z.Field(nice, tip, Z.Int((int)v, tip, nv => Set(nv), 80f));

            if (t == typeof(bool))
                return Z.Toggle(nice, tip, (bool)v, nv => Set(nv));

            if (t == typeof(string))
                return Z.Field(nice, tip, Z.TextInput((string)v ?? "", tip, nv => Set(nv), opt.ControlWidth));

            if (t.IsEnum)
            {
                // The field row itself must be allowed to shrink to the pane, or the wrapped radio inside it never sees a
                // narrower container and a five-option set runs off the pane edge (T-0047's Variant row did).
                var enumRow = Z.Field(nice, tip, EnumControl((Enum)v, tip, nv => Set(nv)));
                enumRow.style.flexShrink = 1;
                return enumRow;
            }

            if (t == typeof(Color))
                return Z.Field(nice, tip, Z.Color((Color)v, tip, nv => Set(nv), 110f));

            if (t == typeof(Gradient))
                // A Gradient (e.g. TintModifier.crossGradient) via Z.Gradient — Unity's own gradient editor, sized
                // not stretched. Previously unrendered by this drawer, so a reflected effect/modifier with a gradient
                // field showed every field EXCEPT the gradient; this closes that gap for every reflected tool
                // (SpriteFx, Pyre, Chunks) at once.
                return Z.Field(nice, tip, Z.Gradient((Gradient)v, tip, nv => Set(nv), opt.ControlWidth));

            if (t == typeof(AnimationCurve))
            {
                var ac = v as AnimationCurve;
                if (ac == null) { ac = AnimationCurve.Linear(0f, 0f, 1f, 1f); field.SetValue(owner, ac); }
                return Z.Field(nice, tip, Z.Curve(ac, tip, nv => Set(nv), opt.ControlWidth));
            }

            if (t == typeof(Vector2))
            {
                // A [Range]-bounded Vector2 is a min/max PAIR (x = low, y = high) → ONE two-handle range slider, per the
                // layout rule that a pair is never two separate controls (the Kiln forms' `tongueX` / `emberRise`
                // ranges). Unbounded stays the plain X/Y row.
                if (range != null)
                {
                    var pair = (Vector2)v;
                    return Z.Field(nice, tip, Z.MinMax(pair.x, pair.y, range.min, range.max, tip,
                        (lo, hi) => Set(new Vector2(lo, hi)), opt.ControlWidth));
                }
                return Vector2Row(nice, (Vector2)v, tip, nv => Set(nv));
            }

            if (t == typeof(Vector2Int))
                return Vector2IntRow(nice, (Vector2Int)v, tip, nv => Set(nv));

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return Z.Field(nice, tip, ObjectByType(t, (UnityEngine.Object)v, tip, nv => Set(nv), opt.ControlWidth));

            // An animatable ZUIValue → the FULL Static / Min-Max / Curve control (Z.Value / ZuiValueControl), not
            // just its static float. Toolkit rule (ui-layout-rules: "an animatable value → Z.Value, whose ⋯ menu
            // switches Static / Min-Max / Curve"). ZUIValue is a Zui type, so this drawer names it directly (no
            // duck-typing) — and every tool that reflects modifier/serialized fields (Pyre, Chunks, a SpriteFx
            // stack) gains real curve authoring here at once, closing the old "static value only" limitation.
            // Placed BEFORE the generic float-wrapper fallback below, which would otherwise catch a ZUIValue by its
            // `staticValue` property and flatten it to one number. A [Range] on the field sets the bounds; without
            // one the control's default range applies and the author can still switch modes via the ⋯ menu.
            if (t == typeof(ZUIValue))
            {
                var zv = v as ZUIValue;
                if (zv == null) { zv = new ZUIValue(); field.SetValue(owner, zv); }

                // Two halves of one spatial value → ONE 2D control. Aiming an offset by dragging two
                // independent sliders and checking the preview to see where it went is the exact ergonomics
                // the layout rules single out; the pad puts the value where the eye already is. Each axis
                // keeps its OWN [Range], so a control spanning -1.5..1.5 horizontally and 0..2 vertically
                // stays honest about both.
                var pair = PairAttributeOf(field);
                if (pair != null)
                {
                    var yField = FindField(FieldsOf(owner.GetType()), pair.YField);
                    if (yField != null && yField.FieldType == typeof(ZUIValue))
                    {
                        var yv = yField.GetValue(owner) as ZUIValue;
                        if (yv == null) { yv = new ZUIValue(); yField.SetValue(owner, yv); }
                        var yRange = (RangeAttribute)Attribute.GetCustomAttribute(yField, typeof(RangeAttribute));
                        var popt = new ZuiValue2DControl.Options
                        {
                            xMin = range?.min ?? -1f, xMax = range?.max ?? 1f,
                            yMin = yRange?.min ?? -1f, yMax = yRange?.max ?? 1f,
                            xLabel = AxisLabel(nice, "X"),
                            yLabel = AxisLabel(ObjectNames.NicifyVariableName(yField.Name), "Y"),
                            // Two pads on one card (an offset and a drift) must not share the pad-vs-sliders
                            // preference, so the key names the owning type and the pair.
                            prefKey = $"{owner.GetType().Name}.{field.Name}",
                        };
                        string pairLabel = pair.Label ?? nice;
                        string pairTip = opt.TooltipFor?.Invoke(field) ?? TooltipAttributeOf(field) ?? tip;
                        return Z.Value2D(pairLabel, zv, yv, popt, pairTip,
                            onChanged: () => opt.OnChanged?.Invoke(),
                            onBeforeMutate: () => opt.OnBeforeChange?.Invoke());
                    }
                }

                var vopt = new ZuiValueControl.Options { controlWidth = opt.ControlWidth };
                if (range != null) vopt.WithRange(range.min, range.max);
                // A value the effect rounds to a whole number must be AUTHORED in whole numbers, or the
                // slider spends half its first unit doing nothing and then jumps a full step at once.
                if (Attribute.IsDefined(field, typeof(ZUIWholeNumberAttribute))) vopt.decimals = 0;
                opt.ConfigureValue?.Invoke(field, vopt);
                // A float→ZUIValue migration companion is conventionally named `<legacy>Value` (the frozen
                // legacy float keeps the plain name) — strip the suffix so the label reads as the PARAM
                // ("Bury", not "Bury Value"). Only when something is left; a field literally named `value`
                // keeps its label.
                string label = nice;
                if (label.EndsWith(" Value") && label.Length > 6) label = label.Substring(0, label.Length - 6);
                return Z.Value(label, zv, vopt, tip,
                    onChanged: () => opt.OnChanged?.Invoke(),
                    onBeforeMutate: () => opt.OnBeforeChange?.Invoke());
            }

            // A NON-ZUIValue wrapper whose payload is a single float (a duck-typed `staticValue`, e.g. a Rulesets
            // RuleParam) — surfaced as a plain float so such tunables stay editable without this file knowing the
            // wrapper's type. (ZUIValue itself is handled above, with its full mode control.)
            var wrapperProp = opt.FloatWrapperProperty?.Invoke(t);
            if (wrapperProp != null && v != null)
            {
                float cur = (float)wrapperProp.GetValue(v);
                void SetWrapped(float nv)
                {
                    opt.OnBeforeChange?.Invoke();
                    wrapperProp.SetValue(v, nv);
                    field.SetValue(owner, v);
                    opt.OnChanged?.Invoke();
                }
                // Same rule as the plain-float case: bounded → a MicroSlider (label + value inside the track),
                // unbounded → a scrub Float wrapped for its external label.
                return range != null
                    ? (VisualElement)Z.MicroSlider(nice, cur, range.min, range.max, tip,
                        SetWrapped, opt.ControlWidth, showValue: true,
                        defaultValue: DefaultWrappedNumberOf(owner.GetType(), field, wrapperProp))
                    : Z.Field(nice, tip, Z.Float(cur, tip, SetWrapped, 80f));
            }

            // A ZuiGradient (the richer base-Gradient-plus-transform-knobs type, NOT Unity's Gradient handled above)
            // → the ZuiGradientControl with its live TRUE-evaluated preview strip. Mutates in place, so wire the
            // host's Undo/dirty hooks directly rather than through Set. Every reflected tool with a ZuiGradient
            // field (a ColorRemap region, a future ZuiFill knob) gains the full editor here at once.
            if (t == typeof(ZuiGradient))
            {
                var zg = v as ZuiGradient;
                if (zg == null) { zg = new ZuiGradient(); field.SetValue(owner, zg); }
                var gc = new ZuiGradientControl(zg, tip) { OnBeforeMutate = opt.OnBeforeChange, OnChanged = opt.OnChanged };
                return Z.Field(nice, tip, gc);
            }

            // A ZuiSwatchRef (inline colour OR a named SwatchPalette swatch) → the Z.Swatch picker. It's a struct,
            // so the control hands back a NEW value which Set writes to the field (Set already fires OnBeforeChange,
            // so the control's own OnBeforeMutate is left unset to avoid double-recording).
            if (t == typeof(ZuiSwatchRef))
            {
                var sc = new ZuiSwatchControl((ZuiSwatchRef)v, tip, opt.ControlWidth) { OnChanged = nv => Set(nv) };
                return Z.Field(nice, tip, sc);
            }

            // A colour ramp that speaks IZuiRamp (Pyre's PyreRamp) → ONE ZuiRampControl: a strip painted from the
            // ramp's own Eval with a marker per stop. This case has to sit BEFORE both the List<> branch and the
            // nested-plain-class branch below, or a PyreRamp falls through to a titled box wrapping a list box of
            // near-identical "Stop N" cards — ten of them for a Jet ramp, which is what this replaces. Type-level,
            // so every authored ramp field in every form adopts it at once with no per-form edit. Mutates in place
            // like the ZuiGradient case above, so the host's Undo/dirty hooks wire straight in rather than through
            // Set; a null field gets a fresh instance the same way.
            if (typeof(IZuiRamp).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract
                && t.GetConstructor(Type.EmptyTypes) != null)
            {
                var ramp = v as IZuiRamp;
                if (ramp == null)
                {
                    var fresh = Activator.CreateInstance(t);
                    field.SetValue(owner, fresh);
                    ramp = (IZuiRamp)fresh;
                }
                var rc = new ZuiRampControl(ramp, tip) { OnBeforeMutate = opt.OnBeforeChange, OnChanged = opt.OnChanged };
                return Z.Field(nice, tip, rc);
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return BuildList(owner, field, nice, tip, opt);

            // A nested plain [Serializable] settings object (a Pyre PyreRamp, a future grouped-options class):
            // a titled box flowing ITS fields, keyed stably by owner type + field name like a list. Null-valued
            // fields get a fresh instance so the box is never empty. Depth-guarded so a self-referential type
            // cannot recurse forever (a real-world nesting is one or two levels).
            if (t.IsClass && !typeof(UnityEngine.Object).IsAssignableFrom(t) && t.IsSerializable && !t.IsAbstract
                && t.GetConstructor(Type.EmptyTypes) != null && _nestDepth < 3)
            {
                if (v == null) { v = Activator.CreateInstance(t); field.SetValue(owner, v); }
                var box = Z.BoxKeyed(nice, tip, $"reflect.nested.{owner.GetType().Name}.{field.Name}");
                _nestDepth++;
                try { FlowFields(box, v, opt); }
                finally { _nestDepth--; }
                return box;
            }

            return null;   // not a type this renderer knows how to show
        }

        static VisualElement BuildList(object owner, FieldInfo field, string nice, string tip, Options opt)
        {
            var elemType = field.FieldType.GetGenericArguments()[0];
            if (field.GetValue(owner) is not IList list)
            {
                list = (IList)Activator.CreateInstance(field.FieldType);
                field.SetValue(owner, list);
            }

            bool isClass = elemType.IsClass && elemType != typeof(string)
                           && !typeof(UnityEngine.Object).IsAssignableFrom(elemType);

            // A STABLE view-capture key (owner type + field name), never the title. The title carries a LIVE
            // element count, so a title-derived fallback key would DRIFT every time the list grows/shrinks —
            // orphaning this box's captured fold/view state in any ZuiViewBar-carrying host (e.g. Pyre, whose
            // bar captures every ZuiBox). BoxKeyed pins the key so the count stays visible in the title without
            // moving the key. (An untitled per-item card below stays a bare, non-captured Z.Box.)
            var box = Z.BoxKeyed($"{nice}  ({list.Count})", tip + $" A list of {PrettyTypeName(elemType)}.",
                $"reflect.list.{owner.GetType().Name}.{field.Name}");
            // A list whose element is NOTHING but a handful of bounded scalars (ExplosiveJetSettings.blasts: five
            // [Range] floats and no more) does not need a header row above a body row — that spends two lines and a
            // card title on saying "#3". Such an element collapses to ONE row: index, its fields inline, the ×.
            // Deliberately narrow: any element carrying a colour, a string, a nested class, a list, an Object
            // reference or a conditional field keeps the old card, because those are real sub-forms.
            bool compact = isClass && IsCompactRowElement(elemType);
            var elemOpt = compact ? CompactOptions(opt) : opt;

            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var card = Z.Box(null, null);
                var remove = Z.Button("×", $"Remove {Singular(nice).ToLowerInvariant()} {idx + 1} from {nice}.", () =>
                {
                    opt.OnBeforeChange?.Invoke();
                    list.RemoveAt(idx);
                    opt.OnChanged?.Invoke();
                    opt.OnStructureChanged?.Invoke();
                }).W(22f);

                if (compact)
                {
                    var elem = list[idx];
                    if (elem == null) { elem = Activator.CreateInstance(elemType); list[idx] = elem; }
                    var row = Z.Row();
                    // NO wrap: a flexible gap in a wrapping row pushes the × onto a line of its own, which is the
                    // "confusing empty space" failure the card-layout rule warns about. The dials shrink instead.
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.flexWrap = Wrap.NoWrap;
                    row.style.alignItems = Align.Center;
                    row.Add(Z.Text($"#{idx + 1}", ZuiText.Small,
                        $"{Singular(nice)} {idx + 1} of {nice}.").W(26f));
                    BuildFields(row, elem, elemOpt);
                    row.Add(Z.Flexible());
                    row.Add(remove);
                    card.Add(row);
                    box.Add(card);
                    continue;
                }

                // Named, not indexed. "[0]" tells the reader nothing they cannot already see from the order,
                // and it names the ARRAY rather than the thing — this is the first replacement, not element
                // zero of anything the author thinks about.
                card.Add(Z.Row(
                    Z.Text($"{Singular(nice)} {idx + 1}", ZuiText.Small, $"{Singular(nice)} {idx + 1} of {nice}."),
                    Z.Flexible(),
                    remove));

                if (isClass)
                {
                    var elem = list[idx];
                    if (elem == null) { elem = Activator.CreateInstance(elemType); list[idx] = elem; }
                    // FLOWED, not stacked. A nested element's fields used to run straight down a narrow
                    // column with the whole width beside them empty — nine sliders tall for one hue
                    // replacement, so two replacements filled the window and you could not see them together.
                    FlowFields(card, elem, opt);
                }
                else
                {
                    var ve = BuildElement(list, idx, elemType, tip, opt);
                    if (ve != null) card.Add(ve);
                }
                box.Add(card);
            }

            // Labelled from the FIELD, not the C# type: "+ Add replacement", never "+ Add HueReplacement".
            // A type name on a button is an implementation detail leaking onto the surface.
            box.Add(Z.Button("+ Add " + Singular(nice).ToLowerInvariant(),
                $"Append another {Singular(nice).ToLowerInvariant()} to {nice}.", () =>
            {
                opt.OnBeforeChange?.Invoke();
                list.Add(isClass || elemType.IsValueType
                    ? Activator.CreateInstance(elemType)
                    : (elemType == typeof(string) ? "" : null));
                opt.OnChanged?.Invoke();
                opt.OnStructureChanged?.Invoke();
            }));
            return box;
        }

        static readonly Dictionary<Type, bool> s_compactRowCache = new();

        /// Whether a list ELEMENT is nothing but a small set of bounded scalars, so its whole card can collapse to
        /// one row. Strict on purpose — this must be an improvement for the lists it fires on and a no-op for every
        /// other list in every other tool:
        ///   • every public serialized field is a [Range] float/int or a bool — a colour, string, enum, Vector,
        ///     nested class, list or Object reference disqualifies the element outright;
        ///   • between 2 and 5 such fields (one field is already a one-liner; six-plus will not fit a row);
        ///   • no [ZUIShowIf] and no [ZUIPair2D] anywhere on it, since a field that appears and disappears (or that
        ///     swallows its partner into a 2D pad) makes the row's width jump around under the user.
        static bool IsCompactRowElement(Type elemType)
        {
            if (s_compactRowCache.TryGetValue(elemType, out bool cached)) return cached;
            bool ok = Compute();
            s_compactRowCache[elemType] = ok;
            return ok;

            bool Compute()
            {
                if (!elemType.IsClass || elemType == typeof(string)) return false;
                if (typeof(UnityEngine.Object).IsAssignableFrom(elemType)) return false;
                int n = 0;
                foreach (var f in FieldsOf(elemType))
                {
                    if (Attribute.IsDefined(f, typeof(ZUIShowIfAttribute))) return false;
                    if (Attribute.IsDefined(f, typeof(ZUIPair2DAttribute))) return false;
                    var ft = f.FieldType;
                    if (ft == typeof(bool)) { n++; continue; }
                    if ((ft == typeof(float) || ft == typeof(int))
                        && Attribute.IsDefined(f, typeof(RangeAttribute))) { n++; continue; }
                    return false;
                }
                return n >= 2 && n <= 5;
            }
        }

        /// The element options for a compact row: identical to the host's, but with narrower dials so several fit
        /// one line. They still shrink further in a narrow pane rather than overflowing (UITK's default flexShrink),
        /// which is what keeps this from producing the horizontal scrollbar the layout rules call a bug signal.
        static Options CompactOptions(Options o) => new Options
        {
            OnBeforeChange = o.OnBeforeChange,
            OnChanged = o.OnChanged,
            OnStructureChanged = o.OnStructureChanged,
            TooltipFor = o.TooltipFor,
            Skip = o.Skip,
            FloatWrapperProperty = o.FloatWrapperProperty,
            ConfigureValue = o.ConfigureValue,
            DefaultFor = o.DefaultFor,
            ControlWidth = 100f,
        };

        static VisualElement BuildElement(IList list, int idx, Type elemType, string tip, Options opt)
        {
            void Set(object nv)
            {
                opt.OnBeforeChange?.Invoke();
                list[idx] = nv;
                opt.OnChanged?.Invoke();
            }
            string etip = $"Element {idx}. {tip}";

            if (typeof(UnityEngine.Object).IsAssignableFrom(elemType))
                return ObjectByType(elemType, (UnityEngine.Object)list[idx], etip, nv => Set(nv), opt.ControlWidth);
            if (elemType.IsEnum)
                return EnumControl((Enum)list[idx], etip, nv => Set(nv));
            if (elemType == typeof(float))
                return Z.Float((float)list[idx], etip, nv => Set(nv), 80f);
            if (elemType == typeof(int))
                return Z.Int((int)list[idx], etip, nv => Set(nv), 80f);
            if (elemType == typeof(string))
                return Z.TextInput((string)(list[idx] ?? ""), etip, nv => Set(nv), opt.ControlWidth);
            if (elemType == typeof(Vector2))
            {
                // Without this branch a List<Vector2> (e.g. a Smudge stroke's points) rendered as EMPTY cards.
                var cur = (Vector2)list[idx];
                return Z.Row(
                    Z.Float(cur.x, etip + " (X)", nv => Set(new Vector2(nv, ((Vector2)list[idx]).y)), 70f),
                    Z.Float(cur.y, etip + " (Y)", nv => Set(new Vector2(((Vector2)list[idx]).x, nv)), 70f));
            }
            if (elemType == typeof(Vector3))
            {
                var cur = (Vector3)list[idx];
                return Z.Row(
                    Z.Float(cur.x, etip + " (X)", nv => { var c = (Vector3)list[idx]; Set(new Vector3(nv, c.y, c.z)); }, 70f),
                    Z.Float(cur.y, etip + " (Y)", nv => { var c = (Vector3)list[idx]; Set(new Vector3(c.x, nv, c.z)); }, 70f),
                    Z.Float(cur.z, etip + " (Z)", nv => { var c = (Vector3)list[idx]; Set(new Vector3(c.x, c.y, nv)); }, 70f));
            }
            if (elemType == typeof(Vector2Int))
            {
                var cur = (Vector2Int)list[idx];
                return Z.Row(
                    Z.Int(cur.x, etip + " (X)", nv => { var c = (Vector2Int)list[idx]; Set(new Vector2Int(nv, c.y)); }, 70f),
                    Z.Int(cur.y, etip + " (Y)", nv => { var c = (Vector2Int)list[idx]; Set(new Vector2Int(c.x, nv)); }, 70f));
            }
            return null;
        }

        /// The field's authored [Tooltip] text, or null when it has none.
        public static string TooltipAttributeOf(FieldInfo field)
        {
            var a = (TooltipAttribute)Attribute.GetCustomAttribute(field, typeof(TooltipAttribute));
            return string.IsNullOrEmpty(a?.tooltip) ? null : a.tooltip;
        }

        static string PrettyTypeName(Type t)
        {
            if (t == typeof(float)) return "number";
            if (t == typeof(int)) return "whole number";
            if (t == typeof(bool)) return "on/off";
            if (t == typeof(string)) return "text";
            if (t.IsEnum) return "choice";
            return t.Name;
        }
    }
}

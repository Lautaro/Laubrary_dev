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

namespace Laubrary.Zui
{
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
            return e is ZuiValue2DControl || e is ZuiGradientControl ||
                   e.Q<ZuiValue2DControl>() != null || e.Q<ZuiGradientControl>() != null;
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
                    nv => Set(nv), opt.ControlWidth, showValue: true);
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
                return range != null
                    ? (VisualElement)Z.MicroSlider(nice, (float)v, range.min, range.max, tip,
                        nv => Set(nv), opt.ControlWidth, showValue: true)
                    : Z.Field(nice, tip, Z.Float((float)v, tip, nv => Set(nv), 80f));

            if (t == typeof(int))
                // Bounded int → a whole-number MicroSlider (decimals 0, rounded at the setter), matching how the
                // hand-written windows render a bounded count (PyrePlus deliberately uses a MicroSlider, not a
                // thumbed SliderInt, for a count). Unbounded → a scrub Int wrapped in a Z.Field for its label.
                return range != null
                    ? (VisualElement)Z.MicroSlider(nice, (int)v, range.min, range.max, tip,
                        nv => Set(Mathf.RoundToInt(nv)), opt.ControlWidth, showValue: true, decimals: 0)
                    : Z.Field(nice, tip, Z.Int((int)v, tip, nv => Set(nv), 80f));

            if (t == typeof(bool))
                return Z.Toggle(nice, tip, (bool)v, nv => Set(nv));

            if (t == typeof(string))
                return Z.Field(nice, tip, Z.TextInput((string)v ?? "", tip, nv => Set(nv), opt.ControlWidth));

            if (t.IsEnum)
                return Z.Field(nice, tip, EnumControl((Enum)v, tip, nv => Set(nv)));

            if (t == typeof(Color))
                return Z.Field(nice, tip, Z.Color((Color)v, tip, nv => Set(nv), 110f));

            if (t == typeof(Gradient))
                // A Gradient (e.g. TintModifier.crossGradient) via Z.Gradient — Unity's own gradient editor, sized
                // not stretched. Previously unrendered by this drawer, so a reflected effect/modifier with a gradient
                // field showed every field EXCEPT the gradient; this closes that gap for every reflected tool
                // (SpriteFx, PyrePlus, Chunks) at once.
                return Z.Field(nice, tip, Z.Gradient((Gradient)v, tip, nv => Set(nv), opt.ControlWidth));

            if (t == typeof(Vector2))
                return Vector2Row(nice, (Vector2)v, tip, nv => Set(nv));

            if (t == typeof(Vector2Int))
                return Vector2IntRow(nice, (Vector2Int)v, tip, nv => Set(nv));

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return Z.Field(nice, tip, ObjectByType(t, (UnityEngine.Object)v, tip, nv => Set(nv), opt.ControlWidth));

            // An animatable ZUIValue → the FULL Static / Min-Max / Curve control (Z.Value / ZuiValueControl), not
            // just its static float. Toolkit rule (ui-layout-rules: "an animatable value → Z.Value, whose ⋯ menu
            // switches Static / Min-Max / Curve"). ZUIValue is a Zui type, so this drawer names it directly (no
            // duck-typing) — and every tool that reflects modifier/serialized fields (PyrePlus, Chunks, a SpriteFx
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
                        SetWrapped, opt.ControlWidth, showValue: true)
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

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return BuildList(owner, field, nice, tip, opt);

            // A nested plain [Serializable] settings object (a PyrePlus PlusRamp, a future grouped-options class):
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
            // orphaning this box's captured fold/view state in any ZuiViewBar-carrying host (e.g. PyrePlus, whose
            // bar captures every ZuiBox). BoxKeyed pins the key so the count stays visible in the title without
            // moving the key. (An untitled per-item card below stays a bare, non-captured Z.Box.)
            var box = Z.BoxKeyed($"{nice}  ({list.Count})", tip + $" A list of {PrettyTypeName(elemType)}.",
                $"reflect.list.{owner.GetType().Name}.{field.Name}");
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var card = Z.Box(null, null);
                // Named, not indexed. "[0]" tells the reader nothing they cannot already see from the order,
                // and it names the ARRAY rather than the thing — this is the first replacement, not element
                // zero of anything the author thinks about.
                card.Add(Z.Row(
                    Z.Text($"{Singular(nice)} {idx + 1}", ZuiText.Small, $"{Singular(nice)} {idx + 1} of {nice}."),
                    Z.Flexible(),
                    Z.Button("×", $"Remove {Singular(nice).ToLowerInvariant()} {idx + 1} from {nice}.", () =>
                    {
                        opt.OnBeforeChange?.Invoke();
                        list.RemoveAt(idx);
                        opt.OnChanged?.Invoke();
                        opt.OnStructureChanged?.Invoke();
                    }).W(22f)));

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

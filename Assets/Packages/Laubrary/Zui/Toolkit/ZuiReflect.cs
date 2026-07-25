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
            foreach (var f in FieldsOf(owner.GetType()))
            {
                if (opt?.Skip != null && opt.Skip(f)) continue;
                var ve = BuildField(owner, f, opt);
                if (ve != null) root.Add(ve);
            }
        }

        /// Build one control for `field` on `owner`, or null when the type isn't renderable.
        public static VisualElement BuildField(object owner, FieldInfo field, Options opt)
        {
            opt ??= new Options();
            string nice = ObjectNames.NicifyVariableName(field.Name);
            string tip = opt.TooltipFor?.Invoke(field)
                         ?? $"{nice} — a {PrettyTypeName(field.FieldType)} value on {owner.GetType().Name}.";
            object v = field.GetValue(owner);
            var range = (RangeAttribute)Attribute.GetCustomAttribute(field, typeof(RangeAttribute));
            var t = field.FieldType;

            void Set(object nv)
            {
                opt.OnBeforeChange?.Invoke();
                field.SetValue(owner, nv);
                opt.OnChanged?.Invoke();
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
                var vopt = new ZuiValueControl.Options { controlWidth = opt.ControlWidth };
                if (range != null) vopt.WithRange(range.min, range.max);
                return Z.Value(nice, zv, vopt, tip,
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

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return BuildList(owner, field, nice, tip, opt);

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
                card.Add(Z.Row(
                    Z.Text($"[{idx}]", ZuiText.Small, $"Element {idx} of {nice}."),
                    Z.Flexible(),
                    Z.Button("×", $"Remove element {idx} from {nice}.", () =>
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
                    BuildFields(card, elem, opt);
                }
                else
                {
                    var ve = BuildElement(list, idx, elemType, tip, opt);
                    if (ve != null) card.Add(ve);
                }
                box.Add(card);
            }

            box.Add(Z.Button("+ Add " + elemType.Name, $"Append a new {PrettyTypeName(elemType)} to {nice}.", () =>
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
            return null;
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

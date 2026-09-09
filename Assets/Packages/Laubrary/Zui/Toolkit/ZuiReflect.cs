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
            /// Per-field inertness (T-0281): given the field and the OWNER instance currently holding it, a
            /// reason string when the field does nothing right now, or null when it can act normally. When it
            /// returns a reason, the control BuildField already produced is drawn disabled with that reason as
            /// its tooltip — the same "declare, don't hide" convention ShaperWindow's own Inert helper uses for
            /// Solids, generalised here so every reflected host gets it once rather than reinventing it. Checked
            /// AFTER the control is built (wraps whatever type case produced it), so it costs nothing for a host
            /// that never sets it.
            public Func<FieldInfo, object, string> InertReason;
            /// Whether editing THIS field can change some OTHER field's InertReason — i.e. it is a guard.
            /// InertReason has no declarative attribute ZuiReflect can inspect the way [ZUIShowIf] gates do
            /// (IsGate below), so a host that supplies InertReason names its own guards here too; only a
            /// declared guard's edit pays for a card rebuild, so a form with 190 fields and a handful of guards
            /// still repaints one control for most edits.
            public Func<FieldInfo, bool> IsInertGuard;
            /// For a reflected IZuiRamp field baked into a fixed-frame lookup table by its host: which of the
            /// ramp's own knobs (its blend-mode row, keyed "space", plus any ZuiRampAdjust field name) the bake
            /// actually reads. Null (the default) means every knob is honoured — a ramp sampled live via
            /// Eval(), or a Fill's own gradient, needs no declaration at all. See ZuiRampControl's own capability
            /// parameter, which this simply feeds per-field.
            public Func<FieldInfo, object, string[]> RampHonouredKnobs;
            /// Re-order a type's own declaration order before it is drawn — the field set and every other
            /// per-field hook (Skip/TooltipFor/ConfigureValue) are unaffected, only where a control LANDS in
            /// the flow. Exists for a host that dumps a generator's dials wholesale (a hosted PyreForm,
            /// T-0220) but knows a couple of them read as misplaced at their declared position — reordering
            /// there, once, beats either hand-listing every dial (unmaintainable for a ~30–190-field form) or
            /// editing the generator's own field order (which is Pyre's file, not this host's to touch).
            /// Return the array unchanged (or null) to leave declaration order as-is.
            public Func<FieldInfo[], FieldInfo[]> ReorderFields;
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
            if (opt?.ReorderFields != null) fields = opt.ReorderFields(fields) ?? fields;
            BuildSubset(root, owner, opt, fields, fields);
        }

        /// Build `subset`, resolving [ZUIShowIf] gates and [ZUIPair2D] partners against `all`. The two differ
        /// only when a caller draws the fields in several passes (grouped boxes): a gate field can then sit in
        /// a different pass from the field it gates, and resolving against the pass alone would silently show
        /// every gated dial at once.
        static void BuildSubset(VisualElement root, object owner, Options opt, FieldInfo[] subset, FieldInfo[] all)
        {
            // A [ZUIPair2D] X field swallows its Y partner into one 2D control, so the partner must not also
            // be drawn on its own further down the card. Collected first, because the Y field can be declared
            // before the X one and a single forward pass would already have drawn it.
            HashSet<string> consumed = null;
            foreach (var f in all)
            {
                var pair = PairAttributeOf(f);
                if (pair == null) continue;
                if (FindField(all, pair.YField) == null) continue;   // bad name → both draw normally
                (consumed ??= new HashSet<string>()).Add(pair.YField);
            }

            foreach (var f in subset)
            {
                if (consumed != null && consumed.Contains(f.Name)) continue;
                if (!VisibleNow(owner, f, all)) continue;
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
            if (owner == null) return;
            var fields = FieldsOf(owner.GetType());
            if (opt?.ReorderFields != null) fields = opt.ReorderFields(fields) ?? fields;
            if (HasGroups(fields)) FlowGrouped(host, owner, opt, fields);
            else FlowSubset(host, owner, opt, fields, fields);
        }

        /// Whether any field of this set asks to sit in a named box. Nothing else in this file changes when
        /// none does, so a card that has not adopted [ZUIGroup] draws exactly as it always did.
        static bool HasGroups(FieldInfo[] fields)
        {
            foreach (var f in fields) if (Attribute.IsDefined(f, typeof(ZUIGroupAttribute))) return true;
            return false;
        }

        static ZUIGroupAttribute GroupOf(FieldInfo f)
            => (ZUIGroupAttribute)Attribute.GetCustomAttribute(f, typeof(ZUIGroupAttribute));

        sealed class GroupBucket
        {
            public string Name, Tooltip;
            public bool Advanced;
            public readonly List<FieldInfo> Fields = new();
        }

        /// A box that opens folded the FIRST time this session builds it, and obeys the user's own fold state
        /// after that — an "advanced" group must be out of the way on arrival without ever re-folding itself
        /// under someone who deliberately opened it. Session-scoped like ZuiBox's own fold state, so the two
        /// forget together on a domain reload.
        static readonly HashSet<string> s_advancedFolded = new();

        /// Fields walked in declaration order: an ungrouped run flows inline, a grouped field lands in its
        /// box, and a box is created where its first member is declared. Advanced groups are held back to the
        /// end of the card regardless of where they were declared.
        static void FlowGrouped(VisualElement host, object owner, Options opt, FieldInfo[] fields)
        {
            var buckets = new List<GroupBucket>();
            var byName = new Dictionary<string, GroupBucket>();
            var slots = new List<object>();          // List<FieldInfo> = an inline run; GroupBucket = a box
            List<FieldInfo> run = null;

            foreach (var f in fields)
            {
                var g = GroupOf(f);
                if (g == null || string.IsNullOrEmpty(g.Group))
                {
                    if (run == null) { run = new List<FieldInfo>(); slots.Add(run); }
                    run.Add(f);
                    continue;
                }
                run = null;
                if (!byName.TryGetValue(g.Group, out var b))
                {
                    b = new GroupBucket { Name = g.Group, Advanced = g.Advanced };
                    byName[g.Group] = b;
                    buckets.Add(b);
                    if (!b.Advanced) slots.Add(b);
                }
                if (string.IsNullOrEmpty(b.Tooltip)) b.Tooltip = g.Tooltip;
                b.Fields.Add(f);
            }

            foreach (var slot in slots)
            {
                if (slot is List<FieldInfo> inline) FlowSubset(host, owner, opt, inline.ToArray(), fields);
                else EmitGroup(host, owner, opt, (GroupBucket)slot, fields);
            }
            foreach (var b in buckets) if (b.Advanced) EmitGroup(host, owner, opt, b, fields);
        }

        static void EmitGroup(VisualElement host, object owner, Options opt, GroupBucket b, FieldInfo[] all)
        {
            // Keyed by where this object SITS as well as by its type, never by the title alone: two generators
            // can both have a "Noise detail" box, and one settings class reused for three nested populations
            // (a Plasma Bloom's chunks / embers / motes) would otherwise fold all three together — the exact
            // reason BoxKeyed exists.
            string key = $"reflect.group.{_groupKeyPath}{owner.GetType().Name}.{b.Name}";

            // T-0320 — a nested settings field carries its own title ([ZUILabel("Fracture")] on the field
            // draws a box called "Fracture"), and its members name the same concern in their [ZUIGroup], so
            // the group drew a SECOND box with the identical title inside the first: measured live on
            // Explosive Jet as six "X inside X" nestings (Fracture, Fracture 2, Flash, Chunks, Gobs, Dust),
            // one of which — Fracture 2, whose inner box is Advanced and therefore folded — shrank the outer
            // box to a 94px stub holding nothing but a repeat of its own name. The UI guide's "never a
            // redundant title" applies; the box that is already there becomes the group's box, so its
            // Advanced fold still folds and its tooltip is kept.
            var enclosing = EnclosingBox(host);
            if (enclosing != null && !string.IsNullOrEmpty(b.Name) && b.Name == enclosing.TitleText)
            {
                var inline = FlowSubset(host, owner, opt, b.Fields.ToArray(), all);
                if (inline.childCount == 0) { host.Remove(inline); return; }
                if (b.Advanced && s_advancedFolded.Add(key)) enclosing.IsOpen = false;
                return;
            }

            var box = Z.BoxKeyed(b.Name, b.Tooltip, key);
            var flow = FlowSubset(box, owner, opt, b.Fields.ToArray(), all);
            // Every dial in the group is currently gated off (a [ZUIShowIf] the owner does not satisfy) — an
            // empty titled box says nothing and costs a line, so it is simply not added.
            if (flow.childCount == 0) return;
            host.Add(box);
            if (b.Advanced && s_advancedFolded.Add(key)) box.IsOpen = false;
        }

        /// The titled ZuiBox this host draws inside, if any — `host` is usually the box itself (a nested
        /// settings field flows its members straight into the box it just made), otherwise the nearest
        /// titled ancestor. Untitled boxes are transparent here: they name nothing to collide with.
        static ZuiBox EnclosingBox(VisualElement host)
        {
            for (var e = host; e != null; e = e.hierarchy.parent)
                if (e is ZuiBox b && !string.IsNullOrEmpty(b.TitleText)) return b;
            return null;
        }

        static VisualElement FlowSubset(VisualElement host, object owner, Options opt,
            FieldInfo[] subset, FieldInfo[] all)
        {
            var flow = new VisualElement();
            flow.style.flexDirection = FlexDirection.Row;
            flow.style.flexWrap = Wrap.Wrap;
            flow.style.alignItems = Align.FlexStart;
            BuildSubset(flow, owner, opt, subset, all);

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
            return flow;
        }

        /// A control that earns a whole line: one drawing a curve or a plot rather than sitting on a row like
        /// a slider. An animatable value counts only in its ENVELOPE modes — in Static or Min-Max it is one
        /// slider row and must flow like anything else.
        public static bool IsWideControl(VisualElement e)
        {
            var val = e as ZuiValueControl ?? e.Q<ZuiValueControl>();
            if (val != null) return val.IsCurveShaped;
            // T-0321 — a titled BOX is wide by nature: it is a card in a stack, not a control on a row. Without
            // this it took `flexBasis: Auto` and content-sized, which is invisible while it is open (its own
            // inner flow already fills the row) and obvious the moment it FOLDS: measured on Explosive Jet,
            // the folded "Fracture 2" card sat at 94.2pt among 323.1pt siblings — a narrow orphan in the
            // stack. A box in a body that is already a Column (the usual case) was never affected, which is
            // why only the reflected flow showed it.
            if (e is ZuiBox) return true;
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
        /// Where the object currently being drawn SITS, for fold-state keys — see EmitGroup.
        [ThreadStatic] static string _groupKeyPath;

        /// Build one control for `field` on `owner`, or null when the type isn't renderable. A thin wrapper over
        /// <see cref="BuildFieldControl"/> that applies <see cref="Options.InertReason"/> (T-0281) to whatever
        /// comes back — kept OUTSIDE that method's many type-branch return points so declaring inertness never
        /// has to be repeated per branch.
        public static VisualElement BuildField(object owner, FieldInfo field, Options opt)
        {
            opt ??= new Options();
            var ve = BuildFieldControl(owner, field, opt);
            if (ve != null && opt.InertReason != null)
            {
                string reason = opt.InertReason(field, owner);
                if (reason != null) { ve.SetEnabled(false); ve.tooltip = reason; }
            }
            return ve;
        }

        static VisualElement BuildFieldControl(object owner, FieldInfo field, Options opt)
        {
            string nice = LabelOf(field);
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
                // A field some InertReason reads to grey ANOTHER field decides whether that other control can
                // act, so editing it has to redraw the card too — same reasoning as the [ZUIShowIf] case just
                // above, driven by the host's own IsInertGuard since InertReason carries no attribute to read.
                if (opt.IsInertGuard != null && opt.IsInertGuard(field)) opt.OnStructureChanged?.Invoke();
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
                        nv => Set(nv), opt.ControlWidth, showValue: true).FitCaption(opt.ControlWidth)
                    : Z.Field(nice, tip, Z.Float((float)v, tip, nv => Set(nv), 80f));

            if (t == typeof(int))
                // Bounded int → a whole-number MicroSlider (decimals 0, rounded at the setter), matching how the
                // hand-written windows render a bounded count (Pyre deliberately uses a MicroSlider, not a
                // thumbed SliderInt, for a count). Unbounded → a scrub Int wrapped in a Z.Field for its label.
                return range != null
                    ? (VisualElement)Z.MicroSlider(nice, (int)v, range.min, range.max, tip,
                        nv => Set(Mathf.RoundToInt(nv)), opt.ControlWidth, showValue: true, decimals: 0)
                        .FitCaption(opt.ControlWidth)
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
                    // T-0312 — the EMBEDDED range control (label + "low – high" inside the track), not
                    // Z.MinMax's slider flanked by two typed fields. Two reasons, both measured rather than
                    // stylistic. (1) Width: `opt.ControlWidth` is the width of Z.MinMax's SLIDER, not of the
                    // composite, so the drawn control came out ControlWidth + two 42px fields + gaps = 252.9px
                    // and the `.zui-field` around it (label 84px, flex-shrink:0) reached 340.9px inside a
                    // 313.3px box — every Torch range dial (Root spread, Root height, Tongue climb/peel/
                    // width/length/life, Ember rise, Ember size) overhung its box by 8–28px. MicroMinMax
                    // carries its own label inside the track, so it costs exactly ControlWidth and needs no
                    // Z.Field wrapper at all. (2) Consistency: the reflected bounded SCALAR beside it is
                    // already a Z.MicroSlider, and ui-layout-rules names MicroMinMax the preferred pair
                    // control ("prefer this one"), keeping Z.MinMax for where typed precision outweighs
                    // footprint — which a form dial with a declared [Range] is not.
                    var pair = (Vector2)v;
                    return Z.MicroMinMax(nice, pair.x, pair.y, range.min, range.max, tip,
                        (lo, hi) => Set(new Vector2(lo, hi)), opt.ControlWidth);
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
                            yLabel = AxisLabel(LabelOf(yField), "Y"),
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
                        SetWrapped, opt.ControlWidth, showValue: true).FitCaption(opt.ControlWidth)
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

            // A colour ramp that speaks IZuiRamp (Pyre's PyreRamp) → ONE ZuiRampControl: Unity's own GradientField
            // plus the "★" project library and the blend-space row. Since T-0223 this is the SAME control a
            // ZuiGradient's own source ramp uses (ZuiGradientEditor.Source), and its "★" reaches the same project
            // library with no stop cap either way — so a ramp and a gradient are edited and exchanged identically.
            // This case has to sit BEFORE both the List<> branch and the
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
                var honoured = opt.RampHonouredKnobs?.Invoke(field, owner);
                var rc = new ZuiRampControl(ramp, tip, honouredKnobs: honoured)
                    { OnBeforeMutate = opt.OnBeforeChange, OnChanged = opt.OnChanged };
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
                string outerPath = _groupKeyPath;
                _groupKeyPath = $"{outerPath}{owner.GetType().Name}.{field.Name}.";
                try { FlowFields(box, v, opt); }
                finally { _nestDepth--; _groupKeyPath = outerPath; }
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
                    // THREE parts, and only the middle one wraps: "#n" │ the dials │ ×. The outer row stays
                    // NoWrap so the remove button can never be pushed onto a line of its own (the "confusing
                    // empty space" failure the card-layout rule warns about), and the dials sit in their own
                    // wrapping strip that takes whatever width is left over, breaking onto further lines when
                    // the column is too narrow to hold them side by side.
                    //
                    // T-0314: before this, all of it was ONE NoWrap row with a flexible gap, on the belief that
                    // "the dials shrink instead". They cannot — every child of the row resolves flex-shrink:0
                    // and ZuiMicroSlider's min-width is 60 — so a five-dial ExplosiveJet blast measured 599px
                    // against a 293.8px card at the pane width Pyre opens at, putting its last three dials and
                    // its × outside the dial pane's clipping viewport: not drawn, not clickable, so an authored
                    // blast could not be removed at all. Wrapping the dials cannot overflow at any width — the
                    // worst case is one dial per line.
                    var row = Z.Row();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.flexWrap = Wrap.NoWrap;
                    // FlexStart, not Center: the index and the × belong on the FIRST line of dials, not floating
                    // half way down a three-line block.
                    row.style.alignItems = Align.FlexStart;
                    row.Add(Z.Text($"#{idx + 1}", ZuiText.Small,
                        $"{Singular(nice)} {idx + 1} of {nice}.").W(26f));
                    // A Z.Row so the dials keep the standard 6px gap between them; grow:1 is what still pins the
                    // × to the right edge now that the flexible spacer is gone.
                    var dials = Z.Row();
                    dials.style.flexWrap = Wrap.Wrap;
                    dials.style.flexGrow = 1f;
                    dials.style.flexShrink = 1f;
                    dials.style.minWidth = 0f;
                    dials.style.marginBottom = 0f;
                    BuildFields(dials, elem, elemOpt);
                    row.Add(dials);
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
        /// one line. They do NOT shrink (a ZuiMicroSlider resolves flex-shrink:0 and carries a 60px min-width);
        /// what keeps the row inside its card is that the dials WRAP — see the compact branch above.
        ///
        /// 105, not 100 (T-0314): a MicroSlider's caption ends 56.9px short of its own width (6px inset plus the
        /// 46px value reserve), so at 100 the caption had 43.1px for a "Violence" that needs 44.9 and was clipped.
        /// 105 gives it 48.1. The line arithmetic still holds at the width Pyre opens at: a 360px column leaves the
        /// dial strip 233.8px, and two dials cost 2 × (105 + 6) = 222.
        static Options CompactOptions(Options o) => new Options
        {
            OnBeforeChange = o.OnBeforeChange,
            OnChanged = o.OnChanged,
            OnStructureChanged = o.OnStructureChanged,
            TooltipFor = o.TooltipFor,
            Skip = o.Skip,
            FloatWrapperProperty = o.FloatWrapperProperty,
            ConfigureValue = o.ConfigureValue,
            ControlWidth = 105f,
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

        /// The caption a field draws: its authored [ZUILabel] when it has one, else the nicified field name.
        /// A generator ported from a research program keeps that program's parameter keys as serialized names
        /// (contract files and [MovedFrom] history are written in them), so the readable name has to come from
        /// somewhere other than the field name.
        public static string LabelOf(FieldInfo field)
        {
            var a = (ZUILabelAttribute)Attribute.GetCustomAttribute(field, typeof(ZUILabelAttribute));
            return string.IsNullOrEmpty(a?.Label) ? ObjectNames.NicifyVariableName(field.Name) : a.Label;
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

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>Stable semantic metadata for a generated field. Binding identity never depends on its label.</summary>
    public sealed class ZuiFieldPresentation
    {
        public enum Family { Scalar, Toggle, Text, Choice, Color, Spatial, Reference, Animated, Gradient, Collection, Composite, Other }
        public enum Role { Identity, Reference, Timing, Spatial, Parameter, Status, Content }
        public enum SizeIntent { Compact, Standard, Wide }

        public Family family;
        public Role role;
        public SizeIntent size;
        public string group;
        public string bindingIdentity;

        public ZuiFieldPresentation(string bindingIdentity, Family family, Role role, string group, SizeIntent size)
        {
            this.bindingIdentity = bindingIdentity;
            this.family = family;
            this.role = role;
            this.group = group;
            this.size = size;
        }

        /// Adds stable semantic selector classes to the field root without adding a second label wrapper.
        public static VisualElement Stamp(VisualElement element, ZuiFieldPresentation presentation)
        {
            if (element == null || presentation == null) return element;
            element.AddToClassList("zui-generated-field");
            element.AddToClassList("zui-family--" + Token(presentation.family.ToString()));
            element.AddToClassList("zui-role--" + Token(presentation.role.ToString()));
            element.AddToClassList("zui-size-" + Token(presentation.size.ToString()));
            if (presentation.family == Family.Spatial)
                element.Query<Label>(className: "zui-field__label").ForEach(label =>
                {
                    if (label.parent != element) label.AddToClassList("zui-component-label");
                });
            if (!string.IsNullOrWhiteSpace(presentation.group))
                element.AddToClassList("zui-group--" + Token(presentation.group));
            if (!string.IsNullOrWhiteSpace(presentation.bindingIdentity))
            {
                element.AddToClassList("zui-binding--" + Token(presentation.bindingIdentity));
                if (string.IsNullOrEmpty(element.name)) element.name = "zui-field:" + presentation.bindingIdentity;
            }
            return element;
        }

        /// Derives useful defaults from the declared field and actual control shape. Hosts may replace any part
        /// through ZuiReflect.Options.PresentationFor while retaining the computed binding identity.
        public static ZuiFieldPresentation ForField(FieldInfo field, Type ownerType, string bindingIdentity,
            VisualElement control, ZuiFieldPresentation overrideValue = null)
        {
            Type type = field?.FieldType;
            Family family = FamilyOf(type);
            Role role = RoleOf(field, family);
            SizeIntent size = control != null && ZuiReflect.IsWideControl(control) ? SizeIntent.Wide : SizeOf(type, field);
            var header = field == null ? null : (HeaderAttribute)Attribute.GetCustomAttribute(field, typeof(HeaderAttribute));
            string group = !string.IsNullOrWhiteSpace(header?.header) ? header.header : field?.DeclaringType?.Name ?? ownerType?.Name;
            return Merge(new ZuiFieldPresentation(bindingIdentity, family, role, group, size), overrideValue, bindingIdentity);
        }

        public static ZuiFieldPresentation ForProperty(string bindingIdentity, Type valueType, bool isWide = false)
        {
            var family = FamilyOf(valueType);
            return new ZuiFieldPresentation(bindingIdentity, family,
                RoleOf(valueType, MemberName(bindingIdentity), family), null, isWide ? SizeIntent.Wide : SizeOf(valueType, null));
        }

        public static ZuiFieldPresentation ForElement(Type valueType, string bindingIdentity, string group, VisualElement control)
        {
            Family family = FamilyOf(valueType);
            SizeIntent size = control != null && ZuiReflect.IsWideControl(control) ? SizeIntent.Wide : SizeOf(valueType, null);
            return new ZuiFieldPresentation(bindingIdentity, family,
                RoleOf(valueType, MemberName(bindingIdentity), family), group, size);
        }

        public static ZuiFieldPresentation ForProperty(string bindingIdentity, FieldInfo field, Type ownerType, VisualElement control)
            => ForField(field, ownerType, bindingIdentity, control);

        static string MemberName(string identity)
        {
            if (string.IsNullOrEmpty(identity)) return string.Empty;
            int dot = identity.LastIndexOf('.');
            return dot < 0 ? identity : identity.Substring(dot + 1);
        }

        static ZuiFieldPresentation Merge(ZuiFieldPresentation defaults, ZuiFieldPresentation replacement, string binding)
        {
            if (replacement == null) return defaults;
            return new ZuiFieldPresentation(
                string.IsNullOrWhiteSpace(replacement.bindingIdentity) ? binding : replacement.bindingIdentity,
                replacement.family, replacement.role,
                string.IsNullOrWhiteSpace(replacement.group) ? defaults.group : replacement.group,
                replacement.size);
        }

        static Family FamilyOf(Type type)
        {
            if (type == null) return Family.Other;
            if (type == typeof(bool)) return Family.Toggle;
            if (type == typeof(string)) return Family.Text;
            if (type == typeof(Enum)) return Family.Choice;
            if (type.IsEnum) return Family.Choice;
            if (type == typeof(float) || type == typeof(int) || type == typeof(double)) return Family.Scalar;
            if (type == typeof(UnityEngine.Color)) return Family.Color;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return Family.Reference;
            if (type == typeof(ZUIValue) || type == typeof(UnityEngine.AnimationCurve)) return Family.Animated;
            if (type == typeof(UnityEngine.Gradient) || type == typeof(ZuiGradient)) return Family.Gradient;
            if (typeof(System.Collections.IList).IsAssignableFrom(type)) return Family.Collection;
            if (type == typeof(UnityEngine.Vector2) || type == typeof(UnityEngine.Vector2Int) ||
                type == typeof(UnityEngine.Vector3) || type == typeof(UnityEngine.Vector3Int)) return Family.Spatial;
            return type.IsClass || type.IsValueType ? Family.Composite : Family.Other;
        }

        static SizeIntent SizeOf(Type type, FieldInfo field)
        {
            if (type == typeof(bool)) return SizeIntent.Compact;
            if ((type == typeof(float) || type == typeof(int)) &&
                (field == null || Attribute.GetCustomAttribute(field, typeof(RangeAttribute)) == null))
                return SizeIntent.Compact;
            if (type != null && (typeof(System.Collections.IList).IsAssignableFrom(type) || type == typeof(UnityEngine.Gradient) ||
                type == typeof(ZuiGradient) || typeof(IZuiRamp).IsAssignableFrom(type))) return SizeIntent.Wide;
            return SizeIntent.Standard;
        }

        static Role RoleOf(FieldInfo field, Family family)
        {
            string name = field?.Name ?? string.Empty;
            Type type = field?.FieldType;
            if (family == Family.Reference) return Role.Reference;
            return RoleOf(type, name, family);
        }

        static Role RoleOf(Type type, string name, Family family)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (Has(n, "name", "title", "label", "identifier", "displayname")) return Role.Identity;
            if (Has(n, "time", "duration", "delay", "rate", "tempo", "frame", "phase", "frequency", "lifetime")) return Role.Timing;
            if (Has(n, "position", "offset", "pivot", "origin", "direction", "rotation", "angle", "scale", "size", "anchor")) return Role.Spatial;
            if (type == typeof(bool) && Has(n, "enabled", "active", "visible", "selected", "valid", "available", "show")) return Role.Status;
            if (family == Family.Collection || family == Family.Composite) return Role.Content;
            return Role.Parameter;
        }

        static bool Has(string value, params string[] words)
        {
            foreach (string word in words) if (value.Contains(word)) return true;
            return false;
        }

        static string Token(string value)
        {
            var chars = new List<char>(value.Length);
            bool dash = false;
            foreach (char raw in value)
            {
                char c = char.ToLowerInvariant(raw);
                if (char.IsLetterOrDigit(c)) { chars.Add(c); dash = false; }
                else if (!dash && chars.Count > 0) { chars.Add('-'); dash = true; }
            }
            if (chars.Count > 0 && chars[chars.Count - 1] == '-') chars.RemoveAt(chars.Count - 1);
            return new string(chars.ToArray());
        }

        /// Shared enum renderer: short sets are segmented, longer sets wrap as radios, and flag enums are multi-select.
        public static VisualElement EnumControl(Enum value, string tooltip, Action<Enum> onChanged)
        {
            Type type = value.GetType();
            string[] names = Enum.GetNames(type);
            Array values = Enum.GetValues(type);
            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++) labels[i] = ObjectNames.NicifyVariableName(names[i]);

            if (Attribute.IsDefined(type, typeof(FlagsAttribute)))
            {
                var bits = new List<long>();
                var bitLabels = new List<string>();
                for (int i = 0; i < values.Length; i++)
                {
                    long bit = Convert.ToInt64(values.GetValue(i));
                    if (bit != 0 && (bit & (bit - 1)) == 0) { bits.Add(bit); bitLabels.Add(labels[i]); }
                }
                long current = Convert.ToInt64(value);
                return Z.SegmentedMulti(i => (current & bits[i]) != 0, bitLabels.ToArray(), tooltip,
                    (i, on) => { current = on ? current | bits[i] : current & ~bits[i]; onChanged?.Invoke((Enum)Enum.ToObject(type, current)); });
            }

            int selected = 0;
            for (int i = 0; i < values.Length; i++) if (values.GetValue(i).Equals(value)) { selected = i; break; }
            if (values.Length <= 3)
                return Z.Segmented(selected, labels, tooltip, i => onChanged?.Invoke((Enum)values.GetValue(i)));
            return Z.MiniRadio(selected, labels, tooltip, i => onChanged?.Invoke((Enum)values.GetValue(i)), wrap: true);
        }
    }
}

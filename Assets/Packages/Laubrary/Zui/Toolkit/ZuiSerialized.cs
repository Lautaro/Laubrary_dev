// ZuiSerialized — build Z controls for a SerializedProperty.
//
// The SerializedProperty twin of ZuiReflect (which works over plain reflection). A tool that edits a
// ScriptableObject through a SerializedObject wants both halves of what Unity's own PropertyField gives —
// automatic Undo and change tracking — WITHOUT its layout, because a bare PropertyField has no width of its
// own and stretches to whatever the window happens to be (the "unconstrained default expand" failure the
// layout rules forbid; measured 604px in a 616px window before the IMGUI original capped it by hand).
//
// So: a real Z control, explicit width, tooltip taken from the field's own [Tooltip], and writes committed
// through SerializedObject.ApplyModifiedProperties — which registers the Undo step itself. Property types
// this file has no better answer for fall back to a PropertyField, given an explicit width and a tooltip so
// the fallback still obeys the same two rules.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiSerialized
    {
        public const float DefaultWidth = 150f;

        /// How wide a PropertyField is allowed to get when the caller didn't say. Comfortably fits a list's
        /// rows and its size field without letting either drift to the far side of a wide window.
        public const float DefaultPropertyMaxWidth = 420f;

        /// The field's own [Tooltip], falling back to a plain description so no control is ever left bare.
        public static string TooltipOf(SerializedProperty prop, string fallback = null)
        {
            if (!string.IsNullOrEmpty(prop.tooltip)) return prop.tooltip;
            if (!string.IsNullOrEmpty(fallback)) return fallback;
            return $"{ObjectNames.NicifyVariableName(prop.name)} — a {prop.propertyType} value on " +
                   $"{prop.serializedObject.targetObject.GetType().Name}.";
        }

        /// A labelled control for one property. `onChanged` (optional) fires after a committed edit — for a
        /// window that has to repaint a preview or rebuild a dependent section.
        public static VisualElement Field(SerializedProperty prop, string label = null, string tooltip = null,
            float width = -1f, System.Action onChanged = null)
        {
            var control = FieldCore(prop, label, tooltip, width, onChanged);
            if (control == null) return null;
            var field = FieldInfoOf(prop);
            var metadata = field != null
                ? ZuiFieldPresentation.ForProperty(prop.propertyPath, field, prop.serializedObject.targetObject?.GetType(), control)
                : ZuiFieldPresentation.ForProperty(prop.propertyPath, PropertyType(prop), isWide: width > DefaultPropertyMaxWidth);
            return ZuiFieldPresentation.Stamp(control, metadata);
        }

        static VisualElement FieldCore(SerializedProperty prop, string label, string tooltip,
            float width, System.Action onChanged)
        {
            string nice = label ?? ObjectNames.NicifyVariableName(prop.name);
            string tip = tooltip ?? TooltipOf(prop);
            var so = prop.serializedObject;
            string path = prop.propertyPath;

            // Re-resolve on write: a SerializedProperty captured in a closure can outlive the object graph it
            // was made against (a rebuild, a domain reload), and writing through a stale one silently does
            // nothing.
            void Commit(System.Action<SerializedProperty> write)
            {
                var p = so.FindProperty(path);
                if (p == null) return;
                so.Update();
                write(p);
                so.ApplyModifiedProperties();
                onChanged?.Invoke();
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Float:
                    return Z.Field(nice, tip, Z.Float(prop.floatValue, tip,
                        v => Commit(p => p.floatValue = v), NumericWidth(width)));

                case SerializedPropertyType.Integer:
                    return Z.Field(nice, tip, Z.Int(prop.intValue, tip,
                        v => Commit(p => p.intValue = v), NumericWidth(width)));

                case SerializedPropertyType.Boolean:
                    return Z.Toggle(nice, tip, prop.boolValue, v => Commit(p => p.boolValue = v));

                case SerializedPropertyType.String:
                    return Z.Field(nice, tip, Z.TextInput(prop.stringValue ?? "", tip,
                        v => Commit(p => p.stringValue = v), width));

                case SerializedPropertyType.Enum:
                {
                    var enumType = FieldInfoOf(prop)?.FieldType;
                    if (enumType == null || !enumType.IsEnum)
                        return Property(prop, nice, tip, width);
                    var current = (System.Enum)System.Enum.ToObject(enumType, prop.intValue);
                    bool flags = System.Attribute.IsDefined(enumType, typeof(System.FlagsAttribute));
                    return Z.Field(nice, tip, ZuiFieldPresentation.EnumControl(current, tip, value => Commit(p =>
                    {
                        if (flags) p.intValue = System.Convert.ToInt32(value);
                        else
                        {
                            var values = System.Enum.GetValues(enumType);
                            int index = 0;
                            for (int i = 0; i < values.Length; i++)
                                if (values.GetValue(i).Equals(value)) { index = i; break; }
                            p.enumValueIndex = index;
                        }
                    })));
                }

                case SerializedPropertyType.Color:
                    return Z.Field(nice, tip, Z.Color(prop.colorValue, tip,
                        v => Commit(p => p.colorValue = v), 110f));

                case SerializedPropertyType.Vector2:
                {
                    var v = prop.vector2Value;
                    return Z.Field(nice, tip, Z.Row(
                        Z.Field("X", tip + " (X)", Z.Float(v.x, tip + " (X)",
                            nv => Commit(p => p.vector2Value = new Vector2(nv, p.vector2Value.y)), 60f)),
                        Z.Field("Y", tip + " (Y)", Z.Float(v.y, tip + " (Y)",
                            nv => Commit(p => p.vector2Value = new Vector2(p.vector2Value.x, nv)), 60f))));
                }

                case SerializedPropertyType.Vector3:
                {
                    var v = prop.vector3Value;
                    return Z.Field(nice, tip, Z.Row(
                        Z.Field("X", tip + " (X)", Z.Float(v.x, tip + " (X)",
                            nv => Commit(p => p.vector3Value = new Vector3(nv, p.vector3Value.y, p.vector3Value.z)), -1f)),
                        Z.Field("Y", tip + " (Y)", Z.Float(v.y, tip + " (Y)",
                            nv => Commit(p => p.vector3Value = new Vector3(p.vector3Value.x, nv, p.vector3Value.z)), -1f)),
                        Z.Field("Z", tip + " (Z)", Z.Float(v.z, tip + " (Z)",
                            nv => Commit(p => p.vector3Value = new Vector3(p.vector3Value.x, p.vector3Value.y, nv)), -1f))));
                }

                case SerializedPropertyType.ObjectReference:
                {
                    var f = new ObjectField { objectType = TypeOfObjectField(prop), value = prop.objectReferenceValue, tooltip = tip };
                    f.AddToClassList("zui-foundation-object");
                    // Width is an explicit caller contract, not a presentation default.
                    if (width >= 0f) f.style.width = width;
                    f.RegisterValueChangedCallback(e => Commit(p => p.objectReferenceValue = e.newValue));
                    return Z.Field(nice, tip, f);
                }

                default:
                    return Property(prop, nice, tip, width);
            }
        }

        /// The escape hatch: Unity's own PropertyField (nested classes, lists, curves, gradients, and any
        /// concrete type carrying its own [CustomPropertyDrawer]), given the explicit width and tooltip the
        /// bare control would otherwise lack. The tooltip sits on the PropertyField itself, so every control
        /// Unity builds underneath inherits it.
        public static VisualElement Property(SerializedProperty prop, string label = null, string tooltip = null,
            float width = 0f)
        {
            string nice = label ?? ObjectNames.NicifyVariableName(prop.name);
            var pf = new PropertyField(prop, nice) { tooltip = tooltip ?? TooltipOf(prop) };
            pf.AddToClassList("zui-foundation-property");
            // The label above already names the field, so the [Header] decorator would print that same word
            // a second time directly over it (real duplicate: Zoe's "Loadout" and "Cues").
            pf.AddToClassList("zui-no-decorators");
            // A PropertyField's own children are built by Unity, not by Zui, so the stylesheet's
            // flex-grow:0 guard on BaseFields never reaches the ListView a list property expands into: left
            // alone it spans the whole window and parks its size field against the far right edge, metres
            // from its label. Nothing catches that either — ZuiAudit exempts Foldouts from the over-width
            // check, and a list PropertyField renders AS a Foldout. So bound it here, once, for every call
            // site: an explicit width when the caller gave one, otherwise a cap that still lets a short
            // field size to its content.
            if (width > 0f) pf.style.width = width;
            else pf.AddToClassList("zui-foundation-property--default-width");
            pf.Bind(prop.serializedObject);
            return pf;
        }

        static System.Type TypeOfObjectField(SerializedProperty prop)
        {
            var target = prop.serializedObject.targetObject;
            var field = target != null ? target.GetType().GetField(prop.name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance) : null;
            return field != null && typeof(Object).IsAssignableFrom(field.FieldType) ? field.FieldType : typeof(Object);
        }

        static float NumericWidth(float width) => width < 0f ? width : Mathf.Min(width, 80f);

        static System.Type PropertyType(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Float: return typeof(float);
                case SerializedPropertyType.Integer: return typeof(int);
                case SerializedPropertyType.Boolean: return typeof(bool);
                case SerializedPropertyType.String: return typeof(string);
                case SerializedPropertyType.Color: return typeof(Color);
                case SerializedPropertyType.Vector2: return typeof(Vector2);
                case SerializedPropertyType.Vector3: return typeof(Vector3);
                case SerializedPropertyType.Enum: return typeof(System.Enum);
                case SerializedPropertyType.ObjectReference: return TypeOfObjectField(prop);
                default: return typeof(object);
            }
        }

        static System.Reflection.FieldInfo FieldInfoOf(SerializedProperty prop)
        {
            var target = prop?.serializedObject?.targetObject;
            if (target == null) return null;
            System.Type type = target.GetType();
            System.Reflection.FieldInfo found = null;
            string[] parts = prop.propertyPath.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "Array")
                {
                    if (type.IsArray) type = type.GetElementType();
                    else if (type.IsGenericType) type = type.GetGenericArguments()[0];
                    if (i + 1 < parts.Length && parts[i + 1].StartsWith("data[")) i++;
                    continue;
                }

                found = null;
                for (System.Type current = type; current != null; current = current.BaseType)
                {
                    found = current.GetField(parts[i], System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly);
                    if (found != null) break;
                }
                if (found == null) return null;
                type = found.FieldType;
            }
            return found;
        }
    }
}

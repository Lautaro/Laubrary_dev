using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Laubrary.SimpleUI;

namespace Laubrary.SimpleUI.Editor
{
    /// <summary>
    /// Utility for SimpleUIBuilder to generate UI hierarchies.
    /// </summary>
    internal static class SimpleUIHierarchyGenerator
    {
        private const float DEFAULT_FONT_SIZE = 24f;
        private const float LABEL_FONT_SIZE = 18f;
        private const float ROW_SPACING = 4f;

        public static int GenerateHierarchy(SimpleUIBuilder builder, Type pocoType)
        {
            var view = builder.targetView;
            if (view == null || pocoType == null)
                return 0;

            var config = pocoType.GetCustomAttribute<SimpleUIAttribute>() ?? new SimpleUIAttribute();
            var members = CollectBindableMembers(pocoType, config);

            if (members.Count == 0)
                return 0;

            Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "SimpleUI Generate Hierarchy");

            ApplyContainerLayout(builder);

            int generated = 0;
            foreach (var member in members)
            {
                if (HasExistingGameObject(view.transform, member.Name))
                    continue;

                CreateMemberRow(builder, member);
                generated++;
            }

            return generated;
        }

        public static List<string> PreviewClearTargets(SimpleUIView view, Type pocoType)
        {
            var result = new List<string>();
            if (view == null || pocoType == null) return result;

            var config = pocoType.GetCustomAttribute<SimpleUIAttribute>() ?? new SimpleUIAttribute();
            var members = CollectBindableMembers(pocoType, config);

            foreach (var member in members)
            {
                string rowName = GetRowName(member.Name);
                if (view.transform.Find(rowName) != null)
                    result.Add(rowName);
            }

            return result;
        }

        public static int ClearGeneratedHierarchy(SimpleUIView view, Type pocoType)
        {
            if (view == null || pocoType == null) return 0;

            var config = pocoType.GetCustomAttribute<SimpleUIAttribute>() ?? new SimpleUIAttribute();
            var members = CollectBindableMembers(pocoType, config);

            Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "SimpleUI Clear Generated");

            int removed = 0;
            foreach (var member in members)
            {
                string rowName = GetRowName(member.Name);
                var rowTransform = view.transform.Find(rowName);
                if (rowTransform != null)
                {
                    Undo.DestroyObjectImmediate(rowTransform.gameObject);
                    removed++;
                }
            }

            return removed;
        }

        private static void ApplyContainerLayout(SimpleUIBuilder builder)
        {
            var root = builder.targetView.transform;
            
            var existingLayouts = root.GetComponents<LayoutGroup>();
            foreach (var l in existingLayouts) Undo.DestroyObjectImmediate(l);

            switch (builder.containerLayout)
            {
                case SimpleUIBuilder.ContainerLayout.Vertical:
                    var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
                    v.spacing = builder.spacing;
                    v.childControlWidth = v.childControlHeight = true;
                    v.childForceExpandWidth = true;
                    v.childForceExpandHeight = false;
                    break;
                case SimpleUIBuilder.ContainerLayout.Horizontal:
                    var h = root.gameObject.AddComponent<HorizontalLayoutGroup>();
                    h.spacing = builder.spacing;
                    h.childControlWidth = h.childControlHeight = true;
                    h.childForceExpandWidth = false;
                    h.childForceExpandHeight = true;
                    break;
                case SimpleUIBuilder.ContainerLayout.Grid:
                    var g = root.gameObject.AddComponent<GridLayoutGroup>();
                    g.spacing = new Vector2(builder.spacing, builder.spacing);
                    g.cellSize = builder.cellSize;
                    g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    g.constraintCount = builder.columns;
                    break;
            }

            if (root.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private static void CreateMemberRow(SimpleUIBuilder builder, MemberInfo member)
        {
            var parent = builder.targetView.transform;
            string rowName = GetRowName(member.Name);

            // Resolve binding mode from attribute, default to OneWay
            var bindAttr = member.ReflectionMember?.GetCustomAttribute<SimpleUIBindAttribute>();
            var bindingMode = bindAttr?.Mode ?? BindingMode.OneWay;

            // Try prefab-based instantiation first
            if (builder.settings != null)
            {
                var prefab = builder.settings.ResolvePrefab(member.FieldType, bindingMode, member.ReflectionMember);
                if (prefab != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    instance.name = rowName;

                    // Wire label text — search case-insensitively for a child named "label"
                    var labelTransform = FindChildCaseInsensitive(instance.transform, "label");
                    if (labelTransform != null)
                    {
                        var labelText = labelTransform.GetComponent<TMPro.TextMeshProUGUI>();
                        if (labelText != null)
                            labelText.text = PrettifyName(member.Name);
                    }

                    // Wire value — rename to field name so BindingResolver can find it
                    var valueTransform = FindChildCaseInsensitive(instance.transform, "value");
                    if (valueTransform != null)
                        valueTransform.name = member.Name;

                    // Apply global text style from settings
                    builder.settings.ApplyTextStyle(instance.transform);

                    Undo.RegisterCreatedObjectUndo(instance, "SimpleUI Generate Row");
                    return;
                }
            }

            // Fallback: build row procedurally
            CreateProceduralRow(builder, member, rowName);
        }

        private static void CreateProceduralRow(SimpleUIBuilder builder, MemberInfo member, string rowName)
        {
            var parent = builder.targetView.transform;
            GameObject row = CreateUIGameObject(rowName, parent);

            if (builder.fieldOrientation == SimpleUIBuilder.FieldOrientation.Horizontal)
            {
                var horizontalLayout = row.AddComponent<HorizontalLayoutGroup>();
                horizontalLayout.spacing = ROW_SPACING;
                horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
                horizontalLayout.childControlWidth = horizontalLayout.childControlHeight = true;
                horizontalLayout.childForceExpandWidth = true;
                horizontalLayout.childForceExpandHeight = false;
            }
            else
            {
                var verticalLayout = row.AddComponent<VerticalLayoutGroup>();
                verticalLayout.spacing = 2f;
                verticalLayout.childAlignment = TextAnchor.UpperLeft;
                verticalLayout.childControlWidth = verticalLayout.childControlHeight = true;
                verticalLayout.childForceExpandWidth = true;
                verticalLayout.childForceExpandHeight = false;
            }

            var rowLayoutElement = row.AddComponent<LayoutElement>();
            rowLayoutElement.preferredHeight = builder.fieldOrientation == SimpleUIBuilder.FieldOrientation.Horizontal ? 30f : 50f;

            // Label
            GameObject labelGo = CreateUIGameObject("Label", row.transform);
            var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
            labelText.text = PrettifyName(member.Name);
            labelText.fontSize = LABEL_FONT_SIZE;
            labelText.fontStyle = TMPro.FontStyles.Bold;
            labelText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            labelText.alignment = builder.fieldOrientation == SimpleUIBuilder.FieldOrientation.Horizontal ?
                TMPro.TextAlignmentOptions.MidlineLeft : TMPro.TextAlignmentOptions.BottomLeft;

            // Value — named after the field for binding
            GameObject valueGo = CreateUIGameObject(member.Name, row.transform);
            var valueText = valueGo.AddComponent<TMPro.TextMeshProUGUI>();
            valueText.text = GetPlaceholderText(member.FieldType);
            valueText.fontSize = DEFAULT_FONT_SIZE;
            valueText.color = Color.white;
            valueText.alignment = builder.fieldOrientation == SimpleUIBuilder.FieldOrientation.Horizontal ?
                TMPro.TextAlignmentOptions.MidlineRight : TMPro.TextAlignmentOptions.TopLeft;

            Undo.RegisterCreatedObjectUndo(row, "SimpleUI Generate Row");
        }

        private static List<MemberInfo> CollectBindableMembers(Type pocoType, SimpleUIAttribute config)
        {
            var result = new List<MemberInfo>();
            var properties = pocoType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (!property.CanRead || ShouldSkipMember(property, config)) continue;
                result.Add(new MemberInfo(property.Name, property.PropertyType, property));
            }
            var fields = pocoType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.Name.Contains("<") || ShouldSkipMember(field, config)) continue;
                result.Add(new MemberInfo(field.Name, field.FieldType, field));
            }
            return result;
        }

        private static bool ShouldSkipMember(System.Reflection.MemberInfo member, SimpleUIAttribute config)
        {
            if (member.GetCustomAttribute<SimpleUIIgnoreAttribute>() != null) return true;
            if (config.bindAll) return false;
            return !member.GetCustomAttributes<SimpleUIBindAttribute>().Any() &&
                   !member.GetCustomAttributes<SimpleUIPathAttribute>().Any();
        }

        /// <summary>
        /// Searches the immediate and nested children of root for a GO whose name matches
        /// targetName case-insensitively. Returns the first match or null.
        /// </summary>
        private static Transform FindChildCaseInsensitive(Transform root, string targetName)
        {
            foreach (Transform child in root)
            {
                if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
                    return child;

                var nested = FindChildCaseInsensitive(child, targetName);
                if (nested != null) return nested;
            }
            return null;
        }

        private static bool HasExistingGameObject(Transform root, string fieldName)
        {
            foreach (Transform child in root)
            {
                if (child.name == fieldName) return true;
                if (HasExistingGameObject(child, fieldName)) return true;
            }
            return false;
        }

        private static GameObject CreateUIGameObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            return go;
        }

        private static string GetRowName(string fieldName) => fieldName + "_Row";

        private static string PrettifyName(string name) => Regex.Replace(name.TrimStart('_'), @"(?<=[a-z])(?=[A-Z])", " ");

        private static string GetPlaceholderText(Type fieldType)
        {
            if (fieldType == typeof(string)) return "\"...\"";
            if (fieldType == typeof(int) || fieldType == typeof(float)) return "0";
            if (fieldType == typeof(bool)) return "false";
            return "-";
        }

        internal readonly struct MemberInfo
        {
            public readonly string Name;
            public readonly Type FieldType;
            public readonly System.Reflection.MemberInfo ReflectionMember;

            public MemberInfo(string name, Type fieldType, System.Reflection.MemberInfo reflectionMember)
            {
                Name = name;
                FieldType = fieldType;
                ReflectionMember = reflectionMember;
            }
        }
    }
}

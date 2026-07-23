// ZUIPolymorphicFoldout.cs
// A [SerializeReference] polymorphic field control: one row (fold arrow + title + a type-switcher button).
// The field's own [Header] (if any) still draws as its own divider line above the row, same as Unity's
// default inspector — see PolymorphicFoldout's suppressHeader param for a hand-curated caller that already
// drew its own section label. The explanation lives in [Tooltip] instead (read on hover) per authoring.md
// #12. This was long documented in zui.md's Quick Index and referenced by authoring.md #11/#12 as an
// existing control — an audit found it was never actually built (Zoe's ICharacterView field still rendered
// through raw SerializedObject+PropertyField, whose default type-switch affordance for a managed reference
// is genuinely hard to find). Built for real 2026-07-20 so the docs stop describing code that doesn't exist.
// Caller is expected to indent whatever it draws below an open foldout (a 15px GUILayout.Space margin is the
// convention used in this package's own callers).

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    const float PolyFoldoutSwitcherWidth = 20f;
    const float PolyFoldoutRowHeight = 18f;

    /// <summary>
    /// Draws one row for a <c>[SerializeReference]</c> polymorphic field: a fold arrow + title (from
    /// <c>[Header]</c> on the field, else the nicified field name; <c>[Tooltip]</c> becomes the row's hover
    /// text) + a small "▾" type-switcher button that pops a menu of every concrete type assignable to the
    /// field's declared type (via <see cref="TypeCache"/>). Returns whether the foldout is open
    /// (<c>property.isExpanded</c>, persisted for free); <paramref name="boxedValue"/> is the current
    /// managed-reference value (null if empty) so the caller can dispatch on its concrete type and draw
    /// whatever fields THAT type has, indented below.
    /// </summary>
    /// <param name="suppressHeader">Skip auto-drawing the field's own [Header] divider — for a caller that
    /// already drew its own section label immediately above this call (a hand-curated DrawAsset override,
    /// e.g. ZoeWindow's "Reactions" header before Zoe.hit). Without this, both render back to back: the
    /// caller's explicit label AND this method's own [Header] auto-draw, showing the same text twice (real
    /// bug, caught by inspection — "Look"/"Reactions" each appeared twice in ZoeWindow). Leave false for any
    /// caller that does NOT draw its own label (e.g. the fully-generic per-property loop, or Zoe/WeaponDef/
    /// AmmoDef's plain default Unity Inspector when opened directly instead of through their own window) —
    /// [Header] still needs to render there, so the attribute itself stays on the field either way.</param>
    public static bool PolymorphicFoldout(SerializedProperty property, out object boxedValue, bool suppressHeader = false)
    {
        boxedValue = property.managedReferenceValue;

        GetHeaderAndTooltip(property, out string header, out string tooltip);
        // [Header] is Unity's own "draw a bold divider line above this field" convention (usually shared by
        // several fields that follow it), NOT a per-field name override -- using it as the row's title was a
        // real bug: a field placed right under a [Header] (e.g. Zoe.hit under [Header("Effects")]) showed the
        // GROUP header text ("Effects") instead of its own name ("Hit"). Render [Header] the same way Unity's
        // default inspector would (its own divider line, above the row), and always title the row itself from
        // the field name.
        if (!suppressHeader && !string.IsNullOrEmpty(header))
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        string title = ObjectNames.NicifyVariableName(property.name);

        Rect row = GUILayoutUtility.GetRect(0f, PolyFoldoutRowHeight, GUILayout.ExpandWidth(true));
        Rect foldRect = new Rect(row.x, row.y, Mathf.Max(20f, row.width - PolyFoldoutSwitcherWidth - 4f), row.height);
        Rect typeRect = new Rect(row.xMax - PolyFoldoutSwitcherWidth, row.y, PolyFoldoutSwitcherWidth, row.height);

        // IMGUI's Foldout does NOT clip overflowing text to its Rect (it draws straight past it) — truncate
        // the STRING itself with an ellipsis rather than rely on layout to protect neighbouring controls.
        string shown = TruncateToWidth(title, EditorStyles.foldout, foldRect.width - 16f);
        bool open = EditorGUI.Foldout(foldRect, property.isExpanded, new GUIContent(shown, tooltip), true);
        property.isExpanded = open;

        string typeLabel = boxedValue != null ? ObjectNames.NicifyVariableName(boxedValue.GetType().Name) : "None";
        if (GUI.Button(typeRect, new GUIContent("▾", $"Type: {typeLabel}\nClick to change."), EditorStyles.miniButton))
            ShowTypeMenu(property);

        return open;
    }

    static void ShowTypeMenu(SerializedProperty property)
    {
        var fieldType = GetManagedReferenceFieldType(property);
        if (fieldType == null) return;

        var so = property.serializedObject;
        string path = property.propertyPath;
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent("None"), property.managedReferenceValue == null, () =>
        {
            var p = so.FindProperty(path);
            so.Update();
            p.managedReferenceValue = null;
            so.ApplyModifiedProperties();
        });

        foreach (var t in TypeCache.GetTypesDerivedFrom(fieldType))
        {
            if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
            if (t.GetConstructor(Type.EmptyTypes) == null) continue;   // needs a parameterless ctor to Activator.CreateInstance
            var captured = t;
            bool isCurrent = property.managedReferenceValue?.GetType() == captured;
            menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(captured.Name)), isCurrent, () =>
            {
                var p = so.FindProperty(path);
                so.Update();
                p.managedReferenceValue = Activator.CreateInstance(captured);
                so.ApplyModifiedProperties();
            });
        }

        menu.ShowAsContext();
    }

    /// <c>managedReferenceFieldTypename</c> is "AssemblyName TypeFullName" for the field's DECLARED type
    /// (the interface/base class), independent of whatever concrete type is currently boxed inside it.
    static Type GetManagedReferenceFieldType(SerializedProperty property)
    {
        string typenames = property.managedReferenceFieldTypename;
        if (string.IsNullOrEmpty(typenames)) return null;
        var parts = typenames.Split(' ');
        if (parts.Length != 2) return null;
        try
        {
            var asm = Assembly.Load(parts[0]);
            return asm?.GetType(parts[1]);
        }
        catch { return null; }
    }

    /// Only resolves simple top-level fields (declared directly on the SerializedObject's target type) —
    /// sufficient for the documented use case (a top-level [SerializeReference] field like Zoe.view). A
    /// field nested inside another object wouldn't resolve here and just falls back to the nicified name.
    static void GetHeaderAndTooltip(SerializedProperty property, out string header, out string tooltip)
    {
        header = null; tooltip = null;
        var target = property.serializedObject.targetObject;
        if (target == null) return;
        var field = target.GetType().GetField(property.name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) return;
        header = field.GetCustomAttribute<HeaderAttribute>()?.header;
        tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip;
    }

    static string TruncateToWidth(string text, GUIStyle style, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f) return text;
        if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;
        const string ellipsis = "…";
        for (int len = text.Length - 1; len > 0; len--)
        {
            string candidate = text.Substring(0, len) + ellipsis;
            if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) return candidate;
        }
        return ellipsis;
    }
}

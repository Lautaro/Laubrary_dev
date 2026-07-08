// ZUIFields.cs
// The editor input fields ZUI was missing: text, object-picker, color, enum popup, string dropdown — plus a
// scroll-view scope. These are deliberately thin, consistently-signed wrappers over EditorGUILayout: there is no
// non-IMGUI way to draw an asset picker or a color swatch, so the win is that ZUI now OWNS these calls (one styled
// site, sheet-scoped via the ZUIWindow instance wrappers, restyleable later) instead of every tool reaching past
// ZUI to raw EditorGUILayout. Labeled overloads use Unity's inline prefix label; the width-capped overloads drop
// into ZUI.HRow / ZUI.Flow rows next to Button/IntField/FloatField.

using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class ZUI
{
    // ── Text ──────────────────────────────────────────────────────────────────
    public static string TextField(string value, float bodyWidth = DefaultFieldWidth)
        => EditorGUILayout.TextField(value ?? "", GUILayout.Width(Mathf.Max(1f, bodyWidth)));

    public static string TextField(string label, string value)
        => EditorGUILayout.TextField(label, value ?? "");

    public static string TextFieldExpanding(string value)
        => EditorGUILayout.TextField(value ?? "");

    // ── Object / asset picker ───────────────────────────────────────────────────
    public static T ObjectField<T>(T value, bool allowSceneObjects = false, float bodyWidth = DefaultFieldWidth) where T : Object
        => (T)EditorGUILayout.ObjectField(value, typeof(T), allowSceneObjects, GUILayout.Width(Mathf.Max(1f, bodyWidth)));

    public static T ObjectField<T>(string label, T value, bool allowSceneObjects = false) where T : Object
        => (T)EditorGUILayout.ObjectField(label, value, typeof(T), allowSceneObjects);

    // ── Color ───────────────────────────────────────────────────────────────────
    public static Color ColorField(Color value, float bodyWidth = DefaultFieldWidth)
        => EditorGUILayout.ColorField(GUIContent.none, value, GUILayout.Width(Mathf.Max(1f, bodyWidth)));

    public static Color ColorField(string label, Color value)
        => EditorGUILayout.ColorField(label, value);

    // ── Enum popup ────────────────────────────────────────────────────────────────
    public static TEnum EnumPopup<TEnum>(TEnum value, float bodyWidth = DefaultFieldWidth) where TEnum : Enum
        => (TEnum)(object)EditorGUILayout.EnumPopup(value, GUILayout.Width(Mathf.Max(1f, bodyWidth)));

    public static TEnum EnumPopup<TEnum>(string label, TEnum value) where TEnum : Enum
        => (TEnum)(object)EditorGUILayout.EnumPopup(label, value);

    // ── String dropdown (index into options) ────────────────────────────────────
    public static int Dropdown(int index, string[] options, float bodyWidth = DefaultFieldWidth)
        => EditorGUILayout.Popup(index, options, GUILayout.Width(Mathf.Max(1f, bodyWidth)));

    public static int Dropdown(string label, int index, string[] options)
        => EditorGUILayout.Popup(label, index, options);

    // ── Scroll view (scope) ──────────────────────────────────────────────────────
    /// <summary>A scrollable region: <c>using (ZUI.ScrollView(ref scroll)) { … }</c>. The ref is updated in place
    /// so you keep your own persisted scroll Vector2.</summary>
    public static ScrollScope ScrollView(ref Vector2 scroll, params GUILayoutOption[] options)
    {
        scroll = EditorGUILayout.BeginScrollView(scroll, options);
        return default;
    }

    public readonly struct ScrollScope : IDisposable
    {
        public void Dispose() => EditorGUILayout.EndScrollView();
    }
}

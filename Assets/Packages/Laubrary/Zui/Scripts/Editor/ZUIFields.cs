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

    // ── Int slider ───────────────────────────────────────────────────────────────
    public static int IntSlider(string label, int value, int min, int max)
        => EditorGUILayout.IntSlider(label, value, min, max);

    // ── Vector fields ──────────────────────────────────────────────────────────────
    public static Vector2 Vector2Field(string label, Vector2 value)
        => EditorGUILayout.Vector2Field(label, value);

    public static Vector2Int Vector2IntField(string label, Vector2Int value)
        => EditorGUILayout.Vector2IntField(label, value);

    // ── Enum (boxed) ───────────────────────────────────────────────────────────────
    // Non-generic sibling of EnumPopup for reflection scenarios where the type is only known as System.Enum.
    public static Enum EnumField(string label, Enum value)
        => EditorGUILayout.EnumPopup(label, value);

    // ── Delayed-commit fields (fire on Enter/blur, not every keystroke) ──────────
    public static string DelayedTextField(string value, float bodyWidth = DefaultFieldWidth)
        => EditorGUILayout.DelayedTextField(value ?? "", GUILayout.Width(Mathf.Max(1f, bodyWidth)));
    public static string DelayedTextField(string label, string value)
        => EditorGUILayout.DelayedTextField(label, value ?? "");
    public static float DelayedFloatField(string label, float value)
        => EditorGUILayout.DelayedFloatField(label, value);
    public static float DelayedFloatField(float value, float bodyWidth = DefaultFieldWidth)
        => EditorGUILayout.DelayedFloatField(value, GUILayout.Width(Mathf.Max(1f, bodyWidth)));
    public static int DelayedIntField(string label, int value)
        => EditorGUILayout.DelayedIntField(label, value);

    // ── Object field by runtime Type (for reflection renderers where T isn't known at compile time) ──
    public static Object ObjectField(Object value, System.Type type, bool allowSceneObjects = false, float bodyWidth = DefaultFieldWidth)
        => EditorGUILayout.ObjectField(value, type, allowSceneObjects, GUILayout.Width(Mathf.Max(1f, bodyWidth)));
    public static Object ObjectField(string label, Object value, System.Type type, bool allowSceneObjects = false)
        => EditorGUILayout.ObjectField(label, value, type, allowSceneObjects);

    // ── Info / note boxes ────────────────────────────────────────────────────────
    // The only IMGUI way to draw a wrapping, iconed callout; ZUI owns the call so tools stop reaching past it.
    public static void InfoBox(string text) => EditorGUILayout.HelpBox(text, MessageType.Info);
    public static void NoteBox(string text) => EditorGUILayout.HelpBox(text, MessageType.None);

    static GUIStyle _helpIconStyle;

    /// <summary>
    /// A small "(?)" glyph that shows <paramref name="tooltip"/> as a native hover tooltip — the standing
    /// convention for explaining what a control DOES or WHY it exists (baked vs. live, a non-obvious unit, a
    /// gotcha) without printing that explanation into the UI itself. Box/section titles and field labels
    /// should stay short and literal; put the "why"/"how it works" text here instead. Place it right after a
    /// box title or field label via <c>ZUI.HRow</c>/inline GUILayout, e.g.
    /// <c>using (ZUI.HRow()) { GUILayout.Label("Live preview subject"); ZUI.HelpIcon("..."); }</c>.
    /// </summary>
    public static void HelpIcon(string tooltip)
    {
        _helpIconStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
            alignment = TextAnchor.MiddleCenter,
        };
        GUILayout.Label(new GUIContent("ⓘ", tooltip), _helpIconStyle, GUILayout.Width(14f));
    }

    /// <summary>
    /// Measures how wide a labelled field (a prefix label + its current text content — an asset name, a typed
    /// string, a dropdown's current selection) actually needs to be to show that content without truncating,
    /// clamped to [min, max]. The fix for "no infinite-width controls" (see EDITOR_TOOL_CONVENTIONS.md)
    /// tightening a field down to a fixed pixel width regardless of its ACTUAL content — a short value gets
    /// wasted space, a long one gets truncated. This grows/shrinks with whatever the field currently shows.
    /// </summary>
    public static float FitWidth(string label, string value, float min = 60f, float max = 320f)
    {
        float labelW = string.IsNullOrEmpty(label) ? 0f : EditorStyles.label.CalcSize(new GUIContent(label)).x + 6f;
        float valueW = EditorStyles.textField.CalcSize(new GUIContent(value ?? "")).x;
        // +34: icon/dropdown-arrow/select-button slack, generous enough that an ObjectField's actual icon +
        // circle-select-button never eats into the measured text (a too-tight slack here was still clipping
        // the last character or two even when the [min,max] clamp wasn't the binding constraint).
        return Mathf.Clamp(labelW + valueW + 34f, min, max);
    }

    /// <summary>
    /// `EditorGUIUtility.labelWidth` is a GLOBAL, ambient Unity setting — a window that sets it once for its
    /// own long labels (e.g. `EditorGUIUtility.labelWidth = 112f;` for "Taper (centre↔edge)") leaks that same
    /// reservation onto EVERY other labelled field drawn afterward in the same OnGUI call, including compact
    /// ones sized via <see cref="FitWidth"/> — which assumes the label consumes roughly ITS OWN text width,
    /// not whatever the ambient value happens to be. The result: a field's total requested width gets split
    /// as "[ambient labelWidth] + [whatever's left]" instead of "[actual label width] + [content]", and the
    /// content portion can end up far narrower than FitWidth intended — exactly the kind of truncation this
    /// was supposed to prevent. Wrap any FitWidth-sized (or otherwise deliberately compact) labelled field in
    /// this scope so Unity's own label reservation matches what was actually measured:
    /// <c>using (ZUI.NarrowLabel("Asset")) EditorGUILayout.ObjectField("Asset", ..., GUILayout.Width(ZUI.FitWidth("Asset", ...)));</c>
    /// </summary>
    public static NarrowLabelScope NarrowLabel(string label, float extraPad = 6f) => new NarrowLabelScope(label, extraPad);

    public struct NarrowLabelScope : IDisposable
    {
        readonly float prev;
        public NarrowLabelScope(string label, float extraPad)
        {
            prev = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = string.IsNullOrEmpty(label) ? 0f : EditorStyles.label.CalcSize(new GUIContent(label)).x + extraPad;
        }
        public void Dispose() => EditorGUIUtility.labelWidth = prev;
    }

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

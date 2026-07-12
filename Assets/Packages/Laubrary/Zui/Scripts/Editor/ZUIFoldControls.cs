// ZUIFoldControls.cs
// FoldControls — a control group where one or more controls are ALWAYS visible and the rest
// appear only when the group is expanded. How it expands is a per-group choice, set from a
// right-click context menu on the header:
//   • Arrow  — an expand triangle on the header toggles the body open/closed on click.
//   • Hover  — the body drops open while the mouse is over the header, and stays open as long
//              as the mouse is anywhere over the header OR the now-visible body.
//
// The mode is remembered per key (EditorPrefs). Expansion decisions use the PREVIOUS frame's
// measured rects so the set of drawn controls is identical across a frame's Layout and Repaint
// passes (otherwise IMGUI throws layout-mismatch errors).
//
// Usage:
//   ZUI.FoldControls("myKey",
//       alwaysVisible: () => { /* header controls */ },
//       expandable:    () => { /* controls shown only when expanded */ });

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    public enum FoldMode { Arrow, Hover }

    class FoldState
    {
        public FoldMode mode;
        public bool arrowOpen;
        public Rect headerRect;
        public Rect bodyRect;
    }

    static readonly Dictionary<string, FoldState> _foldStates = new Dictionary<string, FoldState>();

    static FoldState GetFoldState(string key, FoldMode def)
    {
        if (!_foldStates.TryGetValue(key, out var s))
        {
            s = new FoldState { mode = (FoldMode)EditorPrefs.GetInt(FoldPrefKey(key), (int)def) };
            _foldStates[key] = s;
        }
        return s;
    }

    static string FoldPrefKey(string key) => "ZUI.FoldControls.mode." + key;

    /// <summary>Draw a fold-control group. Returns whether the body is currently expanded.</summary>
    public static bool FoldControls(string key, Action alwaysVisible, Action expandable,
                                    FoldMode defaultMode = FoldMode.Arrow)
    {
        if (string.IsNullOrEmpty(key)) key = "unkeyed";
        var st = GetFoldState(key, defaultMode);
        var ev = Event.current;

        // Decide expansion from the PREVIOUS frame's rects (stable within this frame's passes).
        bool expanded = st.mode == FoldMode.Arrow
            ? st.arrowOpen
            : st.headerRect.Contains(ev.mousePosition) || st.bodyRect.Contains(ev.mousePosition);

        EditorGUILayout.BeginVertical();

        // ── header (always visible) ────────────────────────────────────────────
        Rect headerRect = EditorGUILayout.BeginVertical();
        EditorGUILayout.BeginHorizontal();
        if (st.mode == FoldMode.Arrow)
        {
            var tri = new GUIContent(expanded ? "▼" : "▶"); // ▼ / ▶
            if (GUILayout.Button(tri, EditorStyles.label, GUILayout.Width(14f), GUILayout.Height(16f)))
            {
                st.arrowOpen = !st.arrowOpen;
                GUI.changed = true;
            }
        }
        alwaysVisible?.Invoke();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        if (ev.type != EventType.Layout) st.headerRect = headerRect;

        // ── body (expandable) ──────────────────────────────────────────────────
        if (expanded)
        {
            Rect bodyRect = EditorGUILayout.BeginVertical();
            expandable?.Invoke();
            EditorGUILayout.EndVertical();
            if (ev.type != EventType.Layout) st.bodyRect = bodyRect;
        }
        else if (ev.type != EventType.Layout)
        {
            st.bodyRect = Rect.zero;
        }

        EditorGUILayout.EndVertical();

        // Right-click the header → choose how this group expands.
        if (ev.type == EventType.ContextClick && st.headerRect.Contains(ev.mousePosition))
        {
            ShowFoldMenu(key, st);
            ev.Use();
        }

        return expanded;
    }

    static void ShowFoldMenu(string key, FoldState st)
    {
        // Only one category here (how to expand), so no "Expand on/" submenu — items sit at the top level.
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Arrow (click)"), st.mode == FoldMode.Arrow, () =>
        {
            st.mode = FoldMode.Arrow;
            EditorPrefs.SetInt(FoldPrefKey(key), (int)st.mode);
        });
        menu.AddItem(new GUIContent("Hover"), st.mode == FoldMode.Hover, () =>
        {
            st.mode = FoldMode.Hover;
            EditorPrefs.SetInt(FoldPrefKey(key), (int)st.mode);
        });
        menu.ShowAsContext();
    }
}

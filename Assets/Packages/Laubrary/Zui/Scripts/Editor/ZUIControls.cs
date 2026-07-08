// ZUIControls.cs
// The structural scopes + list widgets the window ports flagged as missing: a toolbar strip, a vertical group, a
// selectable tree/list row, and a multi-select chip. Thin wrappers over EditorStyles so ZUI owns the call site;
// the row/chip carry the left-click + right-click ("isolate"/context) handling that tools were hand-rolling.

using System;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    // ── Toolbar strip ────────────────────────────────────────────────────────────
    /// <summary>A top toolbar strip: <c>using (ZUI.Toolbar()) { … }</c>.</summary>
    public static ToolbarScope Toolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        return default;
    }
    public readonly struct ToolbarScope : IDisposable { public void Dispose() => GUILayout.EndHorizontal(); }

    // ── Vertical group ───────────────────────────────────────────────────────────
    /// <summary>A plain vertical group: <c>using (ZUI.VGroup(GUILayout.Width(210f))) { … }</c>.</summary>
    public static VGroupScope VGroup(params GUILayoutOption[] options)
    {
        GUILayout.BeginVertical(options);
        return default;
    }
    /// <summary>A boxed vertical group (help-box framing) for a card/panel.</summary>
    public static VGroupScope VGroupBox(params GUILayoutOption[] options)
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, options);
        return default;
    }
    public readonly struct VGroupScope : IDisposable { public void Dispose() => GUILayout.EndVertical(); }

    // ── Selectable list/tree row ───────────────────────────────────────────────────
    /// <summary>A left-aligned selectable row (label-styled, bold + "▸" when selected, optional indent). Returns
    /// true on left-click; sets <paramref name="rightClicked"/> on right-click (for isolate / context actions).
    /// Replaces the RowButton pattern tools hand-rolled with GetLastRect + Event handling.</summary>
    public static bool SelectableRow(string label, bool selected, out bool rightClicked, int indent = 0)
    {
        rightClicked = false;
        GUILayout.BeginHorizontal();
        if (indent > 0) GUILayout.Space(indent * 12f);
        var style = selected ? EditorStyles.boldLabel : EditorStyles.label;
        bool clicked = GUILayout.Button((selected ? "▸ " : "") + label, style);
        Rect r = GUILayoutUtility.GetLastRect();
        GUILayout.EndHorizontal();
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 1 && r.Contains(e.mousePosition)) { e.Use(); rightClicked = true; }
        return clicked;
    }

    // ── Multi-select chip ──────────────────────────────────────────────────────────
    /// <summary>A "chip" toggle (mini-button) for tag/filter selection. Returns the new on-state; sets
    /// <paramref name="rightClicked"/> on right-click (isolate / context).</summary>
    public static bool Chip(bool on, string label, out bool rightClicked)
    {
        rightClicked = false;
        bool now = GUILayout.Toggle(on, label, EditorStyles.miniButton);
        Rect r = GUILayoutUtility.GetLastRect();
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 1 && r.Contains(e.mousePosition)) { e.Use(); rightClicked = true; }
        return now;
    }
}

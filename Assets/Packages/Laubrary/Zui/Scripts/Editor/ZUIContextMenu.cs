// ZUIContextMenu.cs
// Declarative wrapper over UnityEditor.GenericMenu so a right-click menu doesn't need a hand-rolled
// GenericMenu at every call site. Pairs with ZUI.SelectableRow/ZUI.Chip's `rightClicked` out-param — the
// canonical shape is:
//   if (ZUI.SelectableRow(label, selected, out bool rightClicked)) { Select(item); }
//   if (rightClicked) ZUI.ContextMenu(ZUI.MenuItem("Rename", () => Rename(item)),
//                                      ZUI.MenuItem("Delete", () => Delete(item)));
//
// Submenus use GenericMenu's own "/" path convention in the label (e.g. "Mode/Static"), same as every
// hand-rolled GenericMenu already in this package — no new syntax to learn.
//
// This is for the common flat/simple case. A menu whose items need to be computed from complex branching
// state (see ZUIValueControl.cs's ShowMenu, ZUIFoldControls.cs's ShowFoldMenu) can stay hand-rolled — this
// wrapper isn't trying to replace GenericMenu, just remove the boilerplate for the common declarative case.

using System;
using UnityEditor;
using UnityEngine;

public static partial class ZUI
{
    public readonly struct ZUIMenuItem
    {
        internal readonly string label;
        internal readonly Action onClick;
        internal readonly bool enabled;
        internal readonly bool isChecked;
        internal readonly bool isSeparator;

        internal ZUIMenuItem(string label, Action onClick, bool enabled, bool isChecked, bool isSeparator)
        {
            this.label = label;
            this.onClick = onClick;
            this.enabled = enabled;
            this.isChecked = isChecked;
            this.isSeparator = isSeparator;
        }
    }

    /// <summary>One clickable menu row. Use "/" in <paramref name="label"/> for a submenu (e.g.
    /// "Mode/Static") — GenericMenu's own convention.</summary>
    public static ZUIMenuItem MenuItem(string label, Action onClick, bool @checked = false, bool enabled = true)
        => new ZUIMenuItem(label, onClick, enabled, @checked, isSeparator: false);

    /// <summary>A separator line, optionally scoped to a submenu path ("" = top level).</summary>
    public static ZUIMenuItem MenuSeparator(string path = "") => new ZUIMenuItem(path, null, true, false, isSeparator: true);

    /// <summary>Builds and immediately shows a GenericMenu from the given items — the declarative
    /// alternative to hand-building one at the call site for straightforward menus.</summary>
    public static void ContextMenu(params ZUIMenuItem[] items)
    {
        var menu = new GenericMenu();
        foreach (var item in items)
        {
            if (item.isSeparator) { menu.AddSeparator(item.label); continue; }
            var content = new GUIContent(item.label);
            if (!item.enabled) menu.AddDisabledItem(content, item.isChecked);
            else menu.AddItem(content, item.isChecked, () => item.onClick?.Invoke());
        }
        menu.ShowAsContext();
    }
}

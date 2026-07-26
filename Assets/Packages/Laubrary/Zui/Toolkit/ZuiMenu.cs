// ZuiMenu — a ZUI-styled, richer stand-in for GenericMenu, built on ZuiPopover. Where GenericMenu can only
// show OS-drawn text rows with an optional checkmark and slash-nested submenus, ZuiMenu composes a floating
// card out of real ZUI controls: labelled items (with an optional embedded icon and a checkmark), section
// headers, separators, persistent toggle rows, and MiniRadio groups. It reads fluently and Show() returns
// the ZuiPopover handle:
//
//     Z.Menu(addButton)
//         .Section("Geometry")
//         .Item("Rotate", "Spin each disc.", () => Add<RotateModifier>())
//         .Item("Bulge",  "Push discs outward.", () => Add<BulgeModifier>())
//         .Separator()
//         .IconItem("eye", "Preview", "Toggle the live overlay.", TogglePreview)
//         .Show();
//
// Item semantics match GenericMenu's: click runs the action and dismisses the menu; `@checked` draws a tick;
// `enabled:false` greys the row out and swallows clicks. Toggle()/Radio() are the flyout extras GenericMenu
// has no equivalent for — they STAY open so a user can flip several settings in one visit (Radio can opt into
// closeOnSelect for a pick-one-and-go group). A section header replaces GenericMenu's "Group/Item" submenu
// nesting with a flat, always-visible heading — richer to scan and one fewer click than a submenu.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public sealed class ZuiMenu
    {
        readonly VisualElement _anchor;
        // Each row is deferred until Show(): it receives the menu body to add into, and a `close` action it
        // captures so a click can dismiss the popover. `close` is invoked at CLICK time (never during build),
        // so it safely resolves the handle that Show() is still in the middle of creating.
        readonly List<Action<VisualElement, Action>> _rows = new();
        float _minWidth = 190f;
        ZuiPopover.Options _opt = new ZuiPopover.Options();

        internal ZuiMenu(VisualElement anchor) { _anchor = anchor; }

        /// Override the menu's minimum width (default 190).
        public ZuiMenu Width(float px) { _minWidth = px; return this; }

        /// Prefer opening ABOVE the anchor (default: below, flipping up only when below overflows).
        public ZuiMenu Above() { _opt.preferredSide = ZuiPopover.Side.Above; return this; }

        /// Run a callback when the menu is dismissed (any cause).
        public ZuiMenu OnClosed(Action cb) { _opt.onClosed = cb; return this; }

        /// A bold section heading — the flat-menu replacement for a GenericMenu submenu group.
        public ZuiMenu Section(string title, string tooltip = null)
        {
            _rows.Add((menu, _) =>
            {
                var l = new Label(title) { tooltip = tooltip, pickingMode = PickingMode.Ignore };
                l.AddToClassList("zui-menu__section");
                menu.Add(l);
            });
            return this;
        }

        /// A thin divider between runs of items.
        public ZuiMenu Separator()
        {
            _rows.Add((menu, _) =>
            {
                var d = new VisualElement { pickingMode = PickingMode.Ignore };
                d.AddToClassList("zui-menu__sep");
                menu.Add(d);
            });
            return this;
        }

        /// A menu item: click runs <paramref name="onClick"/> then closes the menu (GenericMenu semantics).
        /// <paramref name="checked"/> draws a tick in the left gutter; <paramref name="icon"/> (a ZUI icon
        /// name, resolved via ZUIAssetLibrary.FindIcon) draws a 14px glyph before the label;
        /// <paramref name="enabled"/>:false greys the row and ignores clicks.
        public ZuiMenu Item(string label, string tooltip, Action onClick,
            bool @checked = false, bool enabled = true, string icon = null)
        {
            _rows.Add((menu, close) => menu.Add(BuildItem(label, tooltip, onClick, @checked, enabled, icon, close)));
            return this;
        }

        /// An item that leads with an icon (shorthand for Item(..., icon: icon)).
        public ZuiMenu IconItem(string icon, string label, string tooltip, Action onClick, bool enabled = true)
            => Item(label, tooltip, onClick, false, enabled, icon);

        /// A persistent toggle row — stays open on click so several settings can be flipped in one visit
        /// (a real Z.Toggle, checkbox and all). For a one-shot checkmark item that dismisses, use
        /// Item(..., checked: ...) instead.
        public ZuiMenu Toggle(string label, string tooltip, bool value, Action<bool> onChanged)
        {
            _rows.Add((menu, _) =>
            {
                var row = Z.Toggle(label, tooltip, value, onChanged);
                row.AddToClassList("zui-menu__toggle");
                menu.Add(row);
            });
            return this;
        }

        /// A MiniRadio group row (optionally labelled). Stays open by default so it reads as a live setting;
        /// pass closeOnSelect:true for a pick-one-then-dismiss group.
        public ZuiMenu Radio(string label, string[] options, int selected, string tooltip,
            Action<int> onChanged, bool closeOnSelect = false)
        {
            _rows.Add((menu, close) =>
            {
                var radio = Z.MiniRadio(selected, options, tooltip, i =>
                {
                    onChanged?.Invoke(i);
                    if (closeOnSelect) close?.Invoke();
                }, wrap: true);
                VisualElement row = string.IsNullOrEmpty(label) ? radio : Z.Field(label, tooltip, radio);
                row.AddToClassList("zui-menu__radio");
                menu.Add(row);
            });
            return this;
        }

        /// Arbitrary content, for the rare case a caller needs an element the row helpers don't cover. The
        /// element is added into the menu body as-is; call `close` yourself if a click should dismiss.
        public ZuiMenu Custom(Action<VisualElement, Action> build)
        {
            if (build != null) _rows.Add(build);
            return this;
        }

        /// Open the menu and return the ZuiPopover handle (Close() dismisses it).
        public ZuiPopover Show()
        {
            _opt.minWidth = _minWidth;
            var holder = new ZuiPopover[1];
            holder[0] = ZuiPopover.Show(_anchor, panel =>
            {
                var menu = new VisualElement { name = "zui-menu" };
                menu.AddToClassList("zui-menu");
                Action close = () => holder[0]?.Close();
                foreach (var r in _rows) r(menu, close);
                panel.Add(menu);
            }, _opt);
            return holder[0];
        }

        static VisualElement BuildItem(string label, string tooltip, Action onClick,
            bool @checked, bool enabled, string icon, Action close)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("zui-menu__item");

            // A fixed check gutter keeps every label left-aligned whether or not it carries a tick.
            var check = new Label(@checked ? "✓" : "") { pickingMode = PickingMode.Ignore };
            check.AddToClassList("zui-menu__check");
            row.Add(check);

            if (!string.IsNullOrEmpty(icon))
            {
                var img = new Image { image = ZUIAssetLibrary.FindIcon(icon), scaleMode = UnityEngine.ScaleMode.ScaleToFit };
                img.AddToClassList("zui-menu__icon");
                img.pickingMode = PickingMode.Ignore;
                row.Add(img);
            }

            var lbl = new Label(label) { pickingMode = PickingMode.Ignore };
            lbl.AddToClassList("zui-menu__label");
            row.Add(lbl);

            if (enabled)
                row.AddManipulator(new Clickable(() => { onClick?.Invoke(); close?.Invoke(); }));
            else
            {
                row.AddToClassList("zui-menu__item--disabled");
                row.SetEnabled(false);
            }
            return row;
        }
    }
}

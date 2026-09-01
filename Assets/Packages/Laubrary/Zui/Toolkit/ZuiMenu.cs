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

        // ── search/filter (T-0135) — a huge catalog (Shaper's 41-effect list, its composite-generator
        // catalog, any future long menu) needs to be findable and needs to not simply run off the bottom of
        // the window: ZuiPopover.Place clamps the panel's POSITION, never its height, so an unbounded tall
        // menu "sits pinned at the top" with real rows below the fold and invisible (its own doc comment).
        // Search() opts a menu into both fixes at once: a filter field, and a capped, scrollable body.
        sealed class SectionEntry { public VisualElement header; }
        readonly List<(VisualElement row, string text, SectionEntry section)> _searchableItems = new();
        SectionEntry _currentSection;
        bool _searchEnabled;
        string _searchPlaceholder = "Search…";
        float _maxBodyHeight = 360f;

        internal ZuiMenu(VisualElement anchor) { _anchor = anchor; }

        /// Opt this menu into a filter field + a capped/scrollable body — for any catalog that can plausibly
        /// outgrow a screenful (Shaper's effect and generator catalogs are the first consumers). Filters
        /// `Item`/`IconItem` rows by label substring (case-insensitive); a `Section` header hides itself once
        /// every item under it is filtered out. `Toggle`/`Radio`/`IconRow`/`Custom` rows are never filtered —
        /// a persistent setting has nothing to "search" and must stay visible regardless of the query.
        public ZuiMenu Search(string placeholder = "Search…", float maxBodyHeight = 360f)
        {
            _searchEnabled = true;
            _searchPlaceholder = placeholder;
            _maxBodyHeight = maxBodyHeight;
            return this;
        }

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
                _currentSection = new SectionEntry { header = l };
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
            // _currentSection must be read INSIDE the deferred lambda, not here at chain-build time: every
            // row (this Item's own included) is only a queued Action until Show() actually runs `_rows` in
            // order, and that is also when Section()'s OWN lambda updates `_currentSection` — reading it
            // eagerly here always sees whatever it was before the whole fluent chain started (null on a
            // fresh menu), so every item silently landed in no section at all. Found live, T-0135: the
            // per-item search filter worked (it doesn't depend on section), but not one section header ever
            // hid, because the hide-check never had a real section to test against.
            _rows.Add((menu, close) =>
            {
                var section = _currentSection;
                var row = BuildItem(label, tooltip, onClick, @checked, enabled, icon, close);
                menu.Add(row);
                if (_searchEnabled) _searchableItems.Add((row, (label ?? "").ToLowerInvariant(), section));
            });
            return this;
        }

        /// An item that leads with an icon (shorthand for Item(..., icon: icon)).
        public ZuiMenu IconItem(string icon, string label, string tooltip, Action onClick, bool enabled = true)
            => Item(label, tooltip, onClick, false, enabled, icon);

        /// A persistent toggle row — stays open on click so several settings can be flipped in one visit.
        /// Renders as a ZUI button-toggle (Z.ToggleButton / ZuiToggleButton: the label latches visibly
        /// pressed when on), NOT a native checkbox — a UITK Toggle's checkmark box reads as a bare OS
        /// control inside a themed menu card. For a one-shot checkmark item that dismisses, use
        /// Item(..., checked: ...) instead.
        public ZuiMenu Toggle(string label, string tooltip, bool value, Action<bool> onChanged)
        {
            _rows.Add((menu, _) =>
            {
                var row = Z.ToggleButton(label, tooltip, value, onChanged);
                row.AddToClassList("zui-menu__toggle");
                // A full-width menu row, read left like the item rows; never wrap the label — let a long
                // one widen the menu instead (a menu is a content-sized floating card).
                row.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                row.style.whiteSpace = WhiteSpace.NoWrap;
                row.style.marginTop = 1;
                row.style.marginBottom = 1;
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
                // wrap:false — a menu is a floating card that can be any width, so a long option set should
                // WIDEN the menu, not fold onto a second line (the "menu forces new rows" complaint).
                var radio = Z.MiniRadio(selected, options, tooltip, i =>
                {
                    onChanged?.Invoke(i);
                    if (closeOnSelect) close?.Invoke();
                }, wrap: false);
                VisualElement row = string.IsNullOrEmpty(label) ? radio : Z.Field(label, tooltip, radio);
                row.AddToClassList("zui-menu__radio");
                menu.Add(row);
            });
            return this;
        }

        /// A single row of compact, label-LESS icon buttons (e.g. Copy / Paste side by side). The icon says it
        /// all, so no text — they stack horizontally and keep the row narrow. Each button runs its action then
        /// closes the menu; an entry with a null action is greyed out (e.g. Paste with an empty clipboard).
        public ZuiMenu IconRow(params (string icon, string tooltip, Action onClick)[] buttons)
        {
            _rows.Add((menu, close) =>
            {
                var row = new VisualElement();
                row.AddToClassList("zui-menu__iconrow");
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 2; row.style.marginBottom = 1;
                foreach (var b in buttons)
                {
                    var btn = new VisualElement { tooltip = b.tooltip };
                    btn.AddToClassList("zui-menu__item");   // reuse the item hover/greyed styling
                    btn.style.flexDirection = FlexDirection.Row;
                    btn.style.justifyContent = Justify.Center;
                    btn.style.alignItems = Align.Center;
                    btn.style.width = 36; btn.style.height = 22; btn.style.marginRight = 4;
                    btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                        btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 3;
                    var img = new Image { image = ZUIAssetLibrary.FindIcon(b.icon), scaleMode = UnityEngine.ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    img.AddToClassList("zui-menu__icon");
                    img.style.width = 15; img.style.height = 15; img.style.marginRight = 0;
                    btn.Add(img);
                    if (b.onClick != null)
                    {
                        var act = b.onClick;
                        btn.AddManipulator(new Clickable(() => { act(); close?.Invoke(); }));
                    }
                    else { btn.AddToClassList("zui-menu__item--disabled"); btn.SetEnabled(false); }
                    row.Add(btn);
                }
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
                // Inner clicks must NEVER dismiss the menu — only a true OUTSIDE click (which lands on the
                // popover's scrim, not on this panel) or an explicit Item / close-on-select pick closes it.
                // A pointer-down on any control here bubbles up toward the scrim; stopping it at the panel
                // guarantees a click on a toggle / radio / slider (or the menu's own padding) can't be taken
                // for an outside click, so several settings can be adjusted in one visit.
                panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                var menu = new VisualElement { name = "zui-menu" };
                menu.AddToClassList("zui-menu");
                Action close = () => holder[0]?.Close();

                VisualElement rowHost = menu;
                if (_searchEnabled)
                {
                    var search = Z.TextInput("", _searchPlaceholder, q => ApplyFilter(q), 0f);
                    search.AddToClassList("zui-menu__search");
                    search.style.width = StyleKeyword.Auto;
                    search.style.marginBottom = 2f;
                    menu.Add(search);

                    // Capped + scrollable body — the fix for ZuiPopover.Place's own documented limit that it
                    // clamps the panel's POSITION, never its height, so an unbounded menu just runs off the
                    // bottom of the window with the tail invisible instead of scrolling into view.
                    var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "zui-menu-scroll" };
                    scroll.style.maxHeight = _maxBodyHeight;
                    menu.Add(scroll);
                    rowHost = scroll.contentContainer;

                    // Focus the field the moment the menu lands, so typing to filter needs no extra click —
                    // the whole point of opening a searchable menu is almost always "I know what I want, let
                    // me type it", not "let me click into a box first".
                    search.schedule.Execute(() => search.Focus()).ExecuteLater(0);
                }

                foreach (var r in _rows) r(rowHost, close);
                panel.Add(menu);
            }, _opt);
            return holder[0];
        }

        /// Hide every Item/IconItem row whose label doesn't contain `query` (case-insensitive, empty query =
        /// show everything), then hide each Section header whose every item is now hidden. Rows outside a
        /// Section (added before any Section() call) are never hidden by the header pass since they carry a
        /// null `section` — only their own text match governs them.
        void ApplyFilter(string query)
        {
            query = (query ?? "").Trim().ToLowerInvariant();
            var visibleInSection = new Dictionary<SectionEntry, int>();
            foreach (var (row, text, section) in _searchableItems)
            {
                bool match = query.Length == 0 || text.Contains(query);
                row.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                if (match && section != null)
                    visibleInSection[section] = visibleInSection.TryGetValue(section, out int n) ? n + 1 : 1;
            }
            var seen = new HashSet<SectionEntry>();
            foreach (var (_, _, section) in _searchableItems)
            {
                if (section == null || section.header == null || !seen.Add(section)) continue;
                section.header.style.display =
                    visibleInSection.TryGetValue(section, out int n) && n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
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

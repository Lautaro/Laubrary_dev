// ZuiSection — a titled, COLLAPSIBLE section whose header is the toggle.
//
// A plain `Z.Text(..., ZuiText.Section, ..)` heading is only a label: it sits beside the controls it
// names rather than owning them, so it can't collapse anything. A section owns its body, which is what
// makes the header clickable and what lets a long tool panel be folded down to the parts in use.
//
// Children added to a ZuiSection land in its BODY (contentContainer is overridden), so a call site
// reads the same as any other container:
//     var s = Z.Section("Layers", "The blast's draw stack.");
//     root.Add(s);
//     s.Add(theList);            // goes inside the section
//
// Fold state is static and keyed, so it survives the window rebuilds that undo/redo and structural
// edits trigger — a section the user closed must stay closed.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiSection : VisualElement
    {
        static readonly Dictionary<string, bool> s_open = new();

        readonly VisualElement _body;
        readonly string _key;

        // ── optional header checkbox (created lazily by SetHeaderToggle) ──
        readonly VisualElement _header;   // the clickable header row, so the checkbox can be inserted into it
        readonly Label _title;            // the title label, so the checkbox lands just to its LEFT
        readonly Label _closedCaret;      // a ▸ shown ONLY while collapsed, so a folded section stays findable
        readonly string _titleText;       // base title text, so a collapsed-only suffix can be appended/removed
        Toggle _headerToggle;
        Action<bool> _headerToggleChanged;

        // ── optional collapsed-only header suffix (set by SetHeaderSuffix) ──
        Func<string> _headerSuffix;

        /// Children go into the body, not next to the header.
        public override VisualElement contentContainer => _body;

        public bool IsOpen
        {
            get => !s_open.TryGetValue(_key, out bool open) || open;   // default: open
            set { s_open[_key] = value; Apply(); }
        }

        public ZuiSection(string title, string tooltip, string stateKey = null, string icon = null)
        {
            // Title alone collides — "Gradient" heads three different blocks in Pyre — so the tooltip,
            // which is what actually distinguishes them, is part of the key.
            _key = stateKey ?? (title ?? "section") + "" + (tooltip ?? string.Empty);
            AddToClassList("zui-section");

            var header = new VisualElement();
            header.AddToClassList("zui-section__header");
            header.tooltip = tooltip;
            _header = header;

            // A collapsed-only ▸ affordance. Open sections stay caret-free (the bold title reads as the heading, per
            // the earlier request), but a CLOSED section otherwise looks like it simply vanished — users folded one
            // and couldn't find it to reopen. The caret appears only while closed, making "click to expand" obvious.
            _closedCaret = new Label("▸") { pickingMode = PickingMode.Ignore };
            _closedCaret.AddToClassList("zui-section__caret");
            header.Add(_closedCaret);

            // Optional leading icon, tinted to the section-title colour so it reads as part of the heading.
            // Added FIRST so it sits at the head of the row; an enable checkbox added later via
            // SetHeaderToggle inserts just LEFT of the title (i.e. AFTER this icon), giving icon · ☑ · title.
            var iconEl = Z.Icon(icon, 14f);
            if (iconEl != null)
            {
                iconEl.AddToClassList("zui-section__icon");
                header.Add(iconEl);
            }

            // No fold caret on section headers (user request): the bold coloured title + hover highlight
            // already read as an interactive heading, and clicking the header still folds. A closed section
            // dims its header (see the --closed rule) as the collapse cue instead of a chevron.
            var text = new Label(title) { tooltip = tooltip };
            text.AddToClassList("zui-section__title");
            text.pickingMode = PickingMode.Ignore;   // the whole header row is the hit target
            header.Add(text);
            _title = text;
            _titleText = title ?? string.Empty;

            if (!string.IsNullOrEmpty(tooltip))
            {
                header.Add(Z.Flexible());
                var help = Z.HelpIcon(tooltip);
                help.pickingMode = PickingMode.Ignore;
                header.Add(help);
            }

            // Toggle via a Clickable manipulator rather than a raw PointerDownEvent. A bare
            // RegisterCallback<PointerDownEvent> did NOT fire reliably for the header inside a
            // ScrollView (verified live: the fold state machinery worked when driven directly, but a
            // real click never reached it). Clickable is what Button itself uses — it owns the
            // pointer-down/up pair and the capture in between — so a header behaves exactly like the
            // buttons beside it, which are known to work in these windows.
            header.AddManipulator(new Clickable(() => IsOpen = !IsOpen));
            hierarchy.Add(header);

            _body = new VisualElement();
            _body.AddToClassList("zui-section__body");
            hierarchy.Add(_body);

            ZuiLabelAlign.Align(this);   // line up this section's field labels into one tidy column
            Apply();
        }

        /// Give the header a picker MENU: a small icon button just after the title opens `open(anchor)`, and a
        /// RIGHT-CLICK anywhere on the header opens the same menu. The section still folds on a normal left-click of
        /// the header, but not when the button — or a right-click — is used. (Used for e.g. a shape-type picker that
        /// replaces a block of in-body radio rows.)
        public void SetHeaderMenu(string iconName, string tooltip, Action<VisualElement> open)
        {
            if (open == null) return;
            var btn = new VisualElement { tooltip = tooltip };
            btn.AddToClassList("zui-section__headerbtn");
            btn.style.marginLeft = 6;
            btn.style.width = 20; btn.style.height = 15;
            btn.style.justifyContent = Justify.Center;
            btn.style.alignItems = Align.Center;
            btn.style.backgroundColor = new Color(1f, 1f, 1f, 0.07f);
            btn.style.borderTopLeftRadius = btn.style.borderTopRightRadius =
                btn.style.borderBottomLeftRadius = btn.style.borderBottomRightRadius = 3;
            var img = Z.Icon(iconName, 12f);
            if (img != null) { img.pickingMode = PickingMode.Ignore; btn.Add(img); }
            btn.AddManipulator(new Clickable(() => open(btn)));
            btn.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());   // a button click never folds the section
            int idx = _header.IndexOf(_title);
            _header.Insert(idx + 1, btn);
            // Right-click anywhere on the header opens the same menu (anchored to the button).
            _header.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { open(btn); e.StopPropagation(); } });
        }

        void Apply()
        {
            bool open = IsOpen;
            _body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("zui-section--closed", !open);
            if (_closedCaret != null) _closedCaret.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
            // A COLLAPSED section can hide active content (e.g. enabled modifiers). Show the suffix its
            // provider returns (a count like " (2)") on the header while closed; drop it again when open,
            // where the content itself is visible. No provider ⇒ the title is exactly the base text.
            if (_title != null)
                _title.text = _titleText + (open ? string.Empty : (_headerSuffix?.Invoke() ?? string.Empty));
        }

        // ── collapsed-only header suffix ──────────────────────────────────────────────────────────────

        /// Give the header a suffix shown ONLY while the section is COLLAPSED — for surfacing
        /// hidden-but-active content (e.g. "Modifiers (2)" when the folded body holds two enabled
        /// modifiers). `provider` returns the whole suffix string (compose the parens yourself); return
        /// "" for none. DEFAULTS to no suffix, so a section that never calls this renders exactly as
        /// before. Call RefreshHeaderSuffix() to re-evaluate after the hidden count changes while the
        /// section stays collapsed (e.g. the selection it reflects moved under it).
        public void SetHeaderSuffix(Func<string> provider)
        {
            _headerSuffix = provider;
            Apply();
        }

        /// Re-evaluate the header-suffix provider now. No-op when none was set.
        public void RefreshHeaderSuffix() => Apply();

        // ── header checkbox ──────────────────────────────────────────────────────────────────────────

        /// Give the section header a compact checkbox, sitting just LEFT of the title, bound to any bool.
        /// This folds a "Swarm" heading + a separate "Swarm" enable toggle into one row: the title names
        /// the block and the checkbox enables it. Clicking the checkbox toggles the value WITHOUT folding
        /// the section — the pointer-down is stopped before it reaches the header's fold Clickable, exactly
        /// the trick ZuiBox's gear uses; the title (and the rest of the header row) remains the fold zone.
        /// The checkbox carries `tooltip`, so it is ZuiAudit-clean. Idempotent: calling it again just
        /// rebinds and refreshes the existing checkbox. Sections that never call this render exactly as
        /// before (no checkbox added).
        public void SetHeaderToggle(bool value, string tooltip, Action<bool> onChanged)
        {
            _headerToggleChanged = onChanged;

            if (_headerToggle == null)
            {
                _headerToggle = new Toggle { tooltip = tooltip };
                _headerToggle.AddToClassList("zui-audit-allow-toggle");   // fold header, not a checkbox setting
                _headerToggle.AddToClassList("zui-section__toggle");
                // The header row is `align-items: center`, so vertical centring is handled; strip the
                // Toggle's default margins to a tight, small footprint and leave a little air before the title.
                _headerToggle.style.marginTop = 0f;
                _headerToggle.style.marginBottom = 0f;
                _headerToggle.style.marginLeft = 0f;
                _headerToggle.style.marginRight = 4f;
                // Do not let a click on the checkbox fold the section (see ZuiBox's gear StopPropagation).
                _headerToggle.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                _headerToggle.RegisterValueChangedCallback(e => _headerToggleChanged?.Invoke(e.newValue));

                int idx = _header.IndexOf(_title);   // insert just to the LEFT of the title (after the caret)
                if (idx < 0) idx = _header.childCount;
                _header.Insert(idx, _headerToggle);
            }
            else if (!string.IsNullOrEmpty(tooltip))
            {
                _headerToggle.tooltip = tooltip;
            }

            _headerToggle.SetValueWithoutNotify(value);
        }

        /// Refresh the header checkbox's value from outside WITHOUT firing onChanged (e.g. after an undo or
        /// an external state change). No-op if SetHeaderToggle was never called.
        public void SetHeaderToggleWithoutNotify(bool value)
            => _headerToggle?.SetValueWithoutNotify(value);
    }
}

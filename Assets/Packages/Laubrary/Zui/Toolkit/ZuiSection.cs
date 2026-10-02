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
        readonly string _titleText;       // base title text, so a collapsed-only suffix can be appended/removed
        readonly VisualElement _headerControls;
        Toggle _headerToggle;
        Action<bool> _headerToggleChanged;

        // ── optional collapsed-only header suffix (set by SetHeaderSuffix) ──
        Func<string> _headerSuffix;

        // ── the help-icon element, kept so SetTooltip can update it alongside the header/title (T-0200) ──
        Label _help;

        /// Raised after the user folds/unfolds this section by clicking its own header (never on a
        /// programmatic IsOpen set) — mirrors ZuiBox's ViewChanged. Lets an external "toggle bar" (a row of
        /// buttons that shows/hides sections in bulk) stay in sync when the user instead folds a section the
        /// OLD way, by clicking its header directly.
        public event Action ViewChanged;

        // ── header-fold disable (ZuiSectionToggleBar's "either headers OR the bar" mode) ──
        bool _headerFoldDisabled;

        /// When true, clicking the header does NOT fold/unfold the section — used by ZuiSectionToggleBar so
        /// a group of sections is controlled ONLY by its bar, never fought over by two controls at once.
        /// IsOpen can still be set programmatically (that's exactly how the bar drives it) while this is on.
        public bool HeaderFoldDisabled
        {
            get => _headerFoldDisabled;
            set { _headerFoldDisabled = value; _header.EnableInClassList("zui-section__header--nofold", value); Apply(); }
        }

        /// Children go into the body, not next to the header.
        public override VisualElement contentContainer => _body;

        /// Compact secondary controls, aligned at the right of the header just before its help icon.
        /// Their pointer gestures never fold the section. Keep this slot stable when changing view state.
        public VisualElement HeaderControls => _headerControls;

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

            header.Add(Z.Flexible());
            _headerControls = new VisualElement();
            _headerControls.AddToClassList("zui-section__controls");
            _headerControls.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            header.Add(_headerControls);

            if (!string.IsNullOrEmpty(tooltip))
            {
                var help = Z.HelpIcon(tooltip);
                help.pickingMode = PickingMode.Ignore;
                header.Add(help);
                _help = help;
            }

            // Toggle via a Clickable manipulator rather than a raw PointerDownEvent. A bare
            // RegisterCallback<PointerDownEvent> did NOT fire reliably for the header inside a
            // ScrollView (verified live: the fold state machinery worked when driven directly, but a
            // real click never reached it). Clickable is what Button itself uses — it owns the
            // pointer-down/up pair and the capture in between — so a header behaves exactly like the
            // buttons beside it, which are known to work in these windows.
            header.AddManipulator(new Clickable(() =>
            {
                if (_headerFoldDisabled || (_headerToggle != null && !_headerToggle.value)) return;
                IsOpen = !IsOpen; ViewChanged?.Invoke();
            }));
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
            bool enabled = _headerToggle == null || _headerToggle.value;
            bool open = IsOpen && enabled;
            _body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("zui-section--closed", !open);
            EnableInClassList("zui-section--disabled", !enabled);
            // A COLLAPSED section can hide active content (e.g. enabled modifiers). Show the suffix its
            // provider returns (a count like " (2)") on the header while closed; drop it again when open,
            // where the content itself is visible. No provider ⇒ the title is exactly the base text.
            if (_title != null)
                _title.text = _titleText + (open ? string.Empty : (_headerSuffix?.Invoke() ?? string.Empty));

            // Toggle-bar mode (HeaderFoldDisabled): the header can't reopen a closed section anymore — only
            // the bar's own button can — so a bare header for it is dead space, not an affordance. Hide the
            // WHOLE section (header included) to actually save space, which is the point of the bar (T-0065).
            // Classic mode always keeps the header visible even when closed, since clicking it IS how it
            // reopens.
            style.display = (_headerFoldDisabled && !IsOpen) ? DisplayStyle.None : DisplayStyle.Flex;
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

        /// T-0200 — update the header's tooltip (and its help-icon, when the section was built with one) AFTER
        /// construction, for a section whose true state — "no lights, so every layer renders unlit" is the
        /// worked case — can only be known once the caller has looked at live data the constructor never saw.
        /// The title's own tooltip is included so a mouse-over of either the icon-less text or the "?" reads
        /// the same sentence. A section built with no tooltip (nothing to update) leaves the header silently as
        /// it was — SetTooltip does not itself grow a help icon that was never there.
        public void SetTooltip(string tooltip)
        {
            _header.tooltip = tooltip;
            _title.tooltip = tooltip;
            if (_help != null) _help.tooltip = tooltip;
        }

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
                // Do not let a click on the checkbox fold the section (see ZuiBox's gear StopPropagation).
                _headerToggle.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                _headerToggle.RegisterValueChangedCallback(e =>
                {
                    Apply();
                    _headerToggleChanged?.Invoke(e.newValue);
                });

                int idx = _header.IndexOf(_title);   // insert just to the LEFT of the title (after the caret)
                if (idx < 0) idx = _header.childCount;
                _header.Insert(idx, _headerToggle);
            }
            else if (!string.IsNullOrEmpty(tooltip))
            {
                _headerToggle.tooltip = tooltip;
            }

            _headerToggle.SetValueWithoutNotify(value);
            Apply();
        }

        /// Refresh the header checkbox's value from outside WITHOUT firing onChanged (e.g. after an undo or
        /// an external state change). No-op if SetHeaderToggle was never called.
        public void SetHeaderToggleWithoutNotify(bool value)
        {
            _headerToggle?.SetValueWithoutNotify(value);
            Apply();
        }
    }
}

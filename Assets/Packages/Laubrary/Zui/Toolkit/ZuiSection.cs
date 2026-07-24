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
        readonly Label _caret;
        readonly string _key;

        // ── optional header checkbox (created lazily by SetHeaderToggle) ──
        readonly VisualElement _header;   // the clickable header row, so the checkbox can be inserted into it
        readonly Label _title;            // the title label, so the checkbox lands just to its LEFT
        Toggle _headerToggle;
        Action<bool> _headerToggleChanged;

        /// Children go into the body, not next to the header.
        public override VisualElement contentContainer => _body;

        public bool IsOpen
        {
            get => !s_open.TryGetValue(_key, out bool open) || open;   // default: open
            set { s_open[_key] = value; Apply(); }
        }

        public ZuiSection(string title, string tooltip, string stateKey = null)
        {
            // Title alone collides — "Gradient" heads three different blocks in Pyre — so the tooltip,
            // which is what actually distinguishes them, is part of the key.
            _key = stateKey ?? (title ?? "section") + "" + (tooltip ?? string.Empty);
            AddToClassList("zui-section");

            var header = new VisualElement();
            header.AddToClassList("zui-section__header");
            header.tooltip = tooltip;
            _header = header;

            _caret = new Label("▾");
            _caret.AddToClassList("zui-section__caret");
            _caret.pickingMode = PickingMode.Ignore;
            header.Add(_caret);

            var text = new Label(title) { tooltip = tooltip };
            text.AddToClassList("zui-section__title");
            text.pickingMode = PickingMode.Ignore;   // the whole header row is the hit target
            header.Add(text);
            _title = text;

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

            Apply();
        }

        void Apply()
        {
            bool open = IsOpen;
            _caret.text = open ? "▾" : "▸";
            _body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("zui-section--closed", !open);
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

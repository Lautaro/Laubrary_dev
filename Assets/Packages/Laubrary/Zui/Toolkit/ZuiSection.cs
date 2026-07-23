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

            _caret = new Label("▾");
            _caret.AddToClassList("zui-section__caret");
            _caret.pickingMode = PickingMode.Ignore;
            header.Add(_caret);

            var text = new Label(title) { tooltip = tooltip };
            text.AddToClassList("zui-section__title");
            text.pickingMode = PickingMode.Ignore;   // the whole header row is the hit target
            header.Add(text);

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
    }
}

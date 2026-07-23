// ZuiBox — the framed block that Z.Box returns. A titled box owns its content outright (unlike a bare
// heading, which has to work out its own extent), so clicking its title row folds it away and leaves
// just the title behind.
//
// Children added to a ZuiBox land in its BODY (contentContainer is overridden), so `box.Add(..)` after
// construction still puts the control inside the box, below the title, exactly as before.
//
// An untitled box has nothing to click and stays a plain frame.
//
// Fold state is static and keyed by title+tooltip so it survives the window rebuilds every dial edit
// triggers — same reasoning as ZuiSection.
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiBox : VisualElement
    {
        static readonly Dictionary<string, bool> s_open = new();

        readonly VisualElement _body;
        readonly Label _caret;
        readonly string _key;

        public override VisualElement contentContainer => _body;

        public bool IsOpen
        {
            get => _key == null || !s_open.TryGetValue(_key, out bool open) || open;   // default: open
            set { if (_key != null) { s_open[_key] = value; Apply(); } }
        }

        /// `stateKey` distinguishes boxes that share a title — several identical "Matte" boxes down a layer
        /// list would otherwise fold and unfold together, since fold state is keyed by what the box says.
        public ZuiBox(string title, string tooltip, string stateKey = null)
        {
            AddToClassList("zui-box");

            _body = new VisualElement();
            _body.AddToClassList("zui-box__body");

            if (!string.IsNullOrEmpty(title))
            {
                _key = stateKey ?? title + "" + (tooltip ?? string.Empty);

                var titleRow = new VisualElement();
                titleRow.AddToClassList("zui-box__titlerow");
                if (!string.IsNullOrEmpty(tooltip)) titleRow.tooltip = tooltip;

                _caret = new Label("▾");
                _caret.AddToClassList("zui-box__caret");
                _caret.pickingMode = PickingMode.Ignore;
                titleRow.Add(_caret);

                var t = new Label(title) { pickingMode = PickingMode.Ignore };   // the row is the target
                t.AddToClassList("zui-box__title");
                if (!string.IsNullOrEmpty(tooltip)) t.tooltip = tooltip;
                titleRow.Add(t);

                if (!string.IsNullOrEmpty(tooltip))
                {
                    var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
                    spacer.style.flexGrow = 1f;
                    titleRow.Add(spacer);
                    var help = Z.HelpIcon(tooltip);
                    help.pickingMode = PickingMode.Ignore;
                    titleRow.Add(help);
                }

                titleRow.AddManipulator(new Clickable(() => IsOpen = !IsOpen));
                hierarchy.Add(titleRow);
            }

            hierarchy.Add(_body);
            Apply();
        }

        void Apply()
        {
            if (_caret == null) return;
            bool open = IsOpen;
            _caret.text = open ? "▾" : "▸";
            _body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("zui-box--closed", !open);
        }
    }
}

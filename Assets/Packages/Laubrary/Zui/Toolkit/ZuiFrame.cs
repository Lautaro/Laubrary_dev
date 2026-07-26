// ZuiFrame — a titled, bordered container that does NOT fold. The plain "titled frame" primitive that sat
// between the three folding containers ZUI already had (ZuiBox folds from its title, ZuiSection collapses,
// ZuiFoldCard folds a card body) and a bare heading (Z.Text Section, which owns nothing and draws no border).
//
// A frame's job is to wrap ONE self-contained control (a 3D-orientation gizmo, a mini editor) so it reads as
// a single labelled unit with a clear header and a border around it — always visible, never collapsing, so a
// small always-on control never hides behind an accidental header click the way a foldable box would.
//
// Children added to a ZuiFrame land in its BODY (contentContainer is overridden), so `frame.Add(..)` puts the
// control inside the frame, below the title, exactly like the folding containers. The tooltip renders as a
// "?" hover icon on the title row (help sits on the header — ui-layout-rules), never below the content.
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiFrame : VisualElement
    {
        readonly VisualElement _body;

        /// Children go into the body, not next to the header.
        public override VisualElement contentContainer => _body;

        public ZuiFrame(string title, string tooltip, string icon = null)
        {
            AddToClassList("zui-frame");

            if (!string.IsNullOrEmpty(title))
            {
                var header = new VisualElement();
                header.AddToClassList("zui-frame__header");
                if (!string.IsNullOrEmpty(tooltip)) header.tooltip = tooltip;

                // Optional leading icon before the title (same neutral tint as a box icon).
                var iconEl = Z.Icon(icon, 13f);
                if (iconEl != null)
                {
                    iconEl.AddToClassList("zui-frame__icon");
                    header.Add(iconEl);
                }

                var t = new Label(title);
                t.AddToClassList("zui-frame__title");
                if (!string.IsNullOrEmpty(tooltip)) t.tooltip = tooltip;
                header.Add(t);

                if (!string.IsNullOrEmpty(tooltip))
                {
                    var spacer = new VisualElement();
                    spacer.style.flexGrow = 1f;
                    header.Add(spacer);
                    header.Add(Z.HelpIcon(tooltip));
                }
                hierarchy.Add(header);
            }

            _body = new VisualElement();
            _body.AddToClassList("zui-frame__body");
            hierarchy.Add(_body);

            ZuiLabelAlign.Align(this);   // line up this frame's field labels into one tidy column
        }
    }
}

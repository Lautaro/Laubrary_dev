// ZuiBreadcrumb — a horizontal strip of clickable path segments ("Layer Name › Bag 2 › Bag 2.1"), the
// control a nested-node editor uses so drilling into a bag costs nothing in screen space instead of
// growing an indented tree. Built for the Shaper UI design doc's §B3 (T-0126) and its first consumer,
// the Shaper mock editor (T-0130) — but lives here, a peer of ZuiSection/ZuiBox, because the design doc
// itself names Cartographer's tile groups and Zoetrope's nested Zoe composition as plausible future
// consumers of the exact same "you're inside something, get back out" need.
//
// The LAST segment is where you are now — non-interactive, since clicking it would do nothing. Every
// other segment is a link back up. A segment truncates with an ellipsis (and always carries its full text
// as a tooltip) rather than ever wrapping the strip onto a second line.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public sealed class ZuiBreadcrumb : VisualElement
    {
        // An interior segment (not the current one) is capped so a single long name can't starve every
        // other segment of the row; the CURRENT segment is what the user is actually looking at, so it is
        // left free to claim whatever the strip has left (and still truncates via ellipsis if that isn't
        // enough — see the label's own overflow settings below).
        const float InteriorSegmentMaxWidth = 140f;

        readonly List<string> _segments = new List<string>();
        Action<int> _onSegmentClicked;

        public ZuiBreadcrumb()
        {
            AddToClassList("zui-breadcrumb");
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.NoWrap;
            style.overflow = Overflow.Hidden;
            style.alignItems = Align.Center;
        }

        /// Replace the whole path and redraw. `onSegmentClicked(index)` fires when a non-current segment
        /// is clicked — the caller pops the breadcrumb to that depth and rebuilds whatever it scopes.
        public void SetPath(IReadOnlyList<string> segments, Action<int> onSegmentClicked)
        {
            _segments.Clear();
            if (segments != null) _segments.AddRange(segments);
            _onSegmentClicked = onSegmentClicked;
            Rebuild();
        }

        void Rebuild()
        {
            Clear();
            for (int i = 0; i < _segments.Count; i++)
            {
                int index = i;
                bool last = i == _segments.Count - 1;
                string text = _segments[i] ?? string.Empty;

                var seg = new Label(text) { tooltip = text };
                seg.AddToClassList("zui-breadcrumb__segment");
                seg.style.overflow = Overflow.Hidden;
                seg.style.textOverflow = TextOverflow.Ellipsis;
                seg.style.whiteSpace = WhiteSpace.NoWrap;
                seg.style.flexShrink = 1f;
                seg.style.minWidth = 24f;

                if (last)
                {
                    seg.AddToClassList("zui-breadcrumb__segment--current");
                    seg.pickingMode = PickingMode.Ignore;   // you are already here — nothing to click
                }
                else
                {
                    seg.style.maxWidth = InteriorSegmentMaxWidth;
                    seg.AddToClassList("zui-breadcrumb__segment--link");
                    seg.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0) return;
                        _onSegmentClicked?.Invoke(index);
                        e.StopPropagation();
                    });
                }
                Add(seg);

                if (!last)
                {
                    var sep = new Label("›") { pickingMode = PickingMode.Ignore };   // ›
                    sep.AddToClassList("zui-breadcrumb__sep");
                    sep.style.flexShrink = 0f;
                    Add(sep);
                }
            }
        }
    }
}

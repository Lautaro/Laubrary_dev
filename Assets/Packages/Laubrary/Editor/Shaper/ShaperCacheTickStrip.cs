// ShaperCacheTickStrip — a thin, painted readout of which frames are resident in the preview frame cache
// (T-0165), shown under the transport's scrub slider. Pyre shows the same idea as part of its own frame
// cache UI (per the parity inventory: "fill readout" in the transport).
//
// This is a genuinely bespoke small painter, not a control -- it never receives input (pickingMode.Ignore)
// and has no ZUI equivalent to reach for first (ZUI's slider has no tick-mark support today; see the
// ui-layout-rules "sanctioned raw islands" note for the general shape of this exception). Painted rather
// than one child element per frame because a document can carry up to 120 frames (ShaperWindow.cs Frames
// dial) and 120 laid-out elements rebuilt on every cache Progressed tick would be real churn for something
// that repaints many times a second while the pre-baker runs.
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    internal sealed class ShaperCacheTickStrip : VisualElement
    {
        readonly System.Func<int> _total;
        readonly System.Func<int, bool> _isCached;
        readonly System.Func<int> _current;

        static readonly Color CachedColor = new Color(0.36f, 0.74f, 0.42f, 0.9f);
        static readonly Color UncachedColor = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color CurrentColor = new Color(1f, 0.84f, 0.22f, 1f);

        public ShaperCacheTickStrip(System.Func<int> total, System.Func<int, bool> isCached, System.Func<int> current)
        {
            _total = total;
            _isCached = isCached;
            _current = current;

            pickingMode = PickingMode.Ignore;
            style.height = 6f;
            style.flexGrow = 1f;
            tooltip = "Which frames of this document already have their picture cached. Green = cached, "
                    + "yellow = the frame on screen, dim = not cached yet — the background pre-baker fills "
                    + "these in while you are idle.";

            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Repaint without a layout pass -- cache state changes far more often than geometry does.</summary>
        public void Refresh() => MarkDirtyRepaint();

        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            int total = Mathf.Max(1, _total());
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h)) return;

            int current = _current();
            float tickW = w / total;
            var painter = mgc.painter2D;

            for (int i = 0; i < total; i++)
            {
                bool cached = _isCached(i);
                bool isCurrent = i == current;
                painter.fillColor = isCurrent ? CurrentColor : (cached ? CachedColor : UncachedColor);

                float x0 = i * tickW;
                float x1 = x0 + Mathf.Max(1f, tickW - 1f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x0, 0f));
                painter.LineTo(new Vector2(x1, 0f));
                painter.LineTo(new Vector2(x1, h));
                painter.LineTo(new Vector2(x0, h));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}

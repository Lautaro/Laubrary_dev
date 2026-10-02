using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// The wheel-zoom + middle-drag-pan suite for canvas-style elements, extracted once it was wanted by
    /// its fifth view (Tileset Builder's SheetStage grew the original; Cartographer's LevelCanvas is the
    /// first consumer of the extraction). One instance rides a HOST element and owns the view maths:
    ///
    /// - FIT-SCALE for a content size in the host's pane (Zoom 1 = the whole content fits).
    /// - A zoom factor clamped 1..MaxZoom whose wheel gesture zooms TOWARD the pointer — the content
    ///   point under the cursor stays under the cursor, so zooming dives into what you are looking at.
    /// - PAN with edge clamping and WRITE-BACK: the content's edge never detaches from the pane's edge
    ///   by more than Margin, and the clamped value is written back so it can never accumulate
    ///   off-screen; any axis the content fits zeroes its pan (SheetStage's clamp discipline).
    /// - The DEGENERATE-GEOMETRY GUARD: Layout refuses to run against un-laid-out geometry (NaN or
    ///   sub-20px panes), because the clamp writes back into Pan and running it then would destroy the
    ///   user's view position — the "canvas snaps somewhere else after a drop" bug, paid for once.
    /// - MIDDLE-BUTTON drag panning measured in PANEL space, because local coordinates shift under the
    ///   element as it pans.
    /// - Optional EDGE GUTTERS (`InsetLeft/Top/Right/Bottom`) the view stays out of, so a host can draw
    ///   chrome — a ruler strip — beside the content instead of over it.
    ///
    /// This is the TRANSFORM-STYLE mode: the host owns absolutely-positioned content and, after every
    /// successful Layout, places it at Origin and scales it by Scale (both exposed for the host's own
    /// coordinate math — hit-testing is `(local - Origin) / Scale`). The second mode — driving enclosing
    /// ScrollViews' scrollOffset while zoom changes the CELL SIZE — is `ZuiScrollPan`, below in this same
    /// file: same gestures, different maths, deliberately not forked into a copy.
    public sealed class ZuiPanZoom
    {
        readonly VisualElement host;

        /// Zoom factor over the fit scale: 1 = content fit to the pane, up to MaxZoom. Clamped on use,
        /// so the host may persist and restore it without validating.
        public float Zoom = 1f;

        /// Hard ceiling for Zoom. 16 is the proven SheetStage range.
        public float MaxZoom = 16f;

        /// View offset in host pixels while zoomed past fit. Layout clamps it and writes it back.
        public Vector2 Pan;

        /// Breathing room the fit leaves at the pane's edges, and how far the clamp lets a zoomed
        /// content edge detach from the pane edge.
        public float Margin = 4f;

        /// GUTTERS the view must stay out of, in host pixels — space the host reserves at the pane's
        /// edges for chrome it draws itself. Cartographer's RULER STRIPS are why this exists: chrome
        /// painted OVER the content would hide the very cells it is measuring, and a ruler that covers
        /// the top row of a level is worse than no ruler. With an inset set, the fit, the centring, the
        /// pan clamp and the zoom-toward-pointer maths all work inside what is left, so Origin already
        /// accounts for the gutter and the host's hit-testing needs no second correction. All zero (the
        /// default) is the plain whole-pane behaviour every other consumer gets.
        public float InsetLeft, InsetTop, InsetRight, InsetBottom;

        /// The pane the view actually lives in: the host's content box less the reserved gutters. ONE
        /// definition, used by both Layout and the wheel — they must agree about where the centre is or
        /// zooming would walk the content sideways.
        Rect Viewport()
        {
            var r = host.contentRect;
            return new Rect(r.x + InsetLeft, r.y + InsetTop,
                r.width - InsetLeft - InsetRight, r.height - InsetTop - InsetBottom);
        }

        /// Resolved pixel scale after the last successful Layout: fit × clamped Zoom. Multiply content
        /// coordinates by this for the host's own hit-testing and overlay drawing.
        public float Scale { get; private set; } = 1f;

        /// Content's top-left corner in host-local pixels after the last successful Layout. Subtract it
        /// (then divide by Scale) to turn a pointer position into a content coordinate.
        public Vector2 Origin { get; private set; }

        /// True while a middle-drag pan is in flight — hosts usually suppress hover feedback then.
        public bool IsPanning { get; private set; }

        /// Fired after a wheel or pan gesture changed the view — re-run the host's layout pass in it.
        public event System.Action ViewChanged;

        Vector2 contentSize;   // unscaled content pixels, remembered from Layout for the wheel maths
        Vector3 panLast;       // panel-space, not local — local shifts under the element mid-pan

        public ZuiPanZoom(VisualElement host)
        {
            this.host = host;
            host.RegisterCallback<WheelEvent>(OnWheel);
            host.RegisterCallback<PointerDownEvent>(OnDown);
            host.RegisterCallback<PointerMoveEvent>(OnMove);
            host.RegisterCallback<PointerUpEvent>(OnUp);
        }

        /// Resolve Scale and Origin for `contentSizePx` (the content's UNSCALED pixel size) inside the
        /// host's current geometry. Returns false — leaving the previous view untouched — while the host
        /// has no real layout yet or the content size is empty; the host should simply skip positioning
        /// then and try again on its next GeometryChangedEvent.
        public bool Layout(Vector2 contentSizePx)
        {
            contentSize = contentSizePx;
            var rect = Viewport();
            if (float.IsNaN(rect.width) || rect.width < 20f || rect.height < 20f) return false;
            if (contentSizePx.x <= 0f || contentSizePx.y <= 0f) return false;

            float availW = Mathf.Max(1f, rect.width - 2f * Margin);
            float availH = Mathf.Max(1f, rect.height - 2f * Margin);
            float fit = Mathf.Min(availW / contentSizePx.x, availH / contentSizePx.y);
            Scale = fit * Mathf.Clamp(Zoom, 1f, MaxZoom);
            float pw = contentSizePx.x * Scale, ph = contentSizePx.y * Scale;

            // Centered at fit; once a side overflows the pane, the pan takes that axis over — clamped so
            // the content edge never detaches from the pane edge, and written back so it cannot
            // accumulate off-screen. Every term is in HOST-local pixels (rect.x/rect.y carry the gutter),
            // so Origin lands where the host can place content without adding the inset back itself.
            float cx = rect.x + (rect.width - pw) * 0.5f, cy = rect.y + (rect.height - ph) * 0.5f;
            float ox = cx, oy = cy;
            if (pw > rect.width)
            {
                ox = Mathf.Clamp(cx + Pan.x, rect.xMax - pw - Margin, rect.x + Margin);
                Pan.x = ox - cx;
            }
            else Pan.x = 0f;
            if (ph > rect.height)
            {
                oy = Mathf.Clamp(cy + Pan.y, rect.yMax - ph - Margin, rect.y + Margin);
                Pan.y = oy - cy;
            }
            else Pan.y = 0f;
            Origin = new Vector2(ox, oy);
            return true;
        }

        /// Wheel zooms toward the pointer: the content point under the cursor stays under the cursor.
        void OnWheel(WheelEvent e)
        {
            if (contentSize.x <= 0f || contentSize.y <= 0f) return;
            float old = Mathf.Clamp(Zoom, 1f, MaxZoom);
            float zoom = Mathf.Clamp(old * (e.delta.y < 0 ? 1.25f : 0.8f), 1f, MaxZoom);
            if (!Mathf.Approximately(zoom, old))
            {
                Vector2 p = e.localMousePosition;
                Vector2 point = (p - Origin) / Mathf.Max(0.0001f, Scale);
                float ns = Scale / old * zoom;
                float pw = contentSize.x * ns, ph = contentSize.y * ns;
                var rect = Viewport();
                var center = new Vector2(rect.x + (rect.width - pw) * 0.5f, rect.y + (rect.height - ph) * 0.5f);
                Pan = p - point * ns - center;
                Zoom = zoom;
                ViewChanged?.Invoke();
            }
            e.StopPropagation();
        }

        void OnDown(PointerDownEvent e)
        {
            // Middle button only — the canvas has no scrollbars, this IS its scrolling. Every other
            // button belongs to the host's own gestures.
            if (e.button != 2 || contentSize.x <= 0f) return;
            IsPanning = true;
            panLast = e.position;
            host.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!IsPanning) return;
            var d = (Vector2)(e.position - panLast);
            panLast = e.position;
            Pan += d;
            ViewChanged?.Invoke();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!IsPanning || e.button != 2) return;
            IsPanning = false;
            host.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }
    }

    /// ZuiPanZoom's SCROLLVIEW-OFFSET MODE — the same two gestures for a canvas that does NOT fit-scale.
    ///
    /// The distinction is real, not cosmetic. `ZuiPanZoom` proper owns the whole view: it computes a fit
    /// scale, places absolutely-positioned content at an Origin and multiplies it by a Scale. A grid
    /// editor works the other way round: its cells have an AUTHORED pixel size that the user dials, the
    /// content is laid out at that size, and the "view" is simply which part of it the enclosing
    /// ScrollViews are showing. Forcing that through the transform model would replace a crisp
    /// point-sampled grid with a scaled bitmap, so it gets its own entry point instead.
    ///
    /// What it provides:
    /// - MIDDLE-DRAG panning, measured in PANEL space (local coordinates shift under the element as it
    ///   scrolls) and pushed into EVERY enclosing ScrollView — each clamps its own axis, so driving both
    ///   axes at every level is safe and one gesture moves a grid nested in a horizontal scroller inside
    ///   a vertical pane.
    /// - CTRL+WHEEL zoom, handed to the caller as a signed step so the caller can apply it to whatever
    ///   "zoom" means for it (a cell size, a row height). Plain wheel is deliberately left alone — it
    ///   still scrolls the pane, which is what a wheel does everywhere else in the editor.
    ///
    /// `IsPanning` is the flag a host checks to mute hover feedback mid-gesture, exactly as on ZuiPanZoom.
    public sealed class ZuiScrollPan
    {
        readonly VisualElement host;
        readonly System.Action<float> onZoomStep;
        Vector3 last;   // panel-space, not local — local shifts under the element mid-pan

        /// True while a middle-drag pan is in flight.
        public bool IsPanning { get; private set; }

        ZuiScrollPan(VisualElement host, System.Action<float> onZoomStep)
        {
            this.host = host;
            this.onZoomStep = onZoomStep;
        }

        /// Ride `host`. `onZoomStep` is optional: it receives +1 for a zoom-in notch and −1 for a
        /// zoom-out one on Ctrl+wheel; pass null for pan-only.
        public static ZuiScrollPan Attach(VisualElement host, System.Action<float> onZoomStep = null)
        {
            var p = new ZuiScrollPan(host, onZoomStep);
            host.RegisterCallback<PointerDownEvent>(p.OnDown);
            host.RegisterCallback<PointerMoveEvent>(p.OnMove);
            host.RegisterCallback<PointerUpEvent>(p.OnUp);
            if (onZoomStep != null) host.RegisterCallback<WheelEvent>(p.OnWheel);
            return p;
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 2) return;
            IsPanning = true;
            last = e.position;
            host.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!IsPanning) return;
            // Content follows the pointer; each enclosing scroller clamps its own axis, so pushing both
            // axes at every level is safe.
            var d = (Vector2)(e.position - last);
            last = e.position;
            for (var sv = host.GetFirstAncestorOfType<ScrollView>(); sv != null;
                 sv = sv.GetFirstAncestorOfType<ScrollView>())
                sv.scrollOffset -= d;
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!IsPanning || e.button != 2) return;
            IsPanning = false;
            host.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        /// Ctrl+wheel only — a bare wheel keeps scrolling the pane, the way it does everywhere else.
        void OnWheel(WheelEvent e)
        {
            if (!e.ctrlKey) return;
            onZoomStep(e.delta.y < 0 ? 1f : -1f);
            e.StopPropagation();
        }
    }
}

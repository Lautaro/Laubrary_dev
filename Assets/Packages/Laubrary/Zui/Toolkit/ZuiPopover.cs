// ZuiPopover — the UI Toolkit half's floating-panel primitive: a ZUI-styled card, anchored to a trigger
// element, added into the host window's overlay layer, that positions itself relative to the anchor and
// flips/clamps to stay on-screen, dismisses on an outside click or Esc, and hosts arbitrary ZUI controls
// built by a `build(panel)` callback. It is the retained-mode counterpart of the IMGUI ZUI.Popover
// (a PopupWindowContent) — but unlike a real popup WINDOW it lives inside the same panel as the tool, so
// its content is styled by ZuiToolkit.uss for free and it can overlap the tool's own chrome.
//
// Why an in-panel overlay rather than a PopupWindowContent: a UITK EditorWindow draws its whole UI in one
// panel, and a PopupWindow opens a SEPARATE OS-level window that gets no ZUI stylesheet, can't share the
// window's element tree, and clips to its own fixed GetWindowSize. An in-panel overlay inherits the tool's
// palette, can be measured and re-placed against the real window bounds, and is torn down the instant the
// window rebuilds (it is parented to the window's own zui-root, which Rebuild() clears).
//
// ZuiMenu builds the richer GenericMenu stand-in on top of this; callers wanting a bespoke flyout (a hover
// preview, a settings panel) use Z.Popover(anchor, build, opts) directly.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public sealed class ZuiPopover
    {
        /// Which side of the anchor the panel prefers — it flips to the other side when the preferred one
        /// would push the panel off the window, and clamps within the window if neither side fully fits.
        public enum Side { Below, Above }

        public sealed class Options
        {
            /// Panel minimum width (0 = size to content). A menu sets this so short items don't make a
            /// sliver; a wide flyout leaves it 0 and its content dictates the width.
            public float minWidth = 0f;
            /// Panel maximum width (0 = unconstrained).
            public float maxWidth = 0f;
            /// Gap between the anchor's edge and the panel.
            public float gap = 2f;
            /// Keep the panel at least this far from the window edges when clamping.
            public float edgeMargin = 6f;
            public bool dismissOnOutsideClick = true;
            public bool dismissOnEsc = true;
            public Side preferredSide = Side.Below;
            /// Raised once, after the popover is dismissed (outside click, Esc, or Close()).
            public Action onClosed;
        }

        readonly VisualElement _anchor;
        readonly Options _opt;
        readonly VisualElement _host;      // overlay host: the outermost zui-root, else the panel root
        VisualElement _scrim;              // full-window transparent catcher; the panel floats inside it
        VisualElement _panel;              // the card the caller fills
        Vector2 _lastPlaced = new Vector2(float.NaN, float.NaN);
        bool _closed;

        /// The card element the caller's `build` filled — add more content to it and the popover re-places.
        public VisualElement Panel => _panel;
        public bool IsOpen => !_closed && _scrim != null && _scrim.parent != null;

        ZuiPopover(VisualElement anchor, Options opt)
        {
            _anchor = anchor;
            _opt = opt ?? new Options();
            _host = FindHost(anchor);
        }

        /// Show a floating panel anchored to <paramref name="anchor"/>. <paramref name="build"/> fills the
        /// card; the returned handle's Close() dismisses it (also stored so a caller can close it early).
        public static ZuiPopover Show(VisualElement anchor, Action<VisualElement> build, Options options = null)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            var pop = new ZuiPopover(anchor, options);
            pop.Build(build);
            return pop;
        }

        void Build(Action<VisualElement> build)
        {
            _scrim = new VisualElement { name = "zui-popover-scrim" };
            _scrim.AddToClassList("zui-popover__scrim");
            // Always resolve the palette + USS even when the host is a bare panel root (a non-ZuiWindow),
            // and force the scrim's own padding to 0 so the absolutely-positioned panel places in a clean
            // coordinate space (an inherited zui-root padding would offset every WorldToLocal result).
            if (Z.Sheet != null && !_scrim.styleSheets.Contains(Z.Sheet)) _scrim.styleSheets.Add(Z.Sheet);
            _scrim.AddToClassList("zui-root");
            _scrim.style.position = Position.Absolute;
            _scrim.style.left = 0; _scrim.style.top = 0; _scrim.style.right = 0; _scrim.style.bottom = 0;
            _scrim.style.paddingLeft = 0; _scrim.style.paddingRight = 0;
            _scrim.style.paddingTop = 0; _scrim.style.paddingBottom = 0;

            _panel = new VisualElement { name = "zui-popover" };
            _panel.AddToClassList("zui-popover");
            _panel.style.position = Position.Absolute;
            if (_opt.minWidth > 0f) _panel.style.minWidth = _opt.minWidth;
            if (_opt.maxWidth > 0f) _panel.style.maxWidth = _opt.maxWidth;
            // Hidden until the first layout so it never flashes at (0,0) before it is placed.
            _panel.style.visibility = Visibility.Hidden;

            build?.Invoke(_panel);
            _scrim.Add(_panel);

            if (_opt.dismissOnOutsideClick)
                _scrim.RegisterCallback<PointerDownEvent>(OnScrimPointerDown);
            if (_opt.dismissOnEsc)
            {
                _scrim.focusable = true;
                _scrim.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            }

            _host.Add(_scrim);

            // Place once the panel has a resolved size, and re-place on any later size change (content added,
            // a sub-section folded open). The equality guard below stops the position-triggered relayout from
            // looping.
            _panel.RegisterCallback<GeometryChangedEvent>(_ => Place());
            if (_opt.dismissOnEsc) _scrim.schedule.Execute(() => { if (!_closed) _scrim?.Focus(); });
        }

        void OnScrimPointerDown(PointerDownEvent e)
        {
            // Only a click on the scrim ITSELF (outside the panel) dismisses — a click on the panel or any of
            // its controls targets a descendant, never the scrim.
            if (e.target == _scrim) Close();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
        }

        void Place()
        {
            if (_closed || _scrim == null || _panel == null) return;
            Rect host = _scrim.contentRect;
            if (host.width <= 0f || host.height <= 0f) return;
            float pw = _panel.resolvedStyle.width;
            float ph = _panel.resolvedStyle.height;
            if (pw <= 0f || ph <= 0f) return;

            // The anchor's rect in the scrim's local space (scrim padding is 0, so this is a clean map).
            Rect wb = _anchor.worldBound;
            Vector2 tl = _scrim.WorldToLocal(new Vector2(wb.xMin, wb.yMin));
            Vector2 br = _scrim.WorldToLocal(new Vector2(wb.xMax, wb.yMax));
            var a = new Rect(tl, br - tl);

            float m = _opt.edgeMargin, gap = _opt.gap;

            // Horizontal: left-align to the anchor, then push left / clamp so the whole panel stays in-window.
            float x = a.xMin;
            if (x + pw > host.width - m) x = host.width - m - pw;
            if (x < m) x = m;

            // Vertical: prefer the requested side, flip to the other when the preferred one overflows, then
            // clamp within the window as a last resort (a panel taller than the window sits pinned at the top).
            float below = a.yMax + gap;
            float above = a.yMin - gap - ph;
            bool belowFits = below + ph <= host.height - m;
            bool aboveFits = above >= m;
            float y = _opt.preferredSide == Side.Below
                ? (belowFits || !aboveFits ? below : above)
                : (aboveFits || !belowFits ? above : below);
            y = Mathf.Clamp(y, m, Mathf.Max(m, host.height - m - ph));

            var np = new Vector2(x, y);
            if (!(Mathf.Abs(np.x - _lastPlaced.x) < 0.5f && Mathf.Abs(np.y - _lastPlaced.y) < 0.5f))
            {
                _lastPlaced = np;
                _panel.style.left = x;
                _panel.style.top = y;
            }
            _panel.style.visibility = Visibility.Visible;
        }

        /// Dismiss the popover and raise onClosed (idempotent).
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_scrim != null && _scrim.parent != null) _scrim.RemoveFromHierarchy();
            _scrim = null; _panel = null;
            _opt.onClosed?.Invoke();
        }

        // The overlay layer: the OUTERMOST zui-root ancestor (a ZuiWindow's rootVisualElement carries the
        // "zui-root" class, so this is the window's own root — the overlay covers the whole content area, is
        // styled by the shared sheet, and is cleared when the window rebuilds). Falls back to the raw panel
        // root for a popover raised outside a zui-root (e.g. a plain inspector).
        static VisualElement FindHost(VisualElement anchor)
        {
            VisualElement outermostZuiRoot = null;
            for (var v = anchor; v != null; v = v.hierarchy.parent)
                if (v.ClassListContains("zui-root")) outermostZuiRoot = v;
            if (outermostZuiRoot != null) return outermostZuiRoot;
            return anchor.panel != null ? anchor.panel.visualTree : anchor;
        }
    }
}

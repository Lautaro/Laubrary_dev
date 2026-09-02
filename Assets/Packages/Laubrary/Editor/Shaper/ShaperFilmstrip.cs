// ShaperFilmstrip — a horizontal contact-sheet strip of every frame in the document; click a tile to jump the
// transport there (Pyre parity: "filmstrip/contact-sheet mode, click a tile to jump", pyre-inventory rows
// 100-105, T-0176).
//
// New file, own VisualElement class -- ShaperPreviewStage.cs is off-limits this task (T-0168 is concurrently
// adding a drag handle there), and the stage's ShaperPreviewFrameCache instance is private with no accessor
// for a specific frame's pixels. Rather than edit the stage to expose one, this keeps its OWN instance of the
// same cache class (ShaperPreviewFrameCache.cs, T-0165) with its own background pre-baker -- same shape as
// the main preview's cache on purpose, just a second, independently-filled one. There is no cheaper
// "thumbnail resolution" to render at: ShaperDocumentRenderer.RenderFrame always renders at the document's
// own canvas size, so each tile's source texture is full-canvas and the small on-screen tile size is a pure
// ScaleToFit downscale, costing nothing extra to compute.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    internal sealed class ShaperFilmstripElement : VisualElement
    {
        readonly Func<ShaperDocument> _doc;
        readonly Func<int> _current;
        readonly Action<int> _onJump;

        readonly ShaperPreviewFrameCache _cache = new ShaperPreviewFrameCache();
        readonly ShaperPreviewFramePrebaker _prebaker;
        readonly VisualElement _row;
        Texture2D[] _tex = Array.Empty<Texture2D>();

        const float TileSize = 40f;
        static readonly Color CurrentColor = new Color(1f, 0.84f, 0.22f, 1f);
        static readonly Color EdgeColor = new Color(0f, 0f, 0f, 0.25f);

        public ShaperFilmstripElement(Func<ShaperDocument> doc, Func<int> current, Action<int> onJump)
        {
            _doc = doc;
            _current = current;
            _onJump = onJump;
            _prebaker = new ShaperPreviewFramePrebaker(_cache, _doc);
            _prebaker.Progressed += _ => RefreshTiles();
            _prebaker.Completed += _ => RefreshTiles();

            tooltip = "Every frame of this document, in order. Click a tile to jump the transport there.";
            style.height = TileSize + 6f;
            style.flexShrink = 0f;

            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.style.flexGrow = 1f;
            _row = scroll.contentContainer;
            _row.style.flexDirection = FlexDirection.Row;
            Add(scroll);

            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        public void Dispose()
        {
            _prebaker.Stop();
            DestroyTextures();
        }

        void DestroyTextures()
        {
            for (int i = 0; i < _tex.Length; i++)
            {
                if (_tex[i] != null) UnityEngine.Object.DestroyImmediate(_tex[i]);
                _tex[i] = null;
            }
        }

        /// Rebuild the tile elements from scratch. Call when the document changes or its frame count changes
        /// -- a genuinely structural change. A pure content edit (a dial moved, a layer changed) should call
        /// <see cref="Invalidate"/> instead, which keeps the same tiles and just refills their pixels.
        public void Rebuild()
        {
            Dispose();
            _row.Clear();
            var doc = _doc?.Invoke();
            if (doc == null) { _tex = Array.Empty<Texture2D>(); return; }

            int n = Mathf.Max(1, doc.frameCount);
            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);
            _cache.EnsureShape(n, w, h);
            _tex = new Texture2D[n];

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var tile = new VisualElement { tooltip = $"Frame {idx + 1}. Click to jump the transport here." };
                tile.style.width = TileSize; tile.style.height = TileSize;
                tile.style.marginRight = 2f;
                tile.style.borderTopWidth = tile.style.borderBottomWidth = 2f;
                tile.style.borderLeftWidth = tile.style.borderRightWidth = 2f;
                tile.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                StyleCurrent(tile, false);
                tile.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    _onJump?.Invoke(idx);
                });
                _row.Add(tile);
            }
            _prebaker.Start();
            RefreshTiles();
        }

        /// Drop every cached pixel and refill in the background, WITHOUT tearing down the tile elements
        /// themselves -- called on every authored edit (mirrors ShaperPreviewStage.InvalidateFrameCache).
        /// A frame-count change is structural and goes through Rebuild instead.
        public void Invalidate()
        {
            var doc = _doc?.Invoke();
            if (doc == null) return;
            if (Mathf.Max(1, doc.frameCount) != _tex.Length) { Rebuild(); return; }

            _cache.Invalidate();
            DestroyTextures();
            for (int i = 0; i < _row.childCount; i++) _row[i].style.backgroundImage = null;
            _prebaker.Stop();
            _prebaker.Start();
            RefreshTiles();
        }

        /// Repaint existing tiles from whatever the cache already has resident, plus the current-frame
        /// highlight. Never rebuilds -- this fires many times a second while the pre-baker runs.
        public void RefreshTiles()
        {
            var doc = _doc?.Invoke();
            if (doc == null) return;
            int current = _current();
            for (int i = 0; i < _row.childCount && i < _tex.Length; i++)
            {
                StyleCurrent(_row[i], i == current);
                if (_tex[i] != null || !_cache.IsFrameCached(i)) continue;

                var px = _cache.ComputeFrame(i, doc);
                if (px == null || px.Length != _cache.width * _cache.height) continue;
                var tex = new Texture2D(_cache.width, _cache.height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                tex.SetPixels32(px);
                tex.Apply(false);
                _tex[i] = tex;
                _row[i].style.backgroundImage = Background.FromTexture2D(tex);
            }
        }

        static void StyleCurrent(VisualElement tile, bool current)
        {
            var col = current ? CurrentColor : EdgeColor;
            tile.style.borderTopColor = tile.style.borderBottomColor = col;
            tile.style.borderLeftColor = tile.style.borderRightColor = col;
        }
    }
}

// ShaperPreviewStage — the window's live preview surface.
//
// It owns NO pixel path of its own. Every pixel comes from ShaperDocumentRenderer, which is the engine's
// single document renderer (Runtime/Shaper/ShaperDocumentRenderer.cs, T-0153). That is the whole reason the
// renderer was promoted out of the baker into Runtime: preview and bake CANNOT drift, because there is one
// implementation rather than two that agree today and diverge next month.
//
// Row order is deliberately NOT flipped here. ShaperBaker.BlitFrame copies the renderer's Color32[] rows
// straight into a sheet with no flip (ShaperBaker.cs:409-417) and hands that to SetPixels32, whose index 0 is
// bottom-left. Doing the same thing here is what makes "what you previewed is what you baked" literally true
// rather than approximately true; if the orientation is ever wrong it is wrong in BOTH, which is a renderer
// bug to fix once, not a preview bug to paper over with a flip that would then hide it.
using System;
using Laubrary.BackSplash.Editor;
using Laubrary.PyreShaper;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    internal sealed class ShaperPreviewStage : VisualElement
    {
        readonly Func<ShaperDocument> _doc;
        readonly Func<int> _frame;
        readonly BackSplashElement _backdrop;
        readonly VisualElement _image;
        readonly VisualElement _frameBorder;
        Texture2D _tex;

        // ── frame cache + background pre-baker (T-0165) ─────────────────────────────────────────────────────
        // See ShaperPreviewFrameCache.cs's own header for why this is a new pixel-level cache rather than the
        // Runtime geometry cache (ShaperFrameCache/ShaperNodeCache/ShaperCachedEvaluator) the task named --
        // those cache a NODE's distance field, not the finished picture RenderFrame actually costs ~30ms for.
        readonly ShaperPreviewFrameCache _frameCache = new ShaperPreviewFrameCache();
        readonly ShaperPreviewFramePrebaker _prebaker;

        /// <summary>Fires whenever the background pre-baker makes progress OR finishes, so the window can
        /// repaint its cache tick strip / "N/M cached" readout without touching the (expensive) preview
        /// image itself.</summary>
        public event Action CacheProgressed;

        public bool IsFrameCached(int frameIndex) => _frameCache.IsFrameCached(frameIndex);
        public int CountCachedFrames() => _frameCache.CountCachedFrames();
        public int CachedFrameCount => _frameCache.frameCount;

        /// Cosmetic view state, owned by the window and pushed in — see ShaperWindow.Preview.cs for why these
        /// live on the window rather than the document.
        public bool ShowFrameBorder;
        public float Zoom = 1f;

        public ShaperPreviewStage(Func<ShaperDocument> doc, Func<int> frame)
        {
            _doc = doc;
            _frame = frame;
            _prebaker = new ShaperPreviewFramePrebaker(_frameCache, _doc);
            _prebaker.Progressed += _ => CacheProgressed?.Invoke();
            _prebaker.Completed += _ => CacheProgressed?.Invoke();

            AddToClassList("zui-stage");
            style.overflow = Overflow.Hidden;
            style.minHeight = 180f;
            tooltip = "Live render of the whole document at the current frame — every enabled layer, "
                    + "composited. This is the same renderer the bake uses, so what you see here is what "
                    + "gets baked. The backdrop and frame border are preview-only and are never baked.";

            // ── Pyre's blit order — backdrop fill, then image, then the frame on top ─────────────────────
            // Pyre draws all three in ONE IMGUI pass (PyreWindow.Preview.cs DrawBackdrop). That cannot be
            // copied literally here because this stage is retained-mode: a VisualElement paints its own
            // background BEFORE its children, so a backdrop painted onto THIS element would sit behind the
            // frame only by accident, and any later restyle of the stage would silently reorder them.
            // Instead the order is made structural — sibling children, backdrop first, frame second, border
            // last — which is the retained-mode spelling of the same blit order and cannot be reordered by
            // styling.
            //
            // This is UI TOOLKIT throughout. It briefly was not: the backdrop was an IMGUIContainer running
            // BackSplashPainter, the only IMGUI left in an otherwise pure-UITK tool, imported purely to reuse
            // a shared painter whose API is Draw(Rect,...). The right fix was neither to keep the island nor
            // to re-implement the backdrop privately here (which would have been the FOURTH copy of those
            // twenty lines, and BackSplashPainter exists because the third one silently stopped reading
            // imageZoom/imagePos) — it was to add the UITK spelling to the SHARED module. That is
            // BackSplashElement, beside the painter, reading the same BackSplashSettings.
            //
            // The frame keeps its transparency, which is what makes the backdrop visible at all: the layer
            // walk clears its accumulator with Array.Clear and Encode writes STRAIGHT alpha, so every sample
            // outside the shape is genuinely alpha 0 rather than an opaque background colour.
            _backdrop = new BackSplashElement(new Color(0.08f, 0.08f, 0.10f));
            _backdrop.StretchToParentSize();
            Add(_backdrop);

            _image = new VisualElement { pickingMode = PickingMode.Ignore };
            _image.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            _image.StretchToParentSize();
            Add(_image);

            // The canvas-edge outline, as a styled border rather than four EditorGUI.DrawRect calls. Absolute
            // and pointer-transparent, positioned onto the fitted canvas rect whenever the stage resizes —
            // the border has to follow the PICTURE, not the panel, or it stops meaning "this is the canvas
            // edge". Cosmetic: no renderer or baker path can see it.
            _frameBorder = new VisualElement { pickingMode = PickingMode.Ignore };
            _frameBorder.style.position = Position.Absolute;
            _frameBorder.style.display = DisplayStyle.None;
            var edge = new Color(1f, 1f, 1f, 0.28f);
            _frameBorder.style.borderTopWidth = _frameBorder.style.borderBottomWidth = 1f;
            _frameBorder.style.borderLeftWidth = _frameBorder.style.borderRightWidth = 1f;
            _frameBorder.style.borderTopColor = _frameBorder.style.borderBottomColor = edge;
            _frameBorder.style.borderLeftColor = _frameBorder.style.borderRightColor = edge;
            Add(_frameBorder);

            RegisterCallback<GeometryChangedEvent>(_ => LayoutFrameBorder());

            // A Texture2D is an unmanaged Unity object; a window rebuild drops this element and would leak it.
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            Refresh();
        }

        /// Places the canvas-edge outline onto the fitted canvas rect. Called on every geometry change and
        /// whenever the toggle or the document changes, because the fitted rect depends on the stage size and
        /// on the document's own aspect.
        void LayoutFrameBorder()
        {
            var doc = _doc?.Invoke();
            if (!ShowFrameBorder || doc == null)
            {
                _frameBorder.style.display = DisplayStyle.None;
                return;
            }

            float vw = resolvedStyle.width, vh = resolvedStyle.height;
            if (float.IsNaN(vw) || float.IsNaN(vh) || vw <= 0f || vh <= 0f) return;

            var r = FittedCanvasRect(new Rect(0f, 0f, vw, vh), doc);
            _frameBorder.style.display = DisplayStyle.Flex;
            _frameBorder.style.left = r.x;
            _frameBorder.style.top = r.y;
            _frameBorder.style.width = r.width;
            _frameBorder.style.height = r.height;
        }

        /// Where the canvas actually lands inside `view` under ScaleToFit + Zoom — the border has to follow
        /// the picture, not the panel, or it stops meaning "this is the canvas edge".
        static Rect FittedCanvasRect(Rect view, ShaperDocument doc)
        {
            float cw = Mathf.Max(1, doc.canvasWidth), ch = Mathf.Max(1, doc.canvasHeight);
            float scale = Mathf.Min(view.width / cw, view.height / ch);
            float w = cw * scale, h = ch * scale;
            return new Rect(view.x + (view.width - w) * 0.5f, view.y + (view.height - h) * 0.5f, w, h);
        }

        public void Dispose()
        {
            _prebaker.Stop();
            if (_tex == null) return;
            UnityEngine.Object.DestroyImmediate(_tex);
            _tex = null;
        }

        /// <summary>Drop every cached frame's pixels and start the background pre-baker filling them back
        /// in. Called by the window on every authored edit (never on a pure view change like scrubbing,
        /// zoom or the backdrop — see ShaperWindow.cs's Change/Val, which is where this is hooked).</summary>
        public void InvalidateFrameCache()
        {
            _frameCache.Invalidate();
            CacheProgressed?.Invoke();
            StartPrebakeIfNeeded();
        }

        void StartPrebakeIfNeeded()
        {
            var doc = _doc?.Invoke();
            if (doc != null && doc.frameCount > 1) _prebaker.Start();
        }

        public void Refresh()
        {
            var doc = _doc?.Invoke();
            // Retained-mode: the backdrop is a real element, so it is pointed at the current settings rather
            // than being told to repaint. (As an IMGUIContainer this needed a manual MarkDirtyRepaint — one
            // of the small frictions that came with the island.)
            _backdrop.SetSettings(doc?.previewBackSplash);
            LayoutFrameBorder();

            if (doc == null)
            {
                _image.style.backgroundImage = null;
                return;
            }

            // A BLANK CHERRY BEAT shows nothing — not frame 0. ShaperCherry.BlankFrame (-1) is a real
            // authored state: it is what plays during the loop delay, and what an empty cherry sequence
            // renders as. Falling back to frame 0 would silently turn a deliberate gap into a held frame,
            // and the gap is the whole point of authoring one. The backdrop stays visible underneath,
            // which is exactly what a gap should look like.
            int frame = _frame?.Invoke() ?? 0;
            if (frame < 0)
            {
                _image.style.backgroundImage = null;
                return;
            }

            int w = Mathf.Max(1, doc.canvasWidth);
            int h = Mathf.Max(1, doc.canvasHeight);

            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                // Dispose() also stops the pre-baker; StartPrebakeIfNeeded below restarts it once the cache
                // has been re-shaped for the new canvas, so a mid-bake canvas resize does not leave it
                // running against stale dimensions.
                Dispose();
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    // Point + no mips: this is pixel art, and any filtering makes the preview lie about the
                    // bake, whose importer settings ShaperBaker pins to exactly this.
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            // T-0165 — read through the frame cache rather than rendering unconditionally. EnsureShape drops
            // the cache itself when the canvas or frame count actually changed (a document edit already
            // called InvalidateFrameCache for content changes; this additionally catches the shape changing
            // under a resident cache). The applier is passed inside ComputeFrame for the same reason the
            // baker passes it (T-0156): without it a document's authored effects render as a no-op, and
            // passing it in both places is what keeps "preview is what you bake" true.
            _frameCache.EnsureShape(Mathf.Max(1, doc.frameCount), w, h);
            var px = _frameCache.ComputeFrame(frame, doc);
            StartPrebakeIfNeeded();
            if (px == null || px.Length != w * h) return;   // canvas changed under us; next Refresh resizes

            _tex.SetPixels32(px);
            _tex.Apply(false);
            _image.style.backgroundImage = Background.FromTexture2D(_tex);

            // Zoom magnifies the fitted picture only. It is applied as a LAYOUT inset on the image layer, so
            // it cannot reach the renderer: the same Color32[] is displayed larger, never re-rendered at a
            // different size. That is what keeps zoom cosmetic and the bake unaffected.
            float inset = Zoom <= 1f ? 0f : -(Zoom - 1f) * 50f;
            _image.style.left = Length.Percent(inset);
            _image.style.right = Length.Percent(inset);
            _image.style.top = Length.Percent(inset);
            _image.style.bottom = Length.Percent(inset);
        }
    }
}

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
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    internal sealed class ShaperPreviewStage : VisualElement
    {
        readonly Func<ShaperDocument> _doc;
        readonly Func<int> _frame;
        readonly IMGUIContainer _backdrop;
        readonly VisualElement _image;
        Texture2D _tex;

        /// Cosmetic view state, owned by the window and pushed in — see ShaperWindow.Preview.cs for why these
        /// live on the window rather than the document.
        public bool ShowFrameBorder;
        public float Zoom = 1f;

        public ShaperPreviewStage(Func<ShaperDocument> doc, Func<int> frame)
        {
            _doc = doc;
            _frame = frame;

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
            // Instead the order is made structural — two sibling children, backdrop first, frame second —
            // which is the retained-mode spelling of the same blit order and cannot be reordered by styling.
            //
            // The frame keeps its transparency, which is what makes the backdrop visible at all: the layer
            // walk clears its accumulator with Array.Clear and Encode writes STRAIGHT alpha, so every sample
            // outside the shape is genuinely alpha 0 rather than an opaque background colour.
            _backdrop = new IMGUIContainer(DrawBackdrop) { pickingMode = PickingMode.Ignore };
            _backdrop.StretchToParentSize();
            Add(_backdrop);

            _image = new VisualElement { pickingMode = PickingMode.Ignore };
            _image.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            _image.StretchToParentSize();
            Add(_image);

            // A Texture2D is an unmanaged Unity object; a window rebuild drops this element and would leak it.
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            Refresh();
        }

        /// The backdrop is painted through the SHARED BackSplashPainter, not a local copy — that painter
        /// exists precisely because Pyre and Mirage had each written the same twenty lines and drifted.
        /// `contentRect` is this container's own local rect, which is already the space an IMGUIContainer's
        /// GUI calls draw in, so no parent offset is added (adding one double-counts, the same class of
        /// mistake Pyre's popover-anchoring note warns about).
        void DrawBackdrop()
        {
            var doc = _doc?.Invoke();
            BackSplashPainter.Draw(_backdrop.contentRect, doc?.previewBackSplash,
                                   new Color(0.08f, 0.08f, 0.10f));

            if (!ShowFrameBorder || doc == null) return;

            // A 1px outline around the canvas edge — four DrawRect edges, Pyre's own idiom. Cosmetic only:
            // it is drawn in the editor's IMGUI pass over the preview, and no renderer or baker path can see
            // it, so a bordered preview and an unbordered one bake byte-identically.
            var r = FittedCanvasRect(_backdrop.contentRect, doc);
            var col = new Color(1f, 1f, 1f, 0.28f);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), col);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), col);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), col);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), col);
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
            if (_tex == null) return;
            UnityEngine.Object.DestroyImmediate(_tex);
            _tex = null;
        }

        public void Refresh()
        {
            var doc = _doc?.Invoke();
            _backdrop.MarkDirtyRepaint();   // the backdrop is IMGUI; it repaints only when asked

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
                Dispose();
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    // Point + no mips: this is pixel art, and any filtering makes the preview lie about the
                    // bake, whose importer settings ShaperBaker pins to exactly this.
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            // The applier is passed here for the same reason the baker passes it (T-0156): without it a
            // document's authored effects render as a no-op. Passing it in BOTH places is also what keeps the
            // "preview is what you bake" guarantee true — a preview that skipped effects would disagree with
            // its own bake on every document that uses one.
            var px = ShaperDocumentRenderer.RenderFrame(doc, frame, ShaperEffectApplier.Instance);
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

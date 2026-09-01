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
using Laubrary.PyreShaper;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    internal sealed class ShaperPreviewStage : VisualElement
    {
        readonly Func<ShaperDocument> _doc;
        readonly Func<int> _frame;
        readonly VisualElement _image;
        Texture2D _tex;

        public ShaperPreviewStage(Func<ShaperDocument> doc, Func<int> frame)
        {
            _doc = doc;
            _frame = frame;

            AddToClassList("zui-stage");
            style.overflow = Overflow.Hidden;
            style.minHeight = 180f;
            tooltip = "Live render of the whole document at the current frame — every enabled layer, "
                    + "composited. This is the same renderer the bake uses, so what you see here is what "
                    + "gets baked.";

            // The rendered frame sits on its own child rather than on this element's background, so a
            // backdrop layer can later be inserted BEHIND it (Pyre's blit order) without restructuring.
            _image = new VisualElement { pickingMode = PickingMode.Ignore };
            _image.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            _image.StretchToParentSize();
            Add(_image);

            // A Texture2D is an unmanaged Unity object; a window rebuild drops this element and would leak it.
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            Refresh();
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
            if (doc == null)
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
            var px = ShaperDocumentRenderer.RenderFrame(doc, _frame?.Invoke() ?? 0, ShaperEffectApplier.Instance);
            if (px == null || px.Length != w * h) return;   // canvas changed under us; next Refresh resizes

            _tex.SetPixels32(px);
            _tex.Apply(false);
            _image.style.backgroundImage = Background.FromTexture2D(_tex);
        }
    }
}

// ShaperMockPreviewStage — a pixel-exact live render of ONE selected layer's Primitive shape + fill. Not
// a Mirage-quality preview and not real Shaper geometry (the shape math here is a mock's own rough
// approximation, not ported from ShaperPrimitives.cs) — its whole job is to prove a dial visibly changes
// something, the same spirit as ChunksMockWindow's own Painter2D spatial guide.
//
// Renders into a small Texture2D at the document's own canvas resolution and sets it as this element's
// own background image (nearest-neighbour) — the same technique ZuiFillControl's FillSwatch uses,
// simpler than the sanctioned Z.PixelFit/DrawPixels pixel-exact path (which is IMGUIContainer-based) since
// a retained-mode VisualElement's own backgroundImage already blits without any device-pixel rounding
// concern at this preview's scale.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaperMock.Editor
{
    sealed class ShaperMockPreviewStage : VisualElement
    {
        readonly ShaperMockDocument _doc;
        readonly Func<ShaperMockLayer> _selected;
        Texture2D _tex;
        Color32[] _buf;

        public ShaperMockPreviewStage(ShaperMockDocument doc, Func<ShaperMockLayer> selected)
        {
            _doc = doc;
            _selected = selected;
            AddToClassList("zui-stage");
            style.overflow = Overflow.Hidden;
            style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            tooltip = "A pixel-exact live render of the selected layer's shape and fill — not a "
                + "Mirage-quality preview.";
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (_tex != null) { UnityEngine.Object.DestroyImmediate(_tex); _tex = null; }
            });
            Refresh();
        }

        public void Refresh()
        {
            var layer = _selected?.Invoke();
            int size = Mathf.Clamp(_doc != null ? _doc.canvas.width : 64, 8, 256);

            if (_tex == null || _tex.width != size)
            {
                if (_tex != null) UnityEngine.Object.DestroyImmediate(_tex);
                _tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                _buf = new Color32[size * size];
            }

            var clear = new Color32(20, 20, 24, 255);
            for (int i = 0; i < _buf.Length; i++) _buf[i] = clear;

            if (layer != null && layer.enabled && layer.root != null) PaintNode(layer.root, size);

            _tex.SetPixels32(_buf);
            _tex.Apply(false);
            style.backgroundImage = Background.FromTexture2D(_tex);
        }

        void PaintNode(ShaperMockNode node, int size)
        {
            for (int y = 0; y < size; y++)
            {
                // row 0 is the texture's BOTTOM; map it to +1 so the picture reads upright on screen.
                float sy = Mathf.Lerp(1f, -1f, (y + 0.5f) / size);
                for (int x = 0; x < size; x++)
                {
                    float sx = Mathf.Lerp(-1f, 1f, (x + 0.5f) / size);
                    if (!Inside(node, sx, sy)) continue;
                    _buf[y * size + x] = Evaluate(node.fill, sx, sy);
                }
            }
        }

        static bool Inside(ShaperMockNode node, float x, float y)
        {
            float len = Mathf.Sqrt(x * x + y * y);
            switch (node.shapeKind)
            {
                case ShaperMockShapeKind.Disc:
                    return len <= node.discRadius;

                case ShaperMockShapeKind.Ngon:
                {
                    int sides = Mathf.Max(3, node.ngonSides);
                    float theta = Mathf.Atan2(y, x) - node.ngonRotation * Mathf.Deg2Rad;
                    float sector = Mathf.PI * 2f / sides;
                    float local = Mathf.Repeat(theta, sector) - sector * 0.5f;
                    float edge = node.ngonRadius * Mathf.Cos(sector * 0.5f) / Mathf.Max(0.001f, Mathf.Cos(local));
                    // A cheap corner-rounding approximation (pulls the edge in a touch) — good enough to
                    // show the dial does SOMETHING; not a claim about real Shaper corner-rounding math.
                    edge -= node.ngonCornerRadius * node.ngonRadius * 0.25f;
                    return len <= Mathf.Max(0.001f, edge);
                }

                case ShaperMockShapeKind.Star:
                {
                    int arms = Mathf.Max(3, node.starArms);
                    float theta = Mathf.Atan2(y, x) - node.starSkew * Mathf.Deg2Rad;
                    // A smooth "flower" radius wave rather than a pointed polygon-star — simpler math, and
                    // still visibly responds to every one of the five star dials.
                    float wave = 0.5f + 0.5f * Mathf.Cos(arms * theta);
                    float outer = node.starRadius;
                    float inner = Mathf.Clamp(node.starRadius * node.starBaseWidth, 0.01f, outer);
                    float sharp = Mathf.Lerp(1f, 4f, Mathf.Clamp01(node.starLength));
                    float edge = Mathf.Lerp(inner, outer, Mathf.Pow(wave, sharp));
                    return len <= Mathf.Max(0.001f, edge);
                }
            }
            return false;
        }

        static Color32 Evaluate(ShaperMockFill fill, float x, float y)
        {
            if (fill.kind == ShaperMockFillKind.Solid || ShaperMockFill.IsUnseeded(fill.gradient))
                return fill.solidColor;

            float rad = fill.gradientAngleDegrees * Mathf.Deg2Rad;
            Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float t = Mathf.InverseLerp(-1f, 1f, Vector2.Dot(new Vector2(x, y), axis));
            return fill.gradient.Evaluate(Mathf.Clamp01(t));
        }
    }
}

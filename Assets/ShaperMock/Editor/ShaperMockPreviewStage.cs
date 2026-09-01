// ShaperMockPreviewStage — a pixel-exact live render of ONE selected layer's node tree (Primitive shape +
// fill, a Bag's members combined by their combine mode, a Composite rendered as a neutral placeholder
// blob since it has no fill of its own). Not a Mirage-quality preview and not real Shaper geometry/CSG —
// its whole job is to prove a dial visibly changes something, the same spirit as ChunksMockWindow's own
// Painter2D spatial guide. Border/Light/Extrusion/Swarm/Effects/Cache are NOT rendered here — those are
// editor-panel concepts this mock's own preview never claimed to visualise, in the first slice or now.
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

        void PaintNode(ShaperMockNode root, int size)
        {
            for (int y = 0; y < size; y++)
            {
                // row 0 is the texture's BOTTOM; map it to +1 so the picture reads upright on screen.
                float sy = Mathf.Lerp(1f, -1f, (y + 0.5f) / size);
                for (int x = 0; x < size; x++)
                {
                    float sx = Mathf.Lerp(-1f, 1f, (x + 0.5f) / size);
                    if (Sample(root, sx, sy, null, out var color))
                        _buf[y * size + x] = color;
                }
            }
        }

        /// Recursively resolves whether (x,y) lands inside `node`'s subtree and, if so, what colour it
        /// paints — a rough CSG stand-in for a Bag's members (first enabled member is the base; every
        /// later one combines by its own combineMode), not real Shaper geometry.
        static bool Sample(ShaperMockNode node, float x, float y, ShaperMockFill inherited, out Color32 color)
        {
            var fill = node.fill ?? inherited;

            switch (node.kind)
            {
                case ShaperMockNodeKind.Primitive:
                {
                    bool inside = InsidePrimitive(node, x, y);
                    color = inside && fill != null ? Evaluate(fill, x, y) : (Color32)Color.clear;
                    return inside;
                }

                case ShaperMockNodeKind.Bag:
                {
                    bool mask = false;
                    Color32 acc = Color.clear;
                    bool haveBase = false;
                    foreach (var m in node.bagMembers)
                    {
                        if (!m.enabled) continue;
                        bool memberInside = Sample(m, x, y, fill, out var memberColor);
                        if (!haveBase)
                        {
                            mask = memberInside;
                            acc = memberColor;
                            haveBase = true;
                            continue;
                        }
                        switch (m.combineMode)
                        {
                            case ShaperMockCombineMode.Add:
                                if (memberInside) { mask = true; acc = memberColor; }
                                break;
                            case ShaperMockCombineMode.Subtract:
                                if (memberInside) mask = false;
                                break;
                            case ShaperMockCombineMode.Intersect:
                                mask = mask && memberInside;
                                if (mask) acc = memberColor;
                                break;
                        }
                    }
                    color = acc;
                    return mask;
                }

                case ShaperMockNodeKind.Composite:
                {
                    // A Composite has no fill of its own (§B4/§B5) — a neutral placeholder blob so it
                    // still renders SOMETHING, proving the fill/border absence rule doesn't break the
                    // picture rather than pretending to render the real baked generator.
                    float len = Mathf.Sqrt(x * x + y * y);
                    bool inside = len <= 0.55f;
                    color = new Color(0.5f, 0.5f, 0.56f, 1f);
                    return inside;
                }

                case ShaperMockNodeKind.Solid:
                {
                    // No real pseudo-3D rasteriser in this UI mock (out of scope — Solids' actual facet
                    // geometry and lighting live in the real ShaperSolids.cs compute path). A neutral
                    // placeholder blob, same posture as Composite's above, so a Solid node still renders
                    // SOMETHING here — it DOES have its own fill (unlike Composite), so paint it if present.
                    float len = Mathf.Sqrt(x * x + y * y);
                    bool inside = len <= 0.6f;
                    color = inside && fill != null ? Evaluate(fill, x, y) : new Color(0.6f, 0.55f, 0.4f, 1f);
                    return inside;
                }
            }
            color = Color.clear;
            return false;
        }

        static bool InsidePrimitive(ShaperMockNode node, float x, float y)
        {
            float len = Mathf.Sqrt(x * x + y * y);
            switch (node.shapeKind)
            {
                case ShaperMockShapeKind.Rect:
                {
                    float hw = Mathf.Max(0.001f, node.rectHalfW);
                    float hh = Mathf.Max(0.001f, node.rectHalfH);
                    float r = Mathf.Clamp(node.rectCornerRadius, 0f, Mathf.Min(hw, hh));
                    float qx = Mathf.Abs(x) - hw + r;
                    float qy = Mathf.Abs(y) - hh + r;
                    float dist = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                        + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    return dist <= 0f;
                }

                case ShaperMockShapeKind.Ellipse:
                {
                    float rx = Mathf.Max(0.001f, node.ellipseRx);
                    float ry = Mathf.Max(0.001f, node.ellipseRy);
                    return (x * x) / (rx * rx) + (y * y) / (ry * ry) <= 1f;
                }

                case ShaperMockShapeKind.Diamond:
                {
                    float rx = Mathf.Max(0.001f, node.diamondRx);
                    float ry = Mathf.Max(0.001f, node.diamondRy);
                    return Mathf.Abs(x) / rx + Mathf.Abs(y) / ry <= 1f;
                }

                case ShaperMockShapeKind.Triangle:
                {
                    float halfBase = Mathf.Max(0.001f, node.triangleBase * 0.5f);
                    float height = Mathf.Max(0.001f, node.triangleHeight);
                    float apexY = height * 0.5f, baseY = -height * 0.5f;
                    if (y > apexY || y < baseY) return false;
                    float t = Mathf.InverseLerp(baseY, apexY, y);
                    float halfWidthAtY = Mathf.Lerp(halfBase, 0f, t);
                    return Mathf.Abs(x) <= halfWidthAtY;
                }

                case ShaperMockShapeKind.Capsule:
                {
                    float half = Mathf.Max(0f, node.capsuleHalfLength);
                    float r = Mathf.Max(0.001f, node.capsuleRadius);
                    float cx = Mathf.Clamp(x, -half, half);
                    float dx = x - cx;
                    return Mathf.Sqrt(dx * dx + y * y) <= r;
                }

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
            Color c;
            switch (fill.kind)
            {
                case ShaperMockFillKind.Gradient:
                {
                    if (ShaperMockFill.IsUnseeded(fill.gradient)) { c = fill.solidColor; break; }
                    float rad = fill.gradientAngleDegrees * Mathf.Deg2Rad;
                    Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    float t = Mathf.InverseLerp(-1f, 1f, Vector2.Dot(new Vector2(x, y), axis));
                    c = fill.gradient.Evaluate(Mathf.Clamp01(t));
                    break;
                }

                case ShaperMockFillKind.RampByQuantity:
                {
                    // No real published quantity in this mock (§B4's "greyed out" case) — stand in with a
                    // left-to-right ramp so the control still visibly does something when previewed.
                    float t = Mathf.InverseLerp(-1f, 1f, x);
                    c = Color.Lerp(Color.black, fill.rampTint, t);
                    break;
                }

                case ShaperMockFillKind.Texture:
                    c = fill.textureTint;
                    break;

                case ShaperMockFillKind.IndexedStrip:
                {
                    if (fill.stripSlots == null || fill.stripSlots.Count == 0) { c = fill.stripPlainColor; break; }
                    float rad = fill.stripOrientationDegrees * Mathf.Deg2Rad;
                    Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    float raw = Mathf.InverseLerp(-1f, 1f, Vector2.Dot(new Vector2(x, y), axis));
                    float t = Mathf.Repeat(raw * Mathf.Max(0.01f, fill.stripRepeats) + fill.stripOffset, 1f);
                    int idx = Mathf.Clamp(Mathf.FloorToInt(t * fill.stripSlots.Count), 0, fill.stripSlots.Count - 1);
                    c = fill.stripSlots[idx].color;
                    break;
                }

                case ShaperMockFillKind.HeightField:
                {
                    var tints = ShaperMockFill.HeightFieldPresetTints;
                    int idx = Mathf.Clamp(fill.heightFieldPresetIndex, 0, tints.Length - 1);
                    c = tints[idx] * fill.heightFieldTint;
                    break;
                }

                case ShaperMockFillKind.TapestrySteel:
                {
                    float wave = 0.5f + 0.5f * Mathf.Sin((x + y) * Mathf.Max(0.5f, fill.steelCells));
                    c = Color.Lerp(fill.steelBaseLow, fill.steelBaseHigh, wave);
                    break;
                }

                default:   // Solid
                    c = fill.solidColor;
                    break;
            }

            if (fill.quantiseLevels > 1)
            {
                float levels = fill.quantiseLevels;
                c.r = Mathf.Floor(c.r * levels) / levels;
                c.g = Mathf.Floor(c.g * levels) / levels;
                c.b = Mathf.Floor(c.b * levels) / levels;
            }
            return c;
        }
    }
}

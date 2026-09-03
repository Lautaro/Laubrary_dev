// DotGenMath.cs
// The pure geometry + determinism kernel every DotGen module shares: the hash, shape membership and edge
// depth, the anisotropic area transform and its inverse, and the nine anchors.
//
// WHY the hash is written in unsigned 32-bit arithmetic rather than something more idiomatic: the reference
// implementation is JavaScript, where `Math.imul` and `>>>` are exactly a wrapping 32-bit multiply and a
// logical shift. Any C# version that used `int` multiplication or an arithmetic shift would agree for most
// inputs and quietly disagree for the rest, which would show up not as an exception but as a DIFFERENT
// PICTURE for the same seed — the one failure mode a deterministic generator must not have. The return type
// is `double` for the same reason: every comparison the pipeline makes against a probability (spawn chance,
// cull, random selector) happens at the reference's own precision, so a value sitting a hair from the
// threshold falls the same side here as there.

using UnityEngine;

namespace Laubrary.DotGen
{
    /// The shape of a generator area (and of a fill drawer's cell targets). Membership and edge depth below.
    public enum DotShape { Rectangle, Ellipse, Diamond }

    /// The nine attachment points of an area, in reading order (top row first).
    public enum DotAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

    /// An evaluated generator area: an oriented box in normalized frame coordinates (0..1 across the frame),
    /// plus the world point its anchor was attached to and its deterministic index in the evaluation.
    public struct DotArea
    {
        public float cx, cy;        // centre, normalized frame coordinates
        public float w, h;          // size, normalized frame coordinates
        public float rot;           // world rotation, radians
        public float anchorX, anchorY;
        public int idx;             // deterministic instance index across the whole document
    }

    public static class DotGenMath
    {
        public const float Tau = 6.28318530717958647692f;

        /// The reference hash: three integers in, a value in [0,1) out, with no ambient random anywhere.
        /// Transcribed from the POC's `hash(a,b,c)` with JS's exact 32-bit semantics (see the file header).
        public static double Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint x = (uint)a
                       ^ ((uint)b + 0x9e3779b9u) * 0x85ebca6bu
                       ^ ((uint)c + 17u) * 0xc2b2ae35u;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x / 4294967296.0;
            }
        }

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// Is a point inside the unit local shape? Local coordinates span -0.5..+0.5 on both axes.
        public static bool Inside(float x, float y, DotShape shape)
        {
            switch (shape)
            {
                case DotShape.Ellipse: return x * x * 4f + y * y * 4f <= 1f;
                case DotShape.Diamond: return (Mathf.Abs(x) + Mathf.Abs(y)) * 2f <= 1f;
                default: return Mathf.Abs(x) <= 0.5f && Mathf.Abs(y) <= 0.5f;
            }
        }

        /// Zero at the shape boundary, rising to 1 at its centre. This is what the Margin selector reads.
        public static float EdgeDepth(float x, float y, DotShape shape)
        {
            switch (shape)
            {
                case DotShape.Ellipse: return Clamp01(1f - Mathf.Sqrt(x * x * 4f + y * y * 4f));
                case DotShape.Diamond: return Clamp01(1f - (Mathf.Abs(x) + Mathf.Abs(y)) * 2f);
                default: return Clamp01(Mathf.Min(0.5f - Mathf.Abs(x), 0.5f - Mathf.Abs(y)) * 2f);
            }
        }

        /// Local (-0.5..0.5) → world. Deliberately ANISOTROPIC: it rotates first and scales the rotated
        /// coordinate by the area's width and height, which is why a rotated non-square point field can sit
        /// slightly differently from the outline drawn for the same area. That is the reference's behaviour,
        /// not a rounding artefact, and reproducing it is what keeps a seed's picture identical.
        public static Vector2 Xform(float lx, float ly, in DotArea a)
        {
            float ca = Mathf.Cos(a.rot), sa = Mathf.Sin(a.rot);
            return new Vector2(a.cx + (lx * ca - ly * sa) * a.w,
                               a.cy + (lx * sa + ly * ca) * a.h);
        }

        /// World → local, the inverse the selectors use to ask "where in its own area is this dot?".
        public static Vector2 Invform(float x, float y, in DotArea a)
        {
            float dx = x - a.cx, dy = y - a.cy;
            float ca = Mathf.Cos(-a.rot), sa = Mathf.Sin(-a.rot);
            float w = Mathf.Abs(a.w) < 1e-6f ? 1e-6f : a.w;
            float h = Mathf.Abs(a.h) < 1e-6f ? 1e-6f : a.h;
            return new Vector2((dx * ca - dy * sa) / w, (dx * sa + dy * ca) / h);
        }

        /// The local offset of each anchor, in the same -0.5..0.5 local space.
        public static Vector2 AnchorVector(DotAnchor anchor)
        {
            switch (anchor)
            {
                case DotAnchor.TopLeft:     return new Vector2(-0.5f, -0.5f);
                case DotAnchor.Top:         return new Vector2( 0.0f, -0.5f);
                case DotAnchor.TopRight:    return new Vector2( 0.5f, -0.5f);
                case DotAnchor.Left:        return new Vector2(-0.5f,  0.0f);
                case DotAnchor.Right:       return new Vector2( 0.5f,  0.0f);
                case DotAnchor.BottomLeft:  return new Vector2(-0.5f,  0.5f);
                case DotAnchor.Bottom:      return new Vector2( 0.0f,  0.5f);
                case DotAnchor.BottomRight: return new Vector2( 0.5f,  0.5f);
                default:                    return Vector2.zero;
            }
        }

        /// Round half away from zero toward +infinity, matching JS `Math.round` (C#'s default rounds to even,
        /// which would put a dot on the other side of a warp line for exactly-halfway distances).
        public static float RoundJs(float v) => Mathf.Floor(v + 0.5f);

        /// Attach `g`'s chosen anchor to `target`, taking scale and rotation from `basis`. Never clips: a child
        /// may extend well outside the area it was positioned from — that is what lets a tall box grow through
        /// the row above it.
        public static DotArea MakeArea(DotGenerator g, in DotArea basis, Vector2 target, bool alignInside)
        {
            Vector2 an = AnchorVector(g.anchor);
            float rot = basis.rot + g.rotation * Mathf.Deg2Rad;
            float w = basis.w * g.sizeX / 100f;
            float h = basis.h * g.sizeY / 100f;
            float ca = Mathf.Cos(rot), sa = Mathf.Sin(rot);

            Vector2 t = alignInside ? Xform(an.x, an.y, basis) : target;
            float ox = (an.x * w) * ca - (an.y * h) * sa;
            float oy = (an.x * w) * sa + (an.y * h) * ca;

            return new DotArea
            {
                cx = t.x - ox,
                cy = t.y - oy,
                w = w,
                h = h,
                rot = rot,
                anchorX = t.x,
                anchorY = t.y
            };
        }

        /// The fixed frame every root generator is placed inside.
        public static DotArea FixedFrame => new DotArea { cx = 0.5f, cy = 0.5f, w = 1f, h = 1f, rot = 0f, anchorX = 0.5f, anchorY = 0.5f };

        /// Parse a POC hex literal into a colour. Used only for the documented default colours.
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}

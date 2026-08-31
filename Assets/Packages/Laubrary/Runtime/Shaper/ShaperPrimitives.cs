using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// APPEND-ONLY: serialized as an int on authored assets, so an existing shape's kind must never shift index.
    /// </summary>
    public enum ShaperPrimitiveKind
    {
        Rect = 0,
        Ellipse = 1,
        Diamond = 2,
        Triangle = 3,
        Capsule = 4,
        NGon = 5,
        Star = 6,
    }

    /// <summary>
    /// Whether a primitive's sweep runs <i>around</i> it or <i>along</i> it. Declared by the primitive, never
    /// chosen by the user: sweeping a capsule by angle around its centre produces a meaningless bowtie
    /// (design B12). For a bag it is the axis its members agree on, and <see cref="Radial"/> when they disagree.
    /// </summary>
    public enum ShaperSweepAxis { Radial = 0, Longitudinal = 1 }

    /// <summary>
    /// A primitive resolved at compile time into flat numbers the per-sample evaluator can read without
    /// touching a managed object. Every primitive declares three things about itself: its Lipschitz
    /// <see cref="bound"/>, its <see cref="sweepAxis"/>, and its local support extent.
    ///
    /// The <c>p0…p11</c> slots mean different things per kind — see <see cref="ShaperSdf"/> for the table.
    /// </summary>
    public struct ShaperBakedPrimitive
    {
        public ShaperPrimitiveKind kind;
        public int count;
        public float p0, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11;

        /// <summary>Local-space half-extents of an axis-aligned box containing the primitive.</summary>
        public float halfExtentX, halfExtentY;

        /// <summary>Half the primitive's length along its longitudinal axis (X), for a longitudinal sweep.</summary>
        public float longitudinalHalfLength;

        public ShaperSweepAxis sweepAxis;

        /// <summary>The declared bound: <c>reportedDistance ≤ bound × trueDistance</c> everywhere.</summary>
        public float bound;
    }

    /// <summary>
    /// The authored primitive. Every dial is in the primitive's own natural extents; nothing here is a
    /// normalised 0..1 quantity, and nothing is stretched by a second, redundant per-primitive skew (skew
    /// lives in the transform block, where it goes through the <c>σ_min</c> rescale and stays bounded).
    /// </summary>
    [Serializable]
    public class ShaperPrimitiveDef
    {
        public ShaperPrimitiveKind kind = ShaperPrimitiveKind.Rect;

        // Rect
        public float rectHalfW = 50f;
        public float rectHalfH = 50f;
        public float rectCornerRadius = 0f;

        // Ellipse
        public float ellipseRx = 50f;
        public float ellipseRy = 50f;

        // Diamond — vertices at (±rx, 0) and (0, ±ry).
        public float diamondRx = 50f;
        public float diamondRy = 50f;

        // Triangle — isosceles, apex UP at +height/2, base down at −height/2.
        public float triangleBase = 100f;
        public float triangleHeight = 100f;

        // Capsule — the centre segment runs along X from (−halfLength, 0) to (+halfLength, 0).
        public float capsuleHalfLength = 40f;
        public float capsuleRadius = 20f;

        // N-gon — replaces the reference app's hand-fitted hexagon and octagon outright.
        [Range(3, 64)] public int ngonSides = 6;
        public float ngonRadius = 50f;
        /// <summary>Degrees. At 0, vertex 0 points UP (+Y), matching Pyre's tip convention.</summary>
        public float ngonRotation = 0f;
        public float ngonCornerRadius = 0f;

        // Star — Pyre's specification, ported by value (Pyre.cs:489-492).
        [Range(2, 20)] public int starArms = 5;
        /// <summary>Tip radius R.</summary>
        public float starRadius = 50f;
        /// <summary>Arm reach 0..1; valley radius is <c>R·max(MinValleyFraction, 1 − length)</c> — a fraction
        /// of R, so the shape is scale-free. 0.62 ≈ the golden-ratio pentagram inner radius.</summary>
        public ZUIValue starLength = new ZUIValue(0.62f);
        /// <summary>Valley angular position as a fraction of the half-sector, 0.1..1 (1 = the classical midpoint).</summary>
        public ZUIValue starBaseWidth = new ZUIValue(1f);
        /// <summary>Valley swirl in degrees, −60..60 — a true pinwheel twist, clamped so valleys never cross tips.</summary>
        public ZUIValue starSkew = new ZUIValue(0f);
    }

    /// <summary>
    /// Bakes an authored <see cref="ShaperPrimitiveDef"/> into the flat form the evaluator reads, and holds
    /// the per-kind declared bound and sweep axis.
    /// </summary>
    public static class ShaperPrimitives
    {
        /// <summary>Tip 0 points UP. Pyre is +Y up and so are we, so Pyre's angles port unchanged
        /// (<c>PyreRenderer.cs:3268</c>); any reference-app angle would need a sign flip.</summary>
        public const float TipUp = Mathf.PI * 0.5f;

        /// <summary>
        /// The star's valley radius floor, as a fraction of the tip radius R. Keeps a fully-extended arm
        /// (<c>length = 1</c>) from collapsing its valleys onto the centre while staying scale-free, so a
        /// star's proportions never change with its size and a valley can never reach or pass a tip.
        /// </summary>
        public const float MinValleyFraction = 0.01f;

        // Field ids for the animatable star dials, so each draws its own reproducible MinMax sample.
        const uint FldStarLen = 0x51A21E01u;
        const uint FldStarBase = 0x51A21E02u;
        const uint FldStarSkew = 0x51A21E03u;

        /// <summary>
        /// The declared Lipschitz bound of each primitive. Every one of these is 1 because every one is an
        /// <b>exact</b> signed distance function — that is the whole point of departure from the reference
        /// app, whose registry over-reports 1.414× on the diamond at a corner, 2.236× on the triangle's
        /// slanted side and up to 3.13× inside its star.
        /// </summary>
        public static float Bound(ShaperPrimitiveKind kind)
        {
            switch (kind)
            {
                case ShaperPrimitiveKind.Rect: return 1f;
                case ShaperPrimitiveKind.Ellipse: return 1f;
                case ShaperPrimitiveKind.Diamond: return 1f;
                case ShaperPrimitiveKind.Triangle: return 1f;
                case ShaperPrimitiveKind.Capsule: return 1f;
                case ShaperPrimitiveKind.NGon: return 1f;
                case ShaperPrimitiveKind.Star: return 1f;
                default: return 1f;
            }
        }

        /// <summary>The sweep axis each primitive declares for itself.</summary>
        public static ShaperSweepAxis SweepAxis(ShaperPrimitiveKind kind)
            => kind == ShaperPrimitiveKind.Capsule ? ShaperSweepAxis.Longitudinal : ShaperSweepAxis.Radial;

        /// <summary>
        /// Resolve an authored primitive at a normalised frame time into flat numbers. Called once per
        /// compile, never per sample.
        /// </summary>
        public static ShaperBakedPrimitive Bake(ShaperPrimitiveDef def, float phase01, uint seed)
        {
            var b = new ShaperBakedPrimitive();
            if (def == null) def = new ShaperPrimitiveDef();

            b.kind = def.kind;
            b.bound = Bound(def.kind);
            b.sweepAxis = SweepAxis(def.kind);

            switch (def.kind)
            {
                case ShaperPrimitiveKind.Rect:
                {
                    float hw = Mathf.Max(1e-4f, def.rectHalfW);
                    float hh = Mathf.Max(1e-4f, def.rectHalfH);
                    float r = Mathf.Clamp(def.rectCornerRadius, 0f, Mathf.Min(hw, hh));
                    b.p0 = hw; b.p1 = hh; b.p2 = r;
                    b.halfExtentX = hw; b.halfExtentY = hh;
                    b.longitudinalHalfLength = hw;
                    break;
                }
                case ShaperPrimitiveKind.Ellipse:
                {
                    float rx = Mathf.Max(1e-4f, def.ellipseRx);
                    float ry = Mathf.Max(1e-4f, def.ellipseRy);
                    b.p0 = rx; b.p1 = ry;
                    b.halfExtentX = rx; b.halfExtentY = ry;
                    b.longitudinalHalfLength = rx;
                    break;
                }
                case ShaperPrimitiveKind.Diamond:
                {
                    float rx = Mathf.Max(1e-4f, def.diamondRx);
                    float ry = Mathf.Max(1e-4f, def.diamondRy);
                    b.p0 = rx; b.p1 = ry;
                    b.halfExtentX = rx; b.halfExtentY = ry;
                    b.longitudinalHalfLength = rx;
                    break;
                }
                case ShaperPrimitiveKind.Triangle:
                {
                    float halfBase = Mathf.Max(1e-4f, def.triangleBase * 0.5f);
                    float height = Mathf.Max(1e-4f, def.triangleHeight);
                    b.p0 = halfBase; b.p1 = height;
                    b.halfExtentX = halfBase; b.halfExtentY = height * 0.5f;
                    b.longitudinalHalfLength = halfBase;
                    break;
                }
                case ShaperPrimitiveKind.Capsule:
                {
                    float half = Mathf.Max(0f, def.capsuleHalfLength);
                    float r = Mathf.Max(1e-4f, def.capsuleRadius);
                    b.p0 = half; b.p1 = r;
                    b.halfExtentX = half + r; b.halfExtentY = r;
                    b.longitudinalHalfLength = half + r;
                    break;
                }
                case ShaperPrimitiveKind.NGon:
                {
                    int n = Mathf.Clamp(def.ngonSides, 3, 64);
                    float radius = Mathf.Max(1e-4f, def.ngonRadius);
                    float sector = 2f * Mathf.PI / n;
                    float halfSector = Mathf.PI / n;
                    // Rounding preserves the OUTER EXTENT, per spec §5.1. Bake the inner polygon at
                    // circumradius (radius − corner) and offset the field back out by corner: the rounded
                    // vertices then sit exactly on the authored circumradius at every corner radius, and at
                    // corner == radius the shape degenerates to a circle of that same radius.
                    //
                    // The earlier form held the APOTHEM instead (apothemInner = radius·cos − corner), which
                    // kept the flat faces put and shrank the circumradius: a triangle authored at radius 50
                    // with corner 15 measured 35.000, and with corner 35 measured 25.025 — half the authored
                    // size. Holding the outer extent is what the spec requires and what an author expects
                    // from a dial named "radius".
                    float corner = Mathf.Clamp(def.ngonCornerRadius, 0f, radius);
                    float innerRadius = Mathf.Max(0f, radius - corner);
                    float apothemInner = innerRadius * Mathf.Cos(halfSector);
                    float halfEdge = innerRadius * Mathf.Sin(halfSector);

                    // The fold puts an EDGE NORMAL along the folded +X axis. Edge normals bisect vertices, so
                    // offsetting by a half-sector from "up" puts vertex 0 up at rotation 0 (Pyre's convention).
                    float rot = TipUp + halfSector + def.ngonRotation * Mathf.Deg2Rad;

                    b.count = n;
                    b.p0 = apothemInner; b.p1 = halfEdge; b.p2 = corner; b.p3 = rot; b.p4 = sector;

                    // hypot(apothemInner, halfEdge) + corner == innerRadius + corner == radius, exactly.
                    b.halfExtentX = radius; b.halfExtentY = radius;
                    b.longitudinalHalfLength = radius;
                    break;
                }
                case ShaperPrimitiveKind.Star:
                {
                    // ── Pyre's star geometry, PyreRenderer.cs:3259-3281, ported by value ──────────────────
                    float R = Mathf.Max(1e-4f, def.starRadius);
                    int N = Mathf.Clamp(def.starArms, 2, 20);
                    float len = Mathf.Clamp01(ShaperValue.Sample(def.starLength, phase01, seed ^ FldStarLen, 0.62f));

                    // The valley floor is a fraction of R, NOT an absolute constant. Pyre's own floor is
                    // max(0.5, R·(1−len)) where 0.5 is half a texture PIXEL and R is a particle radius in
                    // pixels — a sub-pixel guard. Shaper's units are canvas units and R is authored, so
                    // carrying 0.5 across meant (a) at starRadius = 0.4, length = 1 the floor gave rIn = 0.5
                    // > R, putting the valleys OUTSIDE the tips and turning the polygon inside-out, which
                    // invalidates the star-shapedness the sign test rests on; and (b) at starRadius = 100 the
                    // valleys sat at 0.5 units regardless of R, so the star's proportions changed with its
                    // size. Relative to R, neither can happen: rIn ≤ R·max(f, 1) with f < 1, so a valley can
                    // never reach a tip, and the shape is a pure scale of itself at any radius.
                    float rIn = R * Mathf.Max(MinValleyFraction, 1f - len);
                    float baseW = Mathf.Clamp(ShaperValue.Sample(def.starBaseWidth, phase01, seed ^ FldStarBase, 1f), 0.1f, 1f);
                    float skew = ShaperValue.Sample(def.starSkew, phase01, seed ^ FldStarSkew, 0f) * Mathf.Deg2Rad;

                    float sector = 2f * Mathf.PI / N;   // angular span between adjacent tips
                    float halfSector = Mathf.PI / N;    // the classical (single-valley) midpoint

                    // THE CLAMP THAT STOPS VALLEYS CROSSING TIPS — verbatim, PyreRenderer.cs:3272-3274.
                    //
                    // Pyre's own symmetry argument (PyreRenderer.cs:3231-3237), which is what makes this clamp
                    // provably sufficient rather than merely defensive: unclamped, offA + offB = sector·baseWidth
                    // ≤ sector, because the +skew and −skew cancel. Raising either offset to 0.98·sector
                    // necessarily drives the other to 0.02·sector, so the sum is capped at sector either way.
                    // Therefore offA + offB ≤ sector always ⇒ vA ≤ vB always ⇒ the two valleys never cross.
                    // At worst they COINCIDE — which is exactly what baseWidth = 1 (the default) does, collapsing
                    // to the classical two-segment star with a zero-length, unreachable base chord.
                    float lo = 0.02f * sector, hi = 0.98f * sector;
                    float offA = Mathf.Clamp(halfSector * baseW + skew, lo, hi);   // valley A, angle past tip k
                    float offB = Mathf.Clamp(halfSector * baseW - skew, lo, hi);   // valley B, angle before tip k+1
                    float vA = offA;
                    float vB = sector - offB;
                    if (vB <= vA) vB = vA;   // coincide: the base chord degenerates to a point, and the three-segment
                                             // walk below collapses to Pyre's two-segment branch with no special case.

                    b.count = N;
                    b.p0 = R; b.p1 = rIn; b.p2 = vA; b.p3 = vB; b.p4 = sector; b.p5 = TipUp;

                    // The sector's four vertices, precomputed with the tip at angle 0, so the per-sample path
                    // needs one atan2 and one cos/sin pair instead of twelve trig calls.
                    b.p6 = rIn * Mathf.Cos(vA); b.p7 = rIn * Mathf.Sin(vA);
                    b.p8 = rIn * Mathf.Cos(vB); b.p9 = rIn * Mathf.Sin(vB);
                    b.p10 = Mathf.Cos(sector); b.p11 = Mathf.Sin(sector);

                    b.halfExtentX = R; b.halfExtentY = R;
                    b.longitudinalHalfLength = R;
                    break;
                }
            }
            return b;
        }
    }
}

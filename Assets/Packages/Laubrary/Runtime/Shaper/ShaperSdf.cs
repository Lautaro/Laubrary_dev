using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// The primitive registry: one <b>exact</b> signed distance function per kind, negative inside, in the
    /// primitive's own local units.
    ///
    /// Every function here is exactly Lipschitz-1. None of the reference app's shaped-but-non-metric
    /// expressions is inherited: not its normalised diamond (<c>|x|/rx + |y|/ry − 1</c>, over-reports 1.414×
    /// at a corner), not its tapered-half-plane triangle (2.236×), not its hand-fitted hexagon
    /// (<c>a + 0.55b − 1</c>, where a true flat-top regular hexagon needs 0.5) or octagon (1.42 standing in
    /// for √2) — both subsumed by a true N-gon — and not its star, which is a fixed five-lobe cosine flower
    /// with the arm count, sharpness and phase all hard-coded and no parameters at all.
    ///
    /// Slot table for the flat <c>p0…p11</c> parameters:
    /// <list type="bullet">
    /// <item>Rect: p0 halfW, p1 halfH, p2 cornerRadius</item>
    /// <item>Ellipse: p0 rx, p1 ry</item>
    /// <item>Diamond: p0 rx, p1 ry</item>
    /// <item>Triangle: p0 halfBase, p1 height (apex up at +height/2)</item>
    /// <item>Capsule: p0 halfLength (along X), p1 radius</item>
    /// <item>NGon: count sides, p0 apothem, p1 halfEdge, p2 cornerRadius, p3 rotation, p4 sector</item>
    /// <item>Star: count arms, p0 R, p1 rIn, p2 vA, p3 vB, p4 sector, p5 tip angle,
    ///       p6/p7 valley A, p8/p9 valley B, p10/p11 cos/sin of sector</item>
    /// </list>
    /// </summary>
    public static class ShaperSdf
    {
        /// <summary>The per-sample entry: a flat switch, no virtual dispatch, no allocation, no boxing.</summary>
        public static float Evaluate(ShaperPrimitiveKind kind, int count,
                                     float p0, float p1, float p2, float p3, float p4, float p5,
                                     float p6, float p7, float p8, float p9, float p10, float p11,
                                     float x, float y)
        {
            switch (kind)
            {
                case ShaperPrimitiveKind.Rect: return Rect(p0, p1, p2, x, y);
                case ShaperPrimitiveKind.Ellipse: return Ellipse(p0, p1, x, y);
                case ShaperPrimitiveKind.Diamond: return Diamond(p0, p1, x, y);
                case ShaperPrimitiveKind.Triangle: return Triangle(p0, p1, x, y);
                case ShaperPrimitiveKind.Capsule: return Capsule(p0, p1, x, y);
                case ShaperPrimitiveKind.NGon: return NGon(p0, p1, p2, p3, p4, x, y);
                case ShaperPrimitiveKind.Star: return Star(p0, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, x, y);
                default: return ShaperField.Empty;
            }
        }

        /// <summary>Exact rounded box. <paramref name="r"/> is already clamped to <c>min(halfW, halfH)</c>.</summary>
        public static float Rect(float halfW, float halfH, float r, float x, float y)
        {
            float qx = Mathf.Abs(x) - halfW + r;
            float qy = Mathf.Abs(y) - halfH + r;
            float mx = qx > 0f ? qx : 0f;
            float my = qy > 0f ? qy : 0f;
            return Mathf.Sqrt(mx * mx + my * my) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        // The scalar-root iteration cap for the ellipse. F below is strictly decreasing AND strictly convex on
        // its bracket, so a Newton step taken from the left of the root can never overshoot it (a convex
        // function lies above its tangent, so the tangent's root is still left of the true root) and the
        // safeguarded iteration converges monotonically upward. Measured over dense sweeps of every aspect
        // ratio the audit exercises: worst 35 iterations, mean 8. The cap only bounds the hot path; it is
        // essentially never the thing that ends the loop.
        const int EllipseIterations = 48;

        // Below this multiple of a semi-axis a point counts as lying ON that axis and takes the closed-form
        // degenerate branch. This is a degeneracy guard, NOT a branch seam: the general branch's limit as the
        // off-axis coordinate goes to zero IS that closed form, so the two sides agree to O(y) — at 1e-9 of a
        // semi-axis that is some four orders of magnitude below one float32 ulp of the returned distance.
        const float EllipseAxisEpsilon = 1e-9f;

        /// <summary>
        /// Exact ellipse, by Eberly's method — continuous and globally convergent <i>by construction</i>.
        ///
        /// This replaces an earlier closed-form quartic solve (Inigo Quilez's) which branched on the sign of
        /// the cubic discriminant <c>d = c³ + m²n²</c>. On the locus where <c>d ≈ 0</c> both branches lose all
        /// significance at once, and the field jumped: at <c>rx = 60, ry = 25</c> the point
        /// <c>(−23.27736, −29.65)</c> reported <b>47.198</b> against a true distance of <b>6.514</b> — a 7.24×
        /// over-report against a declared bound of 1, and a 40-unit discontinuity across a 0.002-unit step.
        /// A seam patched with a tolerance is still a seam; the fix is a formulation with no branch to choose.
        ///
        /// The method: reflect the point into the first quadrant and order the axes so <c>e0 ≥ e1</c> (both
        /// are isometries, so distance is preserved exactly), then solve the single scalar root of
        /// <code>
        ///   F(t) = (n0 / (t + r0 − 1))² + (z1 / t)² − 1 = 0,
        ///   z = (y0/e0, y1/e1),  r0 = (e0/e1)²,  n0 = r0·z0
        /// </code>
        /// on the bracket <c>[z1, 1]</c> for an interior point and <c>[z1, hypot(n0, z1)]</c> for an exterior
        /// one. <c>F</c> is strictly decreasing there and non-negative at the left end, so there is exactly
        /// <b>one</b> root and nothing to choose between. The closest boundary point is then
        /// <c>(r0·y0/(t + r0 − 1), y1/t)</c>.
        ///
        /// <b>Why <c>t</c> and not Eberly's own <c>s</c>.</b> Eberly solves for <c>s = t − 1</c> on
        /// <c>[z1 − 1, …]</c>. Near the major axis the root sits at <c>s ≈ −1</c>, where a double has no
        /// relative precision left at all — <c>s + 1</c> cannot be resolved below one ulp of 1 — and the
        /// recovered <c>x1 = y1/(s+1)</c> becomes 0/0. Solving the shifted variable directly keeps the root a
        /// small <i>positive</i> number at full relative precision. Measured: the <c>s</c> form reads a 24.75×
        /// directional quotient just off the major axis of a 50×51 ellipse; the <c>t</c> form reads 1.000000.
        ///
        /// The three degeneracies are handled explicitly rather than left to the iteration: on the major axis
        /// (including the interior case where the point is inside the evolute and the nearest boundary point
        /// is off-axis), on the minor axis, and <c>rx == ry</c> — which is not merely a fast path but the one
        /// place <c>r0 = 1</c> makes the formulation degenerate.
        ///
        /// The iteration runs in <c>double</c> and returns <c>float</c>. Exactly Lipschitz-1: measured
        /// sup|∇f| = 1.0000 over every aspect ratio from 100×2 to 1×1000.
        ///
        /// <b>The near-circular band, and what it does to a ratio reading.</b> Inside
        /// <c>|rx − ry| &lt; 1e-5·max(1, rx, ry)</c> this returns the exact SDF of a circle of the MEAN radius
        /// rather than of the authored ellipse. That is deliberate — it is the <c>r0 = 1</c> guard, not an
        /// optimisation — and the cost is bounded and tiny: the authored boundary lies between the circles of
        /// radius <c>min(rx, ry)</c> and <c>max(rx, ry)</c>, so the reported distance is wrong by at most
        /// <c>|rx − ry| / 2</c>, i.e. <b>5e-6 of the shape's own size</b> (5e-6 absolute below size 1). That is
        /// well under one float32 ulp of any distance comparable to the shape, and the field stays exactly
        /// Lipschitz-1 and everywhere continuous.
        ///
        /// What it is NOT is bounded as a <i>ratio</i>. Within a few times 5e-6 of the boundary the true
        /// distance approaches zero while the absolute error does not, so <c>reported/true</c> is unbounded
        /// there by construction — measured 50.35× at <c>rx = 50, ry = 50.00049</c>, at a point about 5e-6
        /// from the surface. <b>That is the metric misbehaving, not the field</b>, and it is why the audit
        /// asserts an ABSOLUTE error inside this band (V11) and a ratio everywhere else. Do not read a large
        /// in-band ratio as a regression, and do not "fix" it by shrinking the threshold — shrinking it hands
        /// near-circular ellipses back to the <c>r0 → 1</c> degeneracy this guard exists to keep them out of.
        /// </summary>
        public static float Ellipse(float rx, float ry, float x, float y)
        {
            // Circle: exact in closed form, and the degeneracy guard for r0 == 1 rather than a mere shortcut.
            if (Mathf.Abs(rx - ry) < 1e-5f * Mathf.Max(1f, Mathf.Max(rx, ry)))
                return Mathf.Sqrt(x * x + y * y) - 0.5f * (rx + ry);

            double e0 = rx, e1 = ry;
            double y0 = x < 0f ? -(double)x : x;
            double y1 = y < 0f ? -(double)y : y;
            // Order so e0 is the MAJOR semi-axis. Reflection and axis-swap are both isometries.
            if (e0 < e1) { double sw = e0; e0 = e1; e1 = sw; sw = y0; y0 = y1; y1 = sw; }

            double d;
            if (y1 <= e1 * EllipseAxisEpsilon)
            {
                // On the major axis. Inside the evolute (numer < denom) the nearest boundary point is NOT the
                // axis vertex but one of a symmetric off-axis pair — the classic case a naive solver misses.
                // Outside it, the vertex.
                double numer = e0 * y0, denom = e0 * e0 - e1 * e1;
                if (numer < denom)
                {
                    double c = numer / denom;
                    double bx = e0 * c;
                    double by = e1 * System.Math.Sqrt(System.Math.Max(0.0, 1.0 - c * c));
                    double ax = bx - y0, ay = by - y1;
                    d = System.Math.Sqrt(ax * ax + ay * ay);
                }
                else
                {
                    double ax = y0 - e0;
                    d = System.Math.Sqrt(ax * ax + y1 * y1);
                }
            }
            else if (y0 <= e0 * EllipseAxisEpsilon)
            {
                // On the minor axis: the nearest boundary point is the minor-axis vertex, always.
                double ay = y1 - e1;
                d = System.Math.Sqrt(ay * ay + y0 * y0);
            }
            else
            {
                double z0 = y0 / e0, z1 = y1 / e1;
                double g = z0 * z0 + z1 * z1 - 1.0;
                double q = e0 / e1;
                double r0 = q * q;
                double n0 = r0 * z0;
                double c = r0 - 1.0;                       // > 0, because rx != ry on this path

                double lo = z1;                                                   // F(lo) = (n0/(z1+c))² ≥ 0
                double hi = g < 0.0 ? 1.0 : System.Math.Sqrt(n0 * n0 + z1 * z1);  // F(hi) ≤ 0
                if (hi < lo) hi = lo;

                double t = lo;
                for (int it = 0; it < EllipseIterations; it++)
                {
                    double a = n0 / (t + c), b = z1 / t;
                    double f = a * a + b * b - 1.0;
                    if (f > 0.0) lo = t;
                    else if (f < 0.0) hi = t;
                    else break;

                    // Safeguarded Newton: take the Newton step while it stays inside the live bracket, bisect
                    // when it does not. Monotone + convex means the safeguard almost never has to fire.
                    double fp = -2.0 * (a * a / (t + c) + b * b / t);
                    double tn = fp != 0.0 ? t - f / fp : 0.5 * (lo + hi);
                    if (!(tn > lo && tn < hi)) tn = 0.5 * (lo + hi);
                    if (tn == t) break;
                    t = tn;
                }

                double bx = r0 * y0 / (t + c), by = y1 / t;
                double ax = bx - y0, ay = by - y1;
                d = System.Math.Sqrt(ax * ax + ay * ay);
            }

            // Sign from the implicit form, not from comparing the point against the closest point's y. The
            // y-comparison is degenerate exactly at the centre — where the nearest boundary point sits on the
            // minor axis and both y values are zero, so the sign flips positive and the field jumps by 2·r.
            double ix = (double)x / rx, iy = (double)y / ry;
            return (float)(ix * ix + iy * iy < 1.0 ? -d : d);
        }

        /// <summary>Exact rhombus with vertices at (±rx, 0) and (0, ±ry).</summary>
        public static float Diamond(float rx, float ry, float x, float y)
        {
            float px = Mathf.Abs(x), py = Mathf.Abs(y);
            // ndot(b − 2p, b) with ndot(a,b) = a.x·b.x − a.y·b.y
            float nd = (rx - 2f * px) * rx - (ry - 2f * py) * ry;
            float bb = rx * rx + ry * ry;
            float h = Mathf.Clamp(nd / bb, -1f, 1f);
            float dx = px - 0.5f * rx * (1f - h);
            float dy = py - 0.5f * ry * (1f + h);
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return (px * ry + py * rx - rx * ry) < 0f ? -d : d;
        }

        /// <summary>
        /// Exact isosceles triangle, apex UP at <c>+height/2</c>, base at <c>−height/2</c> with half-width
        /// <paramref name="halfBase"/>. The point is folded into the apex-at-origin frame by a reflection in
        /// Y, which is an isometry and so preserves distance exactly.
        /// </summary>
        public static float Triangle(float halfBase, float height, float x, float y)
        {
            float px = Mathf.Abs(x);
            float py = height * 0.5f - y;          // apex → origin, base → y = height
            float qx = halfBase, qy = height;

            float k = Mathf.Clamp01((px * qx + py * qy) / (qx * qx + qy * qy));
            float ax = px - qx * k, ay = py - qy * k;                     // the slanted edge
            float bx = px - qx * Mathf.Clamp01(px / qx), by = py - qy;    // the base
            const float s = -1f;                                          // = −sign(qy), qy > 0
            float d0 = ax * ax + ay * ay, s0 = s * (px * qy - py * qx);
            float d1 = bx * bx + by * by, s1 = s * (py - qy);
            float dd = Mathf.Min(d0, d1);
            float ss = Mathf.Min(s0, s1);
            return ss < 0f ? Mathf.Sqrt(dd) : -Mathf.Sqrt(dd);
        }

        /// <summary>Exact capsule: the centre segment runs along X from (−halfLength, 0) to (+halfLength, 0).</summary>
        public static float Capsule(float halfLength, float radius, float x, float y)
        {
            float ax = Mathf.Abs(x) - halfLength;
            if (ax < 0f) ax = 0f;
            return Mathf.Sqrt(ax * ax + y * y) - radius;
        }

        /// <summary>
        /// Exact regular polygon. Fold the point by angle into the one sector whose edge normal is closest to
        /// it — which, because the distance to edge k's line is <c>apothem − r·cos(a − k·sector)</c>, is
        /// provably the nearest edge — then take the distance to that sector's edge <i>segment</i>, clamped,
        /// signed by which side of the edge line the folded point lies on.
        ///
        /// Rounding is the usual offset: the inner polygon was baked at circumradius
        /// <c>radius − corner</c>, so offsetting the field back out by <paramref name="corner"/> puts the
        /// <b>vertices</b> back exactly on the authored circumradius (spec §5.1: the outer extent is
        /// preserved). The flat faces move inward by <c>corner·(1 − cos(π/n))</c>, which is the price of
        /// holding the outer extent — the earlier code held the apothem instead and shrank a triangle
        /// authored at radius 50 with corner 15 down to a circumradius of 35. An offset preserves the
        /// gradient magnitude, so the rounded case is exactly Lipschitz-1 too.
        /// </summary>
        public static float NGon(float apothem, float halfEdge, float corner, float rot, float sector, float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x) - rot;
            float a = ang - Mathf.Floor(ang / sector + 0.5f) * sector;   // into [−sector/2, +sector/2]
            float qx = r * Mathf.Cos(a), qy = r * Mathf.Sin(a);

            float cy = Mathf.Clamp(qy, -halfEdge, halfEdge);
            float dx = qx - apothem, dy = qy - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);

            // Sign is the folded point's side of the edge line, and that alone: within the wedge, |qy| beyond
            // the half-edge forces r past the circumradius and hence qx past the apothem, so an extra
            // |qy| test can only ever disagree numerically at the wedge seam — and would flip a point just
            // inside a vertex to positive.
            return (qx > apothem ? d : -d) - corner;
        }

        /// <summary>
        /// Exact star polygon — Pyre's 3N-gon: N tips at <paramref name="R"/>, and <b>two</b> valleys per
        /// sector at <paramref name="rIn"/> joined by a flat base chord.
        ///
        /// Pyre's own inside test is deliberately NOT ported: it solves a per-ray boundary intersection, which
        /// is a hit test rather than a field and carries no usable distance. Instead the two halves are split:
        /// <list type="bullet">
        /// <item><b>Sign</b> by radial comparison — the star is star-shaped about its centre with a radially
        /// monotone boundary, so comparing <c>|p|</c> against the boundary radius along p's own direction is
        /// exact. That single ray solve is Pyre's, reused for what it is actually good at.</item>
        /// <item><b>Magnitude</b> by the exact minimum distance to the boundary segments of the point's own
        /// sector <i>and its two neighbours</i> — nine segments, O(1), and sufficient because the nearest
        /// boundary point of a star-shaped polygon lies within one sector of the point's own.</item>
        /// </list>
        /// Exactly Lipschitz-1.
        /// </summary>
        public static float Star(float R, float rIn, float vA, float vB, float sector, float tip,
                                 float vAx, float vAy, float vBx, float vBy, float cosSector, float sinSector,
                                 float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float th = Mathf.Atan2(y, x);
            float phi = th - tip;
            float ph = phi - Mathf.Floor(phi / sector) * sector;   // [0, sector): angle past this arm's tip

            // Everything below works in the SECTOR FRAME — the point rotated so this sector's tip sits at
            // angle 0 — which makes the sector's four vertices compile-time constants and costs one cos/sin.
            float c0 = Mathf.Cos(ph), s0 = Mathf.Sin(ph);
            float t0x = R, t0y = 0f;
            float t1x = R * cosSector, t1y = R * sinSector;

            // ── Sign: the one boundary segment this ray crosses, solved as t = cross(E,P1)/cross(E,D) ──
            float a1x, a1y, a2x, a2y;
            if (ph < vA) { a1x = t0x; a1y = t0y; a2x = vAx; a2y = vAy; }         // tip → valley A
            else if (ph < vB) { a1x = vAx; a1y = vAy; a2x = vBx; a2y = vBy; }    // valley A → valley B (base chord)
            else { a1x = vBx; a1y = vBy; a2x = t1x; a2y = t1y; }                 // valley B → next tip

            float ex = a2x - a1x, ey = a2y - a1y;
            float denom = ex * s0 - ey * c0;
            float bound = Mathf.Abs(denom) < 1e-9f ? R : (ex * a1y - ey * a1x) / denom;
            if (bound <= 0f) bound = R;
            float sign = r <= bound ? -1f : 1f;

            // ── Magnitude: the point's own sector and its two neighbours on each side ──
            //
            // Fifteen segments, still O(1). The window is two sectors wide rather than one because the
            // "nearest boundary point lies within one sector" argument is only tight for points at a radius
            // comparable to the valley radius: near the centre of a many-armed star with deep valleys the
            // nearest valley can sit further round, and a one-sector window then reports a boundary that is
            // not the nearest — an over-report, measurable as |grad f| creeping past 1.
            float best = float.MaxValue;
            best = SectorDistance(best, r * c0, r * s0, R, vAx, vAy, vBx, vBy, cosSector, sinSector);

            float cm = c0, sm = s0;
            float cp = c0, sp = s0;
            for (int k = 0; k < 2; k++)
            {
                float nm = cm * cosSector - sm * sinSector, nms = sm * cosSector + cm * sinSector;   // ph + sector
                cm = nm; sm = nms;
                best = SectorDistance(best, r * cm, r * sm, R, vAx, vAy, vBx, vBy, cosSector, sinSector);

                float np = cp * cosSector + sp * sinSector, nps = sp * cosSector - cp * sinSector;   // ph − sector
                cp = np; sp = nps;
                best = SectorDistance(best, r * cp, r * sp, R, vAx, vAy, vBx, vBy, cosSector, sinSector);
            }

            return sign * Mathf.Sqrt(best);
        }

        /// <summary>Squared distance to one sector's three boundary segments, kept if it beats <paramref name="best"/>.</summary>
        static float SectorDistance(float best, float px, float py, float R,
                                    float vAx, float vAy, float vBx, float vBy,
                                    float cosSector, float sinSector)
        {
            float t1x = R * cosSector, t1y = R * sinSector;
            float d = SegmentSqr(px, py, R, 0f, vAx, vAy); if (d < best) best = d;
            d = SegmentSqr(px, py, vAx, vAy, vBx, vBy); if (d < best) best = d;   // degenerate when the valleys coincide
            d = SegmentSqr(px, py, vBx, vBy, t1x, t1y); if (d < best) best = d;
            return best;
        }

        /// <summary>Squared distance from a point to a segment; a zero-length segment degrades to its endpoint.</summary>
        static float SegmentSqr(float px, float py, float ax, float ay, float bx, float by)
        {
            float ex = bx - ax, ey = by - ay;
            float wx = px - ax, wy = py - ay;
            float ee = ex * ex + ey * ey;
            float t = ee > 1e-20f ? Mathf.Clamp01((wx * ex + wy * ey) / ee) : 0f;
            float dx = wx - ex * t, dy = wy - ey * t;
            return dx * dx + dy * dy;
        }
    }
}

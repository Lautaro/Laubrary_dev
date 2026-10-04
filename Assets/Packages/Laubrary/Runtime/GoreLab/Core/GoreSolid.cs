// Where a line of sight straight into the screen passes through a member's 3D solid: an ellipsoid for a ball, a superellipsoid for a box.
using System;

namespace Laubrary.GoreLab
{
    /// <summary>The solid of one member as plain numbers: what the per-pixel intersection needs.</summary>
    public struct GoreSolidShape
    {
        public bool box;
        public double p;                 // box exponent, clamped to 2..12
        public double dvx, dvy, dvz;     // one pixel toward the viewer, in member unit coordinates
        public double qa;                // dv . dv

        public static GoreSolidShape Of(in MemberTag t)
        {
            var e = GoreTagMath.East(t);
            double rz = GoreTagMath.Depth(t);
            var s = new GoreSolidShape { box = t.kind == MemberKind.Box, dvx = e.z / t.rx, dvy = t.uz / t.ry, dvz = t.fz / rz };
            double n = t.n == 0 ? 4 : t.n;
            s.p = Math.Max(2, Math.Min(12, n));
            s.qa = s.dvx * s.dvx + s.dvy * s.dvy + s.dvz * s.dvz;
            return s;
        }
    }

    public static class GoreSolid
    {
        /// <summary>
        /// Intersect the line O + z * dir (O in member unit coordinates at depth 0, z in pixels toward the viewer) with the member's solid.
        /// zf = front surface depth, zb = back surface depth, pf = front surface point. A line that misses gives its closest approach (zf == zb).
        /// </summary>
        public static void Intersect(in MemberTag t, double ox, double oy, double oz, out double zf, out double zb, out double pfx, out double pfy, out double pfz)
        {
            var s = GoreSolidShape.Of(t);
            Intersect(in s, ox, oy, oz, out zf, out zb, out pfx, out pfy, out pfz);
        }

        /// <summary>Depth (pixels toward the viewer) of the member's front surface under the screen offset (x, y) from its centre.</summary>
        public static double FrontDepth(in MemberTag t, double x, double y)
        {
            var o = GoreTagMath.ToUnit(t, x, y, 0);
            Intersect(t, o.x, o.y, o.z, out double zf, out _, out _, out _, out _);
            return zf;
        }

        public static void Intersect(in GoreSolidShape s, double ox, double oy, double oz, out double zf, out double zb, out double pfx, out double pfy, out double pfz)
        {
            double dx = s.dvx, dy = s.dvy, dz = s.dvz, qa = s.qa;
            if (!s.box)
            {
                double qb = ox * dx + oy * dy + oz * dz, disc = qb * qb - qa * ((ox * ox + oy * oy + oz * oz) - 1);
                if (disc >= 0 && qa > 1e-9)
                {
                    double sq = Math.Sqrt(disc);
                    zf = (-qb + sq) / qa; zb = (-qb - sq) / qa;
                    pfx = ox + dx * zf; pfy = oy + dy * zf; pfz = oz + dz * zf;
                    return;
                }
                double zc = qa > 1e-9 ? -qb / qa : 0;
                zf = zb = zc;
                Normalised(ox + dx * zc, oy + dy * zc, oz + dz * zc, out pfx, out pfy, out pfz);
                return;
            }

            // The superellipsoid sits inside the cube [-1,1]^3: clip the line to the cube first.
            double z0 = double.NegativeInfinity, z1 = double.PositiveInfinity;
            bool miss = false;
            Slab(dx, ox, ref z0, ref z1, ref miss);
            Slab(dy, oy, ref z0, ref z1, ref miss);
            Slab(dz, oz, ref z0, ref z1, ref miss);
            if (miss || !(z0 <= z1) || double.IsInfinity(z0) || double.IsNaN(z0) || double.IsInfinity(z1) || double.IsNaN(z1))
            {
                double zc = qa > 1e-9 ? -(ox * dx + oy * dy + oz * dz) / qa : 0;
                zf = zb = zc;
                Normalised(ox + dx * zc, oy + dy * zc, oz + dz * zc, out pfx, out pfy, out pfz);
                return;
            }

            // g is convex along the line: find its lowest point, then bisect for the two roots.
            double p = s.p, lo = z0, hi = z1;
            for (int it = 0; it < 22; it++)
            {
                double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                if (G(ox, oy, oz, dx, dy, dz, p, m1) < G(ox, oy, oz, dx, dy, dz, p, m2)) hi = m2; else lo = m1;
            }
            double zm = (lo + hi) / 2, gm = G(ox, oy, oz, dx, dy, dz, p, zm);
            if (gm > 0)
            {
                double sc = 1 / Math.Pow(gm + 1, 1 / p);
                zf = zb = zm;
                pfx = (ox + dx * zm) * sc; pfy = (oy + dy * zm) * sc; pfz = (oz + dz * zm) * sc;
                return;
            }
            double a = zm, b = z1;
            for (int it = 0; it < 22; it++) { double m = (a + b) / 2; if (G(ox, oy, oz, dx, dy, dz, p, m) <= 0) a = m; else b = m; }
            zf = (a + b) / 2;
            a = z0; b = zm;
            for (int it = 0; it < 22; it++) { double m = (a + b) / 2; if (G(ox, oy, oz, dx, dy, dz, p, m) <= 0) b = m; else a = m; }
            zb = (a + b) / 2;
            pfx = ox + dx * zf; pfy = oy + dy * zf; pfz = oz + dz * zf;
        }

        static double G(double ox, double oy, double oz, double dx, double dy, double dz, double p, double z)
        {
            return Math.Pow(Math.Abs(ox + dx * z), p) + Math.Pow(Math.Abs(oy + dy * z), p) + Math.Pow(Math.Abs(oz + dz * z), p) - 1;
        }

        static void Slab(double d, double o, ref double z0, ref double z1, ref bool miss)
        {
            if (Math.Abs(d) < 1e-9) { if (Math.Abs(o) > 1) miss = true; return; }
            double t0 = (-1 - o) / d, t1 = (1 - o) / d;
            if (t0 > t1) { double t = t0; t0 = t1; t1 = t; }
            if (t0 > z0) z0 = t0;
            if (t1 < z1) z1 = t1;
        }

        static void Normalised(double x, double y, double z, out double nx, out double ny, out double nz)
        {
            double l = JsMath.Hypot(x, y, z);
            if (l == 0) l = 1;
            nx = x / l; ny = y / l; nz = z / l;
        }
    }
}

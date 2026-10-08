// Member tag maths: normalising saved tags, mirroring, the member's own axes, the 2D outline test and the screen <-> member conversions.
using System;

namespace Laubrary.GoreLab
{
    public static class GoreTagMath
    {
        /// <summary>
        /// Clean up a tag as it is loaded: defaults for missing fields (rx 0 = unset -> 5, n 0 -> 2, a zero up -> from the angle, a zero forward -> toward the viewer),
        /// unit up and forward, forward made perpendicular to up, and the 2D angle re-derived from up unless up points mostly along the line of sight.
        /// </summary>
        public static void Normalize(ref MemberTag t)
        {
            if (t.rx == 0) { t.rx = 5; t.ry = 5; }
            if (t.n == 0) t.n = 2;
            if (t.ux == 0 && t.uy == 0 && t.uz == 0) { t.ux = Math.Cos(t.angle); t.uy = Math.Sin(t.angle); t.uz = 0; }
            else Norm3(ref t.ux, ref t.uy, ref t.uz);
            if (t.fx == 0 && t.fy == 0 && t.fz == 0) { t.fx = 0; t.fy = 0; t.fz = 1; }
            else Norm3(ref t.fx, ref t.fy, ref t.fz);

            double dot = t.fx * t.ux + t.fy * t.uy + t.fz * t.uz;
            double x = t.fx - t.ux * dot, y = t.fy - t.uy * dot, z = t.fz - t.uz * dot, l = JsMath.Hypot(x, y, z);
            if (l < 1e-3)
            {
                x = -t.uy; y = t.ux; z = 0; l = JsMath.Hypot(x, y, z);
                if (l < 1e-3) { x = 1; y = 0; z = 0; l = 1; }
            }
            t.fx = x / l; t.fy = y / l; t.fz = z / l;
            if (JsMath.Hypot(t.ux, t.uy) > 0.2) t.angle = Math.Atan2(t.uy, t.ux);
        }

        /// <summary>The tag of the same member on the horizontally mirrored sprite of width spriteWidth. Only the tag turns; a wound is never mirrored.</summary>
        public static MemberTag Mirror(in MemberTag t, int spriteWidth)
        {
            MemberTag m = t;
            m.cx = spriteWidth - t.cx;
            m.angle = Math.PI - t.angle;
            m.ux = -t.ux;
            m.fx = -t.fx;
            return m;
        }

        /// <summary>The member's right-hand axis (east) = up x forward, in screen space (x right, y down, z toward the viewer).</summary>
        public static (double x, double y, double z) East(in MemberTag t)
        {
            return (t.uy * t.fz - t.uz * t.fy, t.uz * t.fx - t.ux * t.fz, t.ux * t.fy - t.uy * t.fx);
        }

        /// <summary>Is the sprite-local point inside the member's 2D outline (a superellipse around the centre, turned by the tag's angle)?</summary>
        public static bool InsideOutline(in MemberTag t, double px, double py)
        {
            return InsideOutline(t.cx, t.cy, t.rx, t.ry, t.n, Math.Cos(t.angle), Math.Sin(t.angle), px, py);
        }

        /// <summary>The outline test with the angle's cosine and sine precomputed (the per-pixel form).</summary>
        internal static bool InsideOutline(double cx, double cy, double rx, double ry, double n, double ucos, double usin, double px, double py)
        {
            double dx = px - cx, dy = py - cy, along = dx * ucos + dy * usin, across = -dx * usin + dy * ucos;
            return Math.Pow(Math.Abs(across) / rx, n) + Math.Pow(Math.Abs(along) / ry, n) <= 1;
        }

        /// <summary>Half depth of the member toward the viewer: a box's rz (falling back to rx when unset), a ball's rx.</summary>
        public static double Depth(in MemberTag t) { return t.kind == MemberKind.Box && t.rz > 0 ? t.rz : t.rx; }

        /// <summary>A screen offset from the member centre (pixels; z = depth toward the viewer) in member unit coordinates (east, up, forward).</summary>
        public static (double x, double y, double z) ToUnit(in MemberTag t, double x, double y, double z)
        {
            var e = East(t);
            return ((e.x * x + e.y * y + e.z * z) / t.rx, (t.ux * x + t.uy * y + t.uz * z) / t.ry, (t.fx * x + t.fy * y + t.fz * z) / Depth(t));
        }

        /// <summary>A member-coordinate vector as a screen direction (x, y) in pixels.</summary>
        public static (double x, double y) ToScreen(in MemberTag t, double vx, double vy, double vz)
        {
            var e = East(t);
            double rz = Depth(t);
            return (vx * e.x * t.rx + vy * t.ux * t.ry + vz * t.fx * rz, vx * e.y * t.rx + vy * t.uy * t.ry + vz * t.fy * rz);
        }

        static void Norm3(ref double x, ref double y, ref double z)
        {
            double l = JsMath.Hypot(x, y, z);
            if (l == 0) l = 1;
            x /= l; y /= l; z /= l;
        }
    }

    /// <summary>JavaScript's Math.hypot as V8 computes it (scaled, Kahan-compensated), so lengths agree with the prototype to the last bit.</summary>
    internal static class JsMath
    {
        public static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            double max = a > b ? a : b;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            Add(a / max, ref sum, ref comp); Add(b / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }

        public static double Hypot(double a, double b, double c)
        {
            a = Math.Abs(a); b = Math.Abs(b); c = Math.Abs(c);
            if (double.IsInfinity(a) || double.IsInfinity(b) || double.IsInfinity(c)) return double.PositiveInfinity;
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c)) return double.NaN;
            double max = a; if (b > max) max = b; if (c > max) max = c;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            Add(a / max, ref sum, ref comp); Add(b / max, ref sum, ref comp); Add(c / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }

        static void Add(double n, ref double sum, ref double comp)
        {
            double summand = n * n - comp, prelim = sum + summand;
            comp = (prelim - sum) - summand;
            sum = prelim;
        }
    }
}

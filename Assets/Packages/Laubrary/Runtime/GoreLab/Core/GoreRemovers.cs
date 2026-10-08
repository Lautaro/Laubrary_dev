// Removers: what each kind takes out of a line of sight, and the colour of the wound surface it exposes.
// Built-in kinds (plane, capsule) and registered custom kinds feed the same visibility loop through the same contract:
// one removed depth interval per line of sight, and a surface colour at the member-space point where the remaining solid begins.
using System;
using System.Collections.Generic;

namespace Laubrary.GoreLab
{
    public static class GoreRemovers
    {
        static readonly Dictionary<int, IRemoverKind> s_kinds = new Dictionary<int, IRemoverKind>();

        /// <summary>Make a custom remover kind known to the engine. Its id must be at least RemoverKinds.FirstCustom; registering an id again replaces the kind.</summary>
        public static void RegisterKind(IRemoverKind kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (kind.KindId < RemoverKinds.FirstCustom) throw new ArgumentException($"Custom remover kinds need an id >= {RemoverKinds.FirstCustom} (got {kind.KindId}).");
            lock (s_kinds) s_kinds[kind.KindId] = kind;
        }

        public static bool TryGetKind(int kindId, out IRemoverKind kind)
        {
            lock (s_kinds) return s_kinds.TryGetValue(kindId, out kind);
        }

        /// <summary>A slice: removes everything on the side where N . p >= d (member unit coordinates, N unit).</summary>
        public static GoreRemover Plane(double nx, double ny, double nz, double d, int group, int member)
        {
            return new GoreRemover { kind = RemoverKinds.Plane, group = group, member = member, nx = nx, ny = ny, nz = nz, d = d };
        }

        /// <summary>A tunnel from A to B of radius r (member unit coordinates). back = screen direction debris and blood leave along (0,0 = from the geometry).</summary>
        public static GoreRemover Capsule(double ax, double ay, double az, double bx, double by, double bz, double r, int group, int member, double backX = 0, double backY = 0)
        {
            return new GoreRemover { kind = RemoverKinds.Capsule, group = group, member = member, ax = ax, ay = ay, az = az, bx = bx, by = by, bz = bz, r = r, backX = backX, backY = backY };
        }
    }

    /// <summary>A remover with everything that does not change per pixel worked out once per cut.</summary>
    internal struct PreparedRemover
    {
        public GoreRemover src;
        public int kind, group, noise;              // noise = position among the member's removers (selects its ragged-edge noise stream)
        public double e1x, e1y, e1z, e2x, e2y, e2z, rc;    // plane: in-plane axes and the radius of the cut disc
        public double len, uxv, uyv, uzv; public bool hasAxis;   // capsule: length and unit axis
        public double chunkX, chunkY, outX, outY;   // screen directions: where its chunk leaves, where blood leaves the wound
        public IRemoverKind custom;

        public static PreparedRemover Of(in GoreRemover r, int noise)
        {
            var p = new PreparedRemover { src = r, kind = r.kind, group = r.group, noise = noise };
            if (r.kind == RemoverKinds.Plane)
            {
                double ax = Math.Abs(r.ny) > 0.9 ? 1 : 0, ay = Math.Abs(r.ny) > 0.9 ? 0 : 1;
                double cx = r.ny * 0 - r.nz * ay, cy = r.nz * ax - r.nx * 0, cz = r.nx * ay - r.ny * ax;
                double l = JsMath.Hypot(cx, cy, cz); if (l == 0) l = 1;
                p.e1x = cx / l; p.e1y = cy / l; p.e1z = cz / l;
                p.e2x = r.ny * p.e1z - r.nz * p.e1y; p.e2y = r.nz * p.e1x - r.nx * p.e1z; p.e2z = r.nx * p.e1y - r.ny * p.e1x;
                p.rc = Math.Sqrt(Math.Max(1e-4, 1 - r.d * r.d));
            }
            else if (r.kind == RemoverKinds.Capsule)
            {
                double abx = r.bx - r.ax, aby = r.by - r.ay, abz = r.bz - r.az;
                p.len = JsMath.Hypot(abx, aby, abz);
                p.hasAxis = p.len > 1e-6;
                if (p.hasAxis) { p.uxv = abx / p.len; p.uyv = aby / p.len; p.uzv = abz / p.len; }
            }
            else if (!GoreRemovers.TryGetKind(r.kind, out p.custom))
                p.custom = null;   // unknown kind: removes nothing
            return p;
        }
    }

    /// <summary>Per-cut constants the remover evaluation needs.</summary>
    internal struct RemoverEnv
    {
        public double dvx, dvy, dvz, qa;   // one pixel toward the viewer in member coordinates
        public double ja, jf;              // ragged edge amplitude and frequency (member units)
        public int seed;                   // ragged edge noise seed
        public bool bone;
    }

    internal static class RemoverEval
    {
        /// <summary>The depth interval of the line O + z * dv that the remover takes out. False if it takes nothing.</summary>
        public static bool Interval(ref PreparedRemover o, in RemoverEnv env, double ox, double oy, double oz, out double lo, out double hi)
        {
            double dx = env.dvx, dy = env.dvy, dz = env.dvz;
            if (o.kind == RemoverKinds.Plane)
            {
                ref readonly GoreRemover r = ref o.src;
                double g0 = (r.nx * ox + r.ny * oy + r.nz * oz) - r.d, den = r.nx * dx + r.ny * dy + r.nz * dz;
                if (Math.Abs(den) < 1e-6)
                {
                    if (g0 >= 0) { lo = double.NegativeInfinity; hi = double.PositiveInfinity; return true; }
                    lo = hi = 0; return false;
                }
                double z0 = -g0 / den, zs = z0;
                if (env.ja != 0)
                {
                    double qx = ox + dx * z0, qy = oy + dy * z0, qz = oz + dz * z0;
                    double nv = GoreRng.VNoise3(qx * env.jf + 7, qy * env.jf + 11, qz * env.jf + 13, unchecked(env.seed + o.noise * 17));
                    zs = z0 - env.ja * (nv * 2 - 1) / den;
                }
                if (den > 0) { lo = zs; hi = double.PositiveInfinity; } else { lo = double.NegativeInfinity; hi = zs; }
                return true;
            }
            if (o.kind == RemoverKinds.Capsule)
            {
                ref readonly GoreRemover r = ref o.src;
                double L = double.PositiveInfinity, H = double.NegativeInfinity;
                double rr = r.r * r.r;
                SphereHull(ox - r.ax, oy - r.ay, oz - r.az, rr, in env, ref L, ref H);
                SphereHull(ox - r.bx, oy - r.by, oz - r.bz, rr, in env, ref L, ref H);
                if (o.hasAxis)
                {
                    double ux = o.uxv, uy = o.uyv, uz = o.uzv;
                    double wx = ox - r.ax, wy = oy - r.ay, wz = oz - r.az;
                    double wu = wx * ux + wy * uy + wz * uz, du = dx * ux + dy * uy + dz * uz;
                    double wpx = wx - wu * ux, wpy = wy - wu * uy, wpz = wz - wu * uz;
                    double dpx = dx - du * ux, dpy = dy - du * uy, dpz = dz - du * uz;
                    double A = dpx * dpx + dpy * dpy + dpz * dpz, Bq = wpx * dpx + wpy * dpy + wpz * dpz, C = (wpx * wpx + wpy * wpy + wpz * wpz) - rr;
                    bool has = false; double z1 = 0, z2 = 0;
                    if (A < 1e-9) { if (C <= 0) { has = true; z1 = double.NegativeInfinity; z2 = double.PositiveInfinity; } }
                    else
                    {
                        double disc = Bq * Bq - A * C;
                        if (disc >= 0) { double s = Math.Sqrt(disc); has = true; z1 = (-Bq - s) / A; z2 = (-Bq + s) / A; }
                    }
                    if (has)
                    {
                        if (Math.Abs(du) < 1e-9) { if (wu < 0 || wu > o.len) has = false; }
                        else
                        {
                            double s1 = -wu / du, s2 = (o.len - wu) / du;
                            z1 = Math.Max(z1, Math.Min(s1, s2)); z2 = Math.Min(z2, Math.Max(s1, s2));
                        }
                        if (has && z1 <= z2) { if (z1 < L) L = z1; if (z2 > H) H = z2; }
                    }
                }
                lo = L; hi = H;
                return H >= L;
            }
            if (o.custom != null)
            {
                var los = new GoreLineOfSight { ox = ox, oy = oy, oz = oz, dx = dx, dy = dy, dz = dz };
                return o.custom.Interval(in o.src, in los, out lo, out hi);
            }
            lo = hi = 0;
            return false;
        }

        // The interval inside one end sphere (w = O - centre), widened into the hull [L, H].
        static void SphereHull(double wx, double wy, double wz, double rr, in RemoverEnv env, ref double L, ref double H)
        {
            double Bq = wx * env.dvx + wy * env.dvy + wz * env.dvz, C = (wx * wx + wy * wy + wz * wz) - rr, disc = Bq * Bq - env.qa * C;
            if (disc < 0) return;
            double s = Math.Sqrt(disc), a = (-Bq - s) / env.qa, b = (-Bq + s) / env.qa;
            if (a < L) L = a;
            if (b > H) H = b;
        }

        /// <summary>The colour of the wound surface the remover exposes at member-space point q.</summary>
        public static uint Colour(ref PreparedRemover o, in RemoverEnv env, GoreStyle style, double qx, double qy, double qz)
        {
            uint[] flesh = style.flesh;
            if (o.kind == RemoverKinds.Plane)
            {
                double s = qx * o.e1x + qy * o.e1y + qz * o.e1z, t = qx * o.e2x + qy * o.e2y + qz * o.e2z;
                double rad = JsMath.Hypot(s, t) / o.rc;
                double hv = GoreRng.Hash(GoreRng.ToInt32(Math.Floor(s * 9) + 500), GoreRng.ToInt32(Math.Floor(t * 9) + 500), 21);
                if (env.bone && rad < 0.3) return style.bone[(int)(hv * 2)];
                if (rad > 0.82) return flesh[0];
                return flesh[(int)(hv * 3.2)];
            }
            if (o.kind == RemoverKinds.Capsule)
            {
                double h = GoreRng.Hash(GoreRng.ToInt32(Math.Floor(qx * 11) + 500), GoreRng.ToInt32(Math.Floor(qy * 11) + 500), GoreRng.ToInt32(Math.Floor(qz * 11) + 21));
                return GoreColour.Mix(flesh[(int)(h * 3.2)], style.crater, 0.45);
            }
            return o.custom != null ? o.custom.WoundColour(in o.src, style, qx, qy, qz) : flesh[0];
        }
    }
}

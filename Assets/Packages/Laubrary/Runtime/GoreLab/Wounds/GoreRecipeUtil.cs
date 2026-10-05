using System;
using System.Collections.Generic;

namespace Laubrary.GoreLab
{
    /// <summary>The numbers a pellet-style recipe (shotgun, bullet) feeds the shared shot generators.</summary>
    internal struct ShotTuning
    {
        public double energy, radius, toughness, rangeFalloff, straightDepth, coneDeg;
        public int pellets;
    }

    /// <summary>Generators and helpers shared by the recipes. All coordinates are sprite-local pixels of the frame being shown, so the sprite sits at (0, 0).</summary>
    internal static class GoreRecipeUtil
    {
        // Each member after the first draws its random numbers from its own stream, so two members hit by one shot do not get identical holes.
        const int MemberSeedStep = 7919;

        public static int MemberSeed(WoundContext ctx, int member)
        {
            return unchecked(ctx.seed + MemberSeedStep * member);
        }

        /// <summary>True when the member has a usable tag on this frame and the request may hit it.</summary>
        public static bool Usable(WoundContext ctx, int member)
        {
            if (ctx.members == null || member < 0 || member >= ctx.members.Length) return false;
            var m = ctx.members[member];
            if (!m.present || m.skip) return false;
            return ctx.targets == null || (member < ctx.targets.Length && ctx.targets[member]);
        }

        /// <summary>May a slice or neck cut sever this member? (True when the context says nothing.)</summary>
        public static bool CanSever(WoundContext ctx, int member)
            => ctx.sliceable == null || member < 0 || member >= ctx.sliceable.Length || ctx.sliceable[member];

        // A member that cannot be severed keeps a core: nothing may reach deeper than this, measured on the member's own unit shape (surface = 1).
        const double KeepCore = 0.5, MaxHoleRadius = 0.3;

        static double UnitNorm(double x, double y, double z, bool box, double n)
        {
            if (!box) return System.Math.Sqrt(x * x + y * y + z * z);
            n = System.Math.Max(1.5, n);
            return System.Math.Pow(System.Math.Pow(System.Math.Abs(x), n) + System.Math.Pow(System.Math.Abs(y), n) + System.Math.Pow(System.Math.Abs(z), n), 1.0 / n);
        }

        /// <summary>
        /// Makes a remover safe for a member that cannot be severed. A plane cut is dropped. A hole keeps its entry at the surface but is shortened
        /// (and its radius limited) so it never reaches the member's core, so whatever the holes do the core still ties the whole member together.
        /// Returns false when nothing of the remover is left.
        /// </summary>
        static bool MakeUnsevering(ref GoreRemover r, bool box, double squareness)
        {
            if (r.kind == RemoverKinds.Plane) return false;
            if (r.kind != RemoverKinds.Capsule) return true;       // custom kinds are the game's responsibility
            r.r = System.Math.Min(r.r, MaxHoleRadius);
            double limit = KeepCore + r.r;
            const int N = 24;
            int bestStart = -1, bestLen = 0, runStart = -1;
            for (int i = 0; i <= N + 1; i++)
            {
                bool ok = false;
                if (i <= N)
                {
                    double t = i / (double)N;
                    ok = UnitNorm(r.ax + (r.bx - r.ax) * t, r.ay + (r.by - r.ay) * t, r.az + (r.bz - r.az) * t, box, squareness) >= limit;
                }
                if (ok) { if (runStart < 0) runStart = i; }
                else if (runStart >= 0)
                {
                    int len = i - runStart;
                    if (len > bestLen) { bestLen = len; bestStart = runStart; }
                    runStart = -1;
                }
            }
            if (bestLen == 0) return false;
            double t0 = bestStart / (double)N, t1 = (bestStart + bestLen - 1) / (double)N;
            double ax = r.ax + (r.bx - r.ax) * t0, ay = r.ay + (r.by - r.ay) * t0, az = r.az + (r.bz - r.az) * t0;
            double bx = r.ax + (r.bx - r.ax) * t1, by = r.ay + (r.by - r.ay) * t1, bz = r.az + (r.bz - r.az) * t1;
            r.ax = ax; r.ay = ay; r.az = az; r.bx = bx; r.by = by; r.bz = bz;
            return true;
        }

        /// <summary>Gives the removers made for one member their group, member and noise position, then appends them.</summary>
        public static void Append(WoundContext ctx, List<GoreRemover> into, int member, List<GoreRemover> made)
        {
            if (made.Count == 0) return;
            // The ragged edge of a member is seeded by the remover's position among that member's removers.
            int n = 0;
            for (int i = 0; i < ctx.existing.Count; i++) if (ctx.existing[i].member == member) n++;
            for (int i = 0; i < into.Count; i++) if (into[i].member == member && into[i].group == ctx.group) n++;
            bool unsevering = !CanSever(ctx, member) && ctx.members != null && member < ctx.members.Length;
            for (int i = 0; i < made.Count; i++)
            {
                var r = made[i];
                if (unsevering && !MakeUnsevering(ref r, ctx.members[member].tag.kind == MemberKind.Box, ctx.members[member].tag.n)) continue;
                r.group = ctx.group;
                r.member = member;
                r.noiseIndex = n++;
                into.Add(r);
            }
        }

        public static GoreRemover Capsule(WoundContext ctx, int member, in MemberTag t, double x0, double y0, double z0, double x1, double y1, double z1, double radius, double backX, double backY)
        {
            var (aa, ab, ac) = GoreTagMath.ToUnit(t, x0, y0, z0);
            var (ba, bb, bc) = GoreTagMath.ToUnit(t, x1, y1, z1);
            return new GoreRemover
            {
                kind = RemoverKinds.Capsule,
                group = ctx.group,
                member = member,
                ax = aa, ay = ab, az = ac,
                bx = ba, by = bb, bz = bc,
                r = radius,
                backX = backX,
                backY = backY,
            };
        }

        static double Gauss(Func<double> R) { return (R() + R() + R() + R() - 2) / 0.58; }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>A shotgun blast at one member: pellets fly in the screen plane at their own random depth, so the same blast is right whichever way the member later turns.</summary>
        public static void Shotgun(WoundContext ctx, int member, in ShotTuning s, List<GoreRemover> made)
        {
            var h = ctx.members[member].tag;
            double cx = h.cx, cy = h.cy, mean = (h.rx + h.ry) / 2, rz = GoreTagMath.Depth(h);
            var R = GoreRng.Rng(MemberSeed(ctx, member));
            double baseAngle = Math.Atan2(ctx.p1y - ctx.p0y, ctx.p1x - ctx.p0x), cone = (s.coneDeg * Math.PI) / 180;
            double dc = JsMath.Hypot(cx - ctx.p0x, cy - ctx.p0y);
            double fall = Math.Max(0.35, 1 - s.rangeFalloff * Math.Max(0, dc - 10) / 100);
            for (int i = 0; i < s.pellets; i++)
            {
                double off = Clamp(Gauss(R) * 0.55, -1, 1), ang = baseAngle + off * cone;
                double e = s.energy * (0.55 + 0.9 * R()) * (1 - 0.45 * Math.Abs(off));
                double dx = Math.Cos(ang), dy = Math.Sin(ang);
                double tc = (cx - ctx.p0x) * dx + (cy - ctx.p0y) * dy, lat = Math.Abs((cx - ctx.p0x) * dy - (cy - ctx.p0y) * dx);
                double z = (R() * 2 - 1) * 0.9 * rz;                      // how far toward or away from the viewer this pellet flies
                if (lat > mean * 1.15 || tc < -mean) continue;           // misses the member
                double entry = tc - Math.Sqrt(Math.Max(0, mean * mean - lat * lat - z * z * 0.5));
                double pen = (e * fall / Math.Max(0.2, s.toughness)) * 1.1;
                if (pen <= 0.3) continue;
                double rpx = s.radius * (0.55 + 0.75 * Math.Min(1.2, (e / s.energy) * fall));
                double t0 = Math.Max(entry, -mean) - 4, t1 = Math.Max(entry, 0) + pen;
                made.Add(Capsule(ctx, member, h,
                    ctx.p0x + dx * t0 - cx, ctx.p0y + dy * t0 - cy, z,
                    ctx.p0x + dx * t1 - cx, ctx.p0y + dy * t1 - cy, z,
                    Math.Max(0.05, rpx / mean), 0, 0));
            }
        }

        /// <summary>
        /// Straight-on shots: the bullet comes from the viewer's side, so the aim line only decides WHERE on the member it lands. Each pellet takes one
        /// random point on the part of its aim line that crosses the member's visible pixels and digs a shallow hole straight into the screen.
        /// </summary>
        public static void Straight(WoundContext ctx, int member, in ShotTuning s, int count, List<GoreRemover> made)
        {
            var h = ctx.members[member].tag;
            double cx = h.cx, cy = h.cy, mean = (h.rx + h.ry) / 2;
            var R = GoreRng.Rng(MemberSeed(ctx, member));
            double baseAngle = Math.Atan2(ctx.p1y - ctx.p0y, ctx.p1x - ctx.p0x), cone = (s.coneDeg * Math.PI) / 180;
            double dc = JsMath.Hypot(cx - ctx.p0x, cy - ctx.p0y);
            double fall = Math.Max(0.35, 1 - s.rangeFalloff * Math.Max(0, dc - 10) / 100);
            var hits = new List<double>();
            for (int i = 0; i < count; i++)
            {
                double off = count > 1 ? Clamp(Gauss(R) * 0.55, -1, 1) : 0, ang = baseAngle + off * cone;
                double e = s.energy * (0.55 + 0.9 * R()) * (1 - 0.45 * Math.Abs(off));
                double dx = Math.Cos(ang), dy = Math.Sin(ang);

                // Every half pixel along the line that lies on the member's solid, visible pixels.
                hits.Clear();
                for (double t = 0; t < 300; t += 0.5)
                {
                    double x = ctx.p0x + dx * t, y = ctx.p0y + dy * t;
                    if (GoreTagMath.InsideOutline(h, x, y) && (ctx.grid == null || ctx.grid.Solid((int)Math.Floor(x), (int)Math.Floor(y)))) hits.Add(t);
                }
                if (hits.Count == 0) continue;

                double pen = ((e * fall) / Math.Max(0.2, s.toughness)) * 1.1 * s.straightDepth;
                double rpx = s.radius * (0.55 + 0.75 * Math.Min(1.2, (e / s.energy) * fall));

                // With a visibility score, up to ten random spots are tried and the best kept, so a bullet is not wasted where most frames show nothing.
                GoreRemover best = default;
                double bestScore = -1;
                int tries = ctx.holeVisibility != null ? Math.Min(10, hits.Count) : 1;
                for (int k = 0; k < tries && bestScore < 1; k++)
                {
                    double t = hits[(int)(R() * hits.Count)];
                    double x = ctx.p0x + dx * t, y = ctx.p0y + dy * t, ox = x - cx, oy = y - cy;
                    double zf = GoreSolid.FrontDepth(h, ox, oy);
                    if (double.IsNaN(zf)) zf = 0;
                    var op = Capsule(ctx, member, h, ox, oy, zf + 3, ox, oy, zf - Math.Max(0.6, pen), Math.Max(0.05, rpx / mean), -dx, -dy);
                    double score = ctx.holeVisibility != null ? ctx.holeVisibility(op) : 1;
                    if (score > bestScore) { bestScore = score; best = op; }
                }
                made.Add(best);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// A clean slice along the swipe. The cut is a flat plane that contains the line of sight, so it lands on the same place of the body from any
    /// direction; the piece without the neck is the one that flies off.
    /// </summary>
    [Serializable]
    public sealed class SliceRecipe : IWoundRecipe
    {
        [Header("Swipe")]
        [Tooltip("Swipes shorter than this many pixels cut nothing.")]
        public double minSwipeLength = 2;
        [Tooltip("A cut plane further than this fraction of the member's size from its centre misses the member entirely (1 = grazing).")]
        public double planeMissLimit = 0.98;

        [Header("Which side flies")]
        [Tooltip("When the cut passes this close to the neck (in member sizes) the side is decided by the plane's tilt instead of by where the neck lies.")]
        public double neckTolerance = 0.03;

        // The swipe line is sampled this far along both ways to decide which member it crosses most.
        const double CrossingReach = 150;
        const double CrossingStep = 0.5;

        public string DisplayName { get { return "Slice"; } }

        public void Generate(WoundContext ctx, List<GoreRemover> into)
        {
            // Pick the member: the only usable one, or the one the swipe line crosses on most solid pixels (first wins ties).
            int member = -1, bestCount = -1;
            int usable = 0;
            for (int i = 0; i < ctx.members.Length; i++) if ((GoreRecipeUtil.Usable(ctx, i) && GoreRecipeUtil.CanSever(ctx, i))) usable++;
            for (int i = 0; i < ctx.members.Length; i++)
            {
                if (!(GoreRecipeUtil.Usable(ctx, i) && GoreRecipeUtil.CanSever(ctx, i))) continue;
                int count = usable > 1 ? CrossingCount(ctx, i) : 0;
                if (count > bestCount) { bestCount = count; member = i; }
            }
            if (member < 0) return;

            if (!TryPlane(ctx, member, out double nx, out double ny, out double nz, out double d)) return;
            var made = new List<GoreRemover>(1)
            {
                new GoreRemover { kind = RemoverKinds.Plane, nx = nx, ny = ny, nz = nz, d = d },
            };
            GoreRecipeUtil.Append(ctx, into, member, made);
        }

        int CrossingCount(WoundContext ctx, int member)
        {
            var tag = ctx.members[member].tag;
            double dx = ctx.p1x - ctx.p0x, dy = ctx.p1y - ctx.p0y, len = JsMath.Hypot(dx, dy);
            if (len == 0) len = 1;
            dx /= len; dy /= len;
            int n = 0;
            for (double t = -CrossingReach; t <= CrossingReach; t += CrossingStep)
            {
                double x = ctx.p0x + dx * t, y = ctx.p0y + dy * t;
                if (GoreTagMath.InsideOutline(tag, x, y) && (ctx.grid == null || ctx.grid.Solid((int)Math.Floor(x), (int)Math.Floor(y)))) n++;
            }
            return n;
        }

        bool TryPlane(WoundContext ctx, int member, out double nx, out double ny, out double nz, out double d)
        {
            nx = ny = nz = d = 0;
            var h = ctx.members[member].tag;
            double dx = ctx.p1x - ctx.p0x, dy = ctx.p1y - ctx.p0y, l = JsMath.Hypot(dx, dy);
            if (l < minSwipeLength) return false;

            // The swipe's normal on screen, and its signed distance from the member centre.
            double nsx = -dy / l, nsy = dx / l;
            double o = nsx * (ctx.p0x - h.cx) + nsy * (ctx.p0y - h.cy);

            // The plane contains the line of sight, so its normal in member coordinates is the screen normal read through each member axis.
            var e = GoreTagMath.East(h);
            double mx = h.rx * (nsx * e.x + nsy * e.y), my = h.ry * (nsx * h.ux + nsy * h.uy), mz = GoreTagMath.Depth(h) * (nsx * h.fx + nsy * h.fy);
            double ml = JsMath.Hypot(mx, my, mz);
            if (ml < 1e-6) return false;
            nx = mx / ml; ny = my / ml; nz = mz / ml; d = o / ml;
            if (Math.Abs(d) >= planeMissLimit) return false;

            // The flying side is the one without the neck, which sits at (0,-1,0) in member coordinates.
            double neckSide = -ny - d;
            bool flip = Math.Abs(neckSide) > neckTolerance ? neckSide > 0 : ny < 0;
            if (ctx.flipSide) flip = !flip;
            if (flip) { nx = -nx; ny = -ny; nz = -nz; d = -d; }
            return true;
        }
    }
}

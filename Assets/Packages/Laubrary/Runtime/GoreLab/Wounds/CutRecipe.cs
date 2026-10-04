using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// A shallow knife slash: a groove dug straight in from the viewer along the swipe, made of small overlapping dents. The line wobbles sideways
    /// and in depth and now and then breaks, so it is never a perfect line. Hits every tagged member the swipe touches.
    /// </summary>
    [Serializable]
    public sealed class CutRecipe : IWoundRecipe
    {
        [Header("Groove")]
        [Tooltip("Average depth of the groove into the body, in pixels. The real depth varies between about half and one and a half times this, never under 0.6.")]
        public double gashDepth = 1.6;
        [Tooltip("Radius of the groove in pixels.")]
        public double gashWidth = 0.8;
        [Tooltip("How far the groove wanders sideways from the swipe line, in pixels.")]
        public double gashWobble = 0.9;

        // Spacing between dents; kept below the groove radius so neighbouring dents overlap into one groove.
        const double Spacing = 0.8;

        public string DisplayName { get { return "Cut"; } }

        public void Generate(WoundContext ctx, List<GoreRemover> into)
        {
            for (int i = 0; i < ctx.members.Length; i++)
            {
                if (!GoreRecipeUtil.Usable(ctx, i)) continue;
                var made = new List<GoreRemover>();
                Groove(ctx, i, made);
                GoreRecipeUtil.Append(ctx, into, i, made);
            }
        }

        void Groove(WoundContext ctx, int member, List<GoreRemover> made)
        {
            var h = ctx.members[member].tag;
            double cx = h.cx, cy = h.cy, mean = (h.rx + h.ry) / 2;
            double dx0 = ctx.p1x - ctx.p0x, dy0 = ctx.p1y - ctx.p0y, len = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
            if (len < 1) return;
            double dx = dx0 / len, dy = dy0 / len, nx = -dy, ny = dx;
            int seed = GoreRecipeUtil.MemberSeed(ctx, member);

            // Blood and crumbs leave sideways from the groove, toward the upper side of the screen.
            double upX = ny < 0 ? nx : -nx, upY = ny < 0 ? ny : -ny;

            // t is advanced by repeated addition, as the original does, so the sample positions agree to the last bit.
            for (double t = 0; t <= len; t += Spacing)
            {
                if (Noise(t * 0.22, seed, 91) > 0.9) continue;                   // now and then the line breaks for a moment
                double w = (Noise(t * 0.35, seed, 17) * 2 - 1) * gashWobble;
                double x = ctx.p0x + dx * t + nx * w, y = ctx.p0y + dy * t + ny * w;
                if (!GoreTagMath.InsideOutline(h, x, y)) continue;
                if (ctx.grid != null && !ctx.grid.Solid((int)Math.Floor(x), (int)Math.Floor(y))) continue;

                double ox = x - cx, oy = y - cy;
                double zf = GoreSolid.FrontDepth(h, ox, oy);
                if (double.IsNaN(zf)) zf = 0;
                double dep = Math.Max(0.6, gashDepth * (0.55 + 0.9 * Noise(t * 0.5, seed, 33)));
                made.Add(GoreRecipeUtil.Capsule(ctx, member, h, ox, oy, zf + 3, ox, oy, zf - dep, Math.Max(0.05, gashWidth / mean), upX, upY));
            }
        }

        // Smooth 1D value noise in [0,1): the hash of the two neighbouring whole cells, blended with a smoothstep.
        static double Noise(double x, int seed, int channel)
        {
            double fl = Math.Floor(x), f = x - fl, u = f * f * (3 - 2 * f);
            int i = (int)fl;
            return GoreRng.Hash(i, seed, channel) * (1 - u) + GoreRng.Hash(i + 1, seed, channel) * u;
        }
    }
}

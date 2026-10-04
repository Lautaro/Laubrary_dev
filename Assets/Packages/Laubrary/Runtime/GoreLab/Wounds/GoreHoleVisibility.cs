using System;
using System.Collections.Generic;

namespace Laubrary.GoreLab
{
    /// <summary>
    /// How much of an animation shows a hole: used by damage types that choose a spot (a bullet), so a hole is not wasted on a place most frames hide
    /// (an arm gap, under the head, outside the marked shape). The game and the editor's Test tab both call this, so a test bullet lands where a real one would.
    /// </summary>
    public static class GoreHoleVisibility
    {
        /// <summary>The share (0..1) of the given views, each a frame's pixels and member data, in which the remover's hole would land on a solid, uncovered pixel of its member.</summary>
        public static double Score(GoreRemover op, IReadOnlyList<GoreGrid> grids, IReadOnlyList<GoreMemberInput[]> members)
        {
            if (grids == null || grids.Count == 0) return 1;
            int seen = 0, total = 0;
            for (int i = 0; i < grids.Count; i++)
            {
                GoreGrid grid = grids[i];
                GoreMemberInput[] mem = members[i];
                if (grid == null || mem == null || op.member < 0 || op.member >= mem.Length) continue;
                GoreMemberInput m = mem[op.member];
                if (!m.present || m.skip) continue;
                total++;
                var s = GoreTagMath.ToScreen(m.tag, (op.ax + op.bx) * 0.5, (op.ay + op.by) * 0.5, (op.az + op.bz) * 0.5);
                double px = m.tag.cx + s.x, py = m.tag.cy + s.y;
                int ix = (int)Math.Floor(px), iy = (int)Math.Floor(py);
                if (!grid.Solid(ix, iy) || !GoreTagMath.InsideOutline(m.tag, px, py)) continue;
                int idx = iy * grid.w + ix;
                if (m.exempt != null && m.exempt[idx] != 0) continue;
                if (op.member != 0 && mem.Length > 0)
                {
                    GoreMemberInput head = mem[0];                     // the head owns the pixels under it
                    if (head.present && !head.skip && GoreTagMath.InsideOutline(head.tag, px, py) && !(head.exempt != null && head.exempt[idx] != 0)) continue;
                }
                seen++;
            }
            return total == 0 ? 1 : (double)seen / total;
        }
    }
}

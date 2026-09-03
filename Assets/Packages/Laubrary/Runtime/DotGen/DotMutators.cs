// DotMutators.cs
// The three mutators. They run in list order, each one seeing what the one before it left, which is why
// reordering the list changes the result and why the order is editable.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.DotGen
{
    /// Push every dot a small deterministic distance, optionally biased toward one direction.
    [Serializable]
    [DotModule("nudge", "Nudge", "Nudge", Order = 0)]
    public class DotNudgeMutator : DotMutator
    {
        [Range(0f, 20f)]
        [Tooltip("Farthest a dot can move, as a percentage of the area's shorter side.")]
        public float strength = 3f;

        [Range(0, 999)]
        [Tooltip("Changes which way each dot goes, without changing how far.")]
        public int seedOffset = 31;

        [Range(0f, 100f)]
        [Tooltip("Pulls the random directions toward one shared angle. At 100% every dot moves the same way.")]
        public float directionBias = 0f;

        // The reference hides this dial while the bias is zero. [ZUIShowIf] matches a sibling's VALUE, and
        // "any value above zero" is not a value, so the dial stays visible here rather than being gated by a
        // hand-rolled condition inside one card. It reads as inert at bias 0, which it is.
        [Range(-180f, 180f)]
        [Tooltip("The direction the bias pulls toward. Does nothing while Direction bias is zero.")]
        public float biasAngle = 0f;

        public override void Apply(DotGenerator gen, in DotArea area, List<DotPoint> pts, int instIdx, int globalSeed, DotSelector sel, List<DotPoint> removed)
        {
            float minSide = Mathf.Min(area.w, area.h);
            float ba = biasAngle * Mathf.Deg2Rad;
            float b = directionBias / 100f;

            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                float w = sel == null ? 1f : sel.Weight(gen, area, p.x, p.y, i + instIdx * 997, globalSeed);
                float rnd = (float)DotGenMath.Hash01(globalSeed + seedOffset, p.key, instIdx);
                float rnd2 = (float)DotGenMath.Hash01(globalSeed + seedOffset + 1, p.key, instIdx);
                float ra = rnd * DotGenMath.Tau;
                float an = Mathf.Atan2(DotGenMath.Lerp(Mathf.Sin(ra), Mathf.Sin(ba), b),
                                       DotGenMath.Lerp(Mathf.Cos(ra), Mathf.Cos(ba), b));
                float mag = strength / 100f * minSide * w * (0.35f + 0.65f * rnd2);
                p.x += Mathf.Cos(an) * mag;
                p.y += Mathf.Sin(an) * mag;
                pts[i] = p;
            }
        }
    }

    /// Where a warp's field comes from.
    public enum DotWarpField { PointCenters, ParallelLines }

    /// Which way a warp pushes.
    public enum DotWarpForce { Pull, Push }

    /// Pull dots toward, or push them away from, a set of deterministic centres or a set of parallel lines.
    [Serializable]
    [DotModule("warp", "Warp field", "Warp field", Order = 1)]
    public class DotWarpMutator : DotMutator
    {
        [Tooltip("Warp toward scattered centres, or toward a set of evenly spaced lines.")]
        public DotWarpField fieldSource = DotWarpField.PointCenters;

        [Tooltip("Gather the dots in, or drive them apart.")]
        public DotWarpForce force = DotWarpForce.Pull;

        [Range(0f, 25f)]
        [Tooltip("Farthest a dot can move, as a percentage of the area's shorter side.")]
        public float strength = 7f;

        [Range(1, 8)]
        [ZUIShowIf("fieldSource", "PointCenters")]
        [Tooltip("How many centres the dots gather around.")]
        public int centers = 2;

        [Range(5f, 120f)]
        [ZUIShowIf("fieldSource", "PointCenters")]
        [Tooltip("How far a centre's pull reaches, as a percentage of the area's longer side.")]
        public float influenceRadius = 45f;

        [Range(0, 999)]
        [ZUIShowIf("fieldSource", "PointCenters")]
        [Tooltip("Moves the centres somewhere else inside the area.")]
        public int seedOffset = 71;

        [Range(-180f, 180f)]
        [ZUIShowIf("fieldSource", "ParallelLines")]
        [Tooltip("Which way the lines run.")]
        public float lineAngle = 0f;

        [Range(5f, 80f)]
        [ZUIShowIf("fieldSource", "ParallelLines")]
        [Tooltip("Distance between the lines, as a percentage of the area's shorter side.")]
        public float lineSpacing = 24f;

        /// Where this warp's centres land in one area. Public because the gizmo draws exactly these.
        public void Centers(in DotArea a, int globalSeed, List<Vector2> outCenters)
        {
            for (int i = 0; i < centers; i++)
                outCenters.Add(new Vector2(
                    a.cx + ((float)DotGenMath.Hash01(globalSeed + seedOffset, a.idx, i) - 0.5f) * a.w * 0.75f,
                    a.cy + ((float)DotGenMath.Hash01(globalSeed + seedOffset + 9, a.idx, i) - 0.5f) * a.h * 0.75f));
        }

        public override void Apply(DotGenerator gen, in DotArea area, List<DotPoint> pts, int instIdx, int globalSeed, DotSelector sel, List<DotPoint> removed)
        {
            float minSide = Mathf.Min(area.w, area.h);
            float sg = force == DotWarpForce.Pull ? 1f : -1f;

            if (fieldSource == DotWarpField.PointCenters)
            {
                var cs = new List<Vector2>(centers);
                Centers(area, globalSeed, cs);
                float reach = (influenceRadius / 100f) * Mathf.Max(area.w, area.h);

                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    float w = sel == null ? 1f : sel.Weight(gen, area, p.x, p.y, i + instIdx * 997, globalSeed);
                    for (int c = 0; c < cs.Count; c++)
                    {
                        float dx = cs[c].x - p.x, dy = cs[c].y - p.y;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d == 0f) d = 0.001f;
                        float fall = DotGenMath.Clamp01(1f - d / Mathf.Max(0.001f, reach));
                        float amt = strength / 100f * minSide * fall * w * sg;
                        p.x += dx / d * amt;
                        p.y += dy / d * amt;
                    }
                    pts[i] = p;
                }
                return;
            }

            float an = lineAngle * Mathf.Deg2Rad;
            float nx = -Mathf.Sin(an), ny = Mathf.Cos(an);
            float spacing = Mathf.Max(0.01f, lineSpacing / 100f * minSide);

            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                float w = sel == null ? 1f : sel.Weight(gen, area, p.x, p.y, i + instIdx * 997, globalSeed);
                float d = (p.x - area.cx) * nx + (p.y - area.cy) * ny;
                float nearest = DotGenMath.RoundJs(d / spacing) * spacing;
                float delta = nearest - d;
                float fall = 1f - Mathf.Min(1f, Mathf.Abs(delta) / (spacing * 0.5f));
                float amt = strength / 100f * minSide * fall * w * sg;
                float sign = delta == 0f ? 1f : Mathf.Sign(delta);
                p.x += nx * sign * amt;
                p.y += ny * sign * amt;
                pts[i] = p;
            }
        }
    }

    /// Which side of a selector gets removed.
    public enum DotCullAction { CullUnselected, CullSelected }

    /// Remove dots, deterministically, according to a selector's weight.
    [Serializable]
    [DotModule("cull", "Cull", "Cull", Order = 2)]
    public class DotCullMutator : DotMutator
    {
        [Tooltip("Remove the dots the selector did NOT pick, or the ones it did.")]
        public DotCullAction cullAction = DotCullAction.CullUnselected;

        [Range(0, 999)]
        [Tooltip("Changes which dots survive a partial weight, without changing how many.")]
        public int seedOffset = 43;

        public override void Apply(DotGenerator gen, in DotArea area, List<DotPoint> pts, int instIdx, int globalSeed, DotSelector sel, List<DotPoint> removed)
        {
            int write = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                float w = sel == null ? 1f : sel.Weight(gen, area, p.x, p.y, i + instIdx * 997, globalSeed);
                double r = DotGenMath.Hash01(globalSeed + seedOffset, p.key, instIdx);
                bool kill = cullAction == DotCullAction.CullSelected ? r < w : r > w;
                if (kill) { removed.Add(p); continue; }
                pts[write++] = p;
            }
            pts.RemoveRange(write, pts.Count - write);
        }
    }
}

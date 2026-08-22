// ExtrudeModule — extrudes a flat 2D shape up and down independently, with a per-direction beveled edge:
// Steps controls how the edge looks (1 = a hard square 90° corner; more steps = a curved edge, subdivided
// into that many segments), Bevel Angle controls how far the edge arcs, Bevel Size its radius, and Convex/
// Concave whether it bulges outward (rounded-over, like a pillow) or inward (a cove/scallop).
//
// REGIONS — splitting the shape's area into independently-extruded sections (a circle divided into pie
// slices, each with its own height, with control over whether neighbouring sections fuse or stay separate)
// is DELIBERATELY NOT built here. That is a genuinely separate, much larger feature — arbitrary polygon
// partitioning plus per-region extrusion plus boundary-fuse/separate logic is real 2D mesh/CSG work, not an
// incremental addition to this module, and rushing a first pass risked shipping something that looks like
// it works but breaks on any shape past the simplest case. Flagged here rather than silently skipped.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModuleInfo("Extrude", "Sweep", "square")]
    [Serializable]
    public class ExtrudeModule : LatheModule
    {
        // The flat footprint, in the local XZ plane. A closed polygon — the last point implicitly connects
        // back to the first, same convention as Profile Sweep / Skeleton Sweep's cross-sections.
        public List<Vector2> shape = new List<Vector2>
        {
            new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f),
        };

        [Range(0.02f, 4f)] public float upHeight = 0.3f;
        [Range(1, 10)] public int upSteps = 1;
        [Range(1f, 179f)] public float upBevelAngle = 90f;
        [Range(0.001f, 2f)] public float upBevelSize = 0.1f;
        public bool upConvex = true;

        [Range(0.02f, 4f)] public float downHeight = 0.3f;
        [Range(1, 10)] public int downSteps = 1;
        [Range(1f, 179f)] public float downBevelAngle = 90f;
        [Range(0.001f, 2f)] public float downBevelSize = 0.1f;
        public bool downConvex = true;

        public bool capTopBottom = true;

        public override string DisplayName => "Extrude";
        public override string Description =>
            "Extrudes a flat 2D shape up and down independently, each side with its own beveled-edge "
            + "profile — Steps=1 is a hard square edge, more steps curve it; Convex rounds over, Concave "
            + "coves inward.";

        public override void Generate(LatheMeshData data)
        {
            if (shape == null || shape.Count < 3) return;
            int n = shape.Count;

            var normals2D = ComputeOutwardNormals(shape);

            var upProfile = BuildBevelProfile(upHeight, upSteps, upBevelSize, upBevelAngle, upConvex);
            var downProfileRaw = BuildBevelProfile(downHeight, downSteps, downBevelSize, downBevelAngle, downConvex);

            var fullProfile = new List<Vector2>();
            for (int k = downProfileRaw.Count - 1; k >= 1; k--)
                fullProfile.Add(new Vector2(downProfileRaw[k].x, -downProfileRaw[k].y));
            fullProfile.AddRange(upProfile);

            int levels = fullProfile.Count;
            if (levels < 2) return;
            var ring = new int[n, levels];
            for (int i = 0; i < n; i++)
            {
                Vector2 sp = shape[i];
                Vector2 nrm = normals2D[i];
                for (int lvl = 0; lvl < levels; lvl++)
                {
                    Vector2 prof = fullProfile[lvl];
                    Vector3 pos = new Vector3(sp.x + nrm.x * prof.x, prof.y, sp.y + nrm.y * prof.x);
                    ring[i, lvl] = data.AddVert(pos, Vector3.up);   // placeholder — recalculated below
                }
            }

            for (int i = 0; i < n; i++)
            {
                int ni = (i + 1) % n;
                for (int lvl = 0; lvl < levels - 1; lvl++)
                    data.AddQuad(ring[i, lvl], ring[ni, lvl], ring[ni, lvl + 1], ring[i, lvl + 1]);
            }

            if (capTopBottom && n >= 3)
            {
                // Simple triangle fan — correct for a convex shape (the default square), an approximation
                // for a concave one; same caveat as Profile Sweep's own end caps.
                for (int p = 1; p < n - 1; p++) data.AddTri(ring[0, 0], ring[p + 1, 0], ring[p, 0]);
                int top = levels - 1;
                for (int p = 1; p < n - 1; p++) data.AddTri(ring[0, top], ring[p, top], ring[p + 1, top]);
            }

            // The bevels and caps make hand-computed normals not worth the complexity here (contrast
            // Profile/Skeleton Sweep's single radial-outward formula) — clearing lets ToMesh's
            // RecalculateNormals do it properly from the finished geometry.
            data.normals.Clear();
        }

        // A quarter-ish arc (or a partial sweep of it) from the wall's top down to where the bevel ends,
        // in LOCAL (radial-inset r <= 0, height h) space with the wall's base at (0,0). Steps<=1 skips the
        // bevel entirely — literally the "1 step = 90 degree edge" case, no curve at all.
        static List<Vector2> BuildBevelProfile(float wallHeight, int steps, float bevelSize, float bevelAngleDeg, bool convex)
        {
            var pts = new List<Vector2> { Vector2.zero };
            if (steps <= 1)
            {
                pts.Add(new Vector2(0f, wallHeight));
                return pts;
            }
            float bevelR = Mathf.Clamp(bevelSize, 0.0001f, wallHeight);
            float wallTopH = wallHeight - bevelR;
            pts.Add(new Vector2(0f, wallTopH));
            float sweepDeg = Mathf.Clamp(bevelAngleDeg, 1f, 179f);
            for (int j = 1; j <= steps; j++)
            {
                float t = (float)j / steps;
                Vector2 p;
                if (convex)
                {
                    // Centre inset by bevelR — the arc bulges OUTWARD (toward r=0) relative to a straight
                    // chamfer between the same two endpoints, i.e. rounds over.
                    float theta = t * sweepDeg * Mathf.Deg2Rad;
                    p = new Vector2(-bevelR + bevelR * Mathf.Cos(theta), wallTopH + bevelR * Mathf.Sin(theta));
                }
                else
                {
                    // Centre at the OUTER corner (0, wallHeight) — the arc bulges INWARD relative to the
                    // same chamfer, i.e. coves/recesses.
                    float theta = (270f - t * sweepDeg) * Mathf.Deg2Rad;
                    p = new Vector2(bevelR * Mathf.Cos(theta), wallHeight + bevelR * Mathf.Sin(theta));
                }
                pts.Add(p);
            }
            return pts;
        }

        // Per-vertex outward normal for an arbitrary (not necessarily convex) closed polygon: average the
        // two adjacent edges' perpendiculars, then flip toward-centroid if needed — robust to either
        // winding direction, the same "flip toward centroid" trick used elsewhere for a single edge.
        static Vector2[] ComputeOutwardNormals(List<Vector2> shape)
        {
            int n = shape.Count;
            var normals = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = shape[(i - 1 + n) % n], cur = shape[i], next = shape[(i + 1) % n];
                Vector2 e1 = (cur - prev).sqrMagnitude > 1e-10f ? (cur - prev).normalized : Vector2.right;
                Vector2 e2 = (next - cur).sqrMagnitude > 1e-10f ? (next - cur).normalized : e1;
                Vector2 n1 = new Vector2(e1.y, -e1.x);
                Vector2 n2 = new Vector2(e2.y, -e2.x);
                Vector2 avg = n1 + n2;
                normals[i] = avg.sqrMagnitude > 1e-8f ? avg.normalized : n1;
            }
            Vector2 centroid = Vector2.zero;
            foreach (var p in shape) centroid += p;
            centroid /= n;
            for (int i = 0; i < n; i++)
                if (Vector2.Dot(normals[i], shape[i] - centroid) < 0f) normals[i] = -normals[i];
            return normals;
        }
    }
}

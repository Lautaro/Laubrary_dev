// ProfileSweepModule — sweeps a 2D cross-section (the "section") along a path (the "frame") to build a
// tube/rail solid. This is the core new idea behind Lathe: define one section, stamp it along a skeleton.
//
// v1 offers ANALYTIC paths only (Line/Arc/Circle/Helix), not a hand-authored "connect the lines" skeleton
// with real corners — that needs click-to-place gizmo authoring in the preview (a genuine future editor
// feature, not a dial) and was the one open question in the original ask ("not sure how corners would be
// handled"). These four paths are smooth by construction, so v1 sidesteps the corner question entirely
// while proving the sweep pipeline itself (frame construction, ring stamping, capping) end to end — a
// sharp-corner skeleton editor can reuse all of it once it exists; only SamplePath would need a new case.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum SweepPathKind { Line, Arc, Circle, Helix }

    [LatheModuleInfo("Profile Sweep", "Sweep", "path")]
    [Serializable]
    public class ProfileSweepModule : LatheModule
    {
        // The cross-section, in the swept frame's own local (normal, binormal) axes. Treated as a CLOSED
        // polygon — the last point implicitly connects back to the first. A pixel-grid authoring UI for
        // this (the user's original idea) is future work; v1 authors it as a plain point list.
        public List<Vector2> profile = new List<Vector2>
        {
            new Vector2(-0.1f, -0.1f), new Vector2(0.1f, -0.1f), new Vector2(0.1f, 0.1f), new Vector2(-0.1f, 0.1f),
        };

        public SweepPathKind path = SweepPathKind.Circle;
        [Range(0.1f, 8f)] public float length = 2f;          // Line length
        [Range(0.05f, 5f)] public float radius = 1f;          // Arc/Circle/Helix radius
        [Range(1f, 360f)] public float arcDegrees = 180f;     // Arc sweep angle (Circle is always a full 360)
        [Range(1, 8)] public int helixTurns = 2;
        [Range(0f, 3f)] public float helixRise = 0.4f;        // world-Y rise PER TURN
        [Range(0f, 360f)] public float twistPerTurn = 0f;     // extra roll of the profile per full path revolution
        [Range(2, 200)] public int pathSegments = 48;
        public bool capEnds = true;                            // ignored when the path is closed (Circle)

        public override string DisplayName => "Profile Sweep";
        public override string Description =>
            "Sweeps a 2D cross-section along an analytic path (a straight extrusion, an arc, a full ring, or a "
            + "coil) to build a tube/rail solid. The profile is a closed polygon authored as a point list.";

        public override void Generate(LatheMeshData data)
        {
            if (profile == null || profile.Count < 3) return;
            bool closed = path == SweepPathKind.Circle;
            int steps = Mathf.Max(2, pathSegments);
            int pointCount = closed ? steps : steps + 1;

            var centers = new Vector3[pointCount];
            var tangents = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
                SamplePath((float)i / steps, out centers[i], out tangents[i]);

            // Parallel-transport frame: seed a normal orthogonal to the first tangent, then re-orthogonalise
            // it against each new tangent in turn. Avoids the flips a naive "world up" frame gets whenever
            // the path tangent swings past vertical — Circle and Helix both do this every revolution.
            var normals = new Vector3[pointCount];
            var binormals = new Vector3[pointCount];
            Vector3 seed = Mathf.Abs(Vector3.Dot(tangents[0], Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
            normals[0] = Vector3.ProjectOnPlane(seed, tangents[0]).normalized;
            binormals[0] = Vector3.Cross(tangents[0], normals[0]);
            for (int i = 1; i < pointCount; i++)
            {
                Vector3 n = Vector3.ProjectOnPlane(normals[i - 1], tangents[i]);
                if (n.sqrMagnitude < 1e-8f) n = Vector3.ProjectOnPlane(binormals[i - 1], tangents[i]);
                normals[i] = n.normalized;
                binormals[i] = Vector3.Cross(tangents[i], normals[i]);
            }
            if (closed)
            {
                // The transported frame rarely lines up with the start after a full loop — undo the
                // accumulated drift evenly across every ring so the seam doesn't visibly kink.
                float drift = Vector3.SignedAngle(normals[0], normals[pointCount - 1], tangents[0]);
                for (int i = 0; i < pointCount; i++)
                {
                    var q = Quaternion.AngleAxis(-drift * (i / (float)pointCount), tangents[i]);
                    normals[i] = q * normals[i];
                    binormals[i] = q * binormals[i];
                }
            }

            int profCount = profile.Count;
            var ring = new int[pointCount, profCount];
            for (int i = 0; i < pointCount; i++)
            {
                float turnFrac = (float)i / steps;
                float twist = twistPerTurn * turnFrac * (path == SweepPathKind.Helix ? helixTurns : 1f);
                Quaternion roll = Quaternion.AngleAxis(twist, tangents[i]);
                Vector3 n = roll * normals[i], b = roll * binormals[i];
                for (int p = 0; p < profCount; p++)
                {
                    Vector2 pt = profile[p];
                    Vector3 pos = centers[i] + n * pt.x + b * pt.y;
                    // A cheap per-vertex normal: outward through this profile point in the (n,b) plane —
                    // correct for a convex profile (the default square), approximate for a concave one.
                    Vector3 outward = (n * pt.x + b * pt.y);
                    ring[i, p] = data.AddVert(pos, outward.sqrMagnitude > 1e-8f ? outward.normalized : n);
                }
            }

            int segCount = closed ? pointCount : pointCount - 1;
            for (int i = 0; i < segCount; i++)
            {
                int ni = closed ? (i + 1) % pointCount : i + 1;
                for (int p = 0; p < profCount; p++)
                {
                    int np = (p + 1) % profCount;
                    data.AddQuad(ring[i, p], ring[ni, p], ring[ni, np], ring[i, np]);
                }
            }

            if (!closed && capEnds)
            {
                FanCap(data, ring, 0, profCount);
                FanCap(data, ring, pointCount - 1, profCount);
            }
        }

        // Simple triangle fan from point 0 — correct for a convex profile (the default square), an
        // approximation for a concave one; a proper ear-clip triangulation is future work.
        static void FanCap(LatheMeshData data, int[,] ring, int i, int profCount)
        {
            for (int p = 1; p < profCount - 1; p++)
                data.AddTri(ring[i, 0], ring[i, p], ring[i, p + 1]);
        }

        void SamplePath(float t, out Vector3 pos, out Vector3 tangent)
        {
            switch (path)
            {
                case SweepPathKind.Line:
                    pos = Vector3.forward * (t * length);
                    tangent = Vector3.forward;
                    break;

                case SweepPathKind.Arc:
                case SweepPathKind.Circle:
                {
                    float deg = path == SweepPathKind.Circle ? t * 360f : t * arcDegrees;
                    float rad = deg * Mathf.Deg2Rad;
                    pos = new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
                    tangent = new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                    break;
                }
                case SweepPathKind.Helix:
                {
                    float rad = t * 360f * helixTurns * Mathf.Deg2Rad;
                    float y = t * helixRise * helixTurns;
                    pos = new Vector3(Mathf.Cos(rad) * radius, y, Mathf.Sin(rad) * radius);
                    float dTheta = 360f * helixTurns * Mathf.Deg2Rad;
                    tangent = new Vector3(-Mathf.Sin(rad) * radius * dTheta, helixRise * helixTurns, Mathf.Cos(rad) * radius * dTheta);
                    break;
                }
                default:
                    pos = Vector3.zero;
                    tangent = Vector3.forward;
                    break;
            }
            tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector3.forward;
        }
    }
}

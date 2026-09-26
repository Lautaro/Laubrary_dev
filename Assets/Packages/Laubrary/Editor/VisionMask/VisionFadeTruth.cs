using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.VisionMask.Editor
{
    /// INDEPENDENT ground truth for the edge fade (VisionCone.edgeFade), for probes only.
    ///
    /// The rule it encodes, from the spec and nothing else: a point's visibility for one cone is 1 inside the lit
    /// region (inside the omni disc, or within range AND within the half angle AND with no occluder on the straight
    /// line from the eye), and outside it 1 - smoothstep(dist / fade), where dist is the point's Euclidean distance
    /// to the lit region; 0 whenever the eye's line to the point is blocked (a wall edge stays hard; the omni disc
    /// and its band are never blocked). Union over cones = the maximum.
    ///
    /// It deliberately shares NOTHING with VisionMask.hlsl or VisionMask.Visibility: no shadow map, no texel
    /// lookup, no "nearest edge on this side" shortcut. The inside test is Unity's Vector2.Angle in degrees;
    /// the distance is the minimum over EVERY boundary piece (both edge segments, the arc, the omni circle),
    /// each measured on its own; occlusion is a Physics2D.Linecast per point; the curve is written out as
    /// 3x² - 2x³. HANDOVER 7.6b: a truth that copies the thing under test proves nothing — that is exactly how a
    /// vision regression survived two engagements. <see cref="CrossCheck"/> verifies this oracle's distance
    /// against brute-force dense sampling of the boundary, so the oracle is itself checked.
    public static class VisionFadeTruth
    {
        public struct Cone
        {
            public Vector2 eye, facing;          // plane coords; facing need not be unit
            public float halfDeg, range, omni, fade;
            public int occluderMask;             // XY plane only (Physics2D); 0 = none
        }

        /// Snapshot a live cone from its PUBLIC fields and transform (never through VisionMask's published state).
        public static Cone From(VisionCone k, VisionPlane plane)
        {
            var p = k.transform.position;
            var f = plane == VisionPlane.XY ? k.transform.up : k.transform.forward;
            return new Cone
            {
                eye = plane == VisionPlane.XY ? new Vector2(p.x, p.y) : new Vector2(p.x, p.z),
                facing = plane == VisionPlane.XY ? new Vector2(f.x, f.y) : new Vector2(f.x, f.z),
                halfDeg = Mathf.Clamp(k.angle, 0f, 360f) * 0.5f,
                range = Mathf.Max(0f, k.range),
                omni = Mathf.Max(0f, k.omniRadius),
                fade = Mathf.Max(0f, k.edgeFade),
                occluderMask = plane == VisionPlane.XY ? k.occluders.value : 0,
            };
        }

        public static List<Cone> FromLive(IEnumerable<VisionCone> cones, VisionPlane plane)
        {
            var l = new List<Cone>();
            foreach (var k in cones) if (k != null && k.isActiveAndEnabled) l.Add(From(k, plane));
            return l;
        }

        /// The spec's fade curve, written out: 1 at x <= 0, 0 at x >= 1, 1 - (3x² - 2x³) between.
        public static float Curve(float x)
        {
            if (x <= 0f) return 1f;
            if (x >= 1f) return 0f;
            return 1f - (3f * x * x - 2f * x * x * x);
        }

        /// Is w inside the cone's SECTOR (range + angle), ignoring occlusion and the omni disc?
        public static bool InSector(Cone c, Vector2 w)
        {
            var d = w - c.eye;
            if (d.magnitude > c.range) return false;
            if (c.halfDeg >= 180f) return true;
            if (d.sqrMagnitude < 1e-12f) return true;
            return Vector2.Angle(c.facing, d) <= c.halfDeg;
        }

        /// Euclidean distance from w to the sector (0 inside): the minimum over each boundary piece measured alone.
        public static float SectorDistance(Cone c, Vector2 w)
        {
            if (InSector(c, w)) return 0f;
            float best = float.MaxValue;
            float fDeg = Mathf.Atan2(c.facing.y, c.facing.x) * Mathf.Rad2Deg;
            // the arc: angles fDeg - half .. fDeg + half at radius range; nearest arc point = w's own angle clamped
            // into that span (a full circle has no clamp)
            float wDeg = Mathf.Atan2(w.y - c.eye.y, w.x - c.eye.x) * Mathf.Rad2Deg;
            float rel = Mathf.DeltaAngle(fDeg, wDeg);
            float clamped = c.halfDeg >= 180f ? rel : Mathf.Clamp(rel, -c.halfDeg, c.halfDeg);
            float a = (fDeg + clamped) * Mathf.Deg2Rad;
            best = Mathf.Min(best, Vector2.Distance(w, c.eye + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * c.range));
            if (c.halfDeg < 180f)
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    float ea = (fDeg + s * c.halfDeg) * Mathf.Deg2Rad;
                    var end = c.eye + new Vector2(Mathf.Cos(ea), Mathf.Sin(ea)) * c.range;
                    best = Mathf.Min(best, PointSegment(w, c.eye, end));
                }
            }
            return best;
        }

        static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float L2 = ab.sqrMagnitude;
            if (L2 < 1e-12f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2);
            return Vector2.Distance(p, a + ab * t);
        }

        /// One cone's expected visibility at plane point w.
        public static float ConeValue(Cone c, Vector2 w)
        {
            float r = Vector2.Distance(w, c.eye);
            float omniVis = 0f;
            if (c.omni > 0f)
                omniVis = r <= c.omni ? 1f : (c.fade > 0f ? Curve((r - c.omni) / c.fade) : 0f);
            if (omniVis >= 1f) return 1f;

            float d = SectorDistance(c, w);
            float sectorVis = d <= 0f ? 1f : (c.fade > 0f ? Curve(d / c.fade) : 0f);
            if (sectorVis > 0f && c.occluderMask != 0 && Physics2D.Linecast(c.eye, w, c.occluderMask).collider != null)
                sectorVis = 0f;   // hard: nothing behind a wall shows, band or not
            return Mathf.Max(omniVis, sectorVis);
        }

        /// Expected visibility of plane point w under all cones (union = max).
        public static float Value(List<Cone> cones, Vector2 w)
        {
            float best = 0f;
            foreach (var c in cones) best = Mathf.Max(best, ConeValue(c, w));
            return best;
        }

        /// Checks the ORACLE: its analytic SectorDistance against brute force (the distance to `samples` points
        /// spread densely along the whole boundary — edges and arc), at `n` random points around the cone within
        /// `pad` of its reach. Returns the worst disagreement in world units (bounded by the sampling spacing).
        public static float CrossCheck(Cone c, int n, float pad, int samples, int seed, out float spacing)
        {
            var rng = new System.Random(seed);
            var boundary = new List<Vector2>();
            float fDeg = Mathf.Atan2(c.facing.y, c.facing.x) * Mathf.Rad2Deg;
            bool full = c.halfDeg >= 180f;
            float arcLen = c.range * 2f * c.halfDeg * Mathf.Deg2Rad;
            float total = arcLen + (full ? 0f : 2f * c.range);
            spacing = total / samples;
            int arcN = Mathf.Max(2, Mathf.RoundToInt(samples * arcLen / total));
            for (int i = 0; i <= arcN; i++)
            {
                float a = (fDeg - c.halfDeg + 2f * c.halfDeg * i / arcN) * Mathf.Deg2Rad;
                boundary.Add(c.eye + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * c.range);
            }
            if (!full)
            {
                int segN = Mathf.Max(2, (samples - arcN) / 2);
                foreach (float s in new[] { -1f, 1f })
                {
                    float ea = (fDeg + s * c.halfDeg) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(ea), Mathf.Sin(ea));
                    for (int i = 0; i <= segN; i++) boundary.Add(c.eye + dir * (c.range * i / segN));
                }
            }
            float worst = 0f;
            for (int k = 0; k < n; k++)
            {
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0), rad = (float)(rng.NextDouble() * (c.range + pad));
                var w = c.eye + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                if (InSector(c, w)) continue;
                float brute = float.MaxValue;
                foreach (var b in boundary) brute = Mathf.Min(brute, Vector2.Distance(w, b));
                worst = Mathf.Max(worst, Mathf.Abs(brute - SectorDistance(c, w)));
            }
            return worst;
        }
    }
}

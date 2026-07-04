using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Choreographer
{
    /// A single dancer's sampled placement at one moment. Position is normalised (consumer scales it);
    /// facingDegrees points along travel; progress is 0..1 along the dancer's own journey.
    public struct DancerPose
    {
        public Vector2 position;
        public float facingDegrees;
        public float progress;
    }

    /// Optional world anchors a choreography can bind to, expressed in normalised space. Launcher pins the
    /// start of every dancer's journey to one spot (fire a barrage from a single launcher even while the
    /// dancers spread and stagger); Target bends the end of the journey onto a live coordinate (home in on
    /// the player). Both are optional — a choreography ignores an anchor it wasn't told to use.
    public struct ChoreoAnchors
    {
        public bool hasLauncher; public Vector2 launcher;
        public bool hasTarget; public Vector2 target;
        public static readonly ChoreoAnchors None = default;
    }

    /// The one place a Choreography turns into positions. The editor preview and the runtime player both call
    /// this, so what you tune is exactly what plays (WYSIWYG). Pure function of (choreography, index, count,
    /// phase, anchors) plus the path's cached arc-length table — no per-frame allocation, no live state.
    public static class ChoreographySampler
    {
        public static DancerPose Evaluate(Choreography c, int index, int count, float phase)
            => Evaluate(c, index, count, phase, ChoreoAnchors.None);

        /// Evaluate dancer `index` of `count`, at normalised cycle `phase` in [0,1] (0 = start, 1 = everyone done).
        public static DancerPose Evaluate(Choreography c, int index, int count, float phase, ChoreoAnchors anchors)
            => EvaluateProgress(c, index, count, ProgressAt(c, index, count, phase), anchors);

        /// A dancer's own journey progress (0..1) at a given cycle phase. Stagger delays each dancer's start by
        /// ni × stagger; the extended clock lets the last-delayed dancer still finish at phase 1.
        public static float ProgressAt(Choreography c, int index, int count, float phase)
            => Mathf.Clamp01(phase * (1f + c.stagger) - Ni(index, count) * c.stagger);

        public static DancerPose EvaluateProgress(Choreography c, int index, int count, float progress)
            => EvaluateProgress(c, index, count, progress, ChoreoAnchors.None);

        /// Evaluate a dancer by its own journey progress in [0,1] directly (no stagger clock). Handy for drawing
        /// a dancer's whole route, onion-skins and trails. Facing is taken from the actual travelled direction,
        /// so it stays correct through the launcher/target blends.
        public static DancerPose EvaluateProgress(Choreography c, int index, int count, float progress, ChoreoAnchors anchors)
        {
            progress = Mathf.Clamp01(progress);
            Vector2 pos = SamplePosition(c, index, count, progress, anchors);

            const float eps = 0.01f;
            float a = Mathf.Max(0f, progress - eps);
            float b = Mathf.Min(1f, progress + eps);
            if (b - a < 1e-5f) { a = 0f; b = eps; }
            Vector2 d = SamplePosition(c, index, count, b, anchors) - SamplePosition(c, index, count, a, anchors);
            float facing = d.sqrMagnitude > 1e-8f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : 0f;

            return new DancerPose { position = pos, facingDegrees = facing, progress = progress };
        }

        /// The normalised position of a dancer at a given journey progress. The path is the SPINE of the
        /// formation: its first point maps to the launcher and its last point to the target (a translate +
        /// rotate + scale frame), so the whole choreo is expressed relative to those anchors. Dancers spread
        /// around the spine; the spread tapers to zero over the launch/target blend windows so they converge
        /// on the launcher at the start and the target at the end. Public so previews can draw routes cheaply.
        public static Vector2 SamplePosition(Choreography c, int index, int count, float progress, ChoreoAnchors anchors)
        {
            float ni = Ni(index, count);
            bool gather = c.direction == Direction.Gather;
            float travel = gather ? 1f - progress : progress;

            Vector2 P = c.Cache.Sample(travel, c.constantSpeed, out _);
            GetFrame(c, anchors, out Vector2 origin, out Vector2 basePoint);
            Vector2 spine = origin + (P - basePoint);   // translate-only: authored path anchored at the launcher

            // Spread offset around the spine (authored axes), tapered so dancers converge at the ends.
            SpreadLocus(c, ni, out Vector2 off, out float radialDeg);
            if (c.facing == FacingMode.Radial) off = Rotate(off, radialDeg - 90f);
            off = Rotate(off, c.facingAngle);

            bool useL = anchors.hasLauncher && c.useLauncher;
            bool useT = anchors.hasTarget && c.useTarget;
            float taper = 1f;
            if (useL) taper *= Smooth01(progress / Mathf.Max(1e-4f, c.launchBlend));
            if (useT) taper *= Smooth01((1f - progress) / Mathf.Max(1e-4f, c.targetBlend));   // spread convergence window

            Vector2 pos = spine + off * taper;

            // Homing: fly the exact path until Release, then drift the tail so the endpoint lands on the target —
            // which the player samples at the moment THIS dancer reaches Release, not back at launch.
            if (useL && useT)
            {
                float rel = Mathf.Min(c.releaseAt, 0.999f);          // keep the window non-zero so the endpoint always lands
                float w = Smooth01((progress - rel) / (1f - rel));
                Vector2 spineEnd = origin + (c.Cache.Sample(1f, false, out _) - basePoint);
                pos += w * (anchors.target - spineEnd);
            }
            return pos;
        }

        /// Map an authored path point through the (translate-only) launcher frame — the spine, i.e. the centre
        /// route up to the retarget marker — so previews can draw the editable path where the dancers ride it.
        public static Vector2 FramePoint(Choreography c, Vector2 authored, ChoreoAnchors anchors)
        {
            GetFrame(c, anchors, out Vector2 origin, out Vector2 basePoint);
            return origin + (authored - basePoint);
        }

        /// Inverse of FramePoint: turn a framed (on-screen) position back into an authored path point.
        public static Vector2 InverseFramePoint(Choreography c, Vector2 framed, ChoreoAnchors anchors)
        {
            GetFrame(c, anchors, out Vector2 origin, out Vector2 basePoint);
            return basePoint + (framed - origin);
        }

        // Translate-only frame: anchor the authored path's first point to the launcher (or, with only a target,
        // its last point to the target). The path keeps its authored orientation and scale; the target's pull is
        // applied separately as a homing course-correction in SamplePosition.
        static void GetFrame(Choreography c, ChoreoAnchors an, out Vector2 origin, out Vector2 basePoint)
        {
            if (an.hasLauncher && c.useLauncher) { origin = an.launcher; basePoint = c.Cache.Sample(0f, false, out _); }
            else if (an.hasTarget && c.useTarget) { origin = an.target; basePoint = c.Cache.Sample(1f, false, out _); }
            else { origin = Vector2.zero; basePoint = Vector2.zero; }
        }

        /// The start anchor for dancer ni, on a line of `spreadLength` that bends into a full circle at bend = 1.
        /// radialDeg is the outward direction at that point (used by Radial facing).
        public static void SpreadLocus(Choreography c, float ni, out Vector2 anchor, out float radialDeg)
        {
            float half = c.spreadLength * 0.5f;
            if (c.spreadBend < 1e-4f)
            {
                anchor = new Vector2(Mathf.Lerp(-half, half, ni), 0f);
                radialDeg = 90f; // degenerate line: "outward" points up
                return;
            }

            // Arc of total sweep = bend × 360°, radius chosen so its length equals spreadLength.
            float sweep = c.spreadBend * Mathf.PI * 2f;          // radians
            float radius = c.spreadLength / sweep;
            float ang = (ni - 0.5f) * sweep;                      // symmetric about the vertical axis
            // Centre sits at (0,-radius); the chord midpoint passes through the origin.
            anchor = new Vector2(radius * Mathf.Sin(ang), radius * Mathf.Cos(ang) - radius);
            radialDeg = Mathf.Atan2(Mathf.Cos(ang), Mathf.Sin(ang)) * Mathf.Rad2Deg;
        }

        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        public static float Ni(int index, int count) => count > 1 ? (float)index / (count - 1) : 0.5f;

        /// The canonical per-index colour (cyan→magenta by normalised index). Shared by the editor preview and the
        /// runtime debug view so they never diverge.
        public static Color IndexColor(float ni) => Color.HSVToRGB(Mathf.Lerp(0.52f, 0.95f, Mathf.Clamp01(ni)), 0.65f, 1f);

        static float Smooth01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));
    }

    /// Densely resampled path with a cumulative arc-length table, so we can sample either by raw parameter or
    /// at constant speed, and read a tangent, without re-walking the spline each call. Rebuilt when points change.
    public class PathCache
    {
        public bool Dirty = true;
        readonly List<Vector2> pts = new();
        readonly List<float> cum = new();   // cumulative length up to pts[i]
        float total;

        const int PerSegment = 24;

        public void Rebuild(List<Vector2> control, bool smooth)
        {
            pts.Clear(); cum.Clear(); total = 0f;
            Dirty = false;

            if (control == null || control.Count == 0) { pts.Add(Vector2.zero); cum.Add(0f); return; }
            if (control.Count == 1) { pts.Add(control[0]); cum.Add(0f); return; }

            if (!smooth)
            {
                for (int i = 0; i < control.Count; i++) Add(control[i]);
            }
            else
            {
                // Catmull-Rom through all control points, endpoints duplicated for clean tangents.
                for (int i = 0; i < control.Count - 1; i++)
                {
                    Vector2 p0 = control[Mathf.Max(0, i - 1)];
                    Vector2 p1 = control[i];
                    Vector2 p2 = control[i + 1];
                    Vector2 p3 = control[Mathf.Min(control.Count - 1, i + 2)];
                    int startK = i == 0 ? 0 : 1; // avoid duplicating the shared knot
                    for (int k = startK; k <= PerSegment; k++)
                        Add(CatmullRom(p0, p1, p2, p3, (float)k / PerSegment));
                }
            }
            if (pts.Count == 1) cum.Add(0f);
        }

        void Add(Vector2 p)
        {
            if (pts.Count == 0) { pts.Add(p); cum.Add(0f); return; }
            total += Vector2.Distance(pts[pts.Count - 1], p);
            pts.Add(p); cum.Add(total);
        }

        public float Length => total;

        /// Sample at u in [0,1]. constantSpeed maps u to arc length; otherwise u indexes the samples directly.
        public Vector2 Sample(float u, bool constantSpeed, out float tangentDeg)
        {
            u = Mathf.Clamp01(u);
            if (pts.Count == 1) { tangentDeg = 0f; return pts[0]; }

            int i; float f;
            if (constantSpeed && total > 1e-6f)
            {
                float target = u * total;
                i = UpperBound(target);
                float segLen = cum[i + 1] - cum[i];
                f = segLen > 1e-6f ? (target - cum[i]) / segLen : 0f;
            }
            else
            {
                float g = u * (pts.Count - 1);
                i = Mathf.Clamp(Mathf.FloorToInt(g), 0, pts.Count - 2);
                f = g - i;
            }

            Vector2 a = pts[i], b = pts[i + 1];
            Vector2 dir = b - a;
            tangentDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            return Vector2.Lerp(a, b, f);
        }

        int UpperBound(float target)
        {
            int lo = 0, hi = cum.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (cum[mid] <= target) lo = mid; else hi = mid - 1;
            }
            return Mathf.Clamp(lo, 0, pts.Count - 2);
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}

// The editing maths for one member tag: outline, hit tests, marquee, anchored edge resize, the U / F trackball, turning
// and the per-direction default orientation. A straight port of the web prototype's tag editor (deathlab.js), kept in one
// place so the stage, the tabs and the frame helpers agree about every number.
//
// Coordinates are sprite-local pixels: origin top-left, x right, y DOWN, z toward the viewer (the engine's convention).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.GoreLab.Editor
{
    internal static class GoreTagEdit
    {
        /// The smallest half size a dragged shape may have, in pixels.
        public const double MinHalf = 3.0;

        // ── vectors ─────────────────────────────────────────────────────────────────────────────────────

        public static Vector3 Up(in MemberTag t) => new Vector3((float)t.ux, (float)t.uy, (float)t.uz);
        public static Vector3 Forward(in MemberTag t) => new Vector3((float)t.fx, (float)t.fy, (float)t.fz);

        /// East (the member's right) = up x forward.
        public static Vector3 East(in MemberTag t)
            => new Vector3((float)(t.uy * t.fz - t.uz * t.fy), (float)(t.uz * t.fx - t.ux * t.fz), (float)(t.ux * t.fy - t.uy * t.fx));

        /// The on-screen "up" of the 2D outline (the angle, not u3: the two may disagree, see spec 1.2).
        public static Vector2 Up2D(in MemberTag t) => new Vector2((float)Math.Cos(t.angle), (float)Math.Sin(t.angle));

        public static double Dot(in MemberTag a, in MemberTag b) => a.fx * b.fx + a.fy * b.fy + a.fz * b.fz;
        public static double DotForward(in MemberTag t, Vector3 v) => t.fx * v.x + t.fy * v.y + t.fz * v.z;

        /// Keep forward a unit vector perpendicular to up (the prototype's fixFwd, verbatim).
        public static void FixForward(ref MemberTag t)
        {
            double d = t.fx * t.ux + t.fy * t.uy + t.fz * t.uz;
            double x = t.fx - t.ux * d, y = t.fy - t.uy * d, z = t.fz - t.uz * d;
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (l < 1e-3)
            {
                x = -t.uy; y = t.ux; z = 0; l = Math.Sqrt(x * x + y * y);
                if (l < 1e-3) { x = 1; y = 0; z = 0; l = 1; }
            }
            t.fx = x / l; t.fy = y / l; t.fz = z / l;
        }

        /// Set up to a unit vector; the 2D angle follows only while up has a real on-screen direction.
        public static void SetUp(ref MemberTag t, double x, double y, double z)
        {
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (l < 1e-9) return;
            t.ux = x / l; t.uy = y / l; t.uz = z / l;
            if (Math.Sqrt(x * x + y * y) > 0.2) t.angle = Math.Atan2(y, x);
            FixForward(ref t);
        }

        /// Set forward along the given direction, then re-orthogonalise against up.
        public static void SetForward(ref MemberTag t, Vector3 f)
        {
            t.fx = f.x; t.fy = f.y; t.fz = f.z;
            FixForward(ref t);
        }

        /// Turn forward about up: f' = f cos + east sin (+ turns the front toward its own east).
        public static void Yaw(ref MemberTag t, double degrees)
        {
            double a = degrees * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            var e = East(t);
            double x = t.fx * c + e.x * s, y = t.fy * c + e.y * s, z = t.fz * c + e.z * s;
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (l < 1e-9) return;
            t.fx = x / l; t.fy = y / l; t.fz = z / l;
            FixForward(ref t);
        }

        /// A fresh tag at a sprite-local point: 3 px half sizes, up = screen up, facing the direction's forward.
        public static MemberTag NewTag(MemberKind kind, double cx, double cy, Vector3 forward)
        {
            var t = new MemberTag
            {
                kind = kind, cx = cx, cy = cy, rx = MinHalf, ry = MinHalf, rz = MinHalf,
                n = kind == MemberKind.Box ? 4.0 : 2.0,
                angle = -Math.PI / 2, ux = 0, uy = -1, uz = 0,
                fx = forward.x, fy = forward.y, fz = forward.z,
            };
            FixForward(ref t);
            return t;
        }

        /// Depth of a member: a box has its own, a ball uses its across radius.
        public static double Depth(in MemberTag t) => t.kind == MemberKind.Box && t.rz > 0 ? t.rz : t.rx;

        // ── direction defaults (spec 11.3) ──────────────────────────────────────────────────────────────

        /// The forward a member drawn for a Launimator direction should have. Launimator angles: 0 = up the screen (the
        /// back view of a top-down walker), clockwise, 180 = facing the camera. So 90 faces screen right.
        public static Vector3 ExpectedForward(float angleDegrees)
        {
            double a = angleDegrees * Math.PI / 180.0;
            double x = Math.Sin(a), z = -Math.Cos(a);
            if (Math.Abs(x) < 1e-6) x = 0;
            if (Math.Abs(z) < 1e-6) z = 0;
            return new Vector3((float)x, 0f, (float)z);
        }

        // ── outline ─────────────────────────────────────────────────────────────────────────────────────

        /// The 2D outline test the engine uses (the only definition of "inside the marked shape").
        public static bool Inside(in MemberTag t, double px, double py) => GoreTagMath.InsideOutline(t, px, py);

        /// Points around the 2D outline, sprite-local.
        public static void Outline(in MemberTag t, List<Vector2> into, int k = 72)
        {
            into.Clear();
            var u = Up2D(t);
            var v = new Vector2(-u.y, u.x);
            double e = 2.0 / Math.Max(0.5, t.n);
            for (int i = 0; i < k; i++)
            {
                double a = i / (double)k * Math.PI * 2, cs = Math.Cos(a), sn = Math.Sin(a);
                double ax = Math.Sign(cs) * Math.Pow(Math.Abs(cs), e) * t.rx;
                double ay = Math.Sign(sn) * Math.Pow(Math.Abs(sn), e) * t.ry;
                into.Add(new Vector2((float)(t.cx + v.x * ax + u.x * ay), (float)(t.cy + v.y * ax + u.y * ay)));
            }
        }

        /// A sprite-local point in the outline's own axes: x = across, y = along up.
        public static Vector2 ToShape(in MemberTag t, Vector2 p)
        {
            var u = Up2D(t);
            float dx = p.x - (float)t.cx, dy = p.y - (float)t.cy;
            return new Vector2(-dx * u.y + dy * u.x, dx * u.x + dy * u.y);
        }

        static readonly List<Vector2> s_ring = new List<Vector2>();

        /// Distance in pixels from a sprite-local point to the outline.
        public static float EdgeDistance(in MemberTag t, Vector2 p)
        {
            Outline(t, s_ring, 96);
            float best = float.MaxValue;
            for (int i = 0; i < s_ring.Count; i++)
            {
                Vector2 a = s_ring[i], b = s_ring[(i + 1) % s_ring.Count], ab = b - a;
                float l2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                float s = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * s));
            }
            return best;
        }

        // ── gestures ────────────────────────────────────────────────────────────────────────────────────

        /// Marquee: the shape spans the drag corner to corner (axis-aligned), never out from its centre.
        public static void Span(ref MemberTag t, Vector2 a, Vector2 b)
        {
            t.cx = (a.x + b.x) * 0.5; t.cy = (a.y + b.y) * 0.5;
            t.rx = Math.Max(MinHalf, Math.Abs(b.x - a.x) * 0.5);
            t.ry = Math.Max(MinHalf, Math.Abs(b.y - a.y) * 0.5);
            if (t.kind == MemberKind.Box) t.rz = Math.Max(MinHalf, t.rx * 0.6);
        }

        /// Anchored resize from a grabbed outline point: the grabbed side follows the pointer, the far side stays put.
        /// `edge0` is the grabbed point in normalised shape units (across / rx, along / ry) at the start of the drag.
        public static MemberTag ResizeFromEdge(in MemberTag start, Vector2 edge0, Vector2 p)
        {
            var t = start;
            var u = Up2D(start);
            var v = new Vector2(-u.y, u.x);
            var q = ToShape(start, p);
            double rx = start.rx, ry = start.ry, offA = 0, offU = 0;
            for (int ax = 0; ax < 2; ax++)
            {
                float e = ax == 0 ? edge0.x : edge0.y;
                if (Math.Abs(e) <= 0.3f) continue;          // this axis is not the one being pulled
                double sg = Math.Sign(e), r0 = ax == 0 ? start.rx : start.ry, qa = ax == 0 ? q.x : q.y;
                double rn = Math.Max(MinHalf, (sg * qa + r0) / (1 + Math.Abs(e)));
                if (ax == 0) { rx = rn; offA = sg * (rn - r0); } else { ry = rn; offU = sg * (rn - r0); }
            }
            t.rx = rx; t.ry = ry;
            t.cx = start.cx + v.x * offA + u.x * offU;
            t.cy = start.cy + v.y * offA + u.y * offU;
            return t;
        }

        /// Radius of the trackball the U or F dot rides on.
        public static double DotRadius(in MemberTag t, bool up)
            => t.kind == MemberKind.Box ? (up ? t.ry : Depth(t)) : (t.rx + t.ry) * 0.5;

        /// Where the U or F dot sits, sprite-local.
        public static Vector2 DotPosition(in MemberTag t, bool up)
        {
            double r = DotRadius(t, up);
            return up ? new Vector2((float)(t.cx + t.ux * r), (float)(t.cy + t.uy * r))
                      : new Vector2((float)(t.cx + t.fx * r), (float)(t.cy + t.fy * r));
        }

        /// The sphere surface point under the pointer, on the near (zSign 1) or far (zSign -1) half.
        public static Vector3 Trackball(in MemberTag t, bool up, Vector2 p, ref double zSign, ref bool rimOut)
        {
            double r = Math.Max(1e-6, DotRadius(t, up));
            double x = (p.x - t.cx) / r, y = (p.y - t.cy) / r, l = Math.Sqrt(x * x + y * y);
            if (l > 1) { x /= l; y /= l; }
            // Dragged over the rim and back: continue on the far half, so a dot can turn all the way round.
            if (l > 1.02) rimOut = true;
            else if (rimOut && l < 0.98) { zSign = -zSign; rimOut = false; }
            return new Vector3((float)x, (float)y, (float)(Math.Sqrt(Math.Max(0, 1 - x * x - y * y)) * zSign));
        }

        /// Apply a trackball point to the U dot (up follows it) or the F dot (forward slides on the equator).
        public static void ApplyDot(ref MemberTag t, bool up, Vector3 v)
        {
            if (up) { SetUp(ref t, v.x, v.y, v.z); return; }
            double d = v.x * t.ux + v.y * t.uy + v.z * t.uz;
            double x = v.x - t.ux * d, y = v.y - t.uy * d, z = v.z - t.uz * d, l = Math.Sqrt(x * x + y * y + z * z);
            if (l > 1e-3) { t.fx = x / l; t.fy = y / l; t.fz = z / l; }
        }

        /// A tap on a dot flips it to the other half of the sphere.
        public static void FlipDot(ref MemberTag t, bool up)
        {
            if (up) t.uz = -t.uz; else t.fz = -t.fz;
            FixForward(ref t);
        }

        // ── masks ───────────────────────────────────────────────────────────────────────────────────────

        /// The same pixel set as seen on the horizontally mirrored sprite.
        public static int[] MirrorMask(int[] mask, int w)
        {
            if (mask == null || mask.Length == 0) return Array.Empty<int>();
            var o = new int[mask.Length];
            for (int i = 0; i < mask.Length; i++) { int y = mask[i] / w, x = mask[i] % w; o[i] = y * w + (w - 1 - x); }
            return o;
        }

        /// A set written back as the sorted array the asset stores.
        public static int[] ToArray(HashSet<int> set)
        {
            var a = new int[set.Count];
            set.CopyTo(a);
            Array.Sort(a);
            return a;
        }
    }
}

using UnityEngine;

namespace Laubrary.VisionMask
{
    /// One vision cone: an eye at this transform, looking along its facing (transform.up on the XY plane,
    /// transform.forward on XZ — see <see cref="VisionMask.Plane"/>), with an angle, a range and an omni
    /// disc. While enabled it is published to every VisionMask material, and any sprite drawn with one shows
    /// exactly the pixels of itself that lie inside it — nothing is decided per object.
    ///
    /// Optional occlusion: set <see cref="occluders"/> and the cone stops at the first collider on those
    /// layers along each of <see cref="rays"/> rays (Physics2D on XY, Physics on XZ), cast once per frame.
    /// The omni disc is never occluded — it is the carrier's own body glow.
    [ExecuteAlways]
    [AddComponentMenu("Laubrary/VisionMask/Vision Cone")]
    public class VisionCone : MonoBehaviour
    {
        [Tooltip("Full width of the cone, in degrees. 360 sees in every direction (up to Range).")]
        [Range(0f, 360f)] public float angle = 60f;

        [Tooltip("How far the cone reaches, in world units.")]
        [Min(0f)] public float range = 8f;

        [Tooltip("A disc around the eye that is seen in every direction and through occluders — the " +
                 "carrier's own glow, so it is never blind at its feet. 0 = none.")]
        [Min(0f)] public float omniRadius = 0f;

        [Tooltip("Soft edge, in world units: a pixel just OUTSIDE the cone (past its angle, its range or the omni disc) " +
                 "is not cut off but fades out with its distance to the lit region — fully drawn at the edge, gone " +
                 "this far past it. 0 = the exact hard edge. Walls still cut hard: nothing behind an occluder fades in.")]
        [Min(0f)] public float edgeFade = 0f;

        [Tooltip("Layers whose colliders block sight. Nothing = the cone passes through everything.")]
        public LayerMask occluders;

        [Tooltip("Angular resolution of the occlusion shadow: how many directions across the cone get their " +
                 "own reach. A shadow edge is exact to within one of these. Only every 4th is always raycast; " +
                 "the ones between are raycast only where the reach jumps (a shadow edge), so the cost is " +
                 "roughly a quarter of this per frame. An occluder thinner than 4 of these can slip between.")]
        [Range(8, VisionMask.MaxRays)] public int rays = 1024;

        /// Always-cast spacing of the adaptive shadow (see CastShadow).
        public const int CoarseStride = 4;

        /// With an edge fade, the shadow row also covers this much angle beyond each cone edge, so a pixel in the
        /// fade band just outside the angle is shadow-tested against ITS OWN ray, not the edge ray's. A band pixel
        /// at angle δ past the edge lies r·sin δ from it, so 30° covers the whole band for every pixel at least
        /// 2 × edgeFade from the eye. Closer than that (inside the carrier's own body, in practice) a pixel past
        /// the margin borrows the outermost ray's reach.
        public const float FadeShadowMarginDeg = 30f;

        /// Physics raycasts the last shadow cast actually spent (for profiling / the probe).
        [System.NonSerialized] public int lastRaycasts;

        /// Which of the shader's slots this cone landed in at the last publish (-1 = not published).
        [System.NonSerialized] public int publishedSlot = -1;

        void OnEnable() => VisionMask.Register(this);
        void OnDisable() { VisionMask.Unregister(this); publishedSlot = -1; }

        /// Point the cone along a PLANE direction (x,y on XY; x,z on XZ). The steering call a game makes
        /// each frame — aim input, a facing, wherever the light looks.
        public void SetAim(Vector2 planeDirection)
        {
            if (planeDirection.sqrMagnitude < 1e-8f) return;
            if (VisionMask.Plane == VisionPlane.XY) transform.up = new Vector3(planeDirection.x, planeDirection.y, 0f);
            else transform.forward = new Vector3(planeDirection.x, 0f, planeDirection.y);
        }

        /// The cone's facing in plane coordinates (unit length).
        public Vector2 Facing
        {
            get
            {
                var v = VisionMask.Plane == VisionPlane.XY ? transform.up : transform.forward;
                var f = VisionMask.Plane == VisionPlane.XY ? new Vector2(v.x, v.y) : new Vector2(v.x, v.z);
                return f.sqrMagnitude > 1e-8f ? f.normalized : Vector2.up;
            }
        }

        internal void Snapshot(out Vector2 eye, out Vector2 fwd, out float halfRad, out float outRange, out float omni)
        {
            eye = VisionMask.PlaneCoords(transform.position);
            fwd = Facing;
            halfRad = Mathf.Clamp(angle, 0f, 360f) * 0.5f * Mathf.Deg2Rad;
            outRange = Mathf.Max(0f, range);
            omni = Mathf.Max(0f, omniRadius);
        }

        /// The shadow row's layout for this cone: the half angle it covers and how many texels span it. Without a
        /// fade it is exactly the cone (`rays` texels). With one it widens by <see cref="FadeShadowMarginDeg"/> a
        /// side at the SAME angular density, capped at <see cref="VisionMask.MaxRays"/>.
        internal void ShadowLayout(float halfRad, out float coverHalf, out int texels)
        {
            int baseRays = Mathf.Clamp(rays, 1, VisionMask.MaxRays);
            if (edgeFade <= 0f || halfRad >= Mathf.PI)
            {
                coverHalf = halfRad; texels = baseRays;
                return;
            }
            coverHalf = Mathf.Min(Mathf.PI, halfRad + FadeShadowMarginDeg * Mathf.Deg2Rad);
            texels = Mathf.Clamp(Mathf.CeilToInt(baseRays * coverHalf / Mathf.Max(halfRad, 1e-4f)), baseRays, VisionMask.MaxRays);
        }

        /// Fill `reach[0..rays)` with how far each ray gets before an occluder (or `outRange`). Ray i points at
        /// the CENTRE of shadow texel i: angle = -half + (i + 0.5) * (2 half / rays), CCW positive — the
        /// same mapping the shader uses to pick the texel, so a pixel is judged by the ray nearest it. `halfRad`
        /// is the angle the ROW covers (see ShadowLayout) and `outRange` includes the edge fade, so a wall just
        /// past the range still hides what is behind it inside the fade band.
        ///
        /// ADAPTIVE: every CoarseStride-th texel is always cast. Between two coarse rays that hit the SAME
        /// collider (or both hit nothing) with reaches that agree (one smooth surface, or open space) the texels
        /// are interpolated; otherwise — a shadow edge — every texel between them is cast for real. So edges are
        /// exact to one texel at a fraction of the cost of casting them all. The same-collider rule (2026-09-26,
        /// edge fade) matters because the reach-agreement threshold alone (2 % of the range) let a wall's END
        /// be smoothed over whenever its reach was close to the open rays' — harmless while rays stopped at the
        /// range, but with the row cast into the fade band a wall there would leak its corner.
        internal void CastShadow(Vector2 eye, Vector2 fwd, float halfRad, float outRange, int rayCount, float[] reach)
        {
            float step = 2f * halfRad / rayCount;
            bool xy = VisionMask.Plane == VisionPlane.XY;
            if (xy) Physics2D.SyncTransforms(); else Physics.SyncTransforms();
            int casts = 0;
            float jump = Mathf.Max(0.02f * outRange, 0.05f);

            int last = rayCount - 1;
            int prev = 0;
            int hitNow = 0, hitPrev;
            reach[0] = Cast(0);
            hitPrev = hitNow;
            for (int i = CoarseStride; ; i += CoarseStride)
            {
                int cur = Mathf.Min(i, last);
                if (cur == prev) break;
                reach[cur] = Cast(cur);
                int hitCur = hitNow;
                if (hitCur != hitPrev || Mathf.Abs(reach[cur] - reach[prev]) > jump)
                    for (int j = prev + 1; j < cur; j++) reach[j] = Cast(j);
                else
                    for (int j = prev + 1; j < cur; j++) reach[j] = Mathf.Lerp(reach[prev], reach[cur], (float)(j - prev) / (cur - prev));
                prev = cur; hitPrev = hitCur;
                if (cur == last) break;
            }
            lastRaycasts = casts;

            float Cast(int i)
            {
                casts++;
                float ang = -halfRad + (i + 0.5f) * step;
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                var dir = new Vector2(fwd.x * cs - fwd.y * sn, fwd.x * sn + fwd.y * cs);
                if (xy)
                {
                    var hit = Physics2D.Raycast(eye, dir, outRange, occluders.value);
                    hitNow = hit.collider != null ? hit.collider.GetInstanceID() : 0;
                    return hit.collider != null ? hit.distance : outRange;
                }
                bool any = Physics.Raycast(transform.position, new Vector3(dir.x, 0f, dir.y), out var h, outRange,
                                           occluders.value, QueryTriggerInteraction.Ignore);
                hitNow = any && h.collider != null ? h.collider.GetInstanceID() : 0;
                return any ? h.distance : outRange;
            }
        }

        void OnDrawGizmosSelected()
        {
            Snapshot(out _, out var f, out float half, out float r, out float omni);
            bool xy = VisionMask.Plane == VisionPlane.XY;
            Vector3 To3(Vector2 v) => xy ? new Vector3(v.x, v.y, 0f) : new Vector3(v.x, 0f, v.y);
            var o = transform.position;
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
            const int seg = 24;
            Vector3 prev = o;
            for (int i = 0; i <= seg; i++)
            {
                float ang = -half + i * (2f * half / seg);
                var d = new Vector2(f.x * Mathf.Cos(ang) - f.y * Mathf.Sin(ang), f.x * Mathf.Sin(ang) + f.y * Mathf.Cos(ang));
                var p = o + To3(d * r);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.DrawLine(prev, o);
            if (edgeFade > 0f)
            {
                // Where the fade band ends along the range arc (the sides get the same band width).
                Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.35f);
                prev = o + To3(new Vector2(f.x * Mathf.Cos(-half) - f.y * Mathf.Sin(-half), f.x * Mathf.Sin(-half) + f.y * Mathf.Cos(-half)) * (r + edgeFade));
                for (int i = 1; i <= seg; i++)
                {
                    float ang = -half + i * (2f * half / seg);
                    var d = new Vector2(f.x * Mathf.Cos(ang) - f.y * Mathf.Sin(ang), f.x * Mathf.Sin(ang) + f.y * Mathf.Cos(ang));
                    var p = o + To3(d * (r + edgeFade));
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
                Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
            }
            if (omni > 0f)
            {
                prev = o + To3(Vector2.right * omni);
                for (int i = 1; i <= 32; i++)
                {
                    float t = i * Mathf.PI * 2f / 32;
                    var p = o + To3(new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * omni);
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
        }
    }
}

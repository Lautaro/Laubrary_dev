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

        [Tooltip("Layers whose colliders block sight. Nothing = the cone passes through everything.")]
        public LayerMask occluders;

        [Tooltip("Angular resolution of the occlusion shadow: how many directions across the cone get their " +
                 "own reach. A shadow edge is exact to within one of these. Only every 4th is always raycast; " +
                 "the ones between are raycast only where the reach jumps (a shadow edge), so the cost is " +
                 "roughly a quarter of this per frame. An occluder thinner than 4 of these can slip between.")]
        [Range(8, VisionMask.MaxRays)] public int rays = 1024;

        /// Always-cast spacing of the adaptive shadow (see CastShadow).
        public const int CoarseStride = 4;

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

        /// Fill `reach[0..rays)` with how far each ray gets before an occluder (or `range`). Ray i points at
        /// the CENTRE of shadow texel i: angle = -half + (i + 0.5) * (2 half / rays), CCW positive — the
        /// same mapping the shader uses to pick the texel, so a pixel is judged by the ray nearest it.
        ///
        /// ADAPTIVE: every CoarseStride-th texel is always cast. Between two coarse rays whose reaches agree
        /// (one smooth surface, or open space) the texels are interpolated; where they disagree — a shadow
        /// edge — every texel between them is cast for real. So edges are exact to one texel at a fraction of
        /// the cost of casting them all.
        internal void CastShadow(Vector2 eye, Vector2 fwd, float halfRad, float outRange, int rayCount, float[] reach)
        {
            float step = 2f * halfRad / rayCount;
            bool xy = VisionMask.Plane == VisionPlane.XY;
            if (xy) Physics2D.SyncTransforms(); else Physics.SyncTransforms();
            int casts = 0;
            float jump = Mathf.Max(0.02f * outRange, 0.05f);

            int last = rayCount - 1;
            int prev = 0;
            reach[0] = Cast(0);
            for (int i = CoarseStride; ; i += CoarseStride)
            {
                int cur = Mathf.Min(i, last);
                if (cur == prev) break;
                reach[cur] = Cast(cur);
                if (Mathf.Abs(reach[cur] - reach[prev]) > jump)
                    for (int j = prev + 1; j < cur; j++) reach[j] = Cast(j);
                else
                    for (int j = prev + 1; j < cur; j++) reach[j] = Mathf.Lerp(reach[prev], reach[cur], (float)(j - prev) / (cur - prev));
                prev = cur;
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
                    return hit.collider != null ? hit.distance : outRange;
                }
                return Physics.Raycast(transform.position, new Vector3(dir.x, 0f, dir.y), out var h, outRange,
                                       occluders.value, QueryTriggerInteraction.Ignore) ? h.distance : outRange;
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

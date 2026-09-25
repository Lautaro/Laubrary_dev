using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Laubrary.VisionMask
{
    /// Which world plane vision lives on. XY for 2D games (sprites face the camera down -Z), XZ for a
    /// top-down 3D world.
    public enum VisionPlane { XY, XZ }

    /// PER-PIXEL VISION. Publishes every live <see cref="VisionCone"/> as global shader parameters, so any
    /// material including <c>VisionMask.hlsl</c> — the shipped <c>Laubrary/VisionMaskSprite</c> above all —
    /// decides visibility for each of its FRAGMENTS from that fragment's own world position.
    ///
    /// Why it exists, because this has been built wrong repeatedly: "is this monster in the light?" is the
    /// wrong question. Any answer computed per OBJECT (sample points, bounds overlap, a distance halo turned
    /// into an alpha) can only draw the whole sprite or none of it, or fade all of it at once — which is
    /// exactly what a sprite straddling a cone edge must NOT do. The only correct place to ask is the
    /// fragment shader, one pixel at a time, which is what the City's LatticeVision already did for one
    /// lattice-bound cone. This is that technique, generalised: up to 8 cones, XY or XZ, each optionally
    /// occluded (a 1-D shadow map of per-ray reach, cast with physics on the CPU once per frame).
    ///
    /// Publishing happens right before each camera renders (SRP beginContextRendering, or
    /// Camera.onPreCull on the built-in pipeline), so the mask always agrees with where the cones'
    /// transforms are THIS frame regardless of script execution order. Call <see cref="Publish"/> to
    /// force it (a probe rendering a camera by hand in edit mode, for instance).
    ///
    /// CPU twin: <see cref="IsVisible"/> answers the same question with the same numbers for gameplay code.
    public static class VisionMask
    {
        /// The shader's VISION_MASK_MAX_CONES. Cones beyond this are ignored (the first eight registered win).
        public const int MaxCones = 8;
        /// Widest occlusion shadow row. A cone's own `rays` is clamped to this.
        public const int MaxRays = 1024;
        public const string SpriteShaderName = "Laubrary/VisionMaskSprite";

        static readonly List<VisionCone> cones = new();
        static readonly Vector4[] a = new Vector4[MaxCones], b = new Vector4[MaxCones], c = new Vector4[MaxCones];
        static readonly float[][] reach = new float[MaxCones][];
        static float[] shadowPixels;
        static Texture2D shadowTex;
        static readonly Dictionary<int, Material> materials = new();
        static bool hooked;
        static int publishedFrame = -1;

        static readonly int CountId = Shader.PropertyToID("_VisionMaskCount");
        static readonly int PlaneId = Shader.PropertyToID("_VisionMaskPlane");
        static readonly int BypassId = Shader.PropertyToID("_VisionMaskBypass");
        static readonly int AId = Shader.PropertyToID("_VisionMaskA");
        static readonly int BId = Shader.PropertyToID("_VisionMaskB");
        static readonly int CId = Shader.PropertyToID("_VisionMaskC");
        static readonly int ShadowId = Shader.PropertyToID("_VisionMaskShadow");
        static readonly int HiddenAlphaId = Shader.PropertyToID("_HiddenAlpha");

        /// The plane every cone and every masked pixel is measured on.
        public static VisionPlane Plane { get; set; } = VisionPlane.XY;

        /// Debug X-ray: while true every masked pixel draws, cones or not.
        public static bool Bypass { get; set; }

        /// The live cones, in publish order (the first MaxCones are the ones shaders see).
        public static IReadOnlyList<VisionCone> Cones => cones;

        internal static void Register(VisionCone cone)
        {
            if (cone == null || cones.Contains(cone)) return;
            cones.Add(cone);
            Hook();
            if (cones.Count > MaxCones)
                Debug.LogWarning($"VisionMask: {cones.Count} cones are live but shaders see only {MaxCones}; " +
                                 $"'{cone.name}' is ignored until another cone goes away.", cone);
            publishedFrame = -1;
        }

        internal static void Unregister(VisionCone cone)
        {
            cones.Remove(cone);
            publishedFrame = -1;
            if (cones.Count == 0) Publish();   // no cone = nothing visible, immediately
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginContextRendering += (_, _) => PublishOncePerFrame();
            Camera.onPreCull += _ => PublishOncePerFrame();
        }

        static void PublishOncePerFrame()
        {
            // In play mode once per frame is enough (all cameras of a frame see one world). The editor
            // repaints views without advancing frameCount, so there it always republishes.
            if (Application.isPlaying && publishedFrame == Time.frameCount) return;
            Publish();
        }

        /// Push the current cones (and their occlusion) to the GPU now.
        public static void Publish()
        {
            publishedFrame = Time.frameCount;
            int n = 0;
            bool anyOccluded = false;
            for (int i = 0; i < cones.Count && n < MaxCones; i++)
            {
                var k = cones[i];
                if (k == null || !k.isActiveAndEnabled) continue;
                k.Snapshot(out var eye, out var fwd, out float halfRad, out float range, out float omni);
                a[n] = new Vector4(eye.x, eye.y, fwd.x, fwd.y);
                b[n] = new Vector4(Mathf.Cos(halfRad), range, omni, halfRad);
                bool occ = k.occluders.value != 0 && range > 0f;
                int rays = Mathf.Clamp(k.rays, 1, MaxRays);
                c[n] = new Vector4(occ ? 1f : 0f, rays, 0f, 0f);
                if (occ)
                {
                    if (reach[n] == null || reach[n].Length != MaxRays) reach[n] = new float[MaxRays];
                    k.CastShadow(eye, fwd, halfRad, range, rays, reach[n]);
                    anyOccluded = true;
                }
                k.publishedSlot = n;
                n++;
            }
            for (int i = n; i < MaxCones; i++) { a[i] = b[i] = c[i] = Vector4.zero; }

            if (anyOccluded) UploadShadow(n);
            else EnsureShadowTex();

            Shader.SetGlobalFloat(CountId, n);
            Shader.SetGlobalFloat(PlaneId, Plane == VisionPlane.XY ? 0f : 1f);
            Shader.SetGlobalFloat(BypassId, Bypass ? 1f : 0f);
            Shader.SetGlobalVectorArray(AId, a);
            Shader.SetGlobalVectorArray(BId, b);
            Shader.SetGlobalVectorArray(CId, c);
            Shader.SetGlobalTexture(ShadowId, shadowTex);
            publishedCount = n;
        }

        static int publishedCount;

        static void EnsureShadowTex()
        {
            if (shadowTex != null) return;
            shadowTex = new Texture2D(MaxRays, MaxCones, TextureFormat.RFloat, false, true)
            {
                name = "VisionMaskShadow",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            shadowPixels = new float[MaxRays * MaxCones];
            shadowTex.SetPixelData(shadowPixels, 0);
            shadowTex.Apply(false, false);
        }

        static void UploadShadow(int n)
        {
            EnsureShadowTex();
            for (int i = 0; i < n; i++)
                if (c[i].x > 0.5f) System.Array.Copy(reach[i], 0, shadowPixels, i * MaxRays, MaxRays);
            shadowTex.SetPixelData(shadowPixels, 0);
            shadowTex.Apply(false, false);
        }

        /// World position -> plane coordinates, exactly as the shader measures them.
        public static Vector2 PlaneCoords(Vector3 world) => Plane == VisionPlane.XY
            ? new Vector2(world.x, world.y) : new Vector2(world.x, world.z);

        /// CPU twin of the shader's VisionMaskVisibility, over the LAST PUBLISHED state (same cones, same
        /// shadow rows, same maths). For gameplay questions ("can the player see this point?").
        public static bool IsVisible(Vector3 world)
        {
            if (Bypass) return true;
            var p = PlaneCoords(world);
            for (int i = 0; i < publishedCount; i++)
            {
                Vector2 eye = new Vector2(a[i].x, a[i].y), f = new Vector2(a[i].z, a[i].w);
                Vector2 d = p - eye;
                float r2 = d.sqrMagnitude;
                if (r2 <= b[i].z * b[i].z) return true;
                if (r2 > b[i].y * b[i].y || r2 <= 1e-10f) continue;
                var dir = d / Mathf.Sqrt(r2);
                if (b[i].w < 3.14159f && Vector2.Dot(dir, f) < b[i].x) continue;   // 360°: no angle limit (see VisionMask.hlsl)
                if (c[i].x < 0.5f) return true;
                float ang = Mathf.Atan2(f.x * dir.y - f.y * dir.x, Vector2.Dot(f, dir));
                float u = (ang / Mathf.Max(b[i].w, 1e-6f) + 1f) * 0.5f;
                int rays = (int)c[i].y;
                int col = Mathf.Clamp(Mathf.FloorToInt(u * rays), 0, rays - 1);
                float r = reach[i][col];
                if (r2 <= r * r) return true;
            }
            return false;
        }

        /// The shared masking material (one per hidden-alpha value, so every masked sprite batches).
        /// hiddenAlpha 0 = pixels outside every cone are invisible; e.g. 0.15 = a dim ghost/floor.
        public static Material SpriteMaterial(float hiddenAlpha = 0f)
        {
            int key = Mathf.RoundToInt(Mathf.Clamp01(hiddenAlpha) * 255f);
            if (materials.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find(SpriteShaderName);
            if (shader == null)
            {
                Debug.LogError($"VisionMask: shader '{SpriteShaderName}' not found (it ships in the tool's Resources folder).");
                return null;
            }
            m = new Material(shader) { name = $"VisionMaskSprite (hidden {key}/255)", hideFlags = HideFlags.DontSave };
            m.SetFloat(HiddenAlphaId, key / 255f);
            materials[key] = m;
            return m;
        }

        /// True when `mat` is one of the shared masking materials.
        public static bool IsMaskMaterial(Material mat) => mat != null && mat.shader != null && mat.shader.name == SpriteShaderName;

        /// Convenience: mask every stock-material sprite under `root`, now and as the rig grows renderers.
        public static VisionMasked Mask(GameObject root, float hiddenAlpha = 0f)
        {
            if (root == null) return null;
            var m = root.GetComponent<VisionMasked>();
            if (m == null) m = root.AddComponent<VisionMasked>();
            m.hiddenAlpha = hiddenAlpha;
            return m;
        }
    }
}

using Laubrary.Lattice;
using UnityEngine;

namespace Laubrary.LatticePixels
{
    /// <summary>
    /// Publishes the viewer's vision (cone + omni disc, or the occluded shadow
    /// map) as global shader parameters, so every material that includes
    /// LatticeVision.hlsl clips or colours its pixels by the same rule: the
    /// road view, sprites drawn with Laubrary/LatticePixelsSprite, anything
    /// else. Call one of the Set methods every frame the viewer moves.
    /// </summary>
    public static class LatticeVision
    {
        private static readonly int OriginId = Shader.PropertyToID("_LatticeOrigin");
        private static readonly int PlaneId = Shader.PropertyToID("_LatticePlane");
        private static readonly int PosId = Shader.PropertyToID("_LatticeVisionPos");
        private static readonly int DirId = Shader.PropertyToID("_LatticeVisionDir");
        private static readonly int CosId = Shader.PropertyToID("_LatticeVisionCos");
        private static readonly int RangeId = Shader.PropertyToID("_LatticeVisionRange");
        private static readonly int OmniId = Shader.PropertyToID("_LatticeVisionOmni");
        private static readonly int OnId = Shader.PropertyToID("_LatticeVisionOn");
        private static readonly int ModeId = Shader.PropertyToID("_LatticeVisionMode");
        private static readonly int HalfAngleId = Shader.PropertyToID("_LatticeVisionHalfAngle");
        private static readonly int ShadowMapId = Shader.PropertyToID("_LatticeShadowMap");

        private static Texture2D _shadowTex;
        private static float[] _shadow;

        /// <summary>Tells shaders where the lattice is and which plane it lies in. Call once after building the graph.</summary>
        public static void SetGraph(LatticeGraph graph)
        {
            Shader.SetGlobalVector(OriginId, graph.Origin);
            Shader.SetGlobalFloat(PlaneId, graph.Plane == LatticePlane.XZ ? 0f : 1f);
        }

        /// <summary>A forward cone (half angle, range) plus an omni disc. Nothing blocks sight.</summary>
        public static void SetCone(LatticeGraph graph, Vector3 worldPos, Vector3 worldDir, float halfAngleDeg, float range, float omniRadius)
        {
            var (u, v) = graph.PlaneCoords(worldPos);
            var (du, dv) = graph.PlaneCoords(graph.Origin + worldDir);
            Shader.SetGlobalVector(PosId, new Vector4(u, v, 0f, 0f));
            Shader.SetGlobalVector(DirId, new Vector4(du, dv, 0f, 0f));
            Shader.SetGlobalFloat(CosId, Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad));
            Shader.SetGlobalFloat(HalfAngleId, halfAngleDeg * Mathf.Deg2Rad);
            Shader.SetGlobalFloat(RangeId, range);
            Shader.SetGlobalFloat(OmniId, omniRadius);
            Shader.SetGlobalFloat(ModeId, 0f);
            Shader.SetGlobalFloat(OnId, 1f);
        }

        /// <summary>
        /// Same cone and disc, but the cone is blocked by anything that is not
        /// road: rays are cast through the canvas's road mask into a shadow
        /// map that the shaders look up by angle.
        /// </summary>
        public static void SetOccluded(LatticePixelCanvas canvas, Vector3 worldPos, Vector3 worldDir,
            float halfAngleDeg, float range, float omniRadius, int rays = 1024)
        {
            if (_shadow == null || _shadow.Length != rays)
            {
                _shadow = new float[rays];
                if (_shadowTex != null) Object.Destroy(_shadowTex);
                _shadowTex = new Texture2D(rays, 1, TextureFormat.RFloat, false, true)
                {
                    name = "LatticeShadowMap",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }
            canvas.CastShadowMap(worldPos, worldDir, halfAngleDeg, range, _shadow);
            _shadowTex.SetPixelData(_shadow, 0);
            _shadowTex.Apply(false, false);
            Shader.SetGlobalTexture(ShadowMapId, _shadowTex);

            SetCone(canvas.Graph, worldPos, worldDir, halfAngleDeg, range, omniRadius);
            Shader.SetGlobalFloat(ModeId, 1f);
        }

        /// <summary>Nothing is visible.</summary>
        public static void Clear()
        {
            Shader.SetGlobalFloat(OnId, 0f);
        }
    }
}

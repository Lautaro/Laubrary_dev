using System;
using Laubrary.Lattice;
using UnityEngine;

namespace Laubrary.LatticePixels
{
    /// <summary>
    /// Pixel-art renderer for a <see cref="LatticeGraph"/>. Roads are
    /// rasterised into a world-aligned road mask; a reveal mask is painted by
    /// stamping a brush (the player's sprite silhouette) along its swept path;
    /// a shader colours each road pixel from reveal and an optional vision
    /// cone evaluated per pixel. The CPU truth lives in <see cref="Canvas"/>;
    /// only the dirty region is uploaded each frame.
    ///
    /// Colours: index = (revealed ? 1 : 0) + (visible ? 2 : 0).
    /// </summary>
    public sealed class LatticePixelView : MonoBehaviour
    {
        [Tooltip("Texels per world unit. Pixel-art resolution of the road map.")]
        public int PixelsPerUnit = 16;

        [Tooltip("Road band width in texels.")]
        public int RoadWidthPx = 3;

        [Tooltip("Empty cells of canvas around the lattice.")]
        public int MarginCells = 1;

        [Tooltip("Lift above the lattice plane so other flat geometry can sort above it.")]
        public float SurfaceOffset = 0.005f;

        /// <summary>Colours for shadow, revealed, visible, revealed-and-visible.</summary>
        public Color[] StateColors =
        {
            new Color(0.08f, 0.08f, 0.10f, 1f),
            new Color(0.85f, 0.85f, 0.85f, 1f),
            new Color(0.30f, 0.55f, 0.85f, 1f),
            new Color(1.00f, 0.95f, 0.40f, 1f),
        };

        public LatticePixelCanvas Canvas { get; private set; }

        /// <summary>Forwarded from the canvas: every centreline texel of the edge is painted.</summary>
        public event Action<LatticeEdge> EdgeCovered;

        /// <summary>A road texel was painted for the first time, at this world position.</summary>
        public event Action<Vector3> RoadPixelRevealed;

        [Tooltip("Rays cast across the cone per frame in occluded vision mode. More is smoother at range.")]
        public int ShadowRays = 1024;

        private LatticeGraph _graph;
        private Texture2D _roadTex, _revealTex, _staging, _shadowTex;
        private float[] _shadow;
        private byte[] _stagingBuffer;
        private Material _material;
        private byte[] _brush;
        private int _brushW, _brushH;

        private static readonly int RoadMaskId = Shader.PropertyToID("_RoadMask");
        private static readonly int RevealMaskId = Shader.PropertyToID("_RevealMask");
        private static readonly int OriginId = Shader.PropertyToID("_Origin");
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int VisionPosId = Shader.PropertyToID("_VisionPos");
        private static readonly int VisionDirId = Shader.PropertyToID("_VisionDir");
        private static readonly int VisionCosId = Shader.PropertyToID("_VisionCos");
        private static readonly int VisionRangeId = Shader.PropertyToID("_VisionRange");
        private static readonly int VisionOmniId = Shader.PropertyToID("_VisionOmni");
        private static readonly int VisionOnId = Shader.PropertyToID("_VisionOn");
        private static readonly int VisionModeId = Shader.PropertyToID("_VisionMode");
        private static readonly int VisionHalfAngleId = Shader.PropertyToID("_VisionHalfAngle");
        private static readonly int ShadowMapId = Shader.PropertyToID("_ShadowMap");
        private static readonly int[] ColorIds =
        {
            Shader.PropertyToID("_ShadowColor"), Shader.PropertyToID("_RevealedColor"),
            Shader.PropertyToID("_VisibleColor"), Shader.PropertyToID("_BothColor"),
        };

        public void Build(LatticeGraph graph)
        {
            _graph = graph;
            Canvas = new LatticePixelCanvas(graph, PixelsPerUnit, RoadWidthPx, MarginCells);
            Canvas.Bake();
            Canvas.EdgeCovered += e => EdgeCovered?.Invoke(e);
            Canvas.RoadPixelRevealed += (px, py) => RoadPixelRevealed?.Invoke(Canvas.TexelWorld(px, py));

            _roadTex = NewMask("RoadMask");
            _roadTex.SetPixelData(Canvas.Road, 0);
            _roadTex.Apply(false, false);
            _revealTex = NewMask("RevealMask");
            _revealTex.SetPixelData(Canvas.Reveal, 0);
            _revealTex.Apply(false, false);

            var shader = Resources.Load<Shader>("LatticePixels");
            if (shader == null) throw new InvalidOperationException("LatticePixels shader not found in Resources.");
            _material = new Material(shader);
            _material.SetTexture(RoadMaskId, _roadTex);
            _material.SetTexture(RevealMaskId, _revealTex);
            _material.SetVector(OriginId, new Vector4(Canvas.MinU, Canvas.MinV, 0f, 0f));
            _material.SetVector(SizeId, new Vector4(Canvas.SizeU, Canvas.SizeV, 0f, 0f));
            ApplyColors();
            ClearVision();

            if (_brush == null) SetDiscBrush(2);
            BuildQuad();
        }

        /// <summary>Uses a texture's alpha as the brush. Pixels with alpha >= 128 paint. Must be readable.</summary>
        public void SetBrush(Texture2D silhouette)
        {
            _brushW = silhouette.width;
            _brushH = silhouette.height;
            var px = silhouette.GetPixels32();
            _brush = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) _brush[i] = px[i].a;
        }

        public void SetDiscBrush(int radiusPx)
        {
            _brush = LatticePixelCanvas.DiscBrush(radiusPx, out int size);
            _brushW = _brushH = size;
        }

        /// <summary>Paints the brush along the path from one world position to another and uploads the change.</summary>
        public void Paint(Vector3 from, Vector3 to)
        {
            if (Canvas == null) return;
            var dirty = Canvas.StampSweep(from, to, _brush, _brushW, _brushH);
            if (dirty.width > 0 && dirty.height > 0) Upload(dirty);
        }

        /// <summary>Forgets every painted pixel.</summary>
        public void ClearReveal()
        {
            if (Canvas == null) return;
            Canvas.ClearReveal();
            _revealTex.SetPixelData(Canvas.Reveal, 0);
            _revealTex.Apply(false, false);
        }

        /// <summary>
        /// Per-pixel vision: a forward cone (half angle, range) plus an
        /// omni-directional disc, all in world units, evaluated in the shader.
        /// </summary>
        public void SetVision(Vector3 worldPos, Vector3 worldDir, float halfAngleDeg, float range, float omniRadius)
        {
            if (_material == null) return;
            var (u, v) = _graph.PlaneCoords(worldPos);
            var (du, dv) = _graph.PlaneCoords(_graph.Origin + worldDir);
            _material.SetVector(VisionPosId, new Vector4(u, v, 0f, 0f));
            _material.SetVector(VisionDirId, new Vector4(du, dv, 0f, 0f));
            _material.SetFloat(VisionCosId, Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad));
            _material.SetFloat(VisionRangeId, range);
            _material.SetFloat(VisionOmniId, omniRadius);
            _material.SetFloat(VisionOnId, 1f);
            _material.SetFloat(VisionModeId, 0f);
        }

        /// <summary>
        /// Per-pixel vision with occlusion: the omni disc sees everything, but
        /// the forward cone is blocked by anything that is not road, so it only
        /// reaches along the street the player is looking down. Casts a shadow
        /// map on the CPU (ShadowRays rays across the cone) and hands it to the
        /// shader.
        /// </summary>
        public void SetVisionOccluded(Vector3 worldPos, Vector3 worldDir, float halfAngleDeg, float range, float omniRadius)
        {
            if (_material == null) return;
            if (_shadow == null || _shadow.Length != ShadowRays)
            {
                _shadow = new float[ShadowRays];
                if (_shadowTex != null) Destroy(_shadowTex);
                _shadowTex = new Texture2D(ShadowRays, 1, TextureFormat.RFloat, false, true)
                {
                    name = "ShadowMap",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _material.SetTexture(ShadowMapId, _shadowTex);
            }
            Canvas.CastShadowMap(worldPos, worldDir, halfAngleDeg, range, _shadow);
            _shadowTex.SetPixelData(_shadow, 0);
            _shadowTex.Apply(false, false);

            SetVision(worldPos, worldDir, halfAngleDeg, range, omniRadius);
            _material.SetFloat(VisionHalfAngleId, halfAngleDeg * Mathf.Deg2Rad);
            _material.SetFloat(VisionModeId, 1f);
        }

        public void ClearVision()
        {
            if (_material != null) _material.SetFloat(VisionOnId, 0f);
        }

        public void ApplyColors()
        {
            if (_material == null) return;
            for (int i = 0; i < 4 && i < StateColors.Length; i++) _material.SetColor(ColorIds[i], StateColors[i]);
        }

        private Texture2D NewMask(string name)
        {
            var t = new Texture2D(Canvas.Width, Canvas.Height, TextureFormat.R8, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            return t;
        }

        private void Upload(RectInt r)
        {
            if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) == 0)
            {
                _revealTex.SetPixelData(Canvas.Reveal, 0);
                _revealTex.Apply(false, false);
                return;
            }
            if (_staging == null || _staging.width < r.width || _staging.height < r.height)
            {
                int w = Mathf.Max(r.width, _staging != null ? _staging.width : 0);
                int h = Mathf.Max(r.height, _staging != null ? _staging.height : 0);
                if (_staging != null) Destroy(_staging);
                _staging = new Texture2D(w, h, TextureFormat.R8, false, true) { name = "RevealStaging" };
                _stagingBuffer = new byte[w * h];
            }
            int sw = _staging.width;
            for (int y = 0; y < r.height; y++)
                Array.Copy(Canvas.Reveal, (r.y + y) * Canvas.Width + r.x, _stagingBuffer, y * sw, r.width);
            _staging.SetPixelData(_stagingBuffer, 0);
            _staging.Apply(false, false);
            Graphics.CopyTexture(_staging, 0, 0, 0, 0, r.width, r.height, _revealTex, 0, 0, r.x, r.y);
        }

        private void BuildQuad()
        {
            var go = new GameObject("PixelRoads");
            go.transform.SetParent(transform, false);
            Vector3 lift = _graph.Plane == LatticePlane.XZ ? Vector3.up * SurfaceOffset : Vector3.back * SurfaceOffset;
            var mesh = new Mesh { name = "LatticePixelQuad" };
            mesh.vertices = new[]
            {
                _graph.Origin + _graph.PlaneVector(Canvas.MinU, Canvas.MinV) + lift,
                _graph.Origin + _graph.PlaneVector(Canvas.MinU + Canvas.SizeU, Canvas.MinV) + lift,
                _graph.Origin + _graph.PlaneVector(Canvas.MinU + Canvas.SizeU, Canvas.MinV + Canvas.SizeV) + lift,
                _graph.Origin + _graph.PlaneVector(Canvas.MinU, Canvas.MinV + Canvas.SizeV) + lift,
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (_roadTex != null) Destroy(_roadTex);
            if (_revealTex != null) Destroy(_revealTex);
            if (_staging != null) Destroy(_staging);
            if (_shadowTex != null) Destroy(_shadowTex);
            if (_material != null) Destroy(_material);
        }
    }
}

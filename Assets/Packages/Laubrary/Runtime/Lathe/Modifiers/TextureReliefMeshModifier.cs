// TextureReliefMeshModifier — like SurfaceReliefMeshModifier, but the pattern comes from a real authored
// height-field TEXTURE (any Texture2D — an ordinary heightmap, or a Shaper Tapestry height-field preset's
// own `field` texture) instead of a formula. Same box-projected UV every other Surface modifier uses, same
// "clear normals to force ToMesh() to recalculate" escape hatch Taper/Noise/Twist/SurfaceRelief already use.
// T-0124 feasibility result: bake-time cost is sub-millisecond even for a subdivided mesh; QUALITY is
// vertex-density-limited, so a low-poly base mesh needs a subdivision pass upstream to resolve fine detail
// (not built here — a future SubdivideMeshModifier is the natural companion, run before this one in the stack).
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModifierInfo("Texture Relief", "Surface")]
    [Serializable]
    public class TextureReliefMeshModifier : LatheMeshModifier
    {
        // A Read/Write-enabled Texture2D (Shaper's ShaperHeightFieldPreset.field is one; an ordinary
        // imported heightmap needs its own import settings changed to allow CPU read). Silently no-ops
        // when null or unreadable, rather than throwing on every preview repaint.
        public Texture2D heightField;
        [Range(0.1f, 20f)] public float tiling = 3f;
        [Range(-2f, 2f)] public float amount = 0.15f;

        public override string DisplayName => "Texture Relief";
        public override string Description =>
            "Displaces the surface along each vertex's own normal by an authored height-field texture "
            + "(e.g. a Shaper Tapestry height field) — real carved-in geometry, not a texture painted on a "
            + "flat surface. The texture must have Read/Write Enabled in its import settings.";

        public override void Apply(LatheMeshData data, float animT)
        {
            if (heightField == null || !heightField.isReadable) return;
            for (int i = 0; i < data.verts.Count; i++)
            {
                Vector3 p = data.verts[i];
                Vector3 n = i < data.normals.Count ? data.normals[i] : Vector3.up;
                Vector2 uv = LatheMeshData.BoxProject(p, n) * tiling;
                float u = Mathf.Repeat(uv.x, 1f);
                float v = Mathf.Repeat(uv.y, 1f);
                float h = heightField.GetPixelBilinear(u, v).r;
                data.verts[i] = p + n.normalized * (h * amount);
            }
            // The relief reshapes the surface, so the pre-displacement normals no longer match it — same
            // escape hatch SurfaceReliefMeshModifier/Taper/Noise/Twist already use: clear so ToMesh
            // recalculates from the new geometry.
            data.normals.Clear();
        }
    }
}

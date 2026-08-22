// NoiseMeshModifier — displaces each vertex along its own normal by a deterministic noise field. Adds
// organic surface roughness (a pitted rock, a gnarled trunk) without needing a texture. Deterministic —
// same seed, same mesh every time — since Perlin noise is a pure function of its input coordinates, no
// UnityEngine.Random involved.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModifierInfo("Noise", "Shape")]
    [Serializable]
    public class NoiseMeshModifier : LatheMeshModifier
    {
        [Range(0f, 2f)] public float amount = 0.05f;
        [Range(0.1f, 10f)] public float frequency = 2f;
        public int seed = 0;

        public override string DisplayName => "Noise";
        public override string Description =>
            "Displaces each vertex along its own normal by a deterministic 3D-ish noise field — organic "
            + "surface roughness with no texture needed.";

        public override void Apply(LatheMeshData data)
        {
            float sx = seed * 17.13f, sy = seed * 9.71f, sz = seed * 5.37f;
            for (int i = 0; i < data.verts.Count; i++)
            {
                Vector3 p = data.verts[i];
                Vector3 n = i < data.normals.Count ? data.normals[i] : Vector3.up;
                // Two Perlin taps over different coordinate pairs approximate a 3D field from Unity's only
                // built-in (2D) noise — cheap and plenty for a surface-roughness displacement at this scale.
                float a = Mathf.PerlinNoise((p.x + sx) * frequency, (p.y + sy) * frequency);
                float b = Mathf.PerlinNoise((p.y + sy) * frequency, (p.z + sz) * frequency);
                float disp = (a + b - 1f) * amount;   // roughly -amount .. amount
                data.verts[i] = p + n.normalized * disp;
            }
            // The displacement reshapes the surface, so the pre-noise normals no longer match it — same
            // escape hatch Taper uses: clear the list so LatheMeshData.ToMesh recalculates from the new geometry.
            data.normals.Clear();
        }
    }
}

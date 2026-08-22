// SurfaceReliefMeshModifier — a repeating pattern of grooves, ridges, or bumps stamped across a surface as
// real geometry (vertex displacement along the normal, driven by a periodic function over the same
// box-projected tiling coordinate LatheMeshData uses for UVs) rather than a texture. The "3D repeating
// texture of grooves/ridges/bumps" idea, applied to any solid's surface.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum ReliefPattern { Ridges, Grooves, Bumps, Waves }

    [LatheModifierInfo("Surface Relief", "Surface")]
    [Serializable]
    public class SurfaceReliefMeshModifier : LatheMeshModifier
    {
        public ReliefPattern pattern = ReliefPattern.Ridges;
        [Range(0f, 1f)] public float amount = 0.05f;
        [Range(0.1f, 40f)] public float frequencyU = 10f;
        [Range(0.1f, 40f)] public float frequencyV = 10f;

        public override string DisplayName => "Surface Relief";
        public override string Description =>
            "Displaces the surface along each vertex's own normal by a repeating pattern — corrugated "
            + "ridges, cut-in grooves, a grid of raised bumps, or a 2D wave weave. Real geometry, not a texture.";

        public override void Apply(LatheMeshData data)
        {
            for (int i = 0; i < data.verts.Count; i++)
            {
                Vector3 p = data.verts[i];
                Vector3 n = i < data.normals.Count ? data.normals[i] : Vector3.up;
                Vector2 uv = LatheMeshData.BoxProject(p, n);
                float disp = Evaluate(uv);
                data.verts[i] = p + n.normalized * (disp * amount);
            }
            // The relief reshapes the surface, so the pre-displacement normals no longer match it — same
            // escape hatch Taper/Noise/Twist use: clear so ToMesh recalculates from the new geometry.
            data.normals.Clear();
        }

        float Evaluate(Vector2 uv)
        {
            switch (pattern)
            {
                case ReliefPattern.Ridges:
                    return Mathf.Sin(uv.x * frequencyU * Mathf.PI * 2f);
                case ReliefPattern.Grooves:
                    // Same wave as Ridges but clamped to never rise above the surface — cut in, not raised.
                    return Mathf.Min(0f, Mathf.Sin(uv.x * frequencyU * Mathf.PI * 2f));
                case ReliefPattern.Bumps:
                {
                    float fu = Mathf.Repeat(uv.x * frequencyU, 1f) - 0.5f;
                    float fv = Mathf.Repeat(uv.y * frequencyV, 1f) - 0.5f;
                    float d = Mathf.Sqrt(fu * fu + fv * fv) * 2f;
                    return Mathf.Max(0f, 1f - d);   // a dome per grid cell, falling off to 0 at the cell edge
                }
                case ReliefPattern.Waves:
                    return Mathf.Sin(uv.x * frequencyU * Mathf.PI * 2f) * Mathf.Sin(uv.y * frequencyV * Mathf.PI * 2f);
                default:
                    return 0f;
            }
        }
    }
}

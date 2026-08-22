// SurfaceWaveMeshModifier — displaces a solid's surface with a travelling wave: ripples radiating out from
// the local origin (like water), or a jelly-like full-body wobble. Same normal-displacement technique as
// Noise/Surface Relief, but driven by animT (the turntable's current frame fraction) instead of a fixed
// spatial pattern — Speed 0 freezes the phase, so it's just as usable as a purely static wave shape.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum LatheWaveMode { Ripple, Wobble }

    [LatheModifierInfo("Surface Wave", "Shape")]
    [Serializable]
    public class SurfaceWaveMeshModifier : LatheMeshModifier
    {
        public LatheWaveMode mode = LatheWaveMode.Ripple;
        public LatheAxis axis = LatheAxis.Y;
        [Range(0f, 1f)] public float amplitude = 0.08f;
        [Range(0.1f, 10f)] public float frequency = 3f;
        [Range(0f, 5f)] public float speed = 1f;
        [Range(0f, 2f)] public float falloff = 0.3f;

        public override string DisplayName => "Surface Wave";
        public override string Description =>
            "Displaces the surface with a travelling wave — ripples radiating out from the centre (like "
            + "water), or a jelly-like full-body wobble. Speed 0 freezes it into a static wave shape.";

        public override void Apply(LatheMeshData data, float animT)
        {
            float phase = animT * speed * Mathf.PI * 2f;
            for (int i = 0; i < data.verts.Count; i++)
            {
                Vector3 p = data.verts[i];
                Vector3 n = i < data.normals.Count ? data.normals[i] : Vector3.up;
                float disp = mode == LatheWaveMode.Ripple ? Ripple(p, phase) : Wobble(p, phase);
                data.verts[i] = p + n.normalized * disp;
            }
            // Same escape hatch every other displacing modifier uses: the pre-wave normals no longer match
            // the reshaped surface, so clear them and let ToMesh recalculate from the new geometry.
            data.normals.Clear();
        }

        float Ripple(Vector3 p, float phase)
        {
            float dist = PlaneRadius(p, axis);
            float falloffMul = Mathf.Exp(-dist * falloff);
            return Mathf.Sin(dist * frequency - phase) * amplitude * falloffMul;
        }

        float Wobble(Vector3 p, float phase)
        {
            // Two low-frequency sines over the plane perpendicular to `axis`, phase-offset from each other
            // so the whole body squashes and stretches unevenly rather than rippling outward from a point —
            // the "jelly" look.
            Vector2 uv = Plane(p, axis);
            float a = Mathf.Sin(uv.x * frequency + phase);
            float b = Mathf.Sin(uv.y * frequency + phase * 1.3f + 1.7f);
            return (a + b) * 0.5f * amplitude;
        }

        static float PlaneRadius(Vector3 p, LatheAxis axis) =>
            axis == LatheAxis.X ? new Vector2(p.y, p.z).magnitude :
            axis == LatheAxis.Y ? new Vector2(p.x, p.z).magnitude :
            new Vector2(p.x, p.y).magnitude;

        static Vector2 Plane(Vector3 p, LatheAxis axis) =>
            axis == LatheAxis.X ? new Vector2(p.y, p.z) :
            axis == LatheAxis.Y ? new Vector2(p.x, p.z) :
            new Vector2(p.x, p.y);
    }
}

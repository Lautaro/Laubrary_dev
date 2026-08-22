// PrimitiveSolidModule — the baseline LatheModule: a plain box, sphere, cylinder, cone or (tube) ring.
// Proves "several solids share one 3D space" on its own (a Ring-kind + a Box-kind solid = a ring with a
// cube in the middle) and is the fallback every new LatheSolid starts as. The actual geometry builders live
// in LatheMeshBuilders, shared with SkeletonSweepModule's branch joints and SurfaceStampMeshModifier.
using System;
using UnityEngine;

namespace Laubrary.Lathe
{
    public enum PrimitiveKind { Box, Sphere, Cylinder, Cone, Ring }

    [LatheModuleInfo("Primitive", "Primitives", "cube")]
    [Serializable]
    public class PrimitiveSolidModule : LatheModule
    {
        public PrimitiveKind kind = PrimitiveKind.Box;
        [Range(0.05f, 5f)] public float size = 1f;            // box half-extent / sphere & cylinder & cone base radius / ring outer radius
        [Range(0.05f, 5f)] public float height = 1f;           // cylinder / cone / ring thickness (ring reads this as thickness * 0.1)
        [Range(0.01f, 0.95f)] public float ringInner = 0.6f;   // Ring inner radius, as a fraction of `size`
        [Range(3, 64)] public int segments = 24;                // radial segments (sphere/cylinder/cone/ring)
        [Range(2, 32)] public int rings = 12;                   // latitude rings (sphere only)
        public bool capEnds = true;                             // cylinder/cone end caps

        public override string DisplayName => "Primitive";
        public override string Description =>
            "A basic solid — box, sphere, cylinder, cone, or a flat tube ring (a Saturn-style annulus with real thickness).";

        public override void Generate(LatheMeshData data)
        {
            switch (kind)
            {
                case PrimitiveKind.Box: LatheMeshBuilders.BuildBox(data, size); break;
                case PrimitiveKind.Sphere: LatheMeshBuilders.BuildSphere(data, size, Mathf.Max(3, segments), Mathf.Max(2, rings)); break;
                case PrimitiveKind.Cylinder: LatheMeshBuilders.BuildCylinder(data, size, height, Mathf.Max(3, segments), capEnds); break;
                case PrimitiveKind.Cone: LatheMeshBuilders.BuildCone(data, size, height, Mathf.Max(3, segments), capEnds); break;
                case PrimitiveKind.Ring: LatheMeshBuilders.BuildRing(data, size, size * Mathf.Clamp01(ringInner), Mathf.Max(0.01f, height * 0.1f), Mathf.Max(3, segments)); break;
            }
        }
    }
}

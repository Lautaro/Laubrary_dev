// MirrorMeshModifier — mirrors a solid's mesh across a world axis plane, appending the reflected half.
// The "another tool could be mirroring like Lazor" idea from the original ask, applied to solid geometry
// instead of a 2D sprite: build one side, get a symmetric whole.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    [LatheModifierInfo("Mirror", "Symmetry")]
    [Serializable]
    public class MirrorMeshModifier : LatheMeshModifier
    {
        public LatheAxis axis = LatheAxis.X;
        public float plane = 0f;
        public bool weldSeam = true;
        [Range(0.001f, 0.25f)] public float weldDistance = 0.02f;

        public override string DisplayName => "Mirror";
        public override string Description =>
            "Mirrors this solid's mesh across a world axis plane and appends the reflected half — author one "
            + "side, get a symmetric whole. Vertices already on the plane are welded (not duplicated) when "
            + "'Weld Seam' is on, so the seam doesn't crack open.";

        public override void Apply(LatheMeshData data)
        {
            int axisIndex = axis == LatheAxis.X ? 0 : axis == LatheAxis.Y ? 1 : 2;
            int srcVertCount = data.verts.Count;
            var remap = new int[srcVertCount];
            for (int i = 0; i < srcVertCount; i++)
            {
                Vector3 p = data.verts[i];
                float dist = Component(p, axisIndex) - plane;
                if (weldSeam && Mathf.Abs(dist) <= weldDistance) { remap[i] = i; continue; }
                Vector3 mp = ReflectPos(p, axisIndex);
                Vector3 mn = i < data.normals.Count ? ReflectDir(data.normals[i], axisIndex) : Vector3.up;
                remap[i] = data.AddVert(mp, mn);
            }
            int srcTriCount = data.tris.Count;
            for (int t = 0; t < srcTriCount; t += 3)
            {
                int a = remap[data.tris[t]], b = remap[data.tris[t + 1]], c = remap[data.tris[t + 2]];
                // Reflection flips handedness — swap b/c so the mirrored triangle stays consistently wound.
                data.AddTri(a, c, b);
            }
        }

        static float Component(Vector3 v, int axis) => axis == 0 ? v.x : axis == 1 ? v.y : v.z;

        Vector3 ReflectPos(Vector3 v, int axis)
        {
            switch (axis)
            {
                case 0: return new Vector3(2f * plane - v.x, v.y, v.z);
                case 1: return new Vector3(v.x, 2f * plane - v.y, v.z);
                default: return new Vector3(v.x, v.y, 2f * plane - v.z);
            }
        }

        static Vector3 ReflectDir(Vector3 v, int axis)
        {
            switch (axis)
            {
                case 0: return new Vector3(-v.x, v.y, v.z);
                case 1: return new Vector3(v.x, -v.y, v.z);
                default: return new Vector3(v.x, v.y, -v.z);
            }
        }
    }
}

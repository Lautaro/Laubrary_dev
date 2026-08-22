// LatheSolid — one entry in a Lathe spec's solid stack: a transform + tint over a plug-in LatheModule's
// generated geometry, with an optional LatheMeshModifier chain (mirrors PyrePlus's layer = form + modifiers).
// Several solids share one 3D space (LatheSpec.solids), each with its own transform — the "a ring with a
// cube in the middle" case is just two solids, a Ring-kind PrimitiveSolidModule and a Box-kind one.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Lathe
{
    [Serializable]
    public class LatheSolid
    {
        public string name = "Solid";
        public bool enabled = true;
        public Vector3 position = Vector3.zero;
        public Vector3 rotationEuler = Vector3.zero;
        public Vector3 scale = Vector3.one;
        public Color tint = Color.white;

        [SerializeReference] public LatheModule module = new PrimitiveSolidModule();
        [SerializeReference] public List<LatheMeshModifier> modifiers = new List<LatheMeshModifier>();

        public LatheSolid Clone()
        {
            var c = new LatheSolid
            {
                name = name,
                enabled = enabled,
                position = position,
                rotationEuler = rotationEuler,
                scale = scale,
                tint = tint,
                module = module?.Clone(),
                modifiers = new List<LatheMeshModifier>(),
            };
            if (modifiers != null)
                foreach (var m in modifiers)
                    if (m != null) c.modifiers.Add(m.Clone());
            return c;
        }

        /// The composed local-space mesh: the module's own Generate, then every enabled modifier in
        /// order. Caller owns the returned Mesh (HideAndDontSave, never an asset) and must destroy it.
        public Mesh BuildMesh()
        {
            var data = new LatheMeshData();
            module?.Generate(data);
            if (modifiers != null)
                foreach (var m in modifiers)
                    if (m != null && m.enabled) m.Apply(data);
            return data.verts.Count > 0 && data.tris.Count > 0 ? data.ToMesh(name) : null;
        }

        /// World matrix under a shared turntable rotation (spins the whole assembly, and every solid's own
        /// pivot, around the world Y axis together) composed with this solid's own authored transform.
        public Matrix4x4 LocalToWorld(Quaternion turntable) =>
            Matrix4x4.TRS(turntable * position, turntable * Quaternion.Euler(rotationEuler), scale);
    }
}

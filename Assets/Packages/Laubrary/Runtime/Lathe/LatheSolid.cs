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
        [Range(0f, 1f)] public float metallic = 0f;
        [Range(0f, 1f)] public float smoothness = 0.5f;
        // Optional — a box/triplanar-projected UV set is generated automatically (LatheMeshData.ToMesh), so
        // any texture drops straight on without per-shape unwrap work. Null = tint-only (a white texture
        // multiplies through as a no-op), same look as before this field existed.
        public Texture2D texture;
        // A procedural gradient (LatheFillKind != None) TAKES OVER the texture slot at render time — see
        // LathePreview/LatheBakePass. Default None: the plain `texture` field above (or tint alone) applies
        // exactly as before this field existed.
        public LatheSurfaceFill fill = new LatheSurfaceFill();

        // Emit Light — OFF by default and its sub-fields aren't drawn in the UI unless enabled (avoid-bloat
        // rule: optional/modular features stay invisible until opted into). Drives BOTH an emissive material
        // look on this solid's own surface AND a real point Light spawned at its position, so it actually
        // illuminates NEIGHBOURING solids too — "a light bulb on a spaceship" needs the hull around it lit,
        // not just the bulb mesh itself glowing.
        public bool emitLight = false;
        public Color lightColor = Color.white;
        [Range(0f, 10f)] public float lightIntensity = 2f;
        [Range(0.1f, 20f)] public float lightRange = 5f;
        [Range(0f, 5f)] public float emissiveBoost = 1f;   // how bright the surface itself glows (lightColor × this)

        // Animate Texture — OFF by default, same avoid-bloat gating. Scrolls the surface texture/fill's UV
        // offset over the turntable's own frame index, so a tiled texture (a conveyor belt, a scrolling
        // light strip) visibly animates across a baked sprite strip, not just in the live preview.
        public bool animateTexture = false;
        public Vector2 scrollSpeed = new Vector2(0.2f, 0f);   // UV units per full turntable loop
        [Range(0.2f, 10f)] public float tileScale = 1f;

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
                metallic = metallic,
                smoothness = smoothness,
                texture = texture,
                fill = fill?.Clone(),
                emitLight = emitLight,
                lightColor = lightColor,
                lightIntensity = lightIntensity,
                lightRange = lightRange,
                emissiveBoost = emissiveBoost,
                animateTexture = animateTexture,
                scrollSpeed = scrollSpeed,
                tileScale = tileScale,
                module = module?.Clone(),
                modifiers = new List<LatheMeshModifier>(),
            };
            if (modifiers != null)
                foreach (var m in modifiers)
                    if (m != null) c.modifiers.Add(m.Clone());
            return c;
        }

        /// The composed local-space mesh: the module's own Generate, then every enabled modifier in
        /// order. `animT` (0..1, wraps) is the turntable's current frame fraction, passed straight through
        /// to each modifier — only an animating modifier (Surface Wave) reads it. Caller owns the returned
        /// Mesh (HideAndDontSave, never an asset) and must destroy it.
        public Mesh BuildMesh(float animT = 0f)
        {
            var data = new LatheMeshData();
            module?.Generate(data);
            if (modifiers != null)
                foreach (var m in modifiers)
                    if (m != null && m.enabled) m.Apply(data, animT);
            return data.verts.Count > 0 && data.tris.Count > 0 ? data.ToMesh(name) : null;
        }

        /// World matrix under a shared turntable rotation (spins the whole assembly, and every solid's own
        /// pivot, around the world Y axis together) composed with this solid's own authored transform.
        public Matrix4x4 LocalToWorld(Quaternion turntable) =>
            Matrix4x4.TRS(turntable * position, turntable * Quaternion.Euler(rotationEuler), scale);
    }
}

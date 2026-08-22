// LathePreview — a minimal PreviewRenderUtility scene for Lathe: draws every enabled solid's
// freshly-generated mesh with a simple lit, double-sided material, camera orbit-controlled by the window.
// No baking, no colour grade (contrast PyrePlusPlayback3DPreview, which matches a specific VFX pack's HDR
// look) — Lathe's meshes are a few hundred verts each, cheap enough to rebuild every repaint
// (LatheSolid.BuildMesh), so there is nothing here to cache either.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Lathe.Editor
{
    public class LathePreview : System.IDisposable
    {
        PreviewRenderUtility util;
        Material litMat;
        string colorProp, texProp, stProp, emissionProp;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly List<Mesh> scratchMeshes = new List<Mesh>();
        readonly List<Texture2D> scratchTextures = new List<Texture2D>();
        readonly List<GameObject> scratchLights = new List<GameObject>();
        const int FillBakeResolution = 64;

        void EnsureUtil()
        {
            if (util != null) return;
            util = new PreviewRenderUtility();
            util.camera.nearClipPlane = 0.05f;
            util.camera.farClipPlane = 100f;
            util.camera.clearFlags = CameraClearFlags.SolidColor;
            util.camera.allowHDR = false;
            util.camera.allowMSAA = false;
            util.lights[0].intensity = 1.1f;
            util.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            util.lights[1].intensity = 0.5f;
            util.lights[1].transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            // A metallic/smooth material reflects its ENVIRONMENT, not the two directional lights — with no
            // skybox or reflection probe in this isolated preview scene, a fully metallic solid would just
            // render black. A flat ambient colour is what a shader's indirect/reflection term falls back to
            // with nothing else to sample, so this is what makes "shiny" actually read as shiny here rather
            // than as a black void with two hard specular dots.
            util.ambientColor = new Color(0.45f, 0.47f, 0.5f);
        }

        Material LitMat
        {
            get
            {
                if (litMat != null) return litMat;
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                litMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // Double-sided: v1's mesh builders don't guarantee winding matches every render pipeline's
                // front-face convention, and every solid here is closed anyway (the inside is never meant to
                // be seen) — Cull Off sidesteps the whole question rather than risking inside-out geometry.
                if (litMat.HasProperty("_Cull")) litMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                colorProp = litMat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                texProp = litMat.HasProperty("_BaseMap") ? "_BaseMap" : litMat.HasProperty("_MainTex") ? "_MainTex" : null;
                stProp = litMat.HasProperty("_BaseMap_ST") ? "_BaseMap_ST" : litMat.HasProperty("_MainTex_ST") ? "_MainTex_ST" : null;
                emissionProp = litMat.HasProperty("_EmissionColor") ? "_EmissionColor" : null;
                // A material property BLOCK can't toggle a shader KEYWORD per-draw — only the material
                // itself can. Enabling it once, globally, is still correct: a black _EmissionColor (every
                // non-emitting solid's default) contributes nothing, so leaving the keyword on costs nothing
                // visually for the common case.
                if (emissionProp != null) litMat.EnableKeyword("_EMISSION");
                return litMat;
            }
        }

        /// The live preview camera — valid immediately after Render() returns (same frame), used by
        /// LatheWindow's skeleton editor to ray-cast clicks and project node positions to screen space.
        public Camera Camera => util?.camera;

        void ClearScratch()
        {
            foreach (var m in scratchMeshes) if (m != null) Object.DestroyImmediate(m);
            scratchMeshes.Clear();
            foreach (var t in scratchTextures) if (t != null) Object.DestroyImmediate(t);
            scratchTextures.Clear();
            foreach (var go in scratchLights) if (go != null) Object.DestroyImmediate(go);
            scratchLights.Clear();
        }

        /// Renders one frame. `turntableDeg` spins the whole assembly (and each solid's own pivot) around
        /// world Y; `orbitYaw/orbitPitch/orbitDist` place the camera independently of that spin. `animT`
        /// (0..1, wraps) drives Animate Texture's UV scroll — normally frame/turntableFrames, so a scrolling
        /// surface animates across the baked sprite strip, not just in the live preview.
        public Texture Render(LatheSpec spec, Rect viewSize, float turntableDeg, float orbitYaw, float orbitPitch, float orbitDist, float animT = 0f)
        {
            if (spec == null) return null;
            EnsureUtil();
            ClearScratch();

            int w = Mathf.Clamp(Mathf.RoundToInt(viewSize.width), 8, 2048);
            int h = Mathf.Clamp(Mathf.RoundToInt(viewSize.height), 8, 2048);
            util.camera.backgroundColor = spec.previewBackground;

            var mat = LitMat;
            var turntable = Quaternion.Euler(0f, turntableDeg, 0f);

            util.BeginPreview(new Rect(0, 0, w, h), GUIStyle.none);
            try
            {
                Quaternion camRot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
                util.camera.transform.position = camRot * new Vector3(0f, 0f, -Mathf.Max(0.2f, orbitDist));
                util.camera.transform.LookAt(Vector3.zero);
                util.camera.fieldOfView = 35f;

                if (spec.solids != null)
                    foreach (var solid in spec.solids)
                    {
                        if (solid == null || !solid.enabled) continue;
                        var mesh = solid.BuildMesh(animT);
                        if (mesh == null) continue;
                        scratchMeshes.Add(mesh);
                        block.Clear();
                        block.SetColor(colorProp, solid.tint);
                        if (mat.HasProperty("_Metallic")) block.SetFloat("_Metallic", solid.metallic);
                        if (mat.HasProperty("_Smoothness")) block.SetFloat("_Smoothness", solid.smoothness);
                        else if (mat.HasProperty("_Glossiness")) block.SetFloat("_Glossiness", solid.smoothness);
                        if (texProp != null || stProp != null)
                        {
                            Texture2D fillTex = solid.fill != null && solid.fill.kind != LatheFillKind.None
                                ? LatheFillBaker.Bake(solid.fill, FillBakeResolution) : null;
                            if (fillTex != null) scratchTextures.Add(fillTex);

                            Texture2D layerTex = fillTex == null && solid.secondTextureLayer && solid.texture2 != null
                                ? LatheTextureLayerBaker.Bake(solid, animT, FillBakeResolution) : null;
                            if (layerTex != null) scratchTextures.Add(layerTex);

                            if (texProp != null)
                                block.SetTexture(texProp, fillTex != null ? fillTex : layerTex != null ? layerTex
                                    : solid.texture != null ? solid.texture : Texture2D.whiteTexture);

                            if (stProp != null)
                            {
                                // A baked 2-layer composite already has each layer's own tiling/scroll applied
                                // per-pixel — the material's own UV transform must stay identity so it isn't
                                // applied a second time on top (which would move both layers together and
                                // defeat "animate them differently").
                                Vector2 offs = layerTex == null && solid.animateTexture ? solid.scrollSpeed * animT : Vector2.zero;
                                float tile = layerTex == null ? solid.tileScale : 1f;
                                block.SetVector(stProp, new Vector4(tile, tile, offs.x, offs.y));
                            }
                        }
                        if (emissionProp != null)
                            block.SetColor(emissionProp, solid.emitLight ? solid.lightColor * solid.emissiveBoost : Color.black);
                        var worldMatrix = solid.LocalToWorld(turntable);
                        util.DrawMesh(mesh, worldMatrix, mat, 0, block);

                        if (solid.emitLight)
                        {
                            var lightGo = new GameObject("LatheLight") { hideFlags = HideFlags.HideAndDontSave };
                            var light = lightGo.AddComponent<Light>();
                            light.type = LightType.Point;
                            light.color = solid.lightColor;
                            light.intensity = solid.lightIntensity;
                            light.range = solid.lightRange;
                            lightGo.transform.position = worldMatrix.MultiplyPoint3x4(Vector3.zero);
                            util.AddSingleGO(lightGo);
                            scratchLights.Add(lightGo);
                        }
                    }
                util.camera.Render();
                return util.EndPreview();
            }
            catch
            {
                util.EndPreview();
                throw;
            }
        }

        public void Dispose()
        {
            ClearScratch();
            if (litMat != null) { Object.DestroyImmediate(litMat); litMat = null; }
            if (util != null) { util.Cleanup(); util = null; }
        }
    }
}

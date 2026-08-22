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
        string colorProp, texProp;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly List<Mesh> scratchMeshes = new List<Mesh>();

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
                return litMat;
            }
        }

        void ClearScratch()
        {
            foreach (var m in scratchMeshes) if (m != null) Object.DestroyImmediate(m);
            scratchMeshes.Clear();
        }

        /// Renders one frame. `turntableDeg` spins the whole assembly (and each solid's own pivot) around
        /// world Y; `orbitYaw/orbitPitch/orbitDist` place the camera independently of that spin.
        public Texture Render(LatheSpec spec, Rect viewSize, float turntableDeg, float orbitYaw, float orbitPitch, float orbitDist)
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
                        var mesh = solid.BuildMesh();
                        if (mesh == null) continue;
                        scratchMeshes.Add(mesh);
                        block.Clear();
                        block.SetColor(colorProp, solid.tint);
                        if (texProp != null)
                            block.SetTexture(texProp, solid.texture != null ? solid.texture : Texture2D.whiteTexture);
                        util.DrawMesh(mesh, solid.LocalToWorld(turntable), mat, 0, block);
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

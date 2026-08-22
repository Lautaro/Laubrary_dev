// LatheBakePass — a one-shot-per-bake PreviewRenderUtility wrapper, separate from the live LathePreview so
// a bake never fights the interactive preview's own render state. Renders supersampled then box-filters
// down to the target pixel size — crisp edges on flat-lit solids without a soft-VFX bloom chain (contrast
// PyrePlusPlayback3DPreview, which grades against a specific HDR VFX pack; Lathe's solids need none of that).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Lathe.Editor
{
    class LatheBakePass : System.IDisposable
    {
        PreviewRenderUtility util;
        Material litMat;
        string colorProp, texProp, stProp, emissionProp;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

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
            util.ambientColor = new Color(0.45f, 0.47f, 0.5f);   // see LathePreview's EnsureUtil for why

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            litMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (litMat.HasProperty("_Cull")) litMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            colorProp = litMat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            texProp = litMat.HasProperty("_BaseMap") ? "_BaseMap" : litMat.HasProperty("_MainTex") ? "_MainTex" : null;
            stProp = litMat.HasProperty("_BaseMap_ST") ? "_BaseMap_ST" : litMat.HasProperty("_MainTex_ST") ? "_MainTex_ST" : null;
            emissionProp = litMat.HasProperty("_EmissionColor") ? "_EmissionColor" : null;
            if (emissionProp != null) litMat.EnableKeyword("_EMISSION");
        }

        /// Renders one turntable frame at `size` × `supersample`, then box-filters it down to `size`×`size`.
        /// The camera is FIXED (orbitYaw/orbitPitch/fitRadius) — only the subject spins (turntableDeg) —
        /// which is the correct sprite-sheet convention (a fixed viewing angle, a turning subject).
        public Texture2D RenderFrame(LatheSpec spec, int size, int supersample, float turntableDeg,
            float orbitYaw, float orbitPitch, float fitRadius)
        {
            EnsureUtil();
            int ss = size * Mathf.Max(1, supersample);
            util.camera.backgroundColor = spec.previewBackground;
            util.camera.fieldOfView = 35f;

            var turntable = Quaternion.Euler(0f, turntableDeg, 0f);
            Quaternion camRot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            float dist = fitRadius / Mathf.Sin(35f * 0.5f * Mathf.Deg2Rad);
            util.camera.transform.position = camRot * new Vector3(0f, 0f, -Mathf.Max(0.2f, dist));
            util.camera.transform.LookAt(Vector3.zero);

            float animT = Mathf.Repeat(turntableDeg / 360f, 1f);
            var scratch = new List<Mesh>();
            var scratchTex = new List<Texture2D>();
            var scratchLights = new List<GameObject>();
            Texture result;
            util.BeginPreview(new Rect(0, 0, ss, ss), GUIStyle.none);
            try
            {
                if (spec.solids != null)
                    foreach (var solid in spec.solids)
                    {
                        if (solid == null || !solid.enabled) continue;
                        var mesh = solid.BuildMesh(animT);
                        if (mesh == null) continue;
                        scratch.Add(mesh);
                        block.Clear();
                        block.SetColor(colorProp, solid.tint);
                        if (litMat.HasProperty("_Metallic")) block.SetFloat("_Metallic", solid.metallic);
                        if (litMat.HasProperty("_Smoothness")) block.SetFloat("_Smoothness", solid.smoothness);
                        else if (litMat.HasProperty("_Glossiness")) block.SetFloat("_Glossiness", solid.smoothness);
                        if (texProp != null)
                        {
                            Texture2D fillTex = solid.fill != null && solid.fill.kind != LatheFillKind.None
                                ? LatheFillBaker.Bake(solid.fill, 64) : null;
                            if (fillTex != null) scratchTex.Add(fillTex);
                            block.SetTexture(texProp, fillTex != null ? fillTex : solid.texture != null ? solid.texture : Texture2D.whiteTexture);
                        }
                        if (stProp != null)
                        {
                            Vector2 offs = solid.animateTexture ? solid.scrollSpeed * animT : Vector2.zero;
                            block.SetVector(stProp, new Vector4(solid.tileScale, solid.tileScale, offs.x, offs.y));
                        }
                        if (emissionProp != null)
                            block.SetColor(emissionProp, solid.emitLight ? solid.lightColor * solid.emissiveBoost : Color.black);
                        var worldMatrix = solid.LocalToWorld(turntable);
                        util.DrawMesh(mesh, worldMatrix, litMat, 0, block);

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
                result = util.EndPreview();
            }
            finally
            {
                foreach (var m in scratch) if (m != null) Object.DestroyImmediate(m);
                foreach (var t in scratchTex) if (t != null) Object.DestroyImmediate(t);
                foreach (var go in scratchLights) if (go != null) Object.DestroyImmediate(go);
            }

            var down = Downsample(result, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var prevActive = RenderTexture.active;
            RenderTexture.active = down;
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(down);
            return tex;
        }

        // Halves repeatedly (a box filter each step) rather than one big bilinear blit straight to the
        // target — the same reasoning PyrePlusPlayback3DPreview documents: a single big downsample only
        // samples a small neighbourhood per output pixel and most of the frame is never read.
        static RenderTexture Downsample(Texture src, int targetSize)
        {
            RenderTexture cur = null;
            int w = src.width, h = src.height;
            while (w / 2 >= targetSize && h / 2 >= targetSize)
            {
                var next = RenderTexture.GetTemporary(w / 2, h / 2, 0, RenderTextureFormat.ARGB32);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(cur != null ? (Texture)cur : src, next);
                if (cur != null) RenderTexture.ReleaseTemporary(cur);
                cur = next;
                w /= 2; h /= 2;
            }
            if (cur == null || w != targetSize || h != targetSize)
            {
                var final = RenderTexture.GetTemporary(targetSize, targetSize, 0, RenderTextureFormat.ARGB32);
                final.filterMode = FilterMode.Bilinear;
                Graphics.Blit(cur != null ? (Texture)cur : src, final);
                if (cur != null) RenderTexture.ReleaseTemporary(cur);
                return final;
            }
            return cur;
        }

        public void Dispose()
        {
            if (litMat != null) { Object.DestroyImmediate(litMat); litMat = null; }
            if (util != null) { util.Cleanup(); util = null; }
        }
    }
}

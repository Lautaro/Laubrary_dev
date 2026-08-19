// PyrePlusPlayback3DPreview — PROOF OF CONCEPT. Drives a PreviewRenderUtility scene that instantiates the
// Playback3D layer's assigned prefab, applies Speed/Scale/Tint to its ParticleSystem(s), and renders either the
// live 3D frame or a point-filtered pixelated downsample of it. This is the ONE thing this new ShapeForm is
// actually about — see PyrePlusWindow.BuildPlaybackBox for the dials and PyrePlusWindow.Preview.cs's DrawPreview
// for where this class's output replaces the normal composited-canvas blit when the selected layer is Playback3D.
//
// Deliberately NOT the same render path as PyrePlusRenderer (rule #7, "one shared core", does not apply here —
// there is no deterministic closed-form/replay-harness core for a live Unity ParticleSystem sim; PyrePlusRenderer
// itself documents this shape as an editor-preview-only stub with no runtime bake yet).
using UnityEditor;
using UnityEngine;

namespace Laubrary.PyrePlus.Editor
{
    public class PyrePlusPlayback3DPreview : System.IDisposable
    {
        PreviewRenderUtility util;
        GameObject instance;
        GameObject instancedFrom;   // the prefab `instance` was built from, so a prefab swap rebuilds it
        ParticleSystem[] systems;
        double simulatedTime;       // seconds of ParticleSystem time currently simulated up to

        // A small offscreen RenderTexture the pixelated mode downsamples into, point-filtered, then blits back up
        // at the requested grid size. Rebuilt only when the grid size changes.
        RenderTexture pixelRT;
        int pixelRTSize = -1;

        public void EnsureUtil()
        {
            if (util != null) return;
            util = new PreviewRenderUtility();
            util.cameraFieldOfView = 40f;
            util.camera.farClipPlane = 100f;
            util.camera.nearClipPlane = 0.05f;
            // Pulled back/raised from the original (0, 0.6, -3.2) framing, which clipped tall fire/explosion
            // prefabs that rise well above the origin — most VFX packs (Vefects included) pivot at the base.
            util.camera.transform.position = new Vector3(0f, 1.4f, -6.5f);
            util.camera.transform.LookAt(new Vector3(0f, 1f, 0f));
            util.lights[0].intensity = 1.1f;
            util.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            util.lights[1].intensity = 0.4f;
        }

        // (Re)builds the instantiated prefab when the assigned prefab changes (or first use). Destroys the
        // previous instance first — PreviewRenderUtility's own scene, never the real one, so DestroyImmediate is
        // safe/appropriate here (no Undo needed: pure preview state, per the authoring guide's rule #5 exception).
        void EnsureInstance(GameObject prefab)
        {
            if (instance != null && instancedFrom == prefab) return;
            if (instance != null) { Object.DestroyImmediate(instance); instance = null; systems = null; }
            instancedFrom = prefab;
            if (prefab == null) return;

            EnsureUtil();
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            util.AddSingleGO(instance);
            systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            simulatedTime = -1;   // force a re-simulate on the next Render call
        }

        // Applies Speed/Scale/Tint to every ParticleSystem's main module (no reflection needed — MainModule
        // exposes all three directly), then simulates from t=0 up to `scrubSeconds` (Simulate's own "restart"
        // param handles the from-zero re-run; PlayaFudge-free — this IS the deterministic-per-scrub behaviour
        // the Scrub dial promises: the same scrubSeconds always re-simulates to the same visible state).
        void ApplyDialsAndSimulate(PyrePlusLayer layer, float scrubSeconds)
        {
            if (instance == null) return;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.001f, layer.playbackScale);
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                var main = ps.main;
                main.simulationSpeed = Mathf.Max(0.001f, layer.playbackSpeed);
                main.startColor = layer.playbackTint;
                // startSize scaling is folded into the instance's transform scale above (cheaper than rewriting
                // every ParticleSystem's startSize curve/const, and visually equivalent for a uniform scale).
            }
            // restart:true replays deterministically from t=0 each time — Simulate has no incremental "advance
            // from where we left off" that's safe to call from arbitrary editor repaints, so a full re-simulate
            // to the target time is the correct (if not the cheapest) way to land on an exact scrub position.
            for (int i = 0; i < systems.Length; i++)
                systems[i].Simulate(Mathf.Max(0f, scrubSeconds), true, true, false);
            simulatedTime = scrubSeconds;
        }

        // Renders one frame at the given scrub position (seconds into the loop) and returns the resulting
        // texture — either the raw 3D render, or (pixelated == true) a point-filtered downsample of it blown
        // back up to viewSize so it fills the same preview rect. Caller does NOT own/destroy the returned
        // texture (util.EndPreview's own RT and our pixelRT are both owned here).
        public Texture Render(PyrePlusLayer layer, Rect viewSize, bool pixelated, int pixelGrid)
        {
            EnsureInstance(layer.playbackPrefab);
            if (instance == null) return null;

            EnsureUtil();
            int w = Mathf.Max(4, Mathf.RoundToInt(viewSize.width));
            int h = Mathf.Max(4, Mathf.RoundToInt(viewSize.height));
            util.BeginPreview(new Rect(0, 0, w, h), GUIStyle.none);

            ApplyDialsAndSimulate(layer, layer.playbackScrub01 * Mathf.Max(0.01f, layer.playbackLoopDuration));

            util.camera.Render();
            Texture full = util.EndPreview();   // a temp RT owned by PreviewRenderUtility, valid until the next Begin/EndPreview

            if (!pixelated) return full;

            int grid = Mathf.Clamp(pixelGrid, 8, 128);
            if (pixelRT == null || pixelRTSize != grid)
            {
                if (pixelRT != null) { pixelRT.Release(); Object.DestroyImmediate(pixelRT); }
                pixelRT = new RenderTexture(grid, grid, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                pixelRTSize = grid;
            }
            // Point-filtered downsample: Blit from the full-res render into the small grid RT. Reading `full`
            // back requires it be a RenderTexture (which EndPreview's result is), so this is a straight GPU blit.
            // MUST restore RenderTexture.active afterward: Graphics.Blit leaves the small pixelRT active, and this
            // whole method runs inside an IMGUI Repaint callback (PyrePlusWindow.Preview.cs's DrawPlayback3DPreview)
            // — under URP, leaving the wrong RT active here corrupted every GUILayout call drawn AFTER the preview
            // in the same pass (the transport row / backdrop controls silently stopped rendering; confirmed live,
            // this was NOT a false alarm). Save/restore is the standard safe pattern for a Blit issued outside a
            // camera's own render callback.
            var prevActive = RenderTexture.active;
            Graphics.Blit(full, pixelRT);
            RenderTexture.active = prevActive;
            return pixelRT;
        }

        public void Dispose()
        {
            if (instance != null) { Object.DestroyImmediate(instance); instance = null; }
            if (pixelRT != null) { pixelRT.Release(); Object.DestroyImmediate(pixelRT); pixelRT = null; }
            if (util != null) { util.Cleanup(); util = null; }
        }
    }
}

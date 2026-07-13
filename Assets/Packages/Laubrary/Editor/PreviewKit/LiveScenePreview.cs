using System;
using UnityEditor;
using UnityEngine;

namespace Laubrary.PreviewKit.Editor
{
    /// <summary>
    /// Renders a REAL, isolated Unity scene — actual GameObjects and components (SpriteRenderers,
    /// MonoBehaviours, whatever gameplay itself uses) — into an IMGUI editor preview via an orthographic
    /// camera. Wraps <see cref="UnityEditor.PreviewRenderUtility"/> (Unity's own isolated-scene preview
    /// primitive — the same mechanism behind the built-in material/prefab preview thumbnails) with a small,
    /// world-space-focused API.
    ///
    /// The point: an IMGUI window that wants to show "what will this actually look like in the game" should
    /// spawn the SAME components gameplay uses (a real SpriteRenderer, a real ZonedAnimationPlayer, a real
    /// BlastPlayer, ...) into this preview's isolated scene and let a real Camera render them — instead of
    /// hand-reimplementing pivot/scale/flip math in IMGUI, which can only ever approximate the real rendering
    /// and silently drifts out of sync the moment the real component's own behavior changes (a new
    /// pixelsPerUnit, a pivot convention tweak, a field nobody remembered to mirror). Generic and Pyre-agnostic
    /// on purpose — any Laubrary editor tool wanting a "guaranteed same as gameplay" preview can use this.
    ///
    /// One instance per IMGUI window/preview; call <see cref="Dispose"/> when the window closes or the preview
    /// is torn down (its own isolated scene and render target are real Editor resources that must be freed).
    /// </summary>
    public class LiveScenePreview : IDisposable
    {
        readonly PreviewRenderUtility util;
        bool disposed;

        /// The isolated scene's camera — read-only access for callers that need to project world points into
        /// this preview's own screen space (e.g. <c>WorldToScreenPoint</c>) to align other IMGUI elements to
        /// something rendered here.
        public Camera Camera => util.camera;

        public LiveScenePreview()
        {
            util = new PreviewRenderUtility();
            util.camera.orthographic = true;
            util.camera.clearFlags = CameraClearFlags.SolidColor;
            util.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent — composite over the caller's own backdrop
            util.camera.nearClipPlane = 0.01f;
            util.camera.farClipPlane = 100f;
            util.camera.cullingMask = -1;
            foreach (var l in util.lights) l.enabled = false;           // unlit 2D content — sprites don't need scene lights
            util.ambientColor = Color.white;
        }

        /// Moves an EXISTING GameObject (with whatever real components you've already added to it) into this
        /// preview's isolated scene. Idempotent from the caller's side — call once per object at creation
        /// time, not every repaint; Unity errors on re-adding an already-adopted object. Note: Unity itself
        /// forces the object's hideFlags to HideAndDontSave and its layer to the preview culling layer as
        /// part of this move — set up any child renderers etc. before or after, both are fine.
        public void Adopt(GameObject go) => util.AddSingleGO(go);

        /// Creates a fresh GameObject directly inside this preview's isolated scene — never the active scene,
        /// never visible in the Hierarchy. Add whatever real gameplay components you need to it.
        public GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            Adopt(go);
            return go;
        }

        /// Points the orthographic camera at worldCenter, matching a real 2D gameplay camera's own convention
        /// (looking down -Z; worldHeight is the TOTAL visible height in world units, i.e. 2x orthographicSize
        /// — the same quantity a gameplay camera's `orthographicSize * 2` represents).
        public void Frame(Vector3 worldCenter, float worldHeight)
        {
            util.camera.transform.position = new Vector3(worldCenter.x, worldCenter.y, worldCenter.z - 10f);
            util.camera.transform.rotation = Quaternion.identity;
            util.camera.orthographicSize = Mathf.Max(0.01f, worldHeight * 0.5f);
        }

        /// Renders the current scene state at screenRect's size and returns the resulting texture. Call during
        /// a Repaint event, after positioning/ticking whatever you spawned and calling <see cref="Frame"/>.
        public Texture Render(Rect screenRect)
        {
            util.BeginPreview(screenRect, GUIStyle.none);
            util.camera.Render();
            return util.EndPreview();
        }

        /// Renders and draws directly into screenRect (alpha-blended, so a transparent background composites
        /// cleanly over whatever the caller already drew) — the common case.
        public void Draw(Rect screenRect)
        {
            var tex = Render(screenRect);
            if (tex != null) GUI.DrawTexture(screenRect, tex, ScaleMode.StretchToFill, true);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            util.Cleanup();
        }
    }
}

// LazorWindow.Preview.cs — renders the canvas STROKES through Laubrary's ONE shared vector renderer (LazorDraw over
// Shapes) into a RenderTexture, which is blitted into the canvas. Zoom/pan are the preview CAMERA's orthographic
// size + position, so off-screen geometry is scissor-clipped by the GPU — the old IMGUI per-segment culling (lines
// vanishing / starting from the screen edge when zoomed in) is gone by construction, and the canvas is now
// pixel-identical to what the game draws. Grid, symmetry guides, vertex handles and the pen rubber-band stay as
// IMGUI overlays on top. Compiled only when Shapes is present (SHAPES_INSTALLED); otherwise the canvas falls back
// to the legacy IMGUI stroke drawing (see LazorWindow.Canvas.cs).

#if SHAPES_INSTALLED
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Laubrary.Lazor;
using Laubrary.VectorRendering;

namespace Laubrary.Lazor.Editor
{
    public partial class LazorWindow
    {
        Camera _previewCam;
        GameObject _previewCamGo;
        RenderTexture _previewRt;
        readonly List<ResolvedPolyline> _previewResolved = new List<ResolvedPolyline>();
        readonly LazorStrokeBatch _previewBatch = new LazorStrokeBatch();

        void EnsurePreviewCam()
        {
            if (_previewCamGo != null) return;
            _previewCamGo = EditorUtility.CreateGameObjectWithHideFlags("LazorPreviewCam", HideFlags.HideAndDontSave, typeof(Camera));
            _previewCam = _previewCamGo.GetComponent<Camera>();
            _previewCam.orthographic = true;
            _previewCam.clearFlags = CameraClearFlags.SolidColor;
            _previewCam.backgroundColor = CanvasBg;
            _previewCam.cullingMask = 0;          // nothing via normal rendering — we draw in endCameraRendering
            _previewCam.enabled = false;          // rendered on demand via SubmitRenderRequest
            _previewCam.nearClipPlane = 0.01f;
            _previewCam.farClipPlane = 100f;
        }

        // Render the current shape's strokes to _previewRt, framed to match the canvas zoom/pan (grid units).
        void RenderStrokePreview(Rect canvasRect)
        {
            if (shape == null) return;
            EnsurePreviewCam();

            int w = Mathf.Max(4, Mathf.RoundToInt(canvasRect.width));
            int h = Mathf.Max(4, Mathf.RoundToInt(canvasRect.height));
            if (_previewRt == null || _previewRt.width != w || _previewRt.height != h)
            {
                if (_previewRt != null) { _previewRt.Release(); Object.DestroyImmediate(_previewRt); }
                _previewRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                _previewRt.Create();
            }

            // Camera frames grid units so a grid point g lands at the same pixel as GridToLocal(g): orthoSize is
            // half the visible grid-units tall (h px / zoom), centred where the canvas centre sits in grid space.
            _previewCam.orthographicSize = h / (2f * zoom);
            _previewCam.aspect = (float)w / h;
            _previewCam.transform.position = new Vector3(-pan.x / zoom, pan.y / zoom, -10f);

            // Resolve NORMALIZED (~±0.5) once; LazorDraw scales points+thickness by gridResolution -> grid units,
            // which the camera then maps to pixels at `zoom` (so stroke width matches PxThickness automatically).
            _previewResolved.Clear();
            _previewResolved.AddRange(LazorGeometry.Resolve(shape));

            // Shapes' own SRP hook (ImmediateModeShapeDrawer) draws on beginCameraRendering, not end — by
            // endCameraRendering the camera's target is already resolved, so an immediate-mode Draw.Command issued
            // there never lands in the RT (dots/handles still drew because those are separate IMGUI overlays).
            RenderPipelineManager.beginCameraRendering += OnPreviewCameraRender;
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = _previewRt };
                if (RenderPipeline.SupportsRenderRequest(_previewCam, req))
                    RenderPipeline.SubmitRenderRequest(_previewCam, req);
            }
            finally { RenderPipelineManager.beginCameraRendering -= OnPreviewCameraRender; }
        }

        void OnPreviewCameraRender(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _previewCam) return;
            float gr = Mathf.Max(1, shape.gridResolution);
            // Points resolve normalized (~±0.5); scale by gridResolution -> grid units (matches the canvas overlays),
            // which the camera maps to pixels at `zoom` so stroke width matches PxThickness. Identity matrix (rigid).
            _previewBatch.Draw(cam, _previewResolved, Matrix4x4.identity, gr, 1f, Color.white);
        }

        // Blit the rendered strokes into the canvas (clip-local rect — called inside GUI.BeginClip).
        void BlitStrokePreview(Rect canvasRect)
        {
            if (_previewRt == null) return;
            GUI.DrawTexture(new Rect(0, 0, canvasRect.width, canvasRect.height), _previewRt, ScaleMode.StretchToFill, false);
        }

        // Hooked from OnDisable via the partial-method declaration in LazorWindow.Canvas.cs.
        partial void CleanupPreview()
        {
            if (_previewRt != null) { _previewRt.Release(); Object.DestroyImmediate(_previewRt); _previewRt = null; }
            if (_previewCamGo != null) { Object.DestroyImmediate(_previewCamGo); _previewCamGo = null; }
            _previewCam = null;
        }
    }
}
#endif

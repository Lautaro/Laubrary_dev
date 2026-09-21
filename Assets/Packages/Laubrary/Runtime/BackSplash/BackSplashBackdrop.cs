using UnityEngine;

namespace Laubrary.BackSplash
{
    /// <summary>
    /// Renders a <see cref="BackSplash"/> asset as a live Play-mode background: sets the target camera's clear
    /// colour to <see cref="BackSplash.cameraColor"/> and positions/scales a <see cref="SpriteRenderer"/> showing
    /// <see cref="BackSplash.image"/>, sized to fill the camera view at <see cref="BackSplash.imageZoom"/> and
    /// offset by <see cref="BackSplash.imagePos"/> — the same fields the editor-side preview tools (Pyre, Mirage)
    /// already read via <c>BackSplashPainter</c>. No parallel data model: everything comes from the asset.
    ///
    /// This is the runtime counterpart the package didn't have yet (Editor/BackSplash/* only ever rendered into
    /// an IMGUI preview rect). Reassign <see cref="backSplash"/> or edit its fields at any time — <see cref="Apply"/>
    /// re-reads them every frame so a live Inspector tweak (or a debug slider) shows immediately.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackSplashBackdrop : MonoBehaviour
    {
        [Tooltip("The BackSplash asset to render as a full-screen background behind gameplay.")]
        public BackSplash backSplash;

        [Tooltip("Camera whose view this backdrop fills and whose clear colour it sets. Defaults to Camera.main.")]
        public Camera targetCamera;

        [Tooltip("Distance in front of the camera the background sprite sits at. Draw order is controlled by sortingOrder below, not this value, so it mainly matters for perspective cameras.")]
        public float distance = 10f;

        [Tooltip("SpriteRenderer sorting order for the background image. Kept very negative so it draws behind gameplay regardless of scene Z.")]
        public int sortingOrder = -1000;

        SpriteRenderer _renderer;
        Transform _spriteTransform;

        void OnEnable()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            Apply();
        }

        void LateUpdate() => Apply();

        void OnDisable()
        {
            if (_spriteTransform != null) Destroy(_spriteTransform.gameObject);
            _spriteTransform = null;
            _renderer = null;
        }

        /// <summary>Re-reads the BackSplash asset and updates the camera colour + background sprite. Safe to call
        /// every frame (LateUpdate does), and safe to call by hand right after changing a field (e.g. a debug slider).</summary>
        public void Apply()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null || backSplash == null) return;

            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = backSplash.cameraColor;

            EnsureRenderer();

            if (backSplash.image == null)
            {
                _renderer.enabled = false;
                return;
            }
            _renderer.enabled = true;
            _renderer.sprite = backSplash.image;
            _renderer.color = backSplash.imageTint;
            _renderer.sortingOrder = sortingOrder;

            var camTransform = targetCamera.transform;
            _spriteTransform.rotation = camTransform.rotation;

            float viewHeight = targetCamera.orthographic
                ? targetCamera.orthographicSize * 2f
                : 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float viewWidth = viewHeight * Mathf.Max(0.0001f, targetCamera.aspect);

            // imagePos is authored as a raw pixel offset bounded by BackSplash.MaxImageOffset (the same
            // convention BackSplashPainter's editor preview uses) — normalise to [-1,1] and scale by half the
            // camera view so it reads as roughly the same "nudge" here as it does in the editor preview pad.
            Vector2 normalizedOffset = backSplash.imagePos / BackSplash.MaxImageOffset;
            Vector3 offset = camTransform.right * (normalizedOffset.x * viewWidth * 0.5f)
                            + camTransform.up * (normalizedOffset.y * viewHeight * 0.5f);
            _spriteTransform.position = camTransform.position + camTransform.forward * distance + offset;

            // Cover fit, uniform on both axes — see BackSplash.CoverScale's own header for why this must never
            // go back to independent scaleX/scaleY (that was the stretch-to-fill distortion bug, T-0387).
            var spriteSize = _renderer.sprite.bounds.size;
            float scale = BackSplash.CoverScale(viewWidth, viewHeight, spriteSize.x, spriteSize.y, backSplash.imageZoom);
            _spriteTransform.localScale = new Vector3(scale, scale, 1f);
        }

        void EnsureRenderer()
        {
            if (_renderer != null) return;
            var go = new GameObject("~BackSplashImage");
            go.transform.SetParent(transform, false);
            _spriteTransform = go.transform;
            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = sortingOrder;
        }
    }
}

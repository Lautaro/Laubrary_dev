using UnityEngine;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// IZoeInputSource implementation backed by the legacy Input Manager. A fallback for
    /// projects that don't enable the new Input System, or for headless tests where the
    /// Input System isn't initialised. Mirrors the convention used by the project's existing
    /// ShmupPlayer demo (WASD + LMB + cursor).
    ///
    /// For new projects, prefer NewInputSystemSource — gamepad rebinding, action maps, and
    /// per-device control schemes are first-class there and require workarounds here.
    /// </summary>
    public class LegacyInputSource : MonoBehaviour, IZoeInputSource
    {
        [Header("Bindings (legacy Input Manager axes/buttons)")]
        [Tooltip("Horizontal axis name for move X. Defaults to 'Horizontal'.")]
        public string horizontalAxis = "Horizontal";

        [Tooltip("Vertical axis name for move Y. Defaults to 'Vertical'.")]
        public string verticalAxis = "Vertical";

        [Tooltip("Mouse button index for fire. 0 = left button.")]
        public int fireButton = 0;

        [Header("Cursor (optional)")]
        public Camera cursorCamera;
        public float cursorDepth;

        float _cursorDepth;

        void Awake()
        {
            if (cursorCamera == null) cursorCamera = Camera.main;
            _cursorDepth = cursorDepth != 0f ? cursorDepth : transform.position.z;
        }

        public Vector2 ReadMove()
        {
            float x = Input.GetAxisRaw(horizontalAxis);
            float y = Input.GetAxisRaw(verticalAxis);
            var v = new Vector2(x, y);
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        public bool FirePressed() => Input.GetMouseButtonDown(fireButton);
        public bool FireHeld() => Input.GetMouseButton(fireButton);
        public bool FireReleased() => Input.GetMouseButtonUp(fireButton);

        public bool TryReadCursorWorld(out Vector2 worldPosition)
        {
            if (cursorCamera == null)
            {
                worldPosition = default;
                return false;
            }
            Vector3 screen = Input.mousePosition;
            Vector3 world = cursorCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, _cursorDepth));
            worldPosition = new Vector2(world.x, world.y);
            return true;
        }

        public bool TryReadAimDirection(Vector2 selfPosition, out Vector2 aimDirection)
        {
            if (TryReadCursorWorld(out Vector2 cursor))
            {
                Vector2 delta = cursor - selfPosition;
                if (delta.sqrMagnitude > 1e-6f)
                {
                    aimDirection = delta.normalized;
                    return true;
                }
            }
            aimDirection = default;
            return false;
        }
    }
}

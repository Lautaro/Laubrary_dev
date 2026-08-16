using UnityEngine;
using UnityEngine.InputSystem;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// IZoeInputSource implementation backed by the new Input System. Uses InputAction
    /// references the consumer binds in the inspector — bindings live with the player
    /// prefab, not in code, so a game can rebind without recompiling.
    ///
    /// Conventions (matching the project-wide standard from OutBurner's InputBindings):
    ///   - Gamepad is canonical (Steam Deck primary)
    ///   - Keyboard &amp; mouse fallback
    ///   - Stick thresholds: 0.12f for single-axis, 0.0144f² (~0.12 magnitude) for vectors
    ///   - Right stick aims, does NOT fire (separating aim from fire is the user's preference
    ///     — see OutBurner.Inputs.InputBindings.FireHeld's docstring for the reason)
    ///
    /// Enable() / Disable() are called automatically by the Input System when the component
    /// is enabled/disabled, so the actions don't run while the player is parked.
    /// </summary>
    public class NewInputSystemSource : MonoBehaviour, IZoeInputSource
    {
        [Header("Bindings — bind these in the inspector (Input System, Project-wide Actions also work)")]
        [Tooltip("Vector2 action — left stick (gamepad) or WASD (keyboard).")]
        public InputActionReference moveAction;

        [Tooltip("Vector2 action — right stick (gamepad) when pushed, or mouse delta (KB&M).")]
        public InputActionReference aimAction;

        [Tooltip("Button action — right trigger (gamepad) or LMB / Space (KB&M).")]
        public InputActionReference fireAction;

        [Header("Cursor (optional)")]
        [Tooltip("Camera used to convert mouse position to world space. Defaults to Camera.main.")]
        public Camera cursorCamera;

        [Tooltip("Z-plane the cursor's world position is projected onto. The player's own Z by default.")]
        public float cursorDepth;

        // Cache the cursor depth at start (player's Z) so per-frame allocation is zero.
        float _cursorDepth;

        void Awake()
        {
            if (cursorCamera == null) cursorCamera = Camera.main;
            _cursorDepth = cursorDepth != 0f ? cursorDepth : transform.position.z;
        }

        void OnEnable()
        {
            moveAction?.action?.Enable();
            aimAction?.action?.Enable();
            fireAction?.action?.Enable();
        }

        void OnDisable()
        {
            moveAction?.action?.Disable();
            aimAction?.action?.Disable();
            fireAction?.action?.Disable();
        }

        public Vector2 ReadMove()
        {
            if (moveAction == null || moveAction.action == null) return Vector2.zero;
            return moveAction.action.ReadValue<Vector2>();
        }

        public bool FirePressed()
        {
            if (fireAction == null || fireAction.action == null) return false;
            return fireAction.action.WasPressedThisFrame();
        }

        public bool FireHeld()
        {
            if (fireAction == null || fireAction.action == null) return false;
            return fireAction.action.IsPressed();
        }

        public bool FireReleased()
        {
            if (fireAction == null || fireAction.action == null) return false;
            return fireAction.action.WasReleasedThisFrame();
        }

        public bool TryReadCursorWorld(out Vector2 worldPosition)
        {
            if (Mouse.current == null || cursorCamera == null)
            {
                worldPosition = default;
                return false;
            }
            Vector3 screen = Mouse.current.position.ReadValue();
            Vector3 world = cursorCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, _cursorDepth));
            worldPosition = new Vector2(world.x, world.y);
            return true;
        }

        public bool TryReadAimDirection(Vector2 selfPosition, out Vector2 aimDirection)
        {
            // Twin-stick aim (gamepad right stick) — directional, no cursor needed.
            if (aimAction != null && aimAction.action != null)
            {
                Vector2 stick = aimAction.action.ReadValue<Vector2>();
                if (stick.sqrMagnitude > 0.0144f)
                {
                    aimDirection = stick.normalized;
                    return true;
                }
            }

            // Fallback: cursor → self direction.
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

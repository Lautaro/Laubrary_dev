using UnityEngine;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// The missing link between <see cref="IZoeInputSource"/> and the rest of the character stack: moves the
    /// transform from <see cref="IZoeInputSource.ReadMove"/>. Deliberately does nothing else — it doesn't know
    /// about <c>MotionState</c> or animation; a <c>MotionStateSource</c> on the same object infers heading/speed
    /// from the transform delta this driver produces, same as it would for any other mover.
    ///
    /// Sibling of <see cref="AimDriver"/> (aim) and <see cref="ZoeWeaponDriver"/> (fire), not a part of either —
    /// the split is the point: locomotion is perspective-specific (a platformer needs gravity and a jump; a
    /// top-down character doesn't), but aim and fire are not, so those two are reused verbatim by any
    /// perspective while each brings its own motion driver.
    ///
    /// Uses a <see cref="Rigidbody2D"/> (via <see cref="Rigidbody2D.MovePosition"/> in FixedUpdate) when one is
    /// present, else moves the raw transform in Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class TopDownMotionDriver : MonoBehaviour
    {
        [Tooltip("Units per second at full move input magnitude.")]
        public float moveSpeed = 4f;

        IZoeInputSource _input;
        Rigidbody2D _rb;

        IZoeInputSource InputSource => _input != null ? _input : (_input = GetComponent<IZoeInputSource>());

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        void Update()
        {
            if (_rb != null) return;
            var src = InputSource;
            if (src == null) return;
            Move(src, Time.deltaTime);
        }

        void FixedUpdate()
        {
            if (_rb == null) return;
            var src = InputSource;
            if (src == null) return;
            Move(src, Time.fixedDeltaTime);
        }

        void Move(IZoeInputSource src, float dt)
        {
            Vector2 move = src.ReadMove();
            if (move.sqrMagnitude > 1f) move.Normalize();
            if (move.sqrMagnitude <= 0.0001f) return;

            Vector2 delta = move * (moveSpeed * dt);
            if (_rb != null) _rb.MovePosition(_rb.position + delta);
            else transform.position += (Vector3)delta;
        }
    }
}

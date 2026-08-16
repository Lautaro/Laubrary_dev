using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// The missing link between <see cref="IZoeInputSource"/> and the rest of the character stack: moves the
    /// transform from <see cref="IZoeInputSource.ReadMove"/> and writes <see cref="Combatant.aimDirection"/>
    /// from <see cref="IZoeInputSource.TryReadAimDirection"/>. Deliberately does nothing else — it doesn't know
    /// about <c>MotionState</c> or animation; a <c>MotionStateSource</c> on the same object infers heading/speed
    /// from the transform delta this driver produces, same as it would for any other mover.
    ///
    /// Uses a <see cref="Rigidbody2D"/> (via <see cref="Rigidbody2D.MovePosition"/> in FixedUpdate) when one is
    /// present, else moves the raw transform in Update. Aim is resolved every Update regardless, since it should
    /// feel as responsive as the camera/cursor, not tied to the physics step.
    /// </summary>
    [DisallowMultipleComponent]
    public class TopDownMotionDriver : MonoBehaviour
    {
        [Tooltip("Units per second at full move input magnitude.")]
        public float moveSpeed = 4f;

        [Tooltip("When the input source has no aim reading this frame (stick centred, no cursor), fall back to " +
                 "the current move direction instead of holding the last aim.")]
        public bool aimFallsBackToMoveHeading = true;

        IZoeInputSource _input;
        Rigidbody2D _rb;
        Combatant _combatant;
        bool _combatantChecked;

        IZoeInputSource InputSource => _input != null ? _input : (_input = GetComponent<IZoeInputSource>());

        // Lazily resolved, never cached in Awake — a spawner may add Combatant after this component,
        // same reasoning as MotionStateSource.Combatant.
        Combatant CombatantRef
        {
            get
            {
                if (!_combatantChecked) { _combatant = GetComponent<Combatant>(); _combatantChecked = true; }
                return _combatant;
            }
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        void Update()
        {
            var src = InputSource;
            if (src == null) return;

            UpdateAim(src);
            if (_rb == null) Move(src, Time.deltaTime);
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

        void UpdateAim(IZoeInputSource src)
        {
            var combatant = CombatantRef;
            if (combatant == null) return;

            Vector2 selfPos = transform.position;
            if (src.TryReadAimDirection(selfPos, out Vector2 aim))
            {
                combatant.aimDirection = aim;
                return;
            }

            if (aimFallsBackToMoveHeading)
            {
                Vector2 move = src.ReadMove();
                if (move.sqrMagnitude > 0.0001f) combatant.aimDirection = move.normalized;
            }
            // else: hold the last published aimDirection.
        }
    }
}

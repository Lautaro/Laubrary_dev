using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// Side-view locomotion: the body moves only left and right along the line it started on, and FACING is
    /// its own state, independent of movement. Walking against the facing is a backpedal, not a turn; the
    /// facing changes only when <see cref="Turn"/> is called (a turn button, the end of a turn animation).
    ///
    /// The facing is published as <c>Combatant.aimDirection</c>, so everything that already reads aim (a
    /// motion pose on the Aim channel, weapon fire, a backpedal rule) follows it with no extra wiring. That is
    /// also why this driver replaces <see cref="AimDriver"/> rather than sitting beside it: two writers of
    /// the same aim would fight.
    /// </summary>
    [DisallowMultipleComponent]
    public class SideViewMotionDriver : MonoBehaviour
    {
        [Tooltip("Units per second at full move input.")]
        public float moveSpeed = 3f;

        [Tooltip("Which way the body faces when it appears.")]
        public bool startFacingRight = true;

        IZoeInputSource _input;
        Rigidbody2D _rb;
        Combatant _combatant;
        float _lineY;
        bool _facingRight;

        IZoeInputSource InputSource => _input != null ? _input : (_input = GetComponent<IZoeInputSource>());
        // Looked up lazily: the spawner may add the body after this component.
        Rigidbody2D Rb => _rb != null ? _rb : (_rb = GetComponent<Rigidbody2D>());
        Combatant CombatantRef => _combatant != null ? _combatant : (_combatant = GetComponent<Combatant>());

        /// True while the body faces right.
        public bool FacingRight => _facingRight;

        /// Unit vector the body faces: right or left.
        public Vector2 Facing => _facingRight ? Vector2.right : Vector2.left;

        /// Face the other way.
        public void Turn() => _facingRight = !_facingRight;

        // Start, not Awake: a spec sets startFacingRight just after AddComponent, which already ran Awake.
        void Start()
        {
            _facingRight = startFacingRight;
            _lineY = transform.position.y;
        }

        void Update()
        {
            // Every frame, not only on change: nothing else should own aim on a side-view body, but if anything
            // writes it in between, the facing wins again before the next read.
            var combatant = CombatantRef;
            if (combatant != null) combatant.aimDirection = Facing;

            if (Rb == null) Move(Time.deltaTime);
        }

        void FixedUpdate()
        {
            if (Rb != null) Move(Time.fixedDeltaTime);
        }

        void Move(float dt)
        {
            float x = InputSource != null ? Mathf.Clamp(InputSource.ReadMove().x, -1f, 1f) : 0f;
            Vector2 pos = Rb != null ? Rb.position : (Vector2)transform.position;
            Vector2 next = new Vector2(pos.x + x * moveSpeed * dt, _lineY);
            if (next == pos) return;
            if (Rb != null) Rb.MovePosition(next);
            else transform.position = new Vector3(next.x, next.y, transform.position.z);
        }
    }
}

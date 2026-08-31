using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// The missing link between <see cref="IZoeInputSource"/>'s aim reading and
    /// <see cref="Combatant.aimDirection"/>: resolves a world-space aim direction every frame and writes it.
    /// That is the whole job.
    ///
    /// Sibling of <see cref="TopDownMotionDriver"/> (move) and <see cref="ZoeWeaponDriver"/> (fire), not part of
    /// either — twin-stick / mouse-cursor aim reads the same regardless of whether the character below it moves
    /// on a flat plane, a platformer's gravity, or not at all, so this driver is reused verbatim by any
    /// perspective while each brings its own motion driver (or none).
    /// </summary>
    [DisallowMultipleComponent]
    public class AimDriver : MonoBehaviour
    {
        [Tooltip("When the input source has no aim reading this frame (stick centred, no cursor), fall back to " +
                 "the current move direction instead of holding the last aim.")]
        public bool aimFallsBackToMoveHeading = true;

        IZoeInputSource _input;
        Combatant _combatant;
        bool _combatantChecked;

        IZoeInputSource InputSource => _input != null ? _input : (_input = GetComponent<IZoeInputSource>());

        // Lazily resolved, never cached in Awake — a spawner may add Combatant after this component,
        // same reasoning as TopDownMotionDriver.CombatantRef.
        Combatant CombatantRef
        {
            get
            {
                if (!_combatantChecked) { _combatant = GetComponent<Combatant>(); _combatantChecked = true; }
                return _combatant;
            }
        }

        void Update()
        {
            var src = InputSource;
            if (src == null) return;

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

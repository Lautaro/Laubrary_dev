using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Bestiarium
{
    /// Bridges Colosseum's damage events to a CharacterDef's VFX — the "presentation" layer that keeps Colosseum
    /// itself agnostic (Health just fires Damaged/Died; this decides they mean a Pyre flash + Chunks debris). Add it
    /// alongside a Health and point it at a CharacterDef; it plays hit VFX on every damage and death VFX on the
    /// killing blow, at the DamageInfo's world point and direction. Later this also triggers the Zound refs.
    [RequireComponent(typeof(Health))]
    public class CombatPresenter : MonoBehaviour
    {
        public CharacterDef def;

        Health health;

        void Awake() => health = GetComponent<Health>();

        void OnEnable()
        {
            if (health == null) health = GetComponent<Health>();
            if (health != null) { health.Damaged += OnHit; health.Died += OnDeath; }
        }

        void OnDisable()
        {
            if (health != null) { health.Damaged -= OnHit; health.Died -= OnDeath; }
        }

        void OnHit(DamageInfo info)
        {
            if (def != null && def.hit != null && !def.hit.IsEmpty) def.hit.Play(PointOf(info), DirOf(info));
        }

        void OnDeath(DamageInfo info)
        {
            if (def != null && def.death != null && !def.death.IsEmpty) def.death.Play(PointOf(info), DirOf(info));
        }

        // Fall back to the character's own position when the hit didn't record a point.
        Vector2 PointOf(in DamageInfo info) => info.point != Vector2.zero ? info.point : (Vector2)transform.position;

        static float DirOf(in DamageInfo info) =>
            info.direction.sqrMagnitude > 1e-6f ? Mathf.Atan2(info.direction.y, info.direction.x) * Mathf.Rad2Deg : float.NaN;
    }
}

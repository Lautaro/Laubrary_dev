using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Bestiarium
{
    /// Drives a runtime projectile's LOOK + impact from its ProjectileDef: spins the sprite while flying and plays the
    /// impact CombatVfx (Pyre blast + Chunks debris) where it lands. Sits on the projectile GameObject next to the
    /// Colosseum Projectile and listens to its Hit event, so the blast spawns at the exact contact point/direction.
    [RequireComponent(typeof(Projectile))]
    public class ProjectileFx : MonoBehaviour
    {
        public ProjectileDef def;

        Projectile proj;

        void Awake() => proj = GetComponent<Projectile>();

        void OnEnable()
        {
            if (proj == null) proj = GetComponent<Projectile>();
            if (proj != null) proj.Hit += OnHit;
        }

        void OnDisable() { if (proj != null) proj.Hit -= OnHit; }

        void Update()
        {
            if (def != null && def.spin) transform.Rotate(0f, 0f, def.spinSpeed * Time.deltaTime);
        }

        void OnHit(Hurtbox hb, DamageInfo info)
        {
            if (def == null || def.impact == null || def.impact.IsEmpty) return;
            float dir = info.direction.sqrMagnitude > 1e-6f
                ? Mathf.Atan2(info.direction.y, info.direction.x) * Mathf.Rad2Deg : float.NaN;
            def.impact.Play(info.point, dir);
        }
    }
}

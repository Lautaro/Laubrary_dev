using System;
using UnityEngine;

namespace Laubrary.Colosseum
{
    /// Fires <see cref="Projectile"/>s. Give it a projectile prefab and a fire rate; call <see cref="TryFire()"/>
    /// (respecting the cooldown) from input or AI, or set <see cref="autoFire"/> for a turret. It stamps each shot
    /// with the owner's faction so the projectile only hurts hostiles. Spread + projectilesPerShot make shotguns
    /// and fans. The generic emitter behind the demo's player gun and the enemies' return fire.
    public class ProjectileWeapon : MonoBehaviour
    {
        [Tooltip("Projectile prefab to spawn. Damage/speed below override the prefab's own values per shot.")]
        public Projectile projectilePrefab;
        [Tooltip("Whose faction the shots belong to. Auto-found in parents if null.")]
        public Combatant owner;
        [Tooltip("Spawn point + default aim (its right/up). Falls back to this transform.")]
        public Transform muzzle;

        [Min(0.01f)] public float fireRate = 6f;        // shots per second
        public float projectileSpeed = 12f;
        [Min(0f)] public float damage = 10f;
        [Range(0f, 180f)] public float spreadDeg = 0f;
        [Min(1)] public int projectilesPerShot = 1;
        public bool autoFire = false;
        [Tooltip("Default fire direction when TryFire() is called with no direction.")]
        public Vector2 aimDirection = Vector2.up;

        float cooldown;

        /// Fired for each spawned projectile (wire up muzzle flashes, sfx, colour tinting).
        public event Action<Projectile> Fired;

        public bool CanFire => cooldown <= 0f;

        void Reset() { owner = GetComponentInParent<Combatant>(); }
        void Awake() { if (owner == null) owner = GetComponentInParent<Combatant>(); }

        void Update()
        {
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            if (autoFire) TryFire(aimDirection);
        }

        public bool TryFire() => TryFire(aimDirection);

        /// Fire toward a direction if off cooldown. Returns true if a volley went out.
        public bool TryFire(Vector2 dir)
        {
            if (cooldown > 0f || projectilePrefab == null) return false;
            cooldown = 1f / Mathf.Max(0.01f, fireRate);

            Vector3 pos = muzzle != null ? muzzle.position : transform.position;
            if (dir.sqrMagnitude < 1e-6f) dir = aimDirection.sqrMagnitude > 1e-6f ? aimDirection : Vector2.up;
            float baseA = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            Faction fac = owner != null ? owner.faction : null;
            GameObject src = owner != null ? owner.gameObject : gameObject;
            int n = Mathf.Max(1, projectilesPerShot);

            for (int i = 0; i < n; i++)
            {
                float off = n > 1 ? Mathf.Lerp(-spreadDeg, spreadDeg, i / (float)(n - 1))
                                  : (spreadDeg > 0f ? UnityEngine.Random.Range(-spreadDeg, spreadDeg) : 0f);
                float a = (baseA + off) * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));

                var p = Instantiate(projectilePrefab, pos, Quaternion.identity);
                p.damage = damage;
                p.Launch(d, fac, src, projectileSpeed);
                Fired?.Invoke(p);
            }
            return true;
        }
    }
}

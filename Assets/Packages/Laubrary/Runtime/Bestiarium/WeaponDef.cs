using UnityEngine;

namespace Laubrary.Codex
{
    /// A composition recipe for a weapon: its fire stats, the projectile it shoots, and its muzzle VFX. Feeds a
    /// Colosseum ProjectileWeapon at spawn/equip time (fire rate, damage, spread, speed, projectiles-per-shot), and
    /// the muzzle CombatVfx plays at the muzzle each shot. The projectile's own look + impact live on ProjectileDef.
    [CreateAssetMenu(menuName = "Laubrary/Codex/Weapon", fileName = "Weapon")]
    public class WeaponDef : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Weapon";

        [Header("Fire")]
        [Min(0.01f)] public float fireRate = 6f;          // shots per second
        [Min(0f)] public float damage = 10f;
        [Min(0f)] public float projectileSpeed = 12f;
        [Range(0f, 180f)] public float spreadDeg = 0f;
        [Min(1)] public int projectilesPerShot = 1;

        [Header("Ammo")]
        [Tooltip("What this weapon fires (look + impact + trail).")]
        public ProjectileDef projectile;

        [Header("Muzzle")]
        [Tooltip("Flash / smoke played at the muzzle each shot.")]
        public CombatVfx muzzle = new CombatVfx();
        [Tooltip("Muzzle offset from the shooter, along its aim (x = forward, y = up).")]
        public Vector2 muzzleOffset = new Vector2(0.5f, 0f);

        // TODO(zounds): fire Zound ref.
    }
}

using UnityEngine;

namespace Laubrary.Bestiarium
{
    /// A composition recipe for a weapon: its fire stats, the projectile it shoots, and its muzzle effect. Feeds a
    /// Combat2D ProjectileWeapon at spawn/equip time (fire rate, damage, spread, speed, projectiles-per-shot), and
    /// the muzzle effect plays at the muzzle each shot. The projectile's own look + impact live on ProjectileDef.
    [CreateAssetMenu(menuName = "Laubrary/Bestiarium/Weapon", fileName = "Weapon")]
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
        [Tooltip("What this weapon fires (look + impact).")]
        public ProjectileDef projectile;

        [Header("Muzzle")]
        [Tooltip("Flash / smoke played at the muzzle each shot (pluggable effect).")]
        [SerializeReference] public ICombatFx muzzle;
        [Tooltip("Muzzle offset from the shooter, along its aim (x = forward, y = up).")]
        public Vector2 muzzleOffset = new Vector2(0.5f, 0f);

        // TODO(zounds): fire Zound ref.
    }
}

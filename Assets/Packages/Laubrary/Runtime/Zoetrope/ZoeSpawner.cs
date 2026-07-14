using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// The bridge from Zoetrope data recipes to live GameObjects — builds damageable characters and configured
    /// weapons out of the primitives so a scene (the shooting gallery, or a real level) can assemble a fight from Defs.
    public static class ZoeSpawner
    {
        /// Build a damageable character: Combatant + Health + a Hurtbox collider + the Def's pluggable view + a
        /// CombatPresenter wired to the Def's hit/death effects.
        public static GameObject SpawnCharacter(Zoe def, Vector3 pos, Transform parent = null)
        {
            var go = new GameObject(def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : "Character");
            go.transform.position = pos;
            if (parent != null) go.transform.SetParent(parent, true);

            // The pluggable view attaches the visual (sprite / Zoe / Lazor …) and reports its size for the hurtbox.
            Vector2 viewSize = Vector2.one;
            if (def != null && def.view != null) viewSize = def.view.Build(go);

            var health = go.AddComponent<Health>();
            var comb = go.AddComponent<Combatant>();
            if (def != null)
            {
                health.maxHealth = Mathf.Max(1f, def.maxHealth);
                health.invulnerableAfterHit = def.invulnerableAfterHit;
                comb.faction = def.faction;
                comb.label = def.displayName;
            }

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = viewSize;

            go.AddComponent<Hurtbox>();   // owner auto-found on this GO
            go.AddComponent<CombatPresenter>().def = def;

            // Loadout: weapons + abilities the character can activate (trigger = brain for enemies / input for player).
            if (def != null && def.loadout != null && def.loadout.Count > 0)
                go.AddComponent<LoadoutController>().Set(def.loadout);

            // Pluggable AI: a bridge (Zoetrope.Daemon) attaches the brain. The game still supplies the agent body.
            if (def != null && def.brain != null) def.brain.Attach(go);
            return go;
        }

        /// Build an INACTIVE projectile template (a runtime "prefab") from an AmmoDef for a ProjectileWeapon to
        /// clone. Kept inactive so it never flies itself; ProjectileWeapon activates each clone.
        public static Projectile BuildProjectileTemplate(AmmoDef def, LayerMask blockers, Transform holder = null)
        {
            var go = new GameObject((def != null ? def.displayName : "Projectile") + " (template)");
            if (holder != null) go.transform.SetParent(holder, false);
            go.SetActive(false);

            var sr = go.AddComponent<SpriteRenderer>();
            var frames = def != null ? def.Visual?.GetFrames() : null;
            if (frames != null && frames.Length > 0 && frames[0] != null)
            {
                sr.sprite = frames[0];
                go.transform.localScale = Vector3.one * Mathf.Max(0.01f, def.scale);
            }
            sr.sortingOrder = 5;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = sr.sprite != null ? Mathf.Max(0.05f, sr.sprite.bounds.extents.magnitude * 0.5f) : 0.15f;

            go.AddComponent<Rigidbody2D>();   // Projectile.Awake sets it kinematic

            var proj = go.AddComponent<Projectile>();
            if (def != null)
            {
                proj.lifetime = def.lifetime;
                proj.pierce = def.pierce ? 1 : 0;
                proj.faceDirection = def.faceTravel && !def.spin;
                proj.motion = def.motion ?? new PlanarMotion();
            }
            proj.blockers = blockers;

            go.AddComponent<ProjectileFx>().def = def;
            return proj;
        }

        /// Configure (or add) a ProjectileWeapon on `shooter` from a WeaponDef: fire stats + a projectile template +
        /// muzzle VFX on each shot. `owner` supplies the firing faction; `muzzle` is the spawn point.
        public static ProjectileWeapon EquipWeapon(GameObject shooter, WeaponDef def, Combatant owner, Transform muzzle,
                                                   LayerMask projectileBlockers = default)
        {
            var w = shooter.GetComponent<ProjectileWeapon>();
            if (w == null) w = shooter.AddComponent<ProjectileWeapon>();
            w.owner = owner;
            w.muzzle = muzzle;
            w.autoFire = false;
            if (def == null) return w;

            w.fireRate = def.fireRate;
            w.damage = def.damage;
            w.projectileSpeed = def.projectileSpeed;
            w.spreadDeg = def.spreadDeg;
            w.projectilesPerShot = def.projectilesPerShot;
            var ammo = def.ammoTypes != null && def.ammoTypes.Count > 0 ? def.ammoTypes[0] : null;
            w.projectilePrefab = ammo != null
                ? BuildProjectileTemplate(ammo, projectileBlockers, shooter.transform) : null;

            if (def.muzzle != null && !def.muzzle.IsEmpty)
            {
                var fx = def.muzzle;
                Transform m = muzzle != null ? muzzle : shooter.transform;
                w.Fired += _ => fx.Play(m.position);
            }
            return w;
        }
    }
}

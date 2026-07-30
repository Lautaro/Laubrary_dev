using System;
using UnityEngine;

namespace Laubrary.Combat2D
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
        [Tooltip("Fallback fire direction when there's no owner Combatant (or its aimDirection is zero) — " +
                 "an ownerless/standalone weapon's only source of direction. When an owner IS present, " +
                 "ResolvedAimDirection() prefers owner.aimDirection instead, since aim is a Combatant-level " +
                 "concept (see Combatant.aimDirection's own doc) most weapons shouldn't own a second copy of.")]
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
            if (autoFire) TryFire(ResolvedAimDirection());
        }

        public bool TryFire() => TryFire(ResolvedAimDirection());

        /// The direction TryFire()/autoFire use when not given an explicit one. Prefers the owning
        /// Combatant's own aimDirection (set by player input, AI, or a Mirage preview override — see
        /// Combatant.aimDirection) when present and non-zero; falls back to this weapon's own aimDirection
        /// field for an ownerless/standalone weapon.
        Vector2 ResolvedAimDirection()
        {
            if (owner != null && owner.aimDirection.sqrMagnitude > 1e-6f) return owner.aimDirection;
            return aimDirection.sqrMagnitude > 1e-6f ? aimDirection : Vector2.up;
        }

        /// Pulls from the shared per-prefab pool, parents under the shared ~Projectiles container (never the
        /// shooter — a dying shooter or a deactivated weapon slot must not take its already-fired shots with
        /// it), and names the instance for Hierarchy debuggability without coupling its lifetime to `owner`.
        [Tooltip("Radius of the instant-hit probe used by TryHitscanAt. This is the weapon's accuracy in the " +
                 "light-gun sense — how forgiving the crosshair is.")]
        [Min(0f)] public float hitscanRadius = 0.35f;

        /// Raised on every hitscan shot with the point fired at and how many targets it connected with, so a
        /// scene can put a muzzle flash on the gun and an impact at the crosshair. Nothing travels, so this
        /// event is the ONLY place a hitscan shot is visible.
        public event System.Action<Vector3, int> HitscanFired;

        // A stand-in for "the thing that struck", parked at the target point. Combat's filters reason about
        // the striker's POSITION (a depth-band filter asks whether the shot and the target are at the same
        // distance), and a hitscan has no bullet to point at — without this the weapon itself is the striker,
        // which sits with the shooter and answers the question wrong.
        Transform scanProxy;

        /// Fire instantly AT a point: whatever is under it takes the damage and nothing travels. This is the
        /// light-gun / Cabal model — you hit what the crosshair was on when you pulled the trigger, rather
        /// than whatever a travelling bullet happened to brush past on the way there. Returns true if a shot
        /// went out, whether or not it connected.
        public bool TryHitscanAt(Vector3 worldTarget)
        {
            if (cooldown > 0f) return false;
            cooldown = 1f / Mathf.Max(0.01f, fireRate);

            if (scanProxy == null)
            {
                scanProxy = new GameObject($"{name} scan point").transform;
                scanProxy.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            scanProxy.position = worldTarget;

            Faction fac = owner != null ? owner.faction : null;
            GameObject src = owner != null ? owner.gameObject : gameObject;

            int hits = 0;
            var cols = Physics2D.OverlapCircleAll(worldTarget, hitscanRadius);
            for (int i = 0; i < cols.Length; i++)
            {
                var hb = Combat.FindHurtbox(cols[i]);
                if (hb == null) continue;
                if (Combat.TryDamage(hb, fac, src, damage, worldTarget, out _, scanProxy.gameObject)) hits++;
            }

            HitscanFired?.Invoke(worldTarget, hits);
            return true;
        }

        void OnDestroy() { if (scanProxy != null) Destroy(scanProxy.gameObject); }

        /// Optional per-shot motion supplier, overriding whatever the ammo template carries. A FACTORY, not an
        /// instance, because IProjectileMotion holds per-shot state (Init stores origin/direction/target) — one
        /// shared instance would make every projectile in flight fight over the same fields.
        ///
        /// The point of it: a SCENE can decide how shots travel — a depth shooter wanting DepthMotion — without
        /// duplicating the ammo asset per level type, which is what keeps a looted weapon carryable between
        /// scenes. Set at runtime and re-read every shot, so flipping the mode mid-play takes effect at once
        /// rather than only for projectiles pooled afterwards.
        [System.NonSerialized] public System.Func<IProjectileMotion> motionFactory;

        Projectile SpawnProjectile(Vector3 pos)
        {
            var p = ProjectilePool.Get(projectilePrefab);
            if (motionFactory != null) p.motion = motionFactory();
            p.transform.SetPositionAndRotation(pos, Quaternion.identity);
            p.transform.SetParent(ProjectileContainer.Root, true);
            p.gameObject.name = $"{projectilePrefab.name} ({(owner != null ? owner.name : gameObject.name)})";
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);   // allow inactive (runtime-built) templates
            return p;
        }

        /// Fire toward a direction if off cooldown. Returns true if a volley went out.
        public bool TryFire(Vector2 dir)
        {
            if (cooldown > 0f || projectilePrefab == null) return false;
            cooldown = 1f / Mathf.Max(0.01f, fireRate);
            if (dir.sqrMagnitude < 1e-6f) dir = ResolvedAimDirection();
            Vector3 pos = muzzle != null ? muzzle.position : transform.position;
            return FireInternal(pos, dir);
        }

        /// Fire from an explicit spawn position instead of the cached `muzzle` transform — e.g. an
        /// animation's live "Muzzle" MetaLayer point for THIS frame, which moves as the clip plays, unlike
        /// `muzzle` (a Transform positioned once at equip time and never re-synced). Direction still resolves
        /// the normal way (ResolvedAimDirection) — this overrides WHERE the shot originates, not WHICH WAY it
        /// goes; aim is a Combatant-level concept independent of wherever the animation draws the muzzle.
        public bool TryFireFrom(Vector3 originOverride)
        {
            if (cooldown > 0f || projectilePrefab == null) return false;
            cooldown = 1f / Mathf.Max(0.01f, fireRate);
            return FireInternal(originOverride, ResolvedAimDirection());
        }

        bool FireInternal(Vector3 pos, Vector2 dir)
        {
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

                var p = SpawnProjectile(pos);
                p.damage = damage;
                p.Launch(d, fac, src, projectileSpeed);
                Fired?.Invoke(p);
            }
            return true;
        }

        /// Fire toward an explicit world-space target if off cooldown — the entry point for aim techniques that
        /// resolve a real target point (a screen-reticle) rather than just a direction, and for
        /// target-seeking flight (e.g. Projectile's DepthMotion). Returns true if a volley went out. Spread
        /// fans the TARGET itself along screen-space X at its own depth (a resolved-point-appropriate shape),
        /// not the angle-around-a-direction fan <see cref="TryFire(Vector2)"/> uses.
        public bool TryFireAt(Vector3 worldTarget)
        {
            if (cooldown > 0f || projectilePrefab == null) return false;
            cooldown = 1f / Mathf.Max(0.01f, fireRate);

            Vector3 pos = muzzle != null ? muzzle.position : transform.position;
            Faction fac = owner != null ? owner.faction : null;
            GameObject src = owner != null ? owner.gameObject : gameObject;
            int n = Mathf.Max(1, projectilesPerShot);

            float spreadWorld = Mathf.Tan(spreadDeg * Mathf.Deg2Rad) * Vector3.Distance(pos, worldTarget);

            for (int i = 0; i < n; i++)
            {
                float off = n > 1 ? Mathf.Lerp(-spreadWorld, spreadWorld, i / (float)(n - 1))
                                  : (spreadWorld > 0f ? UnityEngine.Random.Range(-spreadWorld, spreadWorld) : 0f);
                Vector3 shotTarget = worldTarget + new Vector3(off, 0f, 0f);

                var p = SpawnProjectile(pos);
                p.damage = damage;
                p.Launch(shotTarget, fac, src, projectileSpeed);
                Fired?.Invoke(p);
            }
            return true;
        }
    }
}

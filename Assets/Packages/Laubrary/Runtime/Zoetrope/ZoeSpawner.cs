using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// The bridge from Zoetrope data recipes to live GameObjects — builds damageable characters and configured
    /// weapons out of the primitives so a scene (the shooting gallery, or a real level) can assemble a fight from Defs.
    public static class ZoeSpawner
    {
        /// Build a damageable character: Combatant + Health + a Hurtbox collider + the Def's pluggable view + a
        /// ReactionFxPlayer wired to the Def's hit/death reactions.
        public static GameObject SpawnCharacter(Zoe def, Vector3 pos, Transform parent = null,
                                                LayerMask projectileBlockers = default)
        {
            var go = new GameObject(def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : "Character");
            go.transform.position = pos;
            if (parent != null) go.transform.SetParent(parent, true);

            // The pluggable view attaches the visual (sprite / Zoe / Lazor …) and reports its size for the hurtbox.
            Vector2 viewSize = Vector2.one;
            if (def != null && def.view != null) viewSize = def.view.Build(go);

            // Cues: only takes effect if the view provided an ICueSink (e.g. ZonedLauminaryView's CueRelay) —
            // a plain SpriteView Zoe has nothing to seed, and that's fine, not an error.
            if (def != null) go.GetComponent<ICueSink>()?.Seed(def.cues);

            // MotionState: character-wide measured motion (velocity/heading/aim/speed), read by every
            // directional pose animator on this character or its composite parts. Cheap, no side effects
            // beyond measuring — always attached so every reader agrees on one answer, same as before any
            // locomotion/motionPose is even authored.
            go.AddComponent<MotionStateSource>();

            // MotionPose (directional) is preferred over the old fixed Locomotion ONLY when actually authored,
            // so an existing Zoe with no MotionPose is completely unaffected. A SpriteView Zoe implements
            // neither IAnimatedView nor IMotionPoseHost, so both silently no-op — nothing to animate, not an
            // error, the same rule cues follow.
            if (def != null && def.motionPose != null && def.motionPose.IsAuthored)
            {
                go.GetComponent<IMotionPoseHost>()?.BindMotionPose(go, def.motionPose);
            }
            else if (def != null && def.locomotion != null && def.locomotion.IsAuthored)
            {
                var animated = go.GetComponent<IAnimatedView>();
                if (animated != null) go.AddComponent<LocomotionAnimator>().Bind(def.locomotion, animated);
            }

            var health = go.AddComponent<Health>();
            var comb = go.AddComponent<Combatant>();
            if (def != null)
            {
                health.maxHealth = Mathf.Max(1f, def.maxHealth);
                health.invulnerableAfterHit = def.invulnerableAfterHit;
                comb.faction = def.faction;
                comb.label = def.displayName;
                // AddComponent<Health> above already ran Awake() synchronously, which lazily set
                // current = maxHealth using the FIELD'S OWN DEFAULT (100) -- def.maxHealth wasn't assigned
                // yet at that point. Revive() re-syncs current to the real max now that it's set, the same
                // real bug class (a field read before the caller's post-AddComponent assignment landed) as
                // WeaponMuzzleCue's Configure()/TargetPracticeController's Start() both exist to avoid.
                // Found live while testing Target Practice: a Zoe with maxHealth=10 was spawning with 100 HP.
                health.Revive();
            }

            // Kinematic body: PushbackEffect (and anything else applying an impulse) needs one to push, and
            // without it that effect no-ops with a warning. Kinematic because movement here is authored by
            // movers writing transforms, not by physics — a dynamic body would fight them and fall.
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            // Without this, a MovePosition-driven kinematic body only actually moves transform.position on
            // physics ticks (fixedDeltaTime, e.g. 50Hz) while everything else samples it every RENDERED frame
            // (which can run much faster) — MotionStateSource's per-frame velocity sample then flickers
            // between the real speed and 0 on the frames physics didn't tick, thrashing walk/idle clips.
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = viewSize;

            go.AddComponent<Hurtbox>();   // owner auto-found on this GO

            // Narrow-phase hit test. The collider above is the broad phase and is deliberately generous; this
            // is what makes a hit land only where the character is actually drawn.
            if (def != null && def.pixelPerfectHits) go.AddComponent<SpriteAlphaHitFilter>();

            // Hurt/death clip + FX, unconditionally added (a VFX-only Zoe with no clip already degrades
            // gracefully — see ReactionFxPlayer's own doc comment). NOT the same thing as Mirage's Target
            // Practice mode (a Mirage-only respawning-dummy convenience, added by MirageSubject instead, that
            // also relies on this same component's finished-events existing).
            go.AddComponent<ReactionFxPlayer>().def = def;

            // AFTER ReactionFxPlayer, so ZoeState finds it and can wait on DeathFinished. This is the single
            // "may I act" gate every mover, weapon and animator consults — without it each one decides for
            // itself and they disagree, which is how corpses end up walking.
            var state = go.AddComponent<ZoeState>();
            if (def != null)
            {
                state.hitStun = def.hit != null ? def.hit.stunSeconds : 0f;
                state.disposal = def.deathDisposal;
                state.deathLinger = def.deathLinger;
            }

            // Loadout: weapons + abilities the character can activate (trigger = brain for enemies / input for player).
            if (def != null && def.loadout != null && def.loadout.Count > 0)
                go.AddComponent<LoadoutController>().Set(def.loadout);

            // Pluggable AI: a bridge (Zoetrope.Daemon) attaches the brain. The game still supplies the agent body.
            if (def != null && def.brain != null) def.brain.Attach(go);

            // Switchable weapon slots — separate from the still-unused Loadout/IActivatable path.
            if (def != null && def.weapons != null && def.weapons.Count > 0)
                EquipWeaponSlots(go, def.weapons, comb, projectileBlockers, def.defaultActiveWeapon);

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
            w.automatic = def.automatic;
            w.projectilePrefab = ammo != null
                ? BuildProjectileTemplate(ammo, projectileBlockers, shooter.transform) : null;

            if (def.muzzle != null && !def.muzzle.IsEmpty)
            {
                var cue = shooter.GetComponent<WeaponMuzzleCue>();
                if (cue == null) cue = shooter.AddComponent<WeaponMuzzleCue>();
                cue.Configure(def, w, muzzle);   // OnEnable already ran before we could set fields directly
            }
            return w;
        }

        /// Build one child slot per weapon (each with its own ProjectileWeapon/muzzle/projectile template via
        /// EquipWeapon), only `activeIndex` enabled. Returns the WeaponSwitcher that toggles between them.
        public static WeaponSwitcher EquipWeaponSlots(GameObject root, System.Collections.Generic.List<WeaponDef> weapons,
                                                       Combatant owner, LayerMask projectileBlockers, int activeIndex = 0)
        {
            var switcher = root.GetComponent<WeaponSwitcher>();
            if (switcher == null) switcher = root.AddComponent<WeaponSwitcher>();

            for (int i = 0; i < weapons.Count; i++)
            {
                var def = weapons[i];
                var slot = new GameObject(def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : $"Weapon_{i}");
                slot.transform.SetParent(root.transform, false);
                // Deactivate BEFORE EquipWeapon so AddComponent<WeaponMuzzleCue> doesn't fire OnEnable yet —
                // it registers/unregisters correctly later, purely as a side effect of SwitchTo's SetActive.
                slot.SetActive(false);

                var muzzleGo = new GameObject("~Muzzle");
                muzzleGo.transform.SetParent(slot.transform, false);
                if (def != null) muzzleGo.transform.localPosition = def.muzzleOffset;

                EquipWeapon(slot, def, owner, muzzleGo.transform, projectileBlockers);
                switcher.slots.Add(slot);
            }

            if (weapons.Count > 0)
                switcher.SwitchTo(Mathf.Clamp(activeIndex, 0, weapons.Count - 1));
            return switcher;
        }
    }
}

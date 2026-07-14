using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Pyre;
using Laubrary.Chunks;
using Laubrary.Zoetrope;
using Laubrary.ZoetropePyre;

namespace Laubrary.Demos.Gallery
{
    /// A shooting gallery for the Codex battle-authoring layer: click to fire the equipped weapon at an idle target
    /// character; the target plays its hit/death VFX and respawns. It assembles sample Zoe/Weapon/Projectile
    /// Defs at runtime from scene-assigned primitives (Pyre blasts, Chunks bursts, factions) + placeholder DemoSprites,
    /// so it demonstrates the whole pipeline end-to-end (Def → ZoeSpawner → Colosseum funnel → CombatVfx).
    public class GalleryDirector : MonoBehaviour
    {
        [Header("Factions")]
        public Faction playerFaction;
        public Faction enemyFaction;

        [Header("Target VFX (Pyre / Chunks)")]
        public BlastSpec hitBlast;
        public BlastSpec deathBlast;
        public ChunkSpec deathDebris;

        [Header("Weapon VFX")]
        public BlastSpec muzzleFlash;
        public BlastSpec impactBlast;
        public ChunkSpec impactSparks;

        [Header("Layout")]
        public Vector3 targetPos = new Vector3(0f, 1.5f, 0f);
        public Vector3 shooterPos = new Vector3(0f, -3.5f, 0f);
        public float respawnDelay = 1.2f;

        Zoe targetDef;
        WeaponDef weaponDef;
        GameObject shooter;
        ProjectileWeapon weapon;
        GameObject targetGo;
        Health targetHealth;
        float respawnClock;

        void Start()
        {
            BuildDefs();

            shooter = new GameObject("Shooter");
            shooter.transform.position = shooterPos;
            var sr = shooter.AddComponent<SpriteRenderer>();
            sr.sprite = DemoSprites.Get(DemoSprites.Shape.Triangle);
            sr.color = new Color(0.6f, 0.85f, 1f);
            shooter.transform.localScale = Vector3.one * 0.7f;
            var comb = shooter.AddComponent<Combatant>();
            comb.faction = playerFaction;
            weapon = ZoeSpawner.EquipWeapon(shooter, weaponDef, comb, shooter.transform);

            SpawnTarget();
        }

        void BuildDefs()
        {
            var proj = ScriptableObject.CreateInstance<AmmoDef>();
            proj.displayName = "Bolt";
            // NOTE: no `visual` asset assigned — AmmoDef's look is now an IChunkAnimation asset reference
            // (a Pyre Blast Chunk Animation or a Reel Chunk Animation), not a raw Sprite this runtime-built
            // demo def could construct in-memory. The bolt still fires and deals damage; it just renders
            // without a sprite. Wiring a real IChunkAnimation asset here is future demo polish, not required
            // for this scene's purpose (proving the Def → ZoeSpawner → Combat2D funnel end-to-end).
            proj.scale = 0.28f;
            proj.lifetime = 3f;
            proj.impact = new PyreChunksFx { blast = impactBlast, chunks = impactSparks };

            weaponDef = ScriptableObject.CreateInstance<WeaponDef>();
            weaponDef.displayName = "Blaster";
            weaponDef.fireRate = 5f;
            weaponDef.damage = 12f;
            weaponDef.projectileSpeed = 16f;
            weaponDef.ammoTypes = new System.Collections.Generic.List<AmmoDef> { proj };
            weaponDef.muzzle = new PyreChunksFx { blast = muzzleFlash };

            targetDef = ScriptableObject.CreateInstance<Zoe>();
            targetDef.displayName = "Dummy";
            targetDef.maxHealth = 60f;
            targetDef.faction = enemyFaction;
            targetDef.view = new SpriteView { sprite = DemoSprites.Get(DemoSprites.Shape.Diamond), scale = 1.3f };
            targetDef.invulnerableAfterHit = 0.03f;
            targetDef.hit = new PyreChunksFx { blast = hitBlast };
            targetDef.death = new PyreChunksFx { blast = deathBlast, chunks = deathDebris };
        }

        void SpawnTarget()
        {
            targetGo = ZoeSpawner.SpawnCharacter(targetDef, targetPos);
            var sr = targetGo.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = new Color(0.95f, 0.5f, 0.5f);   // tint the white placeholder
            targetHealth = targetGo.GetComponent<Health>();
            respawnClock = 0f;
        }

        void Update()
        {
            // Fire the equipped weapon toward the mouse.
            if (Input.GetMouseButton(0) && weapon != null && Camera.main != null)
            {
                Vector3 m = Camera.main.ScreenToWorldPoint(Input.mousePosition); m.z = 0f;
                weapon.TryFire((Vector2)(m - shooter.transform.position));
            }

            // Respawn the target a moment after it dies (letting the death VFX play).
            if (targetHealth != null && targetHealth.IsDead)
            {
                respawnClock += Time.deltaTime;
                if (respawnClock >= respawnDelay)
                {
                    if (targetGo != null) Destroy(targetGo);
                    SpawnTarget();
                }
            }
        }

        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 600, 20), "Codex shooting gallery — hold LEFT MOUSE to fire at the target.");
            if (targetHealth != null && !targetHealth.IsDead)
                GUI.Label(new Rect(10, 30, 600, 20), $"Target HP: {targetHealth.Current:0}/{targetHealth.Max:0}");
            else
                GUI.Label(new Rect(10, 30, 600, 20), "Target down — respawning…");
        }
    }
}

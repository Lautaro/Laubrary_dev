using UnityEngine;
using Laubrary.Colosseum;
using Laubrary.Pyre;
using Laubrary.Chunks;
using Laubrary.Codex;

namespace Laubrary.Demos.Gallery
{
    /// A shooting gallery for the Codex battle-authoring layer: click to fire the equipped weapon at an idle target
    /// character; the target plays its hit/death VFX and respawns. It assembles sample Character/Weapon/Projectile
    /// Defs at runtime from scene-assigned primitives (Pyre blasts, Chunks bursts, factions) + placeholder DemoSprites,
    /// so it demonstrates the whole pipeline end-to-end (Def → CodexArsenal → Colosseum funnel → CombatVfx).
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

        CharacterDef targetDef;
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
            weapon = CodexArsenal.EquipWeapon(shooter, weaponDef, comb, shooter.transform);

            SpawnTarget();
        }

        void BuildDefs()
        {
            var proj = ScriptableObject.CreateInstance<ProjectileDef>();
            proj.displayName = "Bolt";
            proj.sprite = DemoSprites.Get(DemoSprites.Shape.Circle);
            proj.scale = 0.28f;
            proj.lifetime = 3f;
            proj.impact.blast = impactBlast;
            proj.impact.chunks = impactSparks;

            weaponDef = ScriptableObject.CreateInstance<WeaponDef>();
            weaponDef.displayName = "Blaster";
            weaponDef.fireRate = 5f;
            weaponDef.damage = 12f;
            weaponDef.projectileSpeed = 16f;
            weaponDef.projectile = proj;
            weaponDef.muzzle.blast = muzzleFlash;

            targetDef = ScriptableObject.CreateInstance<CharacterDef>();
            targetDef.displayName = "Dummy";
            targetDef.maxHealth = 60f;
            targetDef.faction = enemyFaction;
            targetDef.idleSprite = DemoSprites.Get(DemoSprites.Shape.Diamond);
            targetDef.spriteScale = 1.3f;
            targetDef.invulnerableAfterHit = 0.03f;
            targetDef.hit.blast = hitBlast;
            targetDef.death.blast = deathBlast;
            targetDef.death.chunks = deathDebris;
        }

        void SpawnTarget()
        {
            targetGo = CodexArsenal.SpawnCharacter(targetDef, targetPos);
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

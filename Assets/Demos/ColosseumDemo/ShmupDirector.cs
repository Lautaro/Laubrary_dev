using System.Collections.Generic;
using UnityEngine;
using Laubrary.Colosseum;
using Laubrary.Pyre;
using Laubrary.Chunks;
using Laubrary.Choreographer;

namespace Laubrary.Demos.ColosseumShmup
{
    /// Builds and runs the Colosseum shmup demo: a player ship you fly and fire, and a wave of choreographed enemy
    /// ships that shoot back — all wired on the Colosseum backbone (Factions decide who can hurt whom, Health +
    /// events drive death, Projectiles carry the damage). Enemy deaths spawn Pyre explosions + Chunks debris. This
    /// is the demo meant to supersede the old ChoreographerShmup: same Choreographer + Pyre + Chunks, now with real
    /// health/damage/factions instead of a click counter.
    ///
    /// Fields marked [Baked] are set by ColosseumDemoBake at authoring time (created assets); everything is built
    /// at play time in Start so the scene stays a tiny, robust "director + camera".
    public class ShmupDirector : MonoBehaviour
    {
        [Header("Baked references")]
        public Faction playerFaction;
        public Faction enemyFaction;
        public Projectile playerBulletPrefab;
        public Projectile enemyBulletPrefab;
        public List<BlastSpec> deathBlasts = new();
        public ChunkSpec debris;
        public Choreography choreo;
        public List<Color> tints = new();

        [Header("Tunables (also editable in the in-game panel)")]
        public int enemyCount = 8;
        public float playerFireRate = 8f;
        public float bulletSize = 0.22f;             // 0.05 .. 0.5 world units
        public float explosionIntensity = 1f;        // scales the death blast
        public float enemyFireInterval = 1.7f;
        public Color playerBulletColor = new(0.4f, 0.9f, 1f);
        public Color enemyBulletColor = new(1f, 0.4f, 0.55f);

        static readonly Color[] Palette =
        {
            new(0.4f,0.9f,1f), new(1f,0.85f,0.3f), new(0.5f,1f,0.5f),
            new(1f,0.4f,0.55f), new(0.8f,0.6f,1f), Color.white
        };
        static readonly Color[] Backgrounds =
        {
            new(0.05f,0.06f,0.10f), new(0.02f,0.02f,0.03f), new(0.10f,0.06f,0.12f), new(0.06f,0.10f,0.10f)
        };
        int playerColIdx, enemyColIdx = 3, bgIdx;

        Camera cam;
        ShmupPlayer player;
        Health playerHealth;
        ProjectileWeapon playerWeapon;
        ChoreographyPlayer choreoPlayer;
        readonly List<ShmupEnemy> enemies = new();

        Vector3 playerStart;
        int score, lives = 3;
        const int StartLives = 3;
        float respawnInvuln;
        bool showPanel = true;

        void Start()
        {
            cam = Camera.main;
            if (cam != null) { cam.orthographic = true; cam.backgroundColor = Backgrounds[bgIdx]; }
            float halfH = cam != null ? cam.orthographicSize : 5f;
            float halfW = halfH * (cam != null ? cam.aspect : 1.7778f);

            BuildPlayer(halfW, halfH);
            BuildEnemies(halfW, halfH);
        }

        // ── player ───────────────────────────────────────────────────────────────
        void BuildPlayer(float halfW, float halfH)
        {
            var go = new GameObject("Player");
            playerStart = new Vector3(0f, -halfH + 1.4f, 0f);
            go.transform.position = playerStart;
            go.transform.localScale = Vector3.one * 0.8f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = DemoSprites.Get(DemoSprites.Shape.Triangle);
            sr.color = new Color(0.5f, 1f, 0.7f);
            sr.sortingOrder = 200;

            AddKinematicBody(go);
            var cc = go.AddComponent<CircleCollider2D>();
            cc.isTrigger = true; cc.radius = 0.34f;

            var comb = go.AddComponent<Combatant>();
            comb.faction = playerFaction; comb.label = "Player";
            playerHealth = go.AddComponent<Health>();
            playerHealth.maxHealth = 100f;
            go.AddComponent<Hurtbox>().owner = comb;

            playerWeapon = go.AddComponent<ProjectileWeapon>();
            playerWeapon.owner = comb;
            playerWeapon.projectilePrefab = playerBulletPrefab;
            playerWeapon.fireRate = playerFireRate;
            playerWeapon.projectileSpeed = 15f;
            playerWeapon.damage = 12f;
            playerWeapon.aimDirection = Vector2.up;
            playerWeapon.Fired += p => Dress(p, playerBulletColor);

            player = go.AddComponent<ShmupPlayer>();
            player.weapon = playerWeapon;
            player.minBounds = new Vector2(-halfW + 0.6f, -halfH + 0.6f);
            player.maxBounds = new Vector2(halfW - 0.6f, -1f);

            playerHealth.Died += _ => OnPlayerDied();
        }

        // ── enemies ────────────────────────────────────────────────────────────────
        void BuildEnemies(float halfW, float halfH)
        {
            choreoPlayer = gameObject.AddComponent<ChoreographyPlayer>();
            choreoPlayer.choreography = choreo;
            choreoPlayer.anchor = transform;
            choreoPlayer.worldSize = new Vector2(halfW * 1.7f, halfH * 1.1f);
            choreoPlayer.speed = 1f;
            choreoPlayer.applyFacing = false;
            choreoPlayer.playOnEnable = true;
            choreoPlayer.targets = new List<Transform>();

            int n = Mathf.Max(1, enemyCount);
            for (int i = 0; i < n; i++)
            {
                var e = MakeEnemy(i, n);
                choreoPlayer.targets.Add(e.transform);
            }
            choreoPlayer.Play();
        }

        ShmupEnemy MakeEnemy(int i, int n)
        {
            var go = new GameObject("Enemy " + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.7f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = DemoSprites.Get(DemoSprites.Shape.Diamond);
            sr.sortingOrder = 200;

            AddKinematicBody(go);
            var cc = go.AddComponent<CircleCollider2D>();
            cc.isTrigger = true; cc.radius = 0.42f;

            var comb = go.AddComponent<Combatant>();
            comb.faction = enemyFaction; comb.label = "Enemy " + i;
            var h = go.AddComponent<Health>();
            h.maxHealth = 30f;
            go.AddComponent<Hurtbox>().owner = comb;

            var w = go.AddComponent<ProjectileWeapon>();
            w.owner = comb;
            w.projectilePrefab = enemyBulletPrefab;
            w.fireRate = 3f;
            w.projectileSpeed = 7.5f;
            w.damage = 10f;
            w.Fired += p => Dress(p, enemyBulletColor);

            var enemy = go.AddComponent<ShmupEnemy>();
            enemy.tint = PickTint(i, n);
            enemy.deathBlasts = deathBlasts;
            enemy.debris = debris;
            enemy.weapon = w;
            enemy.playerTarget = player != null ? player.transform : null;
            enemy.fireInterval = enemyFireInterval;
            enemy.choreoPlayer = choreoPlayer;
            enemy.choreoIndex = i;
            enemy.blastScale = explosionIntensity;
            enemy.Killed += OnEnemyKilled;

            enemies.Add(enemy);
            return enemy;
        }

        Color PickTint(int i, int n)
        {
            if (tints != null && tints.Count > 0) return tints[i % tints.Count];
            return Color.HSVToRGB(n > 1 ? i / (float)n : 0.5f, 0.7f, 1f);
        }

        // ── per-frame: push live tunables + handle respawn invuln ───────────────────
        void Update()
        {
            if (playerWeapon != null) playerWeapon.fireRate = playerFireRate;
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i] != null) { enemies[i].blastScale = explosionIntensity; enemies[i].fireInterval = enemyFireInterval; }

            if (respawnInvuln > 0f)
            {
                respawnInvuln -= Time.deltaTime;
                if (respawnInvuln <= 0f && playerHealth != null) playerHealth.invulnerable = false;
            }

            if (Input.GetKeyDown(KeyCode.Tab)) showPanel = !showPanel;
        }

        void Dress(Projectile p, Color c)
        {
            if (p == null) return;
            p.transform.localScale = Vector3.one * Mathf.Clamp(bulletSize, 0.05f, 0.6f);
            var sr = p.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = c;
        }

        void OnEnemyKilled(ShmupEnemy e) => score += 10;

        void OnPlayerDied()
        {
            lives--;
            if (lives < 0) { lives = StartLives; score = 0; }   // soft restart
            if (player != null) player.transform.position = playerStart;
            if (playerHealth != null) { playerHealth.Revive(); playerHealth.invulnerable = true; }
            respawnInvuln = 1.6f;
        }

        static void AddKinematicBody(GameObject go)
        {
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }

        // ── HUD + tuning panel (IMGUI, matching the other Laubrary demos) ───────────
        void OnGUI()
        {
            // HUD
            GUI.Label(new Rect(12, 8, 400, 22), $"Score {score}      Lives {Mathf.Max(0, lives)}");
            var barBg = new Rect(12, 32, 220, 16);
            GUI.Box(barBg, GUIContent.none);
            float frac = playerHealth != null ? playerHealth.Normalized : 0f;
            GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), new Color(0.4f, 1f, 0.5f), frac);
            GUI.DrawTexture(new Rect(barBg.x + 2, barBg.y + 2, (barBg.width - 4) * frac, barBg.height - 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(barBg.x + 4, barBg.y - 1, 220, 18), $"HP {(playerHealth != null ? Mathf.CeilToInt(playerHealth.Current) : 0)}");

            GUI.Label(new Rect(12, 54, 640, 20),
                "Move: arrows / WASD   ·   Fire: auto (also Space / mouse)   ·   Tab: hide panel");

            if (showPanel)
                GUILayout.Window(9271, new Rect(Screen.width - 268, 12, 256, 10), DrawPanel, "Colosseum shmup");
        }

        void DrawPanel(int id)
        {
            GUILayout.Label($"Player fire rate: {playerFireRate:0.0}/s");
            playerFireRate = GUILayout.HorizontalSlider(playerFireRate, 1f, 20f);

            GUILayout.Label($"Bullet size: {bulletSize:0.00}");
            bulletSize = GUILayout.HorizontalSlider(bulletSize, 0.05f, 0.5f);

            GUILayout.Label($"Explosion intensity: {explosionIntensity:0.00}");
            explosionIntensity = GUILayout.HorizontalSlider(explosionIntensity, 0.4f, 2.5f);

            GUILayout.Label($"Enemy fire interval: {enemyFireInterval:0.0}s");
            enemyFireInterval = GUILayout.HorizontalSlider(enemyFireInterval, 0.4f, 4f);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bullet colour", GUILayout.Width(90));
            GUI.color = playerBulletColor;
            if (GUILayout.Button("  ")) { playerColIdx = (playerColIdx + 1) % Palette.Length; playerBulletColor = Palette[playerColIdx]; }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Enemy colour", GUILayout.Width(90));
            GUI.color = enemyBulletColor;
            if (GUILayout.Button("  ")) { enemyColIdx = (enemyColIdx + 1) % Palette.Length; enemyBulletColor = Palette[enemyColIdx]; }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Background")) { bgIdx = (bgIdx + 1) % Backgrounds.Length; if (cam != null) cam.backgroundColor = Backgrounds[bgIdx]; }

            GUI.DragWindow();
        }
    }
}

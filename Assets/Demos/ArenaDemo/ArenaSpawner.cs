using System.Collections.Generic;
using UnityEngine;
using Laubrary.Pyre;
using Laubrary.Chunks;
using Laubrary.Choreographer;

/// Arena demo director: builds a small R-Type-style arena at play time and routes mouse clicks to enemies.
///
/// It adds a ChoreographyPlayer to itself, spawns N placeholder enemies, and registers each enemy transform as
/// a dancer so the Choreographer sweeps them across the screen. Each enemy owns its own Pyre blasts and Chunks
/// debris and handles being clicked (see ArenaEnemy). This component just spawns them and forwards clicks.
///
/// All fields are assigned by ArenaDemoBake at build time (the created assets + tints).
public class ArenaSpawner : MonoBehaviour
{
    public Choreography choreo;
    public int enemyCount = 8;
    public float enemyScale = 0.7f;
    public int clicksPerEnemy = 3;
    public List<Pyre> hitBlasts;
    public List<Pyre> destroyBlasts;
    public ChunkSpec debris;
    public List<Color> tints = new();
    public Camera cam;

    ChoreographyPlayer player;

    void Start()
    {
        if (cam == null) cam = Camera.main;

        // ── the Choreographer: one player driving every enemy transform as a dancer ──
        player = gameObject.AddComponent<ChoreographyPlayer>();
        player.choreography = choreo;
        player.anchor = transform;
        player.worldSize = new Vector2(16f, 8f);   // normalised path → world span
        player.speed = 1f;
        player.applyFacing = true;
        player.playOnEnable = true;
        player.targets = new List<Transform>();

        int n = Mathf.Max(1, enemyCount);
        for (int i = 0; i < n; i++)
        {
            var enemy = MakeEnemy(i, n);
            player.targets.Add(enemy.transform);
        }

        // playOnEnable already started it, but the player was added after OnEnable — kick it explicitly.
        player.Play();
    }

    GameObject MakeEnemy(int i, int n)
    {
        var go = new GameObject("Enemy " + i);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * enemyScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 200;

        // kinematic body + trigger circle so OverlapPoint hit-testing works without physics forces.
        // ZOE SWAP: a real Zoe would hit-test against its authored meta-layer, not this circle.
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        var cc = go.AddComponent<CircleCollider2D>();
        cc.isTrigger = true;
        cc.radius = 0.5f;                 // sprite is 1 unit, so 0.5 local matches at any scale

        var enemy = go.AddComponent<ArenaEnemy>();
        enemy.clicksToDestroy = Mathf.Max(1, clicksPerEnemy);
        enemy.tint = PickTint(i, n);
        enemy.hitBlasts = hitBlasts;
        enemy.destroyBlasts = destroyBlasts;
        enemy.debris = debris;
        enemy.choreoPlayer = player;
        enemy.choreoIndex = i;
        return go;
    }

    Color PickTint(int i, int n)
    {
        if (tints != null && tints.Count > 0) return tints[i % tints.Count];
        return Color.HSVToRGB(n > 1 ? i / (float)n : 0.5f, 0.7f, 1f);   // spread of bright hues
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        var c = cam != null ? cam : Camera.main;
        if (c == null) return;

        Vector3 w = c.ScreenToWorldPoint(Input.mousePosition);
        Vector2 world = new Vector2(w.x, w.y);
        var hit = Physics2D.OverlapPoint(world);
        if (hit != null)
            hit.GetComponentInParent<ArenaEnemy>()?.Hit(world);
    }

    void OnGUI()
    {
        GUI.Label(new Rect(12, 10, 620, 24),
            "Arena demo — click enemies to hit them; a few clicks destroys one (Pyre blast + Chunks debris). They respawn as the choreography loops.");
    }
}

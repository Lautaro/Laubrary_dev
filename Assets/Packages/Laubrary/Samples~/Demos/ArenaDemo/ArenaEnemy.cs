using System.Collections.Generic;
using UnityEngine;
using Laubrary.Pyre;
using Laubrary.Chunks;
using Laubrary.Choreographer;

/// Arena demo: one clickable enemy that ties together the three Laubrary tools.
/// - Choreographer moves this transform (the ChoreographyPlayer drives targets[i] every frame).
/// - Pyre plays a small explosion on a non-lethal hit and a bigger one on the kill.
/// - Chunks throws a tinted debris burst on the kill.
///
/// This enemy is a PLACEHOLDER stand-in for a future Zoetrope "Zoe". A real Zoe would supply
/// authored sprite-sheet animation and a meta-layer for hit detection; here we use one procedural
/// sprite and a circle collider. The "// ZOE SWAP" comments below mark exactly where that art and
/// those animation clips would replace the placeholder behaviour.
public class ArenaEnemy : MonoBehaviour
{
    // ── assigned by the spawner ──────────────────────────────────────────────
    public int clicksToDestroy = 3;
    public Color tint = Color.white;
    public List<Pyre> hitBlasts;
    public List<Pyre> destroyBlasts;
    public ChunkSpec debris;

    // respawn wiring: the choreo that moves us, and which dancer index we are
    public ChoreographyPlayer choreoPlayer;
    public int choreoIndex;

    // ── runtime state ────────────────────────────────────────────────────────
    int remaining;
    bool dead;

    SpriteRenderer sr;
    Collider2D col;
    Vector3 baseScale = Vector3.one;

    // hit reaction (short scale-punch + white flash)
    const float HitReactTime = 0.12f;
    float hitReact;                 // counts down from HitReactTime

    // destroy animation (scale-down + fade)
    const float DestroyTime = 0.25f;
    float destroyAnim;              // counts up 0..DestroyTime while dying

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
    }

    // Look/tint/count are applied in Start, not Awake: the spawner sets tint / clicksToDestroy / scale AFTER
    // AddComponent (which already ran Awake), so reading them in Awake would use the defaults (white).
    void Start()
    {
        baseScale = transform.localScale;

        // ZOE SWAP: a real Zoe would assign its animated sprite sheet + idle clip here, and provide
        // a meta-layer (per-pixel / per-part hit shapes) instead of the single procedural sprite + circle.
        if (sr != null)
        {
            sr.sprite = DemoSprites.Get(DemoSprites.Shape.Diamond);
            sr.color = tint;
        }

        remaining = Mathf.Max(1, clicksToDestroy);
    }

    /// A click landed on this enemy. worldPoint is where the click hit (used to place the small blast).
    public void Hit(Vector2 worldPoint)
    {
        if (dead) return;

        if (remaining > 1)
        {
            // ── non-lethal hit: small random Pyre pop at the click point + a quick reaction ──
            remaining--;
            SpawnBlast(PickRandom(hitBlasts), worldPoint);
            hitReact = HitReactTime;   // Update animates the scale-punch + flash
            // ZOE SWAP: a real Zoe would trigger a "hurt" animation state here.
        }
        else
        {
            // ── killing click: bigger Pyre blast at centre, tinted Chunks debris, then die ──
            Vector3 center = transform.position;
            SpawnBlast(PickRandom(destroyBlasts), center);

            // Chunks: tint the debris to this enemy's colour. The palette overload still needs the spec.
            if (debris != null)
                Chunks.Burst(center, debris, TintPalette());

            dead = true;
            destroyAnim = 0f;
            hitReact = 0f;
            // ZOE SWAP: a real Zoe would play its authored "destroy" animation clip instead of the
            // procedural scale-down + fade below.
        }
    }

    void Update()
    {
        // ── hit reaction: brief scale-punch + white flash, easing back to normal ──
        if (hitReact > 0f && !dead)
        {
            hitReact = Mathf.Max(0f, hitReact - Time.deltaTime);
            float t = hitReact / HitReactTime;                 // 1 at hit → 0 when settled
            transform.localScale = baseScale * (1f + 0.35f * t);
            if (sr != null) sr.color = Color.Lerp(tint, Color.white, t);
            if (hitReact == 0f)
            {
                transform.localScale = baseScale;
                if (sr != null) sr.color = tint;
            }
        }

        // ── destroy animation: shrink to nothing + fade out, then hide ──
        if (dead && destroyAnim < DestroyTime)
        {
            destroyAnim += Time.deltaTime;
            float t = Mathf.Clamp01(destroyAnim / DestroyTime); // 0 → 1
            transform.localScale = baseScale * (1f - t);
            if (sr != null)
            {
                var c = tint; c.a = 1f - t; sr.color = c;
            }
            if (t >= 1f)
            {
                if (col != null) col.enabled = false;
                if (sr != null) sr.enabled = false;
            }
        }

        // ── respawn as the choreography loops this dancer back to the start ──
        if (dead && choreoPlayer != null && choreoPlayer.ProgressOf(choreoIndex) < 0.05f)
            Respawn();
    }

    void Respawn()
    {
        dead = false;
        destroyAnim = 0f;
        hitReact = 0f;
        remaining = Mathf.Max(1, clicksToDestroy);
        transform.localScale = baseScale;
        if (sr != null) { sr.enabled = true; sr.color = tint; }
        if (col != null) col.enabled = true;
        // ZOE SWAP: a real Zoe would reset to its idle animation state here.
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// Spawn a one-shot Pyre explosion at a world position (self-renders on Awake, self-destroys on finish).
    void SpawnBlast(Pyre s, Vector3 pos)
    {
        if (s == null) return;
        var go = new GameObject("Blast");
        go.transform.position = pos;
        var bsr = go.AddComponent<SpriteRenderer>();
        bsr.sortingOrder = 500;                 // over the enemies (~200)
        var bp = go.AddComponent<PyreBlastPlayer>();
        bp.spec = s;
        bp.loop = false;
        bp.destroyOnFinish = true;
        bp.playOnAwake = true;                  // Awake renders the frames + Play()s
    }

    /// A tiny palette of this enemy's tint for the Chunks burst.
    IList<Color32> TintPalette()
    {
        Color32 c = tint;
        return new Color32[] { c, c, c };
    }

    static Pyre PickRandom(List<Pyre> list)
    {
        if (list == null || list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];   // UnityEngine.Random: runtime variety, not determinism
    }
}

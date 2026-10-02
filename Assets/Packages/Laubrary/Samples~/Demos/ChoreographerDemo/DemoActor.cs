using UnityEngine;
using Laubrary.Choreographer;

public enum DemoFaction { Player, PlayerShot, Enemy, EnemyShot, Boss }

/// Demo-only: gives a thing a faction and makes it explode when it touches a faction it's vulnerable to. If it is
/// driven by a ChoreographyPlayer (barrage projectiles, enemy waves), it doesn't get destroyed — it hides, then
/// re-appears when its dancer loops back to the start of the choreography, so the wave keeps coming.
[RequireComponent(typeof(Collider2D))]
public class DemoActor : MonoBehaviour
{
    public DemoFaction faction = DemoFaction.Enemy;
    public DemoFaction[] explodesOn = { DemoFaction.Player, DemoFaction.PlayerShot };

    [Header("Appearance (placeholder sprite assigned at play time)")]
    public DemoSprites.Shape shape = DemoSprites.Shape.Circle;
    public Color color = Color.white;
    public float explosionSize = 1f;

    [Header("Choreo-driven units (optional)")]
    public ChoreographyPlayer choreoPlayer;
    public int choreoIndex;

    bool dead;
    SpriteRenderer sr;
    Collider2D col;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        if (sr != null) { sr.sprite = DemoSprites.Get(shape); sr.color = color; }   // runtime placeholder art
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (dead) return;
        var o = other.GetComponent<DemoActor>();
        if (o == null) return;
        for (int i = 0; i < explodesOn.Length; i++)
            if (o.faction == explodesOn[i]) { Explode(); return; }
    }

    void Explode()
    {
        DemoExplosion.Spawn(transform.position, color, explosionSize);
        if (choreoPlayer != null)
        {
            dead = true;                                   // hide until this dancer loops back
            if (sr) sr.enabled = false;
            if (col) col.enabled = false;
        }
        else Destroy(gameObject);
    }

    void Update()
    {
        if (dead && choreoPlayer != null && choreoPlayer.ProgressOf(choreoIndex) < 0.05f)
        {
            dead = false;
            if (sr) sr.enabled = true;
            if (col) col.enabled = true;
        }
    }
}

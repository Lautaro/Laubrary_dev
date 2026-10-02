using UnityEngine;

/// Demo-only: the player fires bullets to the side. Fire rate (interval), its randomness, bullet speed and size
/// are all tunable at runtime. Bullets are PlayerShots that explode enemies, the boss and boss missiles.
public class DemoPlayerWeapon : MonoBehaviour
{
    [Tooltip("Base seconds between shots.")]
    public float fireInterval = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("How much the interval varies each shot (0 = metronome, 1 = very erratic).")]
    public float intervalRandomness = 0.4f;
    public float bulletSpeed = 12f;
    public float bulletScale = 0.3f;

    public Vector2 fireDir = new(-1f, 0f);   // toward the boss side
    public float spreadDegrees = 25f;
    public Color bulletColor = new(0.4f, 0.9f, 1f, 1f);

    float next;

    void Start() => next = NextInterval();

    void Update()
    {
        next -= Time.deltaTime;
        if (next > 0f) return;
        next = NextInterval();
        Fire();
    }

    float NextInterval() => Mathf.Max(0.03f, fireInterval * Random.Range(1f - intervalRandomness, 1f + intervalRandomness));

    void Fire()
    {
        var go = new GameObject("PlayerShot");
        go.transform.position = transform.position;
        go.transform.localScale = Vector3.one * bulletScale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DemoSprites.Get(DemoSprites.Shape.Circle);
        sr.color = bulletColor;
        sr.sortingOrder = 300;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        var cc = go.AddComponent<CircleCollider2D>();
        cc.isTrigger = true;
        cc.radius = 0.5f;

        var act = go.AddComponent<DemoActor>();
        act.faction = DemoFaction.PlayerShot;
        act.explodesOn = new[] { DemoFaction.Enemy, DemoFaction.Boss, DemoFaction.EnemyShot };
        act.shape = DemoSprites.Shape.Circle;
        act.color = bulletColor;
        act.explosionSize = bulletScale;

        float ang = Random.Range(-spreadDegrees, spreadDegrees);
        Vector2 dir = (Vector2)(Quaternion.Euler(0, 0, ang) * fireDir.normalized);
        var mv = go.AddComponent<DemoMover>();
        mv.velocity = dir * bulletSpeed;
        mv.life = 3f;
    }
}

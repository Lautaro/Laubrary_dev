using UnityEngine;

/// Demo-only explosion: a circle sprite that expands and fades over a short life, then destroys itself. Spawned
/// via DemoExplosion.Spawn() wherever something explodes. No prefab / no shipped assets.
public class DemoExplosion : MonoBehaviour
{
    // Global intensity multipliers driven by the demo UI (size = how big, duration = how long).
    public static float GlobalSizeMul = 1f;
    public static float GlobalDurationMul = 1f;

    public float life = 0.35f;
    public float startScale = 0.4f;
    public float endScale = 1.6f;

    SpriteRenderer sr;
    float t;
    Color baseColor;

    public static void Spawn(Vector3 position, Color color, float size = 1f)
    {
        var go = new GameObject("Explosion");
        go.transform.position = position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DemoSprites.Get(DemoSprites.Shape.Circle);
        sr.color = color;
        sr.sortingOrder = 400;
        var e = go.AddComponent<DemoExplosion>();
        e.sr = sr;
        e.baseColor = color;
        e.life = 0.35f * Mathf.Max(0.05f, GlobalDurationMul);
        float sz = size * Mathf.Max(0.05f, GlobalSizeMul);
        e.startScale *= sz;
        e.endScale *= sz;
        go.transform.localScale = Vector3.one * e.startScale;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, k);
        var c = baseColor; c.a = baseColor.a * (1f - k);
        if (sr) sr.color = c;
        if (k >= 1f) Destroy(gameObject);
    }
}

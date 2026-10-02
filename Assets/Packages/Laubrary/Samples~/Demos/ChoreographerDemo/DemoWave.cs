using System.Collections.Generic;
using UnityEngine;
using Laubrary.Choreographer;

/// Demo-only: owns a ChoreographyPlayer and spawns/maintains N choreo-driven units of one appearance, so the
/// count can be changed at runtime (a slider). Used for the boss barrage and each enemy wave.
[RequireComponent(typeof(ChoreographyPlayer))]
public class DemoWave : MonoBehaviour
{
    public ChoreographyPlayer player;
    [Min(0)] public int count = 5;
    public float scale = 0.8f;
    public DemoSprites.Shape shape = DemoSprites.Shape.Circle;
    public Color color = Color.white;
    public DemoFaction faction = DemoFaction.Enemy;
    public DemoFaction[] explodesOn = { DemoFaction.Player, DemoFaction.PlayerShot };

    readonly List<GameObject> pool = new();

    void Reset() => player = GetComponent<ChoreographyPlayer>();
    void Awake() { if (player == null) player = GetComponent<ChoreographyPlayer>(); }
    void Start() => Rebuild();

    public int Count => count;

    public void SetCount(int n)
    {
        n = Mathf.Max(0, n);
        if (n == count && pool.Count == n) return;
        count = n;
        Rebuild();
    }

    public void SetScale(float s)
    {
        scale = Mathf.Max(0.01f, s);
        foreach (var go in pool)
            if (go)
            {
                go.transform.localScale = Vector3.one * scale;
                var a = go.GetComponent<DemoActor>();
                if (a) a.explosionSize = scale;
            }
    }

    public void SetColor(Color c)
    {
        color = c;
        foreach (var go in pool)
            if (go)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr) sr.color = c;
                var a = go.GetComponent<DemoActor>();
                if (a) a.color = c;
            }
    }

    void Rebuild()
    {
        while (pool.Count < count) pool.Add(MakeDancer(pool.Count));
        while (pool.Count > count) { int last = pool.Count - 1; if (pool[last]) Destroy(pool[last]); pool.RemoveAt(last); }

        player.targets.Clear();
        for (int i = 0; i < pool.Count; i++)
        {
            player.targets.Add(pool[i].transform);
            var a = pool[i].GetComponent<DemoActor>();
            a.choreoPlayer = player;
            a.choreoIndex = i;
        }
    }

    GameObject MakeDancer(int i)
    {
        var go = new GameObject("Dancer " + i);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 200;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        var cc = go.AddComponent<CircleCollider2D>();
        cc.isTrigger = true;
        cc.radius = 0.5f;

        var a = go.AddComponent<DemoActor>();
        a.faction = faction;
        a.explodesOn = explodesOn;
        a.shape = shape;
        a.color = color;
        a.explosionSize = scale;
        return go;
    }
}

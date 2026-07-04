using UnityEngine;
using Laubrary.Pyre;

/// Demo driver: click anywhere to spawn a one-shot explosion at the mouse, and (optionally) auto-spawn a few on
/// a timer to show variety. No baked assets needed — each blast renders its frames through BlastRenderer at
/// runtime via BlastPlayer. Assign a BlastSpec in the inspector, or leave it empty to use a built example.
public class PyreDemoSpawner : MonoBehaviour
{
    [Tooltip("Explosion to spawn. If empty, a built-in example blast is created at runtime.")]
    public BlastSpec spec;

    [Tooltip("Playback speed of each spawned blast, in frames per second.")]
    public float fps = 24f;

    [Tooltip("Also spawn a blast automatically on a timer to show it off without clicking.")]
    public bool autoSpawn = true;

    [Tooltip("Seconds between auto-spawns.")]
    public float autoInterval = 1.2f;

    float timer;
    BlastSpec runtimeSpec;

    BlastSpec ActiveSpec => spec != null ? spec : (runtimeSpec != null ? runtimeSpec : (runtimeSpec = BuildExample()));

    static BlastSpec BuildExample()
    {
        var s = ScriptableObject.CreateInstance<BlastSpec>();
        s.AddExampleContent();
        return s;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) SpawnAt(MouseWorld());

        if (autoSpawn)
        {
            timer += Time.deltaTime;
            if (timer >= autoInterval) { timer = 0f; SpawnAt(RandomViewportPoint()); }
        }
    }

    void SpawnAt(Vector3 pos)
    {
        var go = new GameObject("Blast");
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 500;

        var player = go.AddComponent<BlastPlayer>();
        player.spec = ActiveSpec;
        player.fps = fps;
        player.loop = false;
        player.destroyOnFinish = true;
        player.playOnAwake = false;   // Awake already ran on AddComponent; start it explicitly now that spec is set
        player.Play();
    }

    Vector3 MouseWorld()
    {
        var cam = Camera.main;
        if (cam == null) return transform.position;
        Vector3 p = cam.ScreenToWorldPoint(Input.mousePosition);
        p.z = 0f;
        return p;
    }

    Vector3 RandomViewportPoint()
    {
        var cam = Camera.main;
        if (cam == null) return transform.position;
        Vector3 p = cam.ViewportToWorldPoint(new Vector3(Random.Range(0.15f, 0.85f), Random.Range(0.15f, 0.85f), 0f));
        p.z = 0f;
        return p;
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Laubrary.Choreographer;

/// Dev-host convenience: builds runnable Choreographer demo scenes. Not shipped with the package.
public static class ChoreoDemoBuilder
{
    const string Dir = "Assets/Demos/ChoreographerDemo";

    // ── the schmup "queue": a line of dancers following one sine path, staggered so they chase each other ──
    [MenuItem("Laubrary/Choreographer/Build Demo Scene (Queue)")]
    public static void BuildQueue()
    {
        EnsureDir();
        var c = LoadOrSeed(Dir + "/QueueSine.asset", SeedQueue);   // never overwrites an existing asset
        NewScene();
        var root = new GameObject("Choreographer Demo");
        var player = MakePlayer(root, c);
        SpawnDancers(player, root, c.defaultCount, 1f);
        SaveScene(Dir + "/ChoreographerDemo.unity", c.defaultCount, "queue");
    }

    // ── a barrage: all missiles launch from the Launcher, fan out mid-flight, then home onto the Target ──
    [MenuItem("Laubrary/Choreographer/Build Demo Scene (Barrage)")]
    public static void BuildBarrage()
    {
        EnsureDir();
        var c = LoadOrSeed(Dir + "/Barrage.asset", SeedBarrage);   // never overwrites an existing asset
        NewScene();
        var root = new GameObject("Choreographer Barrage");
        var player = MakePlayer(root, c);

        var launcher = MakeMarker("Launcher", new Color(0.3f, 0.95f, 0.4f), new Vector3(-6f, 0f, 0f),
            new Vector2(-7f, -3.5f), new Vector2(-4.5f, 3.5f), 1.5f);
        var target = MakeMarker("Target (player)", new Color(0.95f, 0.3f, 0.3f), new Vector3(6f, 0f, 0f),
            new Vector2(4.5f, -3.5f), new Vector2(7f, 3.5f), 3f);
        player.launcher = launcher.transform;
        player.target = target.transform;

        root.AddComponent<ChoreoDemoControlUI>();   // runtime UI to steer boss/player wandering in play mode

        SpawnDancers(player, root, c.defaultCount, 0.5f);
        SaveScene(Dir + "/ChoreographerBarrage.unity", c.defaultCount, "barrage");
    }

    // ── shared helpers ────────────────────────────────────────────────────────
    static void EnsureDir()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Demos")) AssetDatabase.CreateFolder("Assets", "Demos");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Demos", "ChoreographerDemo");
    }

    // GUARD RAIL: seed an asset's fields ONLY when creating it brand-new. If it already exists on disk it is
    // returned exactly as the user left it — a rebuild never resets or deletes tuned data. To intentionally
    // restore defaults, use the explicit "Reset … to defaults" menu, which asks first.
    static Choreography LoadOrSeed(string path, System.Action<Choreography> seed)
    {
        var c = AssetDatabase.LoadAssetAtPath<Choreography>(path);
        if (c != null) return c;
        c = ScriptableObject.CreateInstance<Choreography>();
        seed(c);
        AssetDatabase.CreateAsset(c, path);
        AssetDatabase.SaveAssets();
        return c;
    }

    static void SeedQueue(Choreography c)
    {
        c.pathPoints = SinePoints(17, 0.3f);
        c.smooth = true; c.constantSpeed = true;
        c.spreadLength = 0f; c.spreadBend = 0f;
        c.facing = FacingMode.Fixed; c.facingAngle = 0f;
        c.defaultCount = 12; c.duration = 3f; c.stagger = 0.6f;
        c.direction = Direction.Scatter; c.loop = true;
        c.useLauncher = false; c.useTarget = false;
    }

    static void SeedBarrage(Choreography c)
    {
        c.pathPoints = new List<Vector2> { new(-0.5f, 0f), new(0f, 0.35f), new(0.5f, 0f) }; // gentle arc bump
        c.smooth = true; c.constantSpeed = true;
        c.spreadLength = 1.3f; c.spreadBend = 0.15f;   // fan the missiles mid-flight
        c.facing = FacingMode.Fixed; c.facingAngle = 0f;
        c.defaultCount = 14; c.duration = 2.5f; c.stagger = 0.5f;
        c.direction = Direction.Scatter; c.loop = true;
        c.useLauncher = true; c.launchBlend = 0.3f;
        c.useTarget = true; c.retargetAt = 0.6f;
    }

    // Non-destructive: add the runtime control UI to the already-open scene (no rebuild, no data touched).
    [MenuItem("Laubrary/Choreographer/Add Controls to Open Scene")]
    public static void AddControlsToOpenScene()
    {
        if (Object.FindFirstObjectByType<ChoreoDemoControlUI>() != null) { Debug.Log("[ChoreoDemo] Controls already in the scene."); return; }
        var player = Object.FindFirstObjectByType<ChoreographyPlayer>();
        if (player == null) { Debug.Log("[ChoreoDemo] No ChoreographyPlayer in the open scene — open the barrage scene first."); return; }
        player.gameObject.AddComponent<ChoreoDemoControlUI>();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ChoreoDemo] Added runtime controls to the open scene.");
    }

    // The ONLY path that overwrites a tuned asset — and it asks first.
    [MenuItem("Laubrary/Choreographer/Reset Queue asset to defaults")]
    public static void ResetQueue() => ConfirmReset(Dir + "/QueueSine.asset", SeedQueue, "Queue");

    [MenuItem("Laubrary/Choreographer/Reset Barrage asset to defaults")]
    public static void ResetBarrage() => ConfirmReset(Dir + "/Barrage.asset", SeedBarrage, "Barrage");

    static void ConfirmReset(string path, System.Action<Choreography> seed, string label)
    {
        var c = AssetDatabase.LoadAssetAtPath<Choreography>(path);
        if (c == null) { Debug.Log($"[ChoreoDemo] {label} asset not found — nothing to reset."); return; }
        if (!EditorUtility.DisplayDialog("Reset choreography?",
            $"This OVERWRITES all data in the {label} asset with the built-in defaults, and cannot be undone. Continue?",
            "Reset", "Cancel")) return;
        seed(c); EditorUtility.SetDirty(c); AssetDatabase.SaveAssets();
        Debug.Log($"[ChoreoDemo] {label} asset reset to defaults.");
    }

    static void NewScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.09f);
        }
    }

    static ChoreographyPlayer MakePlayer(GameObject root, Choreography c)
    {
        var player = root.AddComponent<ChoreographyPlayer>();
        player.choreography = c;
        player.anchor = root.transform;
        player.worldSize = new Vector2(12f, 5f);
        player.applyFacing = true;
        player.targets = new List<Transform>();
        return player;
    }

    static void SpawnDancers(ChoreographyPlayer player, GameObject root, int n, float scale)
    {
        var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Dancer " + i);
            go.transform.SetParent(root.transform);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = knob;
            sr.color = Color.HSVToRGB(Mathf.Lerp(0.52f, 0.95f, n > 1 ? i / (float)(n - 1) : 0.5f), 0.65f, 1f);
            player.targets.Add(go.transform);
        }
    }

    static GameObject MakeMarker(string name, Color color, Vector3 pos, Vector2 boxMin, Vector2 boxMax, float speed)
    {
        var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 1.2f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = knob; sr.color = color;
        var w = go.AddComponent<ChoreoDemoWander>();
        w.min = boxMin; w.max = boxMax; w.speed = speed;
        return go;
    }

    static void SaveScene(string path, int n, string label)
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[ChoreoDemo] Built {label} scene at {path} with {n} dancers.");
    }

    static List<Vector2> SinePoints(int n, float amp)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);
            pts.Add(new Vector2(Mathf.Lerp(-0.5f, 0.5f, u), amp * Mathf.Sin(u * Mathf.PI * 2f)));
        }
        return pts;
    }
}

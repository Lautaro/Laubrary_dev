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

    // ── the full shmup: boss barrage + player (wanders & shoots) + four enemy formation waves + debug view ──
    [MenuItem("Laubrary/Choreographer/Build Demo Scene (Shmup)")]
    public static void BuildShmup()
    {
        EnsureDir();
        var barrageC = LoadOrSeed(Dir + "/Barrage.asset", SeedBarrage);
        var waveSine = LoadOrSeed(Dir + "/EnemySineSweep.asset", SeedSineSweep);
        var waveDive = LoadOrSeed(Dir + "/EnemyVDive.asset", SeedVDive);
        var waveArc = LoadOrSeed(Dir + "/EnemyArc.asset", SeedArc);
        var waveLoner = LoadOrSeed(Dir + "/EnemyLoner.asset", SeedLoner);

        NewScene();

        // Player (right side): wanders and fires bullets to the side. Nothing kills it in the demo.
        var player = MakeActor("Player", null, DemoSprites.Shape.Triangle, new Color(0.4f, 0.9f, 1f), 0.9f, DemoFaction.Player, new DemoFaction[0]);
        player.transform.position = new Vector3(6f, 0f, 0f);
        var pw = player.AddComponent<ChoreoDemoWander>(); pw.min = new(3.5f, -3.5f); pw.max = new(8f, 3.5f); pw.speed = 3f;
        player.AddComponent<DemoPlayerWeapon>();

        // Boss (left side): wanders and fires the homing barrage.
        var boss = MakeActor("Boss", null, DemoSprites.Shape.Square, new Color(0.95f, 0.35f, 0.3f), 1.6f, DemoFaction.Boss, new DemoFaction[0]);
        boss.transform.position = new Vector3(-6f, 0f, 0f);
        var bw = boss.AddComponent<ChoreoDemoWander>(); bw.min = new(-8f, -3.5f); bw.max = new(-4.5f, 3.5f); bw.speed = 1.5f;

        // Barrage projectiles: EnemyShots that explode on the player.
        var barrageRoot = new GameObject("Barrage");
        var barragePlayer = MakePlayer(barrageRoot, barrageC);
        barragePlayer.launcher = boss.transform;
        barragePlayer.target = player.transform;
        SpawnActorDancers(barragePlayer, barrageRoot, barrageC.defaultCount, 0.4f, DemoSprites.Shape.Circle,
            new Color(1f, 0.7f, 0.2f), DemoFaction.EnemyShot, new[] { DemoFaction.Player });
        barrageRoot.AddComponent<ChoreographyDebugView>().player = barragePlayer;

        // Four enemy formation waves — each a choreography, exploding on the player or the player's shots.
        MakeChoreoWave("Wave Sine", waveSine, new Vector2(18f, 8f), new Vector3(0f, 2.5f, 0f), DemoSprites.Shape.Circle, new Color(0.6f, 0.9f, 0.4f), 0.8f);
        MakeChoreoWave("Wave VDive", waveDive, new Vector2(18f, 10f), new Vector3(0f, 0.5f, 0f), DemoSprites.Shape.Square, new Color(0.9f, 0.6f, 0.3f), 0.8f);
        MakeChoreoWave("Wave Arc", waveArc, new Vector2(18f, 8f), new Vector3(0f, 1.5f, 0f), DemoSprites.Shape.Triangle, new Color(0.9f, 0.45f, 0.85f), 0.8f);
        MakeChoreoWave("Wave Loner", waveLoner, new Vector2(16f, 8f), new Vector3(0f, -2.5f, 0f), DemoSprites.Shape.Diamond, new Color(0.95f, 0.85f, 0.35f), 1.0f);

        var ui = new GameObject("Demo Controls"); ui.AddComponent<ChoreoDemoControlUI>();

        SaveScene(Dir + "/ChoreographerShmup.unity", 0, "shmup");
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
        c.useTarget = true; c.releaseAt = 0.6f; c.targetBlend = 0.35f;
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

    // ── shmup actor / wave helpers ──────────────────────────────────────────────
    static GameObject MakeActor(string name, Transform parent, DemoSprites.Shape shape, Color color, float scale,
                                DemoFaction faction, DemoFaction[] explodesOn)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 200;                       // sprite itself is assigned at play time by DemoActor

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        var cc = go.AddComponent<CircleCollider2D>();
        cc.isTrigger = true;
        cc.radius = 0.5f;                            // sprite is 1 unit, so 0.5 local matches at any scale

        var act = go.AddComponent<DemoActor>();
        act.faction = faction;
        act.explodesOn = explodesOn;
        act.shape = shape;
        act.color = color;
        act.explosionSize = scale;
        return go;
    }

    static void SpawnActorDancers(ChoreographyPlayer player, GameObject root, int n, float scale,
                                  DemoSprites.Shape shape, Color color, DemoFaction faction, DemoFaction[] explodesOn)
    {
        for (int i = 0; i < n; i++)
        {
            var go = MakeActor("Dancer " + i, root.transform, shape, color, scale, faction, explodesOn);
            var act = go.GetComponent<DemoActor>();
            act.choreoPlayer = player;               // hide+respawn on the wave loop instead of destroying
            act.choreoIndex = i;
            player.targets.Add(go.transform);
        }
    }

    static void MakeChoreoWave(string name, Choreography choreo, Vector2 worldSize, Vector3 anchorPos,
                               DemoSprites.Shape shape, Color color, float scale)
    {
        var root = new GameObject(name);
        root.transform.position = anchorPos;
        var cp = root.AddComponent<ChoreographyPlayer>();
        cp.choreography = choreo;
        cp.anchor = root.transform;
        cp.worldSize = worldSize;
        cp.applyFacing = true;
        cp.targets = new List<Transform>();
        SpawnActorDancers(cp, root, choreo.defaultCount, scale, shape, color, DemoFaction.Enemy,
            new[] { DemoFaction.Player, DemoFaction.PlayerShot });
        root.AddComponent<ChoreographyDebugView>().player = cp;
    }

    // ── enemy formation seeds (4 types; only ever written when the asset is first created) ──
    static void SeedSineSweep(Choreography c)
    {
        c.pathPoints = SinePoints(17, 0.15f);
        c.smooth = true; c.constantSpeed = true;
        c.spreadLength = 3f; c.spreadBend = 0f; c.facing = FacingMode.Fixed;
        c.defaultCount = 6; c.duration = 6f; c.stagger = 0.25f; c.direction = Direction.Scatter; c.loop = true;
        c.useLauncher = false; c.useTarget = false;
    }

    static void SeedVDive(Choreography c)
    {
        c.pathPoints = new List<Vector2> { new(-0.6f, 0.4f), new(-0.2f, 0f), new(0.2f, -0.2f), new(0.6f, -0.5f) };
        c.smooth = true; c.constantSpeed = true;
        c.spreadLength = 2.5f; c.spreadBend = 0.25f; c.facing = FacingMode.Fixed;
        c.defaultCount = 5; c.duration = 5f; c.stagger = 0.15f; c.direction = Direction.Scatter; c.loop = true;
        c.useLauncher = false; c.useTarget = false;
    }

    static void SeedArc(Choreography c)
    {
        c.pathPoints = new List<Vector2> { new(-0.6f, 0f), new(0f, 0.35f), new(0.6f, 0f) };
        c.smooth = true; c.constantSpeed = true;
        c.spreadLength = 3.5f; c.spreadBend = 0.1f; c.facing = FacingMode.Fixed;
        c.defaultCount = 7; c.duration = 6f; c.stagger = 0.2f; c.direction = Direction.Scatter; c.loop = true;
        c.useLauncher = false; c.useTarget = false;
    }

    static void SeedLoner(Choreography c)
    {
        c.pathPoints = new List<Vector2> { new(-0.6f, -0.2f), new(-0.2f, 0.3f), new(0.2f, -0.3f), new(0.6f, 0.2f) };
        c.smooth = true; c.constantSpeed = true;
        c.spreadLength = 0f; c.spreadBend = 0f; c.facing = FacingMode.Fixed;
        c.defaultCount = 1; c.duration = 7f; c.stagger = 0f; c.direction = Direction.Scatter; c.loop = true;
        c.useLauncher = false; c.useTarget = false;
    }
}

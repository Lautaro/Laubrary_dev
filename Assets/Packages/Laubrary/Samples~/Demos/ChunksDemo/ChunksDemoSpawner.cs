using UnityEngine;
using Laubrary.Chunks;

/// Demo driver for the Chunks tool. Left-click anywhere to throw a radial spark burst at the cursor; press the
/// directional key to fire a wall-mounted burst that fans up-and-out and settles on the floor, so you can see the
/// cone and floor bounce/rest; press the sampled key to throw sampled pseudo-3D debris cut from a demo sprite's
/// own pixels. Everything is play-mode (chunks are moving physical objects). No shipped assets.
public class ChunksDemoSpawner : MonoBehaviour
{
    [Tooltip("Radial 'pop' burst fired at the mouse on left-click.")]
    public ChunkSpec radialSpec;
    [Tooltip("Directional burst fired from a wall point when the key is pressed.")]
    public ChunkSpec directionalSpec;
    [Tooltip("Key that fires the directional burst.")]
    public KeyCode directionalKey = KeyCode.Space;

    [Header("Sampled pseudo-3D debris")]
    [Tooltip("Burst that samples small chunks out of a source sprite's own pixels and tumbles them " +
             "(squash + shade) — see ChunkSpec.sampleSource/tumble.")]
    public ChunkSpec sampledSpec;
    [Tooltip("Shape sampled as the 'exploding object' — built at runtime via DemoSprites, no shipped art.")]
    public DemoSprites.Shape sampledShape = DemoSprites.Shape.Circle;
    [Tooltip("Key that fires the sampled-debris burst at the mouse position.")]
    public KeyCode sampledKey = KeyCode.T;
    [Tooltip("Where the directional (wall-mounted) burst originates, in world space.")]
    public Vector2 wallOrigin = new Vector2(-8f, -2f);
    [Tooltip("Direction of the directional burst, degrees (0 = right, 90 = up).")]
    public float wallDirectionDeg = 60f;

    [Header("Composed effect (Chunks 2.0)")]
    [Tooltip("A fully AUTHORED composed burst. Everything it does — which character is fractured, which " +
             "blasts go off, and whether each one sits behind, between or in front of the flying pieces — " +
             "lives in the ChunkSpec asset itself. This script does not configure any of it; it only decides " +
             "WHEN and WHERE the burst happens, which is the whole point: the effect is authored, not coded.")]
    public ChunkSpec composedSpec;
    [Tooltip("The character that blows up. The burst fires at this object's position and the object is " +
             "hidden while its pieces are in the air, so the fracture reads as the character coming apart " +
             "rather than as debris appearing next to it. Leave empty to fire at the mouse instead.")]
    public SpriteRenderer composedTarget;
    [Tooltip("How long the character stays hidden after it blows up, before it reappears so you can do it " +
             "again. Roughly the lifetime of the pieces.")]
    public float composedRespawnSeconds = 2.5f;
    [Tooltip("Key that fires the composed burst.")]
    public KeyCode composedKey = KeyCode.C;

    [Header("Floor visual")]
    [Tooltip("Draw a floor strip at the spec's floorY so the resting/bouncing is visible. Turn off if the scene " +
             "already provides a floor object.")]
    public bool drawFloor = true;
    public float floorWidth = 24f;

    Camera cam;

    void Start()
    {
        cam = Camera.main;
        if (drawFloor && radialSpec != null) BuildFloor(radialSpec.floorY);
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;

        if (Input.GetMouseButtonDown(0) && radialSpec != null)
            Chunks.Burst(MouseWorld(), radialSpec);

        if (directionalSpec != null && Input.GetKeyDown(directionalKey))
            Chunks.Burst(wallOrigin, directionalSpec, wallDirectionDeg);

        if (composedSpec != null && Input.GetKeyDown(composedKey))
            FireComposed();

        if (sampledSpec != null && Input.GetKeyDown(sampledKey))
        {
            // DemoSprites builds its sprite at runtime (no shipped assets), so the source is assigned
            // here rather than pre-serialized on the ChunkSpec asset.
            sampledSpec.sampleSource = DemoSprites.Get(sampledShape);
            Chunks.Burst(MouseWorld(), sampledSpec);
        }
    }

    /// Blow up the character. Deliberately the whole of it: pick a point, hide the thing that is coming
    /// apart, fire the authored recipe. Nothing here reaches into the ChunkSpec to set a sprite or a blast —
    /// an earlier version of this method did, and it silently overwrote whatever had been authored in the
    /// Chunks window, so the window's own settings could never be seen in the demo.
    void FireComposed()
    {
        Vector2 at = composedTarget != null ? (Vector2)composedTarget.transform.position : MouseWorld();
        Chunks.Burst(at, composedSpec);

        // The character goes away while its pieces are in the air, then comes back so the demo can be
        // replayed. Only the RENDERER is toggled, never the GameObject: disabling the object would also stop
        // anything else on it, and the demo's whole job is to be re-pressable.
        if (composedTarget != null && composedTarget.enabled)
        {
            composedTarget.enabled = false;
            CancelInvoke(nameof(ShowComposedTarget));
            Invoke(nameof(ShowComposedTarget), Mathf.Max(0.1f, composedRespawnSeconds));
        }
    }

    void ShowComposedTarget()
    {
        if (composedTarget != null) composedTarget.enabled = true;
    }

    void OnGUI()
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        s.normal.textColor = Color.white;
        GUI.Label(new Rect(10, 10, 620, 110),
            "Left-click: radial burst\n" +
            directionalKey + ": directional wall burst\n" +
            sampledKey + " (at mouse): sampled pseudo-3D debris tumble\n" +
            composedKey + ": blow up the character — it fractures, with blasts behind, between and in " +
            "front of the pieces (all authored in the Chunks window)", s);
    }

    Vector2 MouseWorld()
    {
        if (cam == null) return Vector2.zero;
        Vector3 sp = Input.mousePosition;
        sp.z = Mathf.Abs(cam.transform.position.z); // put the point on the z=0 plane for an orthographic cam
        Vector3 w = cam.ScreenToWorldPoint(sp);
        return new Vector2(w.x, w.y);
    }

    void BuildFloor(float y)
    {
        var go = new GameObject("Demo Floor");
        go.transform.position = new Vector3(0f, y - 0.05f, 0f);
        go.transform.localScale = new Vector3(floorWidth, 0.1f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DemoSprites.Get(DemoSprites.Shape.Square);
        sr.color = new Color(0.25f, 0.25f, 0.32f, 1f);
        sr.sortingOrder = -10;
    }
}

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

        if (sampledSpec != null && Input.GetKeyDown(sampledKey))
        {
            // DemoSprites builds its sprite at runtime (no shipped assets), so the source is assigned
            // here rather than pre-serialized on the ChunkSpec asset.
            sampledSpec.sampleSource = DemoSprites.Get(sampledShape);
            Chunks.Burst(MouseWorld(), sampledSpec);
        }
    }

    void OnGUI()
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        s.normal.textColor = Color.white;
        GUI.Label(new Rect(10, 10, 500, 80),
            "Left-click: radial burst\n" +
            directionalKey + ": directional wall burst\n" +
            sampledKey + " (at mouse): sampled pseudo-3D debris tumble", s);
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

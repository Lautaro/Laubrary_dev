using UnityEngine;

/// Demo-only: drifts a transform to random points inside a box, picking a new destination on arrival.
/// Used to move the Launcher and Target around so the barrage's launcher/target binding is visible.
public class ChoreoDemoWander : MonoBehaviour
{
    public Vector2 min = new(-7f, -3f);
    public Vector2 max = new(-4f, 3f);
    public float speed = 2f;
    public float arriveDistance = 0.25f;

    Vector3 dest;

    void Start() { Pick(); transform.position = dest; Pick(); }

    void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, dest, speed * Time.deltaTime);
        if ((transform.position - dest).sqrMagnitude <= arriveDistance * arriveDistance) Pick();
    }

    void Pick() => dest = new Vector3(Random.Range(min.x, max.x), Random.Range(min.y, max.y), 0f);

    /// Jump to a fresh random destination immediately (used by the demo control UI).
    public void JumpNow() => Pick();
}

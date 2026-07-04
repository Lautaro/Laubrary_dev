using UnityEngine;

/// Demo-only: drives a transform at a constant velocity and despawns it after a lifetime. Used for the player's
/// bullets (choreo-driven things are moved by ChoreographyPlayer instead).
public class DemoMover : MonoBehaviour
{
    public Vector2 velocity;
    public float life = 3f;

    float t;

    void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);
        t += Time.deltaTime;
        if (t >= life) Destroy(gameObject);
    }
}

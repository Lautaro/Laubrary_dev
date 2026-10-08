using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.ZoetropeLaunimator;

namespace Laubrary.GoreLabDemo
{
    /// <summary>
    /// Makes a demo imp wander: it walks in one of the eight directions its walk animation has, picks a new random direction every few seconds,
    /// and turns back toward the middle when it reaches the edge of the arena. Purely a demo mover; the gore tool knows nothing about it.
    /// </summary>
    public sealed class GoreLabDemoWalker : MonoBehaviour
    {
        public Vector2 arenaMin = new Vector2(-9f, -5.5f);
        public Vector2 arenaMax = new Vector2(9f, 3.5f);
        public float speed = 1.6f;
        public Vector2 turnEvery = new Vector2(1.2f, 4f);

        MotionPoseAnimator _pose;
        SpriteRenderer _sr;
        float _heading = -1f;
        float _turnAt;

        void Start()
        {
            _pose = GetComponent<MotionPoseAnimator>();
            _sr = GetComponentInChildren<SpriteRenderer>();
            PickHeading();
        }

        void PickHeading()
        {
            SetHeading(Random.Range(0, 8) * 45f);
            _turnAt = Time.time + Random.Range(turnEvery.x, turnEvery.y);
        }

        void SetHeading(float h)
        {
            if (Mathf.Approximately(h, _heading)) return;
            _heading = h;
            if (_pose != null) _pose.SetPoseOverride(MotionCondition.Always, h);
        }

        // Heading 0 is away from the camera (up the screen), clockwise, so 90 is to the right and 180 toward the camera.
        static Vector2 Dir(float heading) { float r = heading * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(r), Mathf.Cos(r)); }

        void Update()
        {
            if (Time.time >= _turnAt) PickHeading();
            Vector2 p = transform.position;
            Vector2 d = Dir(_heading);
            p += d * (speed * Time.deltaTime);

            // At an edge, turn to a random direction that points back inside.
            bool out_ = p.x < arenaMin.x || p.x > arenaMax.x || p.y < arenaMin.y || p.y > arenaMax.y;
            if (out_)
            {
                p = new Vector2(Mathf.Clamp(p.x, arenaMin.x, arenaMax.x), Mathf.Clamp(p.y, arenaMin.y, arenaMax.y));
                Vector2 mid = (arenaMin + arenaMax) * 0.5f - p;
                for (int tries = 0; tries < 16; tries++)
                {
                    float h = Random.Range(0, 8) * 45f;
                    if (Vector2.Dot(Dir(h), mid) > 0.1f) { SetHeading(h); break; }
                }
                _turnAt = Time.time + Random.Range(turnEvery.x, turnEvery.y);
            }
            transform.position = new Vector3(p.x, p.y, 0f);
            if (_sr != null) _sr.sortingOrder = -Mathf.RoundToInt(p.y * 100f);   // nearer (lower) imps draw in front
        }
    }
}

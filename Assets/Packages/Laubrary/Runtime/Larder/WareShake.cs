using UnityEngine;

namespace Laubrary.Larder
{
    /// A cheap "it got hit" rattle. One component per Ware; call Shake(amount) as often as you like — it just tops up
    /// a magnitude + timer, so there's no coroutine spawned per hit. Update jitters localPosition around the rest pose
    /// and eases back to it. The rest pose is captured lazily so it survives objects that are positioned after Awake.
    [DisallowMultipleComponent]
    public class WareShake : MonoBehaviour
    {
        [Tooltip("Seconds for a rattle to fully decay.")]
        public float duration = 0.15f;

        Vector3 basePos;
        bool captured;
        float timer;
        float magnitude;

        void Start() => Capture();

        void Capture()
        {
            if (captured) return;
            basePos = transform.localPosition;
            captured = true;
        }

        public void Shake(float amount)
        {
            Capture();
            magnitude = Mathf.Max(magnitude, amount);
            timer = duration;
        }

        void Update()
        {
            if (!captured) return;
            if (timer <= 0f)
            {
                transform.localPosition = basePos;
                magnitude = 0f;
                return;
            }
            timer -= Time.deltaTime;
            float k = Mathf.Clamp01(timer / Mathf.Max(0.0001f, duration));
            Vector2 j = Random.insideUnitCircle * magnitude * k;
            transform.localPosition = basePos + new Vector3(j.x, j.y, 0f);
        }
    }
}

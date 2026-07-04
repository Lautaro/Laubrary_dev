using UnityEngine;

namespace Laubrary.Larder
{
    /// A single destructible product on a shelf. It owns a WareSpec, asks WareGenerator for one sprite per damage
    /// stage (once, cached), and shows stage 0. Every Hit rattles the object and coughs up a colour-sampled pixel
    /// burst; each hpPerStage hits advance it one stage (swapping in the more-wrecked sprite) until, past the last
    /// stage, it flings a final chunk and removes itself. The spec/renderer are guarded so a half-wired object is inert
    /// rather than throwing.
    [RequireComponent(typeof(SpriteRenderer))]
    [DisallowMultipleComponent]
    public class ShelfWare : MonoBehaviour
    {
        public WareSpec spec;
        public int currentStage;
        public int hpPerStage = 1;
        public bool rattleOnHit = true;
        public bool flingChunkOnBreak = true;

        SpriteRenderer sr;
        WareShake shake;
        Sprite[] stages;
        int hp;

        void Start()
        {
            sr = GetComponent<SpriteRenderer>();
            shake = GetComponent<WareShake>();
            if (shake == null) shake = gameObject.AddComponent<WareShake>();

            if (spec == null || sr == null) return;

            stages = WareGenerator.CreateAllStages(spec);
            currentStage = Mathf.Clamp(currentStage, 0, stages.Length - 1);
            sr.sprite = stages[currentStage];
            hp = Mathf.Max(1, hpPerStage);

            FitBoxCollider();
        }

        /// Take a hit at a world point: burst, rattle, and step the damage forward.
        public void Hit(Vector2 worldPoint)
        {
            if (spec == null || sr == null || stages == null || sr.sprite == null) return;

            SpawnBurst(worldPoint, 1f);
            if (rattleOnHit && shake != null) shake.Shake(0.06f);

            hp--;
            if (hp > 0) return;

            currentStage++;
            if (currentStage >= stages.Length)
            {
                if (flingChunkOnBreak) SpawnBurst(transform.position, 1.6f);
                Destroy(gameObject);
                return;
            }
            sr.sprite = stages[currentStage];
            hp = Mathf.Max(1, hpPerStage);
            FitBoxCollider();
        }

        void SpawnBurst(Vector3 at, float scaleMul)
        {
            var tex = sr.sprite != null ? sr.sprite.texture : null;
            if (tex == null) return;
            var rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
            var colors = WareGenerator.SampleOpaqueColors(tex, rng, 16);
            float unit = 1f / Mathf.Max(1f, spec.pixelsPerUnit);
            WareDebris.Burst(at, colors, sr.bounds.size.magnitude * 0.25f * scaleMul + unit);
        }

        // Keep any BoxCollider2D matched to the current sprite so clicks land on what you see.
        void FitBoxCollider()
        {
            if (sr.sprite == null) return;
            if (TryGetComponent(out BoxCollider2D box))
            {
                box.size = sr.sprite.bounds.size;
                box.offset = sr.sprite.bounds.center;
            }
        }
    }
}

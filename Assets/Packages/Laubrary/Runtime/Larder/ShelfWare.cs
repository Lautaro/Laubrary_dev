using UnityEngine;

namespace Laubrary.Larder
{
    /// A single destructible product on a shelf. Its damage-stage sprites come from one of two sources: pre-baked
    /// sprite assets set in <see cref="bakedStages"/> (so the product is visible and editable in the scene at author
    /// time), or, if none are assigned, generated on the fly from a WareSpec via WareGenerator. Either way it shows
    /// stage 0 and every Hit rattles the object, coughs up a colour-sampled pixel burst, and — each hpPerStage hits —
    /// advances one stage until, past the last, it flings a final chunk and removes itself. Everything is guarded so a
    /// half-wired object is inert rather than throwing.
    [RequireComponent(typeof(SpriteRenderer))]
    [DisallowMultipleComponent]
    public class ShelfWare : MonoBehaviour
    {
        [Tooltip("Pre-baked damage-stage sprites (stage 0 = intact). If set, these are used and the product shows in " +
                 "the scene at author time. Leave empty to generate from the WareSpec at runtime instead.")]
        public Sprite[] bakedStages;
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
            if (sr == null) return;

            if (bakedStages != null && bakedStages.Length > 0) stages = bakedStages;
            else if (spec != null) stages = WareGenerator.CreateAllStages(spec);
            else return; // no source of sprites — stay inert

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
            float ppu = spec != null ? spec.pixelsPerUnit : sr.sprite.pixelsPerUnit;
            float unit = 1f / Mathf.Max(1f, ppu);
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

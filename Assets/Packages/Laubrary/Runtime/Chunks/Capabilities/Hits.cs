using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Chunks
{
    /// Lets what a producer throws actually HURT what it touches — a wall peppered by bullet debris, embers
    /// that also burn. A cheap circle approximation, never pixel-perfect (that would need Burst/Jobs and is a
    /// bigger, separate decision), which is why it is a capability an author adds rather than something every
    /// recipe carries.
    [System.Serializable]
    public class Hits : ChunkModifier
    {
        public override string KindName => "Hits";

        public override bool CanTarget(ChunkCapability producer)
            => producer is DebrisScatter || producer is FragmentFracture;

        [Min(0f)]
        [Tooltip("Damage one piece deals, once per target.")]
        public float damage = 5f;

        [Range(0.1f, 3f)]
        [Tooltip("Collider radius as a multiple of the piece's own current size, so it shrinks as the piece does.")]
        public float radiusScale = 0.5f;

        /// Arm one spawned piece: a trigger circle plus a Combat2D hitbox, sized off the piece's own extent.
        ///
        /// Trigger callbacks need a Rigidbody2D on one side of the pair, and Chunks moves everything by plain
        /// transform maths rather than through Physics2D — so the body is Kinematic purely to make the trigger
        /// fire against a static target's collider; it is never pushed by real forces.
        public void Attach(GameObject go, float radiusUnits, Combatant owner)
        {
            if (!enabled || go == null) return;

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;

            var circle = go.GetComponent<CircleCollider2D>();
            if (circle == null) circle = go.AddComponent<CircleCollider2D>();
            circle.isTrigger = true;
            circle.radius = Mathf.Max(0.0001f, radiusUnits) * radiusScale;
            circle.enabled = true;

            var hitbox = go.GetComponent<Hitbox>();
            if (hitbox == null) hitbox = go.AddComponent<Hitbox>();
            hitbox.owner = owner;
            hitbox.damage = damage;
            hitbox.oncePerTarget = true;
            hitbox.enabled = true;
            hitbox.Arm();
        }
    }
}

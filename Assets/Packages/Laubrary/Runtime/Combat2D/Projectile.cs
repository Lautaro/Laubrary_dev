using System;
using UnityEngine;

namespace Laubrary.Combat2D
{
    /// A moving damage-dealer — a bullet, missile, thrown rock. Fire it with <see cref="Launch"/> (a weapon does
    /// this) and it travels in a straight line, damaging the first hostile <see cref="Hurtbox"/> it overlaps, then
    /// despawning (or piercing a few more). Blocked by colliders on the <see cref="blockers"/> layers (walls). Uses
    /// a kinematic Rigidbody2D so trigger callbacks fire; movement is by transform for simple straight flight.
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class Projectile : MonoBehaviour
    {
        [Min(0f)] public float damage = 10f;
        public float speed = 12f;
        [Min(0.05f)] public float lifetime = 4f;
        [Min(0)]
        [Tooltip("Extra hostiles it can pass through before dying (0 = dies on the first hit).")]
        public int pierce = 0;
        [Tooltip("Physics layers that stop the projectile (walls / world). Hurtboxes are handled separately.")]
        public LayerMask blockers = 0;
        [Tooltip("Rotate the sprite to face travel direction on launch.")]
        public bool faceDirection = true;

        public Faction faction;
        public GameObject source;

        /// Fired when the projectile damages a hurtbox.
        public event Action<Hurtbox, DamageInfo> Hit;
        /// Fired when it despawns for any reason (lifetime, wall, or its last hit).
        public event Action Expired;

        Vector2 dir = Vector2.right;
        float age;
        int hitsLeft;
        bool spent;

        void Awake()
        {
            var rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
            hitsLeft = pierce;
        }

        /// Fire in a direction (need not be normalized). Sets faction/source, optional speed override, orientation.
        public void Launch(Vector2 direction, Faction fac, GameObject src, float? speedOverride = null)
        {
            dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            faction = fac;
            source = src;
            if (speedOverride.HasValue) speed = speedOverride.Value;
            hitsLeft = pierce;
            age = 0f;
            spent = false;
            if (faceDirection)
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        void Update()
        {
            transform.position += (Vector3)(dir * (speed * Time.deltaTime));
            age += Time.deltaTime;
            if (age >= lifetime) Expire();
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (spent) return;

            // Hit a wall / world collider on a blocker layer → stop.
            if (blockers.value != 0 && (blockers.value & (1 << other.gameObject.layer)) != 0) { Expire(); return; }

            var hb = Combat.FindHurtbox(other);
            if (hb == null) return;

            Vector2 point = other.ClosestPoint(transform.position);
            if (!Combat.TryDamage(hb, faction, source, damage, point, out var info)) return;

            Hit?.Invoke(hb, info);
            if (hitsLeft-- <= 0) Expire();
        }

        void Expire()
        {
            if (spent) return;
            spent = true;
            Expired?.Invoke();
            Destroy(gameObject);
        }
    }
}

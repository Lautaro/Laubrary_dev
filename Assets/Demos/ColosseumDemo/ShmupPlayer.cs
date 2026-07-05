using UnityEngine;
using Laubrary.Colosseum;

namespace Laubrary.Demos.ColosseumShmup
{
    /// The player ship: arrow/WASD movement clamped to the play area, and a Colosseum ProjectileWeapon fired up.
    /// It is a Combatant (Player faction) with a Health + Hurtbox, so enemy bullets damage it through the exact
    /// same funnel the player's bullets use on enemies. Death/respawn is handled by the director.
    [RequireComponent(typeof(Health))]
    public class ShmupPlayer : MonoBehaviour
    {
        public float moveSpeed = 9f;
        public ProjectileWeapon weapon;
        public bool autoFire = true;
        public Vector2 minBounds = new(-7.5f, -4.2f);
        public Vector2 maxBounds = new(7.5f, -1f);

        Health health;

        void Awake() { health = GetComponent<Health>(); }

        void Update()
        {
            // Don't drive a dead ship (the director revives it).
            if (health != null && health.IsDead) return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 p = transform.position + new Vector3(h, v, 0f).normalized * (moveSpeed * Time.deltaTime);
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.y = Mathf.Clamp(p.y, minBounds.y, maxBounds.y);
            transform.position = p;

            if (weapon != null && (autoFire || Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)))
                weapon.TryFire(Vector2.up);
        }
    }
}

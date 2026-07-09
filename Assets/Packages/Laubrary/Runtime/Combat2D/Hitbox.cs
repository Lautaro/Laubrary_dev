using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Combat2D
{
    /// A region that DEALS damage while armed — a melee swing, a contact-damage body, a hazard. Put it on a trigger
    /// collider; every hostile <see cref="Hurtbox"/> that overlaps takes <see cref="damage"/> (once per arming when
    /// <see cref="oncePerTarget"/>). Arm/Disarm gates the active window (drive it from an attack animation). For a
    /// bullet, use <see cref="Projectile"/> instead.
    [RequireComponent(typeof(Collider2D))]
    public class Hitbox : MonoBehaviour
    {
        [Tooltip("Combatant dealing the damage (its faction decides who can be hit). Auto-found in parents if null.")]
        public Combatant owner;
        [Min(0f)] public float damage = 10f;
        [Tooltip("Only deals damage while armed. Turn off between swings.")]
        public bool armed = true;
        [Tooltip("Hit each target at most once per arming (so a lingering trigger doesn't multi-hit).")]
        public bool oncePerTarget = true;

        /// Fired for each landed hit (the hurtbox struck + the damage dealt).
        public event Action<Hurtbox, DamageInfo> HitLanded;

        readonly HashSet<Health> struck = new();

        void Reset() { owner = GetComponentInParent<Combatant>(); }
        void Awake() { if (owner == null) owner = GetComponentInParent<Combatant>(); }
        void OnEnable() => struck.Clear();

        /// Open the strike window (and forget who's been hit).
        public void Arm() { armed = true; struck.Clear(); }
        /// Close the strike window.
        public void Disarm() => armed = false;

        void OnTriggerEnter2D(Collider2D other) => TryHit(other);
        void OnTriggerStay2D(Collider2D other) => TryHit(other);

        void TryHit(Collider2D other)
        {
            if (!armed) return;
            var hb = Combat.FindHurtbox(other);
            if (hb == null) return;

            var h = hb.Health;
            if (oncePerTarget && h != null && struck.Contains(h)) return;

            Vector2 point = other.ClosestPoint(transform.position);
            Faction af = owner != null ? owner.faction : null;
            GameObject ao = owner != null ? owner.gameObject : null;
            if (Combat.TryDamage(hb, af, ao, damage, point, out var info))
            {
                if (oncePerTarget && h != null) struck.Add(h);
                HitLanded?.Invoke(hb, info);
            }
        }
    }
}

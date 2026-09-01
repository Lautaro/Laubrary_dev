using System;
using UnityEngine;
using UnityEngine.Events;

namespace Laubrary.Combat2D
{
    /// Hit points + the damage/heal/death events everything else keys off. Colosseum stays agnostic about what a
    /// death MEANS — it just fires <see cref="Died"/>; consumers spawn a Pyre blast, a Chunks burst, play a Zoe
    /// death clip, add score, etc. Prefer the C# events in code; the UnityEvents are there for designer wiring.
    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, IDamageable
    {
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("Ignore all incoming damage while true.")]
        public bool invulnerable = false;
        [Tooltip("Seconds of invulnerability granted after taking a hit (0 = none). Stops one shot dealing many hits.")]
        public float invulnerableAfterHit = 0f;

        [NonSerialized] float current;
        [NonSerialized] bool inited;
        float invulnTimer;

        // Lazily init to full the first time anything reads/writes health, so it's correct regardless of whether
        // Awake has run yet (edit-mode tools, or a component configured the same frame it was added).
        void EnsureInit() { if (!inited) { current = maxHealth; inited = true; } }

        public float Current { get { EnsureInit(); return current; } }
        public float Max => maxHealth;
        public float Normalized { get { EnsureInit(); return maxHealth > 0f ? Mathf.Clamp01(current / maxHealth) : 0f; } }
        public bool IsDead { get { EnsureInit(); return current <= 0f; } }
        public bool IsInvulnerable => invulnerable || invulnTimer > 0f;

        // ── C# events (primary) ──────────────────────────────────────────────────
        /// Fired on every damage instance that actually reduced health.
        public event Action<DamageInfo> Damaged;
        /// Fired when healed; carries the amount healed.
        public event Action<float> Healed;
        /// Fired once, when health reaches 0. Carries the killing blow.
        public event Action<DamageInfo> Died;
        /// Fired when a dead character is brought back — the counterpart to <see cref="Died"/>. Consumers undo
        /// whatever they did on death. Exists because reviving used to be a silent field write: nothing that
        /// reacted to a death (hit-detection off, a pending body cleanup, a death animation holding the view)
        /// was ever told to undo itself, so a respawned character came back permanently unhittable.
        public event Action Revived;

        // ── UnityEvents (designer wiring) ────────────────────────────────────────
        // A concrete subclass is required for Unity to serialize a UnityEvent that carries a float.
        [System.Serializable] public class HealthChangedEvent : UnityEvent<float> { }

        [Tooltip("Fires whenever health changes; passes the new normalized value (0..1). Good for a health bar.")]
        public HealthChangedEvent onHealthChanged = new();
        [Tooltip("Fires once when health hits 0.")]
        public UnityEvent onDied = new();
        [Tooltip("Fires when a dead character is brought back (Revive). The designer-wiring counterpart to onDied.")]
        public UnityEvent onRevived = new();

        void Awake() => EnsureInit();

        void Update() { if (invulnTimer > 0f) invulnTimer -= Time.deltaTime; }

        public void ApplyDamage(in DamageInfo info)
        {
            EnsureInit();
            if (IsDead || IsInvulnerable || info.amount <= 0f) return;

            current = Mathf.Max(0f, current - info.amount);
            if (invulnerableAfterHit > 0f) invulnTimer = invulnerableAfterHit;

            Damaged?.Invoke(info);
            onHealthChanged?.Invoke(Normalized);

            if (current <= 0f) { Died?.Invoke(info); onDied?.Invoke(); }
        }

        /// Convenience: take a plain amount of damage from nowhere in particular.
        public void Damage(float amount) => ApplyDamage(new DamageInfo(amount));

        public void Heal(float amount)
        {
            EnsureInit();
            if (IsDead || amount <= 0f) return;
            current = Mathf.Min(maxHealth, current + amount);
            Healed?.Invoke(amount);
            onHealthChanged?.Invoke(Normalized);
        }

        /// Grant i-frames for `seconds` right now, without a hit having landed. EXTENDS an existing window
        /// rather than replacing it, so a shorter grant can never cut a longer one short — the sort of
        /// ordering bug that only shows up under fire. Distinct from `invulnerableAfterHit`, which is a
        /// permanent property of the character rather than something a moment does.
        public void GrantInvulnerability(float seconds)
        {
            EnsureInit();
            if (seconds > 0f) invulnTimer = Mathf.Max(invulnTimer, seconds);
        }

        /// Instantly kill (fires Died, attributed to nothing).
        public void Kill()
        {
            if (IsDead) return;
            invulnTimer = 0f; invulnerable = false;
            ApplyDamage(new DamageInfo(current));
        }

        /// Refill to full and clear any i-frames (e.g. on respawn).
        public void Revive(float toHealth = -1f)
        {
            current = toHealth > 0f ? Mathf.Min(maxHealth, toHealth) : maxHealth;
            inited = true;
            invulnTimer = 0f;
            onHealthChanged?.Invoke(Normalized);
            // Fired unconditionally, NOT gated on "was it actually dead": Revive is already idempotent, and
            // every listener's job here is to undo death effects, which is a no-op when there were none. A
            // gate would instead have to be right about a question (was this a real revival?) that the caller
            // already answered by calling at all — and being wrong means a character that stays unhittable.
            Revived?.Invoke();
            onRevived?.Invoke();
        }
    }
}

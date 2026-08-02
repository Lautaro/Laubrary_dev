using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// What happens to the body once a character dies.
    public enum DeathDisposal
    {
        /// Vanish the instant it dies. Right when an explosion replaces the body.
        Immediate,
        /// Play the death clip through, then vanish. The usual answer for an authored death animation.
        WhenDeathClipEnds,
        /// Play the death clip, then lie there for `deathLinger` seconds before vanishing.
        AfterDelay,
        /// Never remove it — the game owns the corpse (a lootable body, a pooled enemy).
        Leave,
    }

    /// The single answer to "may this character act right now?", plus the body's fate once it cannot.
    ///
    /// This exists because the alternative is every mover, weapon and animator asking the question its own
    /// way, and them disagreeing. Real symptoms that came from exactly that: corpses that kept walking
    /// because the mover never asked whether it was dead; a hurt animation that never showed because the
    /// walk cycle re-issued itself a frame later; and a death animation nobody ever saw because the spawner
    /// destroyed the object the instant health hit zero. One flag, asked by everyone, fixes all three.
    ///
    /// Attached by <see cref="ZoeSpawner.SpawnCharacter"/>, so every Zoe has one and a consumer can rely on
    /// it being there.
    [RequireComponent(typeof(Health))]
    public class ZoeState : MonoBehaviour
    {
        Health _health;
        ReactionFxPlayer _reactions;
        float _stunnedUntil;
        bool _disposing;

        /// Seconds of stun applied on each hit. Set from the Zoe's hit reaction at spawn.
        public float hitStun;

        public DeathDisposal disposal = DeathDisposal.WhenDeathClipEnds;
        public float deathLinger = 1.5f;

        public bool IsDead => _health != null && _health.IsDead;
        public bool IsStunned => Time.time < _stunnedUntil;

        /// THE gate. Movement, firing and locomotion all check this — a dead or reeling character does
        /// neither, and nothing has to reason about which of those two it is.
        public bool CanAct => !IsDead && !IsStunned;

        void Awake()
        {
            _health = GetComponent<Health>();
            _reactions = GetComponent<ReactionFxPlayer>();
        }

        void OnEnable()
        {
            if (_health != null) { _health.Damaged += OnDamaged; _health.Died += OnDied; }
            if (_reactions != null) _reactions.DeathFinished += OnDeathClipFinished;
        }

        void OnDisable()
        {
            if (_health != null) { _health.Damaged -= OnDamaged; _health.Died -= OnDied; }
            if (_reactions != null) _reactions.DeathFinished -= OnDeathClipFinished;
        }

        void OnDamaged(DamageInfo _)
        {
            // Stun on EVERY hit, including the killing one — the recoil reads the same either way, and a
            // character that keeps advancing through the frame it dies on looks unhittable.
            if (hitStun > 0f) _stunnedUntil = Mathf.Max(_stunnedUntil, Time.time + hitStun);
        }

        void OnDied(DamageInfo _)
        {
            // Colliders off immediately: a corpse mid-death-animation must not still be shootable, or the
            // player wastes shots on something already dead and it reads as the gun not registering.
            foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;

            switch (disposal)
            {
                case DeathDisposal.Immediate: Dispose(); break;
                case DeathDisposal.AfterDelay: Invoke(nameof(Dispose), Mathf.Max(0f, deathLinger)); break;
                case DeathDisposal.Leave: break;
                case DeathDisposal.WhenDeathClipEnds:
                    // If there is no death clip, DeathFinished never fires — fall back to the delay rather
                    // than leaving the body standing there forever.
                    if (_reactions == null || string.IsNullOrEmpty(_reactions.def?.death?.clip))
                        Invoke(nameof(Dispose), Mathf.Max(0f, deathLinger));
                    break;
            }
        }

        void OnDeathClipFinished()
        {
            if (disposal == DeathDisposal.WhenDeathClipEnds) Dispose();
        }

        void Dispose()
        {
            if (_disposing) return;
            _disposing = true;
            Destroy(gameObject);
        }
    }
}

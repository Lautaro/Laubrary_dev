using System.Collections.Generic;
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
        bool _reactionsSubscribed;
        float _stunnedUntil;
        bool _disposing;

        /// Resolved LAZILY, never cached at Awake. ZoeSpawner happens to add ReactionFxPlayer before this
        /// component, but nothing guarantees that in general: MirageSubject.EnsureTargetPractice adds a
        /// ReactionFxPlayer to an already-placed GameObject that may already carry a ZoeState, and an
        /// Awake-time GetComponent found nothing, cached the null forever, and so never subscribed to
        /// DeathFinished or consulted DeathClipArmed — the body then either vanished mid-death-animation or
        /// never vanished at all. Exactly the spawn-ordering trap LocomotionAnimator.State and
        /// ReactionFxPlayer.Arbiter already dodge the same way.
        ReactionFxPlayer Reactions => _reactions != null ? _reactions : (_reactions = GetComponent<ReactionFxPlayer>());

        /// Are we actually waiting on a death clip that really started? Only then may a DeathFinished signal
        /// dispose the body. Without this the no-clip case disposed instantly: ReactionFxPlayer announces
        /// "death finished" synchronously when there is nothing to play, and that arrives BEFORE this
        /// component's own death handler has even run, so the linger below was scheduled onto an object that
        /// had already been destroyed.
        bool _awaitingDeathClip;

        /// Exactly the colliders THIS component switched off on death, so a revival turns those back on and
        /// nothing else. "Re-enable everything" would switch on colliders some other system deliberately
        /// disabled (an unequipped weapon slot, a projectile template) and is order-dependent on weapon
        /// switching; recording is neither.
        readonly List<Collider2D> _disabledOnDeath = new();

        /// Seconds of stun applied on each hit, ON TOP of whatever the hit reaction itself asks for.
        ///
        /// ZoeSpawner used to copy `def.hit.stunSeconds` in here at spawn and never look again, which made the
        /// authored value a snapshot: editing it on a live character changed nothing, and the stun on a death
        /// or a named state was read by nothing at all. <see cref="ReactionFxPlayer"/> now calls
        /// <see cref="ApplyStun"/> from whichever reaction actually played, so this field is left for a game
        /// that wants a flat per-character stun of its own. Zero by default, so nothing sets it twice.
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
        }

        void OnEnable()
        {
            if (_health != null) { _health.Damaged += OnDamaged; _health.Died += OnDied; _health.Revived += OnRevived; }
            EnsureReactionsSubscribed();
        }

        void OnDisable()
        {
            if (_health != null) { _health.Damaged -= OnDamaged; _health.Died -= OnDied; _health.Revived -= OnRevived; }
            if (_reactionsSubscribed && _reactions != null) _reactions.DeathFinished -= OnDeathClipFinished;
            _reactionsSubscribed = false;
        }

        /// Subscribe to the reaction player's DeathFinished, retrying on every death until it succeeds. Tried
        /// again (rather than only at OnEnable) because the component may legitimately arrive later than this
        /// one — see the Reactions property. Idempotent: `_reactionsSubscribed` keeps a retry from
        /// double-subscribing, which would dispose the body twice.
        void EnsureReactionsSubscribed()
        {
            if (_reactionsSubscribed) return;
            var r = Reactions;
            if (r == null) return;
            r.DeathFinished += OnDeathClipFinished;
            _reactionsSubscribed = true;
        }

        /// Stun this character for `seconds`, EXTENDING an existing stun rather than replacing it — the longer
        /// of the two wins, so a short flinch landing on top of a long one cannot cut the long one short.
        /// Non-positive input is a no-op, which is what lets every reaction call it unconditionally without
        /// each one having to check whether its own `stunSeconds` was authored.
        ///
        /// This is THE way a stun is applied. It is public because the thing that knows how long a stun lasts
        /// is the reaction that just played, and that lives on <see cref="ReactionFxPlayer"/>, not here.
        public void ApplyStun(float seconds)
        {
            if (seconds > 0f) _stunnedUntil = Mathf.Max(_stunnedUntil, Time.time + seconds);
        }

        void OnDamaged(DamageInfo _)
        {
            // Stun on EVERY hit, including the killing one — the recoil reads the same either way, and a
            // character that keeps advancing through the frame it dies on looks unhittable. This is the flat
            // per-character stun only; the hit REACTION's own stunSeconds is applied by ReactionFxPlayer, from
            // the reaction that actually played.
            ApplyStun(hitStun);
        }

        void OnDied(DamageInfo _)
        {
            // Last chance to hook up: a ReactionFxPlayer added after this component's OnEnable is still the
            // one that will announce the death clip's end, and a missed subscription here is a body that
            // vanishes mid-animation or never vanishes at all.
            EnsureReactionsSubscribed();

            // Colliders off immediately: a corpse mid-death-animation must not still be shootable, or the
            // player wastes shots on something already dead and it reads as the gun not registering.
            // Recorded as we go so OnRevived can put back exactly this set (see _disabledOnDeath).
            // includeInactive stays at its default false on purpose: an inactive weapon slot, and the
            // inactive projectile TEMPLATE that ZoeSpawner parents under the character, are not part of the
            // body's hit detection and must not be switched on by a later revival.
            _disabledOnDeath.Clear();
            foreach (var c in GetComponentsInChildren<Collider2D>())
            {
                if (c == null || !c.enabled) continue;
                c.enabled = false;
                _disabledOnDeath.Add(c);
            }

            switch (disposal)
            {
                case DeathDisposal.Immediate: Dispose(); break;
                case DeathDisposal.Leave: break;

                // BOTH clip-aware policies ask the same question first, because both of them promise the death
                // clip gets to play. Ask whether a death clip ACTUALLY started, not whether one was configured:
                // a clip name the view doesn't know arms nothing, and asking the data (as the old
                // string.IsNullOrEmpty check did) says "yes there's a clip" for it — which left the body
                // standing there forever waiting on a finish signal that could never come.
                // This runs AFTER ReactionFxPlayer's own death handler (ZoeSpawner adds it first, and
                // Health.Died is multicast in subscription order), so the answer is already settled.
                case DeathDisposal.WhenDeathClipEnds:
                case DeathDisposal.AfterDelay:
                    _awaitingDeathClip = Reactions != null && Reactions.DeathClipArmed;
                    // No clip playing = nothing to wait for, so the fate starts NOW: WhenDeathClipEnds has
                    // nothing left to happen, and AfterDelay starts its linger immediately. (Both land on the
                    // same call because "clip ended" and "clip never started" are the same instant here.)
                    if (!_awaitingDeathClip) Invoke(nameof(Dispose), Mathf.Max(0f, deathLinger));
                    break;
            }
        }

        // `interrupted` is accepted but not acted on: death is the top of the priority ladder and should never
        // be preempted, but if something does preempt it the body must still be cleaned up rather than becoming
        // immortal — so the disposal policy applies either way.
        void OnDeathClipFinished(bool interrupted)
        {
            if (!_awaitingDeathClip) return;   // only ever set by the two clip-aware policies, so this is the gate
            _awaitingDeathClip = false;

            // AfterDelay's own definition is "play the death clip, THEN lie there for deathLinger seconds", and
            // it used to start that timer at the MOMENT OF DEATH instead — so a 2s death animation with a 1.5s
            // linger disposed the body half a second before its own animation finished, and the linger was not
            // a linger at all but an overlap. Now the clip ends first and the timer starts here, which is what
            // the enum has always said and what task 5 of ZOE_PALETTE_BUILD_PLAN.md asks for.
            if (disposal == DeathDisposal.AfterDelay) Invoke(nameof(Dispose), Mathf.Max(0f, deathLinger));
            else Dispose();
        }

        /// Undo everything death did, so a pooled/respawning character is reusable. Every line here is load
        /// bearing: without the CancelInvoke a respawn is destroyed by the still-pending linger timer a
        /// second or two later, which looks exactly like the respawn "not working".
        void OnRevived()
        {
            CancelInvoke(nameof(Dispose));
            _disposing = false;
            _awaitingDeathClip = false;
            _stunnedUntil = 0f;   // a corpse revived mid-stun must be able to act again immediately

            foreach (var c in _disabledOnDeath)
                if (c != null) c.enabled = true;
            _disabledOnDeath.Clear();
        }

        void Dispose()
        {
            if (_disposing) return;
            _disposing = true;
            Destroy(gameObject);
        }
    }
}

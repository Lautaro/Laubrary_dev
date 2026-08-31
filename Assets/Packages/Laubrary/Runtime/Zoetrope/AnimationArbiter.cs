using System;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Owns <see cref="IAnimatedView.PlayClip"/> for one character (or, once composite bodies wire per-part
    /// arbiters, one part). Locomotion, events, and anything else that wants the body to show something
    /// SUBMITS a claim instead of calling the view directly — highest priority wins; a claim from the SAME
    /// owner at the SAME priority reasserts without restarting the clip, which is what lets Locomotion call
    /// this every frame without retriggering.
    ///
    /// Replaced <c>LocomotionAnimator.SuppressFor</c> (now deleted), a two-party special case that only knew
    /// about locomotion vs a reaction — a dash, a jump or an aim pose had nowhere to plug in.
    /// <see cref="ZoeState"/>.<c>CanAct</c> stays as the gameplay gate ("may this character act"); it is no
    /// longer the animation arbiter, and both still apply.
    /// </summary>
    public class AnimationArbiter : MonoBehaviour
    {
        // ── the priority ladder ───────────────────────────────────────────────
        // ONE authority for "what outranks what", so the numbers cannot drift apart across the files that
        // claim. Stated explicitly rather than left to each claimant's own constant, because "higher wins"
        // is only a rule if everyone agrees what the numbers are.

        /// Steady-state movement (LocomotionAnimator / MotionPoseAnimator). The floor: anything else outranks it.
        public const float PriorityLocomotion = 0f;
        /// A named event raised on the character (teleport, taunt, spawn) — outranks movement, yields to being hurt.
        public const float PriorityNamedState = 100f;
        /// Reacting to a hit. Outranks a named state: what just happened TO you beats what you were doing.
        public const float PriorityHurt = 200f;
        /// Dying. Top of the ladder, and deliberately far above it — nothing preempts a death.
        public const float PriorityDeath = 1000f;

        IAnimatedView _view;
        object _owner;
        float _priority;
        float _expiresAt = float.PositiveInfinity;
        string _clip;
        bool _loop;
        Action _onInterrupted;

        IAnimatedView View => _view != null ? _view : (_view = GetComponent<IAnimatedView>());

        /// Fires when the arbiter becomes free — no active claim, whether from expiry or an explicit
        /// <see cref="Release"/>. A steady-state owner (Locomotion) listens and resubmits its own current clip;
        /// this is what "reasserts whichever claim is next" reduces to when there is only ever one steady
        /// claimant and everything else is a transient interruption.
        public event Action Reassert;

        public bool HasControl(object owner) => ReferenceEquals(_owner, owner);

        /// Would a claim by <paramref name="owner"/> at <paramref name="priority"/> be allowed in (ignoring
        /// whether the view knows the clip)? Exists so a claimant can find out it is outranked BEFORE tearing
        /// down whatever it was doing — asking by attempting the claim means a refusal still costs the
        /// incumbent its bookkeeping, which is the difference between "nothing happened" and "the reaction
        /// that was playing quietly lost its remaining effects".
        public bool WouldAccept(object owner, float priority) =>
            owner != null && !(HasActiveClaim && !ReferenceEquals(_owner, owner) && priority <= _priority);

        /// Is there a claim that has NOT yet run out? Checked here rather than only in <see cref="Update"/>,
        /// so a claim that expired earlier this frame stops blocking immediately instead of at the next tick.
        bool HasActiveClaim => _owner != null && Time.time < _expiresAt;

        /// <summary>Attempt to play <paramref name="clip"/> as <paramref name="owner"/> at <paramref name="priority"/>.
        /// Refused (returns false, nothing played) if a DIFFERENT owner holds an active claim at the same or a
        /// higher priority — equal priority does NOT steal, so a reaction already running is not cut off by the
        /// next thing that happens to claim at its level.
        /// <paramref name="durationSeconds"/> &lt;= 0 means "until released or superseded" (a looping idle/move
        /// clip); &gt; 0 auto-expires the claim, after which <see cref="Reassert"/> fires.
        /// <paramref name="onInterrupted"/> is called if a DIFFERENT owner later takes the claim away, which is
        /// how a cut-short reaction gets to run its own teardown instead of being silently dropped.</summary>
        public bool Play(object owner, float priority, string clip, bool loop, float durationSeconds = 0f,
                         Action onComplete = null, Action onInterrupted = null)
        {
            if (owner == null) return false;

            bool sameOwner = ReferenceEquals(_owner, owner);
            bool active = HasActiveClaim;

            // Strict-greater preemption. `<=` (not `<`) is the whole point: an equal-priority newcomer used to
            // take control from a reaction that was still playing.
            if (active && !sameOwner && priority <= _priority) return false;

            // Same owner asking for exactly what it already has: reassert without restarting the clip. This is
            // what lets a steady-state claimant call every frame without the body twitching on frame 0 forever.
            if (sameOwner && active && priority == _priority && _clip == clip && _loop == loop) return true;

            // Snapshot, so a claim that turns out not to play can be rolled back. Committing the claim before
            // knowing whether the view accepted the clip let anyone lock the arbiter on nothing (an unknown
            // clip name, or a view that doesn't exist at all) and never give it back.
            var prevOwner = _owner;
            float prevPriority = _priority;
            float prevExpiresAt = _expiresAt;
            string prevClip = _clip;
            bool prevLoop = _loop;
            var prevInterrupted = _onInterrupted;

            _owner = owner;
            _priority = priority;
            _clip = clip;
            _loop = loop;
            _expiresAt = durationSeconds > 0f ? Time.time + durationSeconds : float.PositiveInfinity;
            _onInterrupted = onInterrupted;

            if (!Dispatch(clip, loop, priority, onComplete))
            {
                _owner = prevOwner;
                _priority = prevPriority;
                _expiresAt = prevExpiresAt;
                _clip = prevClip;
                _loop = prevLoop;
                _onInterrupted = prevInterrupted;
                return false;
            }

            // Only now that the claim is definitely ours: tell whoever we displaced. Notified AFTER the state
            // has moved (and reading a local, not the field) so a callback that itself claims or releases sees
            // the new truth rather than re-entering into the old one.
            if (!sameOwner && prevOwner != null) prevInterrupted?.Invoke();
            return true;
        }

        /// <summary>Re-play the clip this owner ALREADY holds, from its start, without re-submitting a claim.
        /// Returns false (and plays nothing) if <paramref name="owner"/> is not the current holder.
        ///
        /// This is what a multi-pass event (a reaction authored to loop N times, or to last a fixed number of
        /// seconds longer than its clip) needs for its second and later passes. It cannot use
        /// <see cref="Play"/>: the same owner re-submitting the same clip at the same priority hits the
        /// "reassert without restarting" rule above and silently plays NOTHING, which is the correct answer
        /// for a steady-state claimant calling every frame and exactly the wrong one here. Going straight to
        /// the view instead would work for a single-player view and break a composite one, whose parts each
        /// arbitrate on their own and would never hear about it — so the re-play routes through the same
        /// dispatch the first pass used, carrying the same priority.</summary>
        public bool Replay(object owner, Action onComplete = null)
        {
            if (!ReferenceEquals(_owner, owner) || string.IsNullOrEmpty(_clip)) return false;
            return Dispatch(_clip, _loop, _priority, onComplete);
        }

        /// One place every actual "put this on screen" call goes through, so the composite case can never be
        /// forgotten at one of the call sites. A view that fans a claim out to independently-arbitrated
        /// sub-views (a composite body's parts) needs the PRIORITY, not just the clip name — see
        /// <see cref="IArbitratedView"/> for why calling the parts' views directly re-opens the exact stomping
        /// bug this class exists to close.
        bool Dispatch(string clip, bool loop, float priority, Action onComplete)
        {
            var view = View;
            if (view == null) return false;
            if (view is IArbitratedView fanned) return fanned.PlayClipArbitrated(clip, loop, priority, onComplete);
            return view.PlayClip(clip, loop, onComplete);
        }

        /// Give up a claim early (an event ending before its authored duration, a dash cut short). No-op if
        /// <paramref name="owner"/> doesn't currently hold the claim — never lets a stale reference steal
        /// control back from whoever holds it now. Does NOT call the owner's own onInterrupted: giving up
        /// voluntarily is not being interrupted.
        public void Release(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            ClearClaim();
            Reassert?.Invoke();
        }

        void ClearClaim()
        {
            // Hand the sub-views back FIRST, while the fan-out this claim created is still the live one. A
            // composite body's per-part claims outlive the root claim otherwise, and since nothing can outrank
            // a claim nobody gave back, every part stays frozen on the last frame of whatever was playing.
            // Null-conditional on the interface, not on View: a single-player view simply isn't one of these.
            (View as IArbitratedView)?.ReleaseFannedClaims();

            _owner = null;
            _priority = 0f;
            _expiresAt = float.PositiveInfinity;
            _clip = null;
            _loop = false;
            _onInterrupted = null;
        }

        void Update()
        {
            // Running out of authored time is the claim COMPLETING, not being interrupted, so onInterrupted
            // deliberately does not fire here — only Reassert does.
            if (_owner != null && Time.time >= _expiresAt)
            {
                ClearClaim();
                Reassert?.Invoke();
            }
        }
    }
}

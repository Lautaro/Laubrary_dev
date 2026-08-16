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
    /// Replaces <see cref="ZoeState"/>.CanAct + the old <c>LocomotionAnimator.SuppressFor</c>, a two-party
    /// special case that only knew about locomotion vs a reaction — a dash, a jump or an aim pose had nowhere
    /// to plug in. <c>ZoeState.CanAct</c> stays as the gameplay gate ("may this character act"); it is no
    /// longer the animation arbiter.
    /// </summary>
    public class AnimationArbiter : MonoBehaviour
    {
        IAnimatedView _view;
        object _owner;
        float _priority;
        float _expiresAt = float.PositiveInfinity;

        IAnimatedView View => _view != null ? _view : (_view = GetComponent<IAnimatedView>());

        /// Fires when the arbiter becomes free — no active claim, whether from expiry or an explicit
        /// <see cref="Release"/>. A steady-state owner (Locomotion) listens and resubmits its own current clip;
        /// this is what "reasserts whichever claim is next" reduces to when there is only ever one steady
        /// claimant and everything else is a transient interruption.
        public event Action Reassert;

        public bool HasControl(object owner) => ReferenceEquals(_owner, owner);

        /// <summary>Attempt to play <paramref name="clip"/> as <paramref name="owner"/> at <paramref name="priority"/>.
        /// Refused (returns false, nothing played) if a DIFFERENT owner currently holds a higher priority.
        /// <paramref name="durationSeconds"/> &lt;= 0 means "until released or superseded" (a looping idle/move
        /// clip); &gt; 0 auto-expires the claim, after which <see cref="Reassert"/> fires.</summary>
        public bool Play(object owner, float priority, string clip, bool loop, float durationSeconds = 0f, Action onComplete = null)
        {
            if (_owner != null && !ReferenceEquals(_owner, owner) && priority < _priority) return false;

            _owner = owner;
            _priority = priority;
            _expiresAt = durationSeconds > 0f ? Time.time + durationSeconds : float.PositiveInfinity;

            return View != null && View.PlayClip(clip, loop, onComplete);
        }

        /// Give up a claim early (an event ending before its authored duration, a dash cut short). No-op if
        /// <paramref name="owner"/> doesn't currently hold the claim — never lets a stale reference steal
        /// control back from whoever holds it now.
        public void Release(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            _owner = null;
            _priority = 0f;
            _expiresAt = float.PositiveInfinity;
            Reassert?.Invoke();
        }

        void Update()
        {
            if (_owner != null && Time.time >= _expiresAt)
            {
                _owner = null;
                _priority = 0f;
                _expiresAt = float.PositiveInfinity;
                Reassert?.Invoke();
            }
        }
    }
}

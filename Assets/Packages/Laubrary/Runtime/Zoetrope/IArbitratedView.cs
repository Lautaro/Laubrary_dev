using System;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Optional capability a view MAY provide (discovered by <see cref="AnimationArbiter"/> via a type test,
    /// same pattern as <see cref="IZonedView"/>/<see cref="IFlippableView"/>) — "I am not one player, I am
    /// several SUB-VIEWS that each arbitrate on their own, so hand me the claim's PRIORITY and I will fan it
    /// out."
    ///
    /// This exists for the composite body. A composite Zoe's root has no single player to drive: the real
    /// players live one per body part, each with its own <see cref="AnimationArbiter"/> that a part's
    /// locomotion claims every time its direction changes. Playing a whole-body reaction by calling the parts'
    /// views DIRECTLY would go around those per-part arbiters — the walk cycle would take the part back on its
    /// very next change and stomp the reaction, which is precisely the bug the arbiter exists to prevent. So
    /// the root arbiter hands the priority down and each part arbitrates the claim itself; a part whose
    /// locomotion is outranked yields, and a part that does not know the clip simply keeps walking.
    ///
    /// A view with ONE player (<c>AnimatedViewRelay</c>) does not implement this — there is nothing to fan out
    /// to, and the arbiter's plain <see cref="IAnimatedView.PlayClip"/> path is exactly right for it.
    ///
    /// This is also the seam per-part reaction targeting plugs into (ZOE_PALETTE_BUILD_PLAN.md task 6, "which
    /// part of the character a state speaks for"): the fan-out already selects a SUBSET of parts, so naming
    /// that subset is a parameter, not a new mechanism.
    /// </summary>
    public interface IArbitratedView
    {
        /// <summary>Play <paramref name="clip"/> on every sub-view that knows it, each sub-view claiming at
        /// <paramref name="priority"/> through its OWN arbiter. Returns whether at least one sub-view started
        /// — which is what the calling arbiter treats as "the view accepted the clip".
        /// <paramref name="onComplete"/> fires ONCE, when the last still-running sub-view finishes (or is
        /// itself preempted, so a preempted part can never leave the whole reaction hanging forever waiting
        /// on a signal that is no longer coming). Ignored when looping, matching
        /// <see cref="IAnimatedView.PlayClip"/>.</summary>
        bool PlayClipArbitrated(string clip, bool loop, float priority, Action onComplete);

        /// <summary>The root claim is over — hand every sub-view back to whatever was driving it before.
        /// Called by <see cref="AnimationArbiter"/> when its own claim is released or expires. Without it the
        /// sub-claims outlive the root claim and each part stays frozen on the reaction's last frame, since
        /// nothing else can outrank a claim nobody gave back.</summary>
        void ReleaseFannedClaims();
    }
}

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A Zoe-event effect that plays a named clip on the target Zoe via its <see cref="IAnimatedView"/> — the
    /// Play-Reel entry of the Zoe-event effect palette (ZOE_EVENTS_DESIGN.md step 3), exposing what was previously
    /// only the top-level convenience <see cref="ReactionFx.clip"/> as a first-class, list-orderable effect. Lives
    /// in Zoetrope CORE (it needs only the view abstraction, no presentation module). No-ops silently for a plain
    /// <c>SpriteView</c> Zoe (no <see cref="IAnimatedView"/>), exactly like <see cref="ReactionFx.clip"/> does —
    /// nothing to play is not an error.
    /// </summary>
    [System.Serializable]
    public class PlayReelEffect : IEffect
    {
        [UnityEngine.Tooltip("The clip to play on the Zoe's animated view. Empty = nothing to play.")]
        public string clip = "";
        [UnityEngine.Tooltip("Loop the clip instead of playing it once.")]
        public bool loop = false;

        public bool IsEmpty => string.IsNullOrEmpty(clip);

        public void Apply(EventContext ctx)
        {
            if (string.IsNullOrEmpty(clip)) return;
            ctx.View?.PlayClip(clip, loop);
        }
    }

    // DEFERRED (design step 5): a StunEffect (a core Zoetrope IEffect) slots in right here alongside Pushback /
    // Play-Reel — a timed effect that puts the Zoe's brain/AI into a stun state for X seconds — once the AI/brain
    // exposes a stun state for an EventContext accessor to drive. Not buildable yet (no such state exists); see
    // ZOE_EVENTS_DESIGN.md "Stun — DEFERRED".
}

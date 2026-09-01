namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A Zoe-event effect that plays a named clip on the target Zoe via its <see cref="IAnimatedView"/> — the
    /// Play-Lauminary entry of the Zoe-event effect palette (ZOE_EVENTS_DESIGN.md step 3), exposing what was previously
    /// only the top-level convenience <see cref="ReactionFx.clip"/> as a first-class, list-orderable effect. Lives
    /// in Zoetrope CORE (it needs only the view abstraction, no presentation module). No-ops silently for a plain
    /// <c>SpriteView</c> Zoe (no <see cref="IAnimatedView"/>), exactly like <see cref="ReactionFx.clip"/> does —
    /// nothing to play is not an error.
    ///
    /// <para>⚠️ This plays a BARE CLIP and nothing else: no duration model, no body flash, no effect list of
    /// its own. Those live on a state CARD, and the way to get them is to ask the character for a named state
    /// (<see cref="ReactionFxPlayer.Raise(string)"/>), which resolves the whole card. A clip name is the one
    /// part of a look that can be named on its own, which is exactly why reducing a look to one is such an
    /// easy mistake to make — the Zoe window's own cue Raise tooltip says the same thing from the other side
    /// ("the WHOLE reaction … which the Effect slot below cannot express on its own").</para>
    /// </summary>
    [System.Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, null, "PlayReelEffect")]
    public class PlayLauminationEffect : IEffect
    {
        [UnityEngine.Tooltip("The clip to play on the Zoe's animated view — just the animation, with no " +
                             "duration, no body flash and no effects of its own. For a whole look, declare it " +
                             "under Custom events and raise it by name instead. Empty = nothing to play.")]
        public string clip = "";
        [UnityEngine.Tooltip("Loop the clip instead of playing it once.")]
        public bool loop = false;

        public bool IsEmpty => string.IsNullOrEmpty(clip);

        public void Apply(EventContext ctx)
        {
            if (string.IsNullOrEmpty(clip)) return;

            // Route through the character's AnimationArbiter, never straight at the view. An effect card that
            // writes to the view directly is a second, unpoliced channel onto the body — it would happily
            // paint over a death animation, and on a COMPOSITE character it would go around the per-part
            // arbiters entirely, so the walk cycle would take the part back on its next direction change.
            //
            // It claims at PriorityNamedState because that is honestly what this is: something the character
            // was ASKED to show. It therefore outranks locomotion and is refused while a hurt or a death holds
            // the body — which is correct, not a limitation: a reaction that owns the body is already showing
            // its own clip, and an effect on that same reaction's list fighting it would be authoring at odds
            // with itself. A refusal plays nothing and deliberately does NOT fall through to the raw view.
            //
            // The claim is anonymous (a fresh token, with nobody left holding a reference), so it must never be
            // one nothing can outrank: at 100 a hurt, a death or the next named state all take it back, and a
            // non-looping clip additionally auto-expires after its own length so locomotion resumes on its own.
            var arbiter = ctx.Transform != null ? ctx.Transform.GetComponent<AnimationArbiter>() : null;
            if (arbiter == null) { if (ctx.View != null) ctx.View.PlayClip(clip, loop); return; }

            float secs = !loop && ctx.View != null ? ctx.View.GetClipSeconds(clip) : 0f;
            arbiter.Play(new object(), AnimationArbiter.PriorityNamedState, clip, loop, durationSeconds: secs);
        }
    }

    // DEFERRED (design step 5): a StunEffect (a core Zoetrope IEffect) slots in right here alongside Pushback /
    // Play-Lauminary — a timed effect that puts the Zoe's brain/AI into a stun state for X seconds — once the AI/brain
    // exposes a stun state for an EventContext accessor to drive. Not buildable yet (no such state exists); see
    // ZOE_EVENTS_DESIGN.md "Stun — DEFERRED".
}

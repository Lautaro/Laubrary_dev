using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// How a Body-SpriteFx attachment plays its stack against the EVENT that fired it. This lives on the
    /// ATTACHMENT (the Zoe event's effect entry), NOT the stack asset — same stack, different bindings: one
    /// authored flicker can loop over a stun, play once on a hit, and fade a death out. The timed modes need
    /// the event's duration (the reaction clip's length, via <see cref="IAnimatedView.GetClipSeconds"/>,
    /// stamped into <see cref="EventContext.EventSecondsRemaining"/>); when it is unknown — no clip, a plain
    /// SpriteView, or a zoned strip with no fixed end — each mode degrades to a single play.
    /// </summary>
    public enum FxPlaybackMode
    {
        /// One play-through of the stack's own duration — the original behaviour. Deliberately value 0 so
        /// every PRE-EXISTING serialized entry (which never wrote this field) keeps doing exactly what it did;
        /// the editor's Add-effect menu starts NEW cards on <see cref="Loop"/> (the design default).
        Once = 0,
        /// Repeat whole passes back-to-back for the event's remaining duration, ending WITH the event.
        Loop = 1,
        /// Start late so the play ENDS exactly as the event does — a fade-out. The window is
        /// <see cref="BodySpriteFxEffect.fxSeconds"/> (0 = the stack's own duration); an event shorter than
        /// the window compresses the play to fit, so the fade still lands on the event's end.
        RunAtEnd = 2,
        /// Once forward at the event's start, once BACKWARD timed to end with the event — flash in, mirror out.
        PingPong = 3,
        /// One play-through run BACKWARD (life 1→0). The same stack that materialises a character therefore
        /// dematerialises it, from a second card on the departure event — no mirrored copy of the recipe, and
        /// no reversal of the effect ORDER (which would change the effect, not mirror it). The hashing grain
        /// and the sprite underneath keep running forwards; see <see cref="SpriteFxFilter.PlayReversed()"/>.
        OnceReversed = 4,
    }

    /// <summary>
    /// A Zoe-event effect that applies a <see cref="SpriteFxSpec"/> (a "SpriteFx Stack") to the Zoe's OWN body
    /// renderer — a hurt/death flash, tint or dissolve that RIDES the live animation via a
    /// <see cref="SpriteFxFilter"/> on the body's <see cref="SpriteRenderer"/>. This is the same behaviour as the
    /// top-level <see cref="ReactionFx.bodyFx"/> slot (KEPT and still fired by
    /// <see cref="ReactionFxPlayer.PlayBodyFx"/>; this effect is purely additive), now promoted to a first-class,
    /// list-orderable palette effect (ZOE_EVENTS_DESIGN.md step 3). Lives in Zoetrope CORE: Zoetrope depends DOWN
    /// on SpriteFx (a legal downward asmdef dependency — SpriteFx never references Zoetrope), the very edge
    /// <see cref="ReactionFxPlayer.PlayBodyFx"/> already uses.
    ///
    /// The <see cref="playback"/> binding decides how the stack plays against the event's timeline; the filter
    /// primitives it drives (<see cref="SpriteFxFilter.PlayLooping"/> / <see cref="SpriteFxFilter.SchedulePlay"/>)
    /// stay policy-free in SpriteFx, so the event-vocabulary lives here where the event is.
    /// </summary>
    [System.Serializable]
    public class BodySpriteFxEffect : IEffect
    {
        [Tooltip("The SpriteFx Stack applied to the Zoe's own body sprite when this effect fires — a hurt/" +
                 "death flash, tint or dissolve riding on top of the live animation (via a SpriteFxFilter added to " +
                 "the body renderer). Empty = nothing.")]
        public SpriteFxSpec stack;

        [Tooltip("How the stack plays against this event: Once = a single play-through; Loop = repeat for the " +
                 "event's remaining duration; Run At End = start so it finishes exactly as the event ends (a " +
                 "fade-out); Ping Pong = once forward now, once backward timed to the end; Once Reversed = a " +
                 "single play-through run backwards, so one authored stack covers both an arrival and a " +
                 "departure. Timed modes degrade to a single play when the event's duration is unknown (no " +
                 "clip, or a clip with no fixed end).")]
        public FxPlaybackMode playback = FxPlaybackMode.Once;

        [Tooltip("Run At End only: the fade-out window in seconds — the stack starts when the event has this " +
                 "much time left. 0 = use the stack's own duration.")]
        [Min(0f)] public float fxSeconds = 0f;

        public bool IsEmpty => stack == null;

        public void Apply(EventContext ctx)
        {
            if (stack == null) return;
            var sr = ctx.Renderer;
            if (sr == null) return;
            var filter = sr.GetComponent<SpriteFxFilter>();
            if (filter == null) filter = sr.gameObject.AddComponent<SpriteFxFilter>();
            filter.stack = stack;
            // Same resolver ReactionFxPlayer.PlayBodyFx wires for the legacy slot this effect promotes: without
            // it, a stack whose modifier reads an External/MetaLayer position (e.g. RelightModifier "Follow" a
            // painted muzzle) silently falls back to a fixed spot the instant it's authored as a list card instead.
            filter.externalPositionResolver = new ZoeMetaPositionResolver(ctx.View, sr);

            float remaining = ctx.EventSecondsRemaining;   // 0 = unknown → every timed mode degrades to a single play

            // WHO OWNS THE TIMEBASE. A stack is a SHAPE over normalized life, not a schedule — the same
            // reason it never references a visual. Its parameters run 0→1 and mean nothing in seconds, so
            // the host says how long that 0→1 takes:
            //
            //   1. the event's CLIP, when it has one — a flash tied to a death animation should last exactly
            //      as long as the death animation, and re-timing the clip should re-time the flash with it;
            //   2. else fxSeconds on this effect — a static sprite has no length, so the event states one;
            //   3. else the stack's own duration, as a last-resort default for a stack played from nowhere.
            //
            // The stack used to win outright, which made every effect a fixed 0.15s regardless of what it
            // was riding: authoring a slower death animation silently left the flash finishing early.
            float passDur = remaining > 0f ? remaining
                          : fxSeconds > 0f ? fxSeconds
                          : Mathf.Max(0.001f, stack.duration);

            switch (playback)
            {
                default:
                case FxPlaybackMode.Once:
                    filter.Play(passDur);   // the host's timebase, not the stack's
                    break;

                case FxPlaybackMode.Loop:
                    if (remaining > 0f) filter.PlayLooping(remaining);
                    else filter.Play();   // unknown event length — degrade to a single play (see the mode's tooltip)
                    break;

                case FxPlaybackMode.RunAtEnd:
                {
                    if (remaining <= 0f) { filter.Play(); break; }   // unknown event length — degrade to Once
                    float window = fxSeconds > 0f ? fxSeconds : passDur;
                    if (remaining <= window)
                    {
                        // The event is already inside the window — compress the whole play into what's left,
                        // so the fade still completes its curve exactly on the event's end.
                        filter.Play(remaining);
                    }
                    else
                    {
                        filter.SchedulePlay(remaining - window, window);
                    }
                    break;
                }

                case FxPlaybackMode.OnceReversed:
                    filter.PlayReversed(passDur);
                    break;

                case FxPlaybackMode.PingPong:
                    filter.Play(passDur);   // forward pass, now
                    // The backward pass is timed to END with the event, but never starts before the forward
                    // pass finishes; with no known event length it follows the forward pass immediately (a
                    // there-and-back pulse).
                    filter.SchedulePlay(remaining > 0f ? Mathf.Max(passDur, remaining - passDur) : passDur,
                                        passDur, reversed: true);
                    break;
            }
        }
    }
}

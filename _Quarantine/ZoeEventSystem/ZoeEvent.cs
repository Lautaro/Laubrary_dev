using System.Collections.Generic;
using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.Zoetrope
{
    // FxTriggerType, FxPlacementType, FxEntry, and EventDurationMode are shared with the still-live
    // ReactionFx system (ReactionFx.cs) — declared there, reused here as-is rather than duplicated.

    /// <summary>ONE thing a Zoe can do: a named bundle of {duration, visual, body treatment, effects,
    /// consequences}. Hit, Hurt, Death, a teleport and a taunt are all this same class — there are no special
    /// fields for the "built-in" ones any more.
    ///
    /// <para><b>Why uniform.</b> The specialness used to be attached to the SLOT (<c>Zoe.hit</c> /
    /// <c>Zoe.death</c>), not to the event: the player read <c>def.death</c>, and THAT is what gated
    /// <see cref="ZoeState.CanAct"/>, drove disposal, and fired the finished-event a respawn waits on. So a
    /// second death authored in the custom list played its animation and did not actually kill — no disposal,
    /// CanAct never cleared, respawn never fired. It looked authored and was silently broken. Same for hits:
    /// only the event in the <c>hit</c> field granted invulnerability. Multiple deaths, and hits that differ
    /// by weapon, are simply not expressible with fixed fields. Now the specialness travels WITH the event via
    /// the consequences below, so all five deaths dispose correctly.</para>
    ///
    /// <para><b>Consequences are plain fields; triggers are pluggable.</b> Consequences must COMBINE (a taunt
    /// may stun; a teleport may grant invulnerability) and polymorphic kinds cannot combine without inventing
    /// a HitAndStunAndInvuln type. Triggers are mutually exclusive and carry genuinely different data, so they
    /// are <c>[SerializeReference]</c>. See <see cref="IZoeEventTrigger"/>.</para></summary>
    [System.Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, null, "ReactionFx")]
    public class ZoeEvent
    {
        [Tooltip("The name this event is raised by. Typed ONCE, here, where it is declared; everywhere else — " +
                 "a frame cue, a weapon's fire event, a Mirage step — PICKS it from this character's declared " +
                 "list rather than retyping it. Case-sensitive and must be unique: two events sharing an id " +
                 "means only the first can ever play.")]
        public string id = "";

        [Tooltip("An OCCASION that raises this event with nobody naming it — when damaged, when killed, when " +
                 "spawned. None = raised by reference only, which is the primary path: whether a hit should " +
                 "make this character flinch is game logic (armour, poise, a boss phase), not character data. " +
                 "A trigger is the convenience that keeps a Zoe with no bespoke game code working on its own.")]
        [SerializeReference] public IZoeEventTrigger trigger;

        [Tooltip("The clip this reaction plays (via the view's IAnimatedView, if it provides one) and sources " +
                 "FrameEvent/MetaLayer names from for the FX list below. Empty = no clip — Immediate-trigger " +
                 "FX still fire normally (this is what keeps a plain SpriteView Zoe's hit VFX working).")]
        public string clip = "";

        [Tooltip("How long this event lasts. Clip loops = the clip played this many times. Fixed seconds = an " +
                 "explicit length, and the only mode a character whose visual is a static sprite can use, since " +
                 "a still has no length of its own. Everything riding the event is timed off the answer.")]
        public EventDurationMode durationMode = EventDurationMode.ClipLoops;

        [Tooltip("How many times the clip plays. Each pass runs the whole clip; an On-Frame effect still fires " +
                 "ONCE per event, on the first pass that reaches its frame, not once per loop.")]
        [Min(1)] public int loops = 1;

        [Tooltip("The event's length in seconds, when Fixed seconds is the duration mode. With a clip, the clip " +
                 "repeats to fill it; with a static sprite, this is the only thing that gives the event a length.")]
        [Min(0f)] public float seconds = 0f;

        // ── CONSEQUENCES: what playing this event does to the character itself ────────────────────────────
        // Plain optional fields, deliberately not polymorphic kinds, because they must combine freely. Each
        // one was previously hardcoded to whichever fixed slot happened to own it; moving them here is what
        // makes a second death actually kill and a fire-hit actually grant i-frames.

        [Tooltip("Seconds the character is stunned when this event fires — it stops moving and acting, so a " +
                 "hit visibly INTERRUPTS rather than being something it walks through. 0 = no stun.")]
        [Min(0f)] public float stunSeconds = 0f;

        [Tooltip("Seconds of invulnerability granted when this event fires — stops one shot dealing many hits. " +
                 "0 = none. Was the Zoe-wide 'Invuln. After Hit', which only the single fixed hit slot could " +
                 "use; per-event means a heavy blow can grant a longer window than a graze.")]
        [Min(0f)] public float invulnerableSeconds = 0f;

        [Tooltip("This event KILLS the character: it gates acting (ZoeState.CanAct goes false), and the body " +
                 "is disposed of per the settings below once the event finishes. This flag — not which field " +
                 "the event sits in — is what makes an event a death, so a character can have as many " +
                 "different deaths as it has ways of dying.")]
        public bool endsCharacter = false;

        [Tooltip("What happens to the body once this event has killed it. Only used when Ends character is on.")]
        public DeathDisposal disposal = DeathDisposal.WhenDeathClipEnds;

        [Tooltip("Seconds the body lingers before vanishing. Used by After Delay, and as the fallback when " +
                 "When Death Clip Ends has no clip to wait for. Only used when Ends character is on.")]
        [Min(0f)] public float linger = 1.5f;

        [Tooltip("Optional SpriteFx Stack played on the character's OWN sprite the instant this reaction fires — a " +
                 "hurt/death flash, tint or dissolve that rides on top of the live animation (applied via a " +
                 "SpriteFxFilter added to the body's SpriteRenderer). Leave empty for no body effect. Unlike the FX " +
                 "list below (which SPAWNS effects at a point), this FILTERS the character's existing sprite in place.")]
        public SpriteFxSpec bodyFx;

        public List<FxEntry> fx = new List<FxEntry>();

        public bool IsEmpty => string.IsNullOrEmpty(clip) && bodyFx == null && (fx == null || fx.Count == 0);

        /// Loop count, floored at one. A reaction authored before <see cref="loops"/> existed deserializes it
        /// as 0, which must read as the single play-through it has always been — not as "never plays".
        public int Loops => Mathf.Max(1, loops);

        /// <summary>How long this event lasts, in seconds, given how long its clip is
        /// (<paramref name="clipSeconds"/>; 0 when there is no clip, no animated view, or a clip with no fixed
        /// end). Returns 0 for "unknown", which is what makes every timed playback binding degrade to a single
        /// play instead of inventing a length.
        ///
        /// This is the ONE place the event's timebase is decided, so the runtime player, the authoring window
        /// and the SpriteFx preview cannot drift into three different answers about how long the same event
        /// takes.</summary>
        public float DurationSeconds(float clipSeconds)
        {
            if (durationMode == EventDurationMode.FixedSeconds) return Mathf.Max(0f, seconds);
            return clipSeconds > 0f ? clipSeconds * Loops : 0f;
        }

        /// <summary>
        /// Convenience: a reaction whose whole content is one effect fired immediately. This is the direct
        /// replacement for the pre-unification code that assigned a bare <see cref="ICombatFx"/> (a
        /// PyreChunksFx, say) straight to <c>Zoe.hit</c>/<c>Zoe.death</c> — those fields are a ZoeEvent now,
        /// so the effect gets wrapped in a single Immediate <see cref="FxEntry"/> instead.
        /// </summary>
        /// <param name="effect">The effect to fire. Null yields an empty reaction.</param>
        /// <param name="clip">Optional clip for the reaction to play alongside the effect.</param>
        public static ZoeEvent Immediate(ICombatFx effect, string clip = "")
        {
            var r = new ZoeEvent { clip = clip };
            if (effect != null)
                r.fx.Add(new FxEntry { trigger = FxTriggerType.Immediate, fx = effect });
            return r;
        }

        /// <summary>
        /// Fire this reaction's <see cref="FxTriggerType.Immediate"/> effects at an explicit world point,
        /// without a <see cref="ZoeEventPlayer"/> or a live animation driving it.
        ///
        /// This is the "just play the VFX here" entry point for callers that own their own death/hit moment
        /// (a plain projectile impact, an enemy that despawns itself) rather than routing through a Zoe's
        /// animated view. Because there is no clip and no view, only Immediate entries can fire —
        /// FrameEvent-triggered entries need clip playback and are skipped — and every effect spawns at
        /// <paramref name="worldPos"/>, since per-entry placement (meta-layer, follow) has no view to resolve
        /// against. For full placement/FrameEvent behaviour, drive the reaction through ZoeEventPlayer.
        /// </summary>
        /// <param name="worldPos">Where to spawn the effects.</param>
        /// <param name="directionDeg">Aim for directional effects, in degrees; NaN = omni-directional.</param>
        public void Play(Vector2 worldPos, float directionDeg = float.NaN)
        {
            if (fx == null) return;
            var ctx = EventContext.ForPoint(worldPos, directionDeg);
            for (int i = 0; i < fx.Count; i++)
            {
                var entry = fx[i];
                if (entry == null || !entry.enabled || entry.trigger != FxTriggerType.Immediate) continue;
                if (entry.fx == null || entry.fx.IsEmpty) continue;
                // A bare point-play has no live view to resolve placement against, so every effect fires at the
                // supplied point — the context's Position is worldPos, so an ICombatFx spawns exactly where the
                // old entry.fx.Play(worldPos, directionDeg) did.
                entry.fx.Apply(ctx);
            }
        }
    }
}

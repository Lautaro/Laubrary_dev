using System.Collections.Generic;
using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.Zoetrope
{
    /// <summary>When an FX entry spawns: right away at Placement (no animation needed), or in sync with a
    /// named FrameEvent as the reaction's <see cref="ReactionFx.clip"/> plays. A MetaLayer (Point or Shape)
    /// never fires anything itself — it's queryable data, only meaningful as a <see cref="FxPlacementType"/>.</summary>
    /// When an effect in a reaction fires.
    ///
    /// There used to be a FrameEvent option that matched an AUTHORED frame event by name. It is gone: a lauminary
    /// frame can already trigger things directly (that is what frame events are for, and what the
    /// LaunimatorZounds bridge uses), so having reactions match them too was a second way to say the same
    /// thing — and the expensive way, since it made every timed effect wait on someone authoring an event
    /// first. Picking a frame NUMBER needs no authoring at all.
    public enum FxTriggerType
    {
        /// The moment the reaction starts.
        Immediate,
        /// On a chosen frame of the reaction's own clip.
        OnFrame,
    }

    /// <summary>Where an FX entry spawns.</summary>
    public enum FxPlacementType
    {
        /// The DamageInfo's world point (the collision point). A fixed point in space — Follow is meaningless here.
        HitPosition,
        /// The character's current sprite bounds centre (a visual mid-point, distinct from its registration anchor).
        TargetPosition,
        /// The character's transform.position (the registration anchor — "the crosshair in the Laumination Builder").
        TargetOrigin,
        /// A named MetaLayer's current point (Point or Vector mode — see <see cref="FxEntry.metaLayerId"/>),
        /// falling back to TargetPosition when nothing is authored under that id.
        MetaPoint,
        /// A declared composite body part, plus <see cref="FxEntry.localOffset"/> in that part's local space.
        BodyPart,
    }

    /// <summary>One optional named ALTERNATIVE to an <see cref="FxEntry"/>'s default effect: "when the request
    /// carries this name, play THIS instead of the default". The worked case is a gun whose muzzle look has an
    /// ordinary flash by default plus a "FlamethrowerPowerup" slot that swaps in a flame effect while that
    /// powerup is up.
    ///
    /// It exists because the only alternative was declaring a whole SECOND state row just to change ONE effect —
    /// which also duplicates that row's clip, duration, stun and body flash, and those duplicates then drift
    /// apart silently. A slot swaps the effect and leaves everything else on the one card.
    ///
    /// Deliberately NOT the same mechanism as passing a VALUE through to an effect ("the same flash, but
    /// bigger"): a value tunes an effect that already exists, while a slot picks a DIFFERENT effect asset
    /// entirely, which no value could express. Both are wanted; they solve different problems.</summary>
    [System.Serializable]
    public class FxOverride
    {
        [Tooltip("The name a request carries to select this effect instead of the entry's default. Typed ONCE, " +
                 "here, where the slot is DECLARED — callers pick it rather than retyping it. An empty name can " +
                 "never be selected, because an empty override name means 'no override' and yields the default.")]
        public string name = "";

        [Tooltip("The effect played INSTEAD of the entry's default when this slot is selected. Leave it empty " +
                 "and the slot is inert and the default plays — a half-authored slot must not silently come to " +
                 "mean 'play nothing'.")]
        [SerializeReference] public IEffect fx;
    }

    /// <summary>One "spawn this effect, here, when this happens" binding inside a <see cref="ReactionFx"/>'s FX
    /// list. <see cref="fx"/> is pluggable (e.g. a <c>PyreChunksFx</c> bundling a blast + chunk burst) so core
    /// Zoetrope stays free of any specific VFX module, same as <see cref="ICombatFx"/> everywhere else.</summary>
    [System.Serializable]
    public class FxEntry
    {
        [Tooltip("Uncheck to MUTE this effect: it stays in the list (keeping all its settings) but never fires — " +
                 "for quickly isolating which effect does what. Defaults on; existing entries stay enabled.")]
        public bool enabled = true;

        public FxTriggerType trigger = FxTriggerType.Immediate;

        [Tooltip("Which frame of the reaction's clip this fires on, when trigger is On Frame. 1 is the first " +
                 "frame. Past the clip's last frame it fires on the last one rather than never — an effect " +
                 "that silently does nothing because a clip got shorter is the worse failure.")]
        [Min(1)] public int frame = 1;

        // The position PICKER — which of the event's position in-params this effect spawns at. Promoted from a
        // fixed placement enum into the position picker; its four values are unchanged so existing data reads as
        // before (see EventContext.TryResolvePosition).
        public FxPlacementType placement = FxPlacementType.HitPosition;
        [Tooltip("MetaLayer id to sample (Point or Vector mode — whichever is actually authored), when " +
                 "placement == MetaPoint. Falls back to the sprite's own visual centre if nothing is painted " +
                 "under this id anywhere — the effect still spawns, just not tracking a live point yet.")]
        public string metaLayerId = "";
        [Tooltip("Declared body part to anchor this effect to when At is Body Part.")]
        public string bodyPart = "";
        [Tooltip("Local offset from the selected body part. It can mirror with that part's facing.")]
        public Vector2 localOffset;
        public bool mirrorOffsetWithFacing = true;

        [Tooltip("Which of the event's direction params aims this effect. HitDirection (default) reproduces the " +
                 "old behaviour; None fires omni-directionally.")]
        public DirectionParam direction = DirectionParam.HitDirection;
        [Tooltip("How the spawned visual is rotated. None leaves it upright (what every effect authored before " +
                 "this existed does); Face event direction points its forward along the chosen direction param; " +
                 "Fixed angle uses one absolute angle.")]
        public FxRotationMode rotation = FxRotationMode.None;
        [Tooltip("Degrees added on top of Face event direction, for art whose forward is not where the Pyre's " +
                 "anchor (or +X, when it has none) says.")]
        public float angleOffsetDeg;
        [Tooltip("Mirror instead of over-rotating: a shot aimed left shows the MIRRORED visual at a small angle " +
                 "rather than the right-facing one rotated 180° and drawn upside down. Without a rotation it " +
                 "mirrors with the body's own facing instead.")]
        public bool flipWithFacing;
        [Tooltip("Absolute angle used when Rotation is Fixed Angle.")]
        public float fixedAngleDeg;

        [Tooltip("Which of the event's scalar params this effect can size itself by (read by effect kinds that " +
                 "use it; the spawn-VFX kind ignores it). Additive — no existing effect changes.")]
        public ScalarParam scalar = ScalarParam.Amount;

        [Tooltip("Keep re-sampling Placement every frame and move the spawned effect with it, instead of " +
                 "spawning once and letting it live on its own. Meaningless for HitPosition (a fixed world " +
                 "point) — ignored there.")]
        public bool follow;

        // Widened from ICombatFx to the general IEffect (SAME field name, so the ~SerializeReference concrete
        // refs — all PyreChunksFx — keep deserializing untouched). An ICombatFx IS an IEffect, so existing data
        // fits; a future slice adds other IEffect kinds. `entry.fx.IsEmpty` still works (IEffect declares it).
        //
        // This is the REQUIRED DEFAULT effect, and stays exactly as it was when `overrides` below was added —
        // same name, same type, same ~SerializeReference — precisely so every already-authored asset keeps its
        // existing `rid:` reference byte-for-byte. Overrides are additive alternatives; they never migrate,
        // rewrite or displace what is stored here. Resolve() is how you read it.
        [SerializeReference] public IEffect fx;

        [Tooltip("Optional named ALTERNATIVES to the effect above. A request to show this state may carry one " +
                 "override name; if it matches a slot here, that slot's effect plays INSTEAD of the default. " +
                 "No slots (the case for everything authored before this existed) = the default always plays.")]
        public List<FxOverride> overrides = new List<FxOverride>();

        /// <summary>The effect this entry actually plays for <paramref name="overrideName"/>: a matching,
        /// non-empty <see cref="overrides"/> slot when there is one, otherwise <see cref="fx"/>, the required
        /// default.
        ///
        /// An unmatched name deliberately yields the DEFAULT rather than nothing. An override is a SWAP, not a
        /// gate: a typo'd, stale or unknown powerup name must still produce the ordinary muzzle flash, never a
        /// gun that silently emits nothing — a missing effect is far harder to notice and diagnose than a wrong
        /// one. A slot whose own effect is null or empty is skipped for exactly the same reason.</summary>
        /// <param name="overrideName">The name the request carried. Null or empty = "no override", the path
        /// every call site that predates this mechanism takes.</param>
        public IEffect Resolve(string overrideName)
        {
            // Asking for nothing is the overwhelmingly common path, so it costs one check and never walks the
            // list — this runs per effect, per spawn.
            if (string.IsNullOrEmpty(overrideName) || overrides == null) return fx;
            for (int i = 0; i < overrides.Count; i++)
            {
                var slot = overrides[i];
                // Case-INSENSITIVE, matching the one comparison rule the rest of Zoetrope is being moved to
                // (state-name lookup, the cue relay and the editor windows all agree on OrdinalIgnoreCase).
                // Written this way from the start deliberately: an override name and a state name sit side by
                // side on the same request, and two adjacent naming systems that disagree about case is exactly
                // the "resolves here, mysteriously doesn't there" trap the case pass exists to close.
                if (slot == null || !string.Equals(slot.name, overrideName, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (slot.fx == null || slot.fx.IsEmpty) continue;
                return slot.fx;
            }
            return fx;
        }
    }

    /// <summary>How long a reaction lasts — the event's OWN timebase, which everything riding it is measured
    /// against (a Body SpriteFx stack, a timed playback binding, an effect that has to end with the event).
    ///
    /// The event owns this, not the effects hanging off it: a SpriteFx stack is a shape over normalized life
    /// with no opinion about seconds, so something above it has to say how long that 0→1 takes. The character
    /// and the moment are what know.</summary>
    public enum EventDurationMode
    {
        /// Play the clip <see cref="ReactionFx.loops"/> times. One loop (the default) is a plain single
        /// play-through, which is what every reaction authored before this field existed does — deliberately
        /// value 0 so those keep behaving identically.
        ClipLoops = 0,
        /// An explicit length in seconds, independent of any clip. The only mode that can give a duration to a
        /// character whose visual is a STATIC sprite, since a still has no length of its own.
        FixedSeconds = 1,
    }

    /// <summary>Replaces the old separate <c>Zoe.hit</c>/<c>Zoe.death</c> (VFX-only) + <c>Zoe.hitReaction</c>
    /// (clip-only) split: one reaction owns BOTH which clip plays AND the FX triggered off that same clip's
    /// authored events/meta-layers, so there's exactly one place to author "what happens when this character
    /// gets hurt" (or dies) instead of two disconnected sections that had to be kept in sync by hand.</summary>
    [System.Serializable]
    public class ReactionFx
    {
        [Tooltip("The clip this reaction plays (via the view's IAnimatedView, if it provides one) and sources " +
                 "FrameEvent/MetaLayer names from for the FX list below. Empty = no clip — Immediate-trigger " +
                 "FX still fire normally (this is what keeps a plain SpriteView Zoe's hit VFX working).")]
        public string clip = "";

        [Tooltip("Which named composite body part this reaction speaks for — empty (the default) means the " +
                 "WHOLE body, exactly as every reaction has always behaved: the clip plays on every part that " +
                 "knows it. Naming one part (e.g. \"Legs\") confines it to that part alone, which is what lets " +
                 "a character show more than one thing at once — walking legs under a firing upper body. " +
                 "Meaningless on a single-part Zoe (there is only one part) and safely ignored there.")]
        public string targetPart = "";

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

        // The tooltip used to promise "it stops moving and acting". It does not: ZoeState.CanAct is consulted
        // by the two ANIMATORS (Locomotion, MotionPoseAnimator) and by nothing else — the motion driver and
        // the weapon driver never ask — so a stun today freezes the walk/idle cycle and leaves the character
        // able to move and shoot. Widening what CanAct gates is a real behaviour change and its own task;
        // until then the tooltip says what actually happens, because a dial that lies about its own effect is
        // worse than one that admits a limit.
        [Tooltip("Seconds the character is stunned when this reaction fires. Today a stun freezes its " +
                 "walk/idle animation for that long, so a hit visibly INTERRUPTS rather than being something " +
                 "it animates straight through — it does NOT yet stop it moving or firing. 0 = no stun. " +
                 "Careful on a state that repeats: a 0.5s stun on a full-auto weapon's fire state leaves the " +
                 "character permanently frozen.")]
        [Min(0f)] public float stunSeconds = 0f;

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
        /// PyreChunksFx, say) straight to <c>Zoe.hit</c>/<c>Zoe.death</c> — those fields are a ReactionFx now,
        /// so the effect gets wrapped in a single Immediate <see cref="FxEntry"/> instead.
        /// </summary>
        /// <param name="effect">The effect to fire. Null yields an empty reaction.</param>
        /// <param name="clip">Optional clip for the reaction to play alongside the effect.</param>
        public static ReactionFx Immediate(ICombatFx effect, string clip = "")
        {
            var r = new ReactionFx { clip = clip };
            if (effect != null)
                r.fx.Add(new FxEntry { trigger = FxTriggerType.Immediate, fx = effect });
            return r;
        }

        /// <summary>
        /// Fire this reaction's <see cref="FxTriggerType.Immediate"/> effects at an explicit world point,
        /// without a <see cref="ReactionFxPlayer"/> or a live animation driving it.
        ///
        /// This is the "just play the VFX here" entry point for callers that own their own death/hit moment
        /// (a plain projectile impact, an enemy that despawns itself) rather than routing through a Zoe's
        /// animated view. Because there is no clip and no view, only Immediate entries can fire —
        /// FrameEvent-triggered entries need clip playback and are skipped — and every effect spawns at
        /// <paramref name="worldPos"/>, since per-entry placement (meta-layer, follow) has no view to resolve
        /// against. For full placement/FrameEvent behaviour, drive the reaction through ReactionFxPlayer.
        /// </summary>
        /// <param name="worldPos">Where to spawn the effects.</param>
        /// <param name="directionDeg">Aim for directional effects, in degrees; NaN = omni-directional.</param>
        /// <param name="overrideName">Optional <see cref="FxOverride"/> slot name, so this bare point-play can
        /// honour a swap the same way a full <c>ReactionFxPlayer</c> run does. Omitted (the default) it behaves
        /// exactly as before: every entry plays its required default effect.</param>
        public void Play(Vector2 worldPos, float directionDeg = float.NaN, string overrideName = null)
        {
            if (fx == null) return;
            var ctx = EventContext.ForPoint(worldPos, directionDeg);
            for (int i = 0; i < fx.Count; i++)
            {
                var entry = fx[i];
                if (entry == null || !entry.enabled || entry.trigger != FxTriggerType.Immediate) continue;
                // Resolve(null) returns entry.fx, so with no override name this is the identical effect the
                // previous `entry.fx` read produced — resolved ONCE and reused, so the guard and the Apply can
                // never disagree about which effect this entry is playing.
                var effect = entry.Resolve(overrideName);
                if (effect == null || effect.IsEmpty) continue;
                // A bare point-play has no live view to resolve placement against, so every effect fires at the
                // supplied point — the context's Position is worldPos, so an ICombatFx spawns exactly where the
                // old entry.fx.Play(worldPos, directionDeg) did.
                effect.Apply(ctx);
            }
        }
    }
}

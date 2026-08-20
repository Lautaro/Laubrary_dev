using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Laubrary.Combat2D;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.Mirage
{
    /// <summary>The live character a trigger list is driving, resolved once per spawn and handed to every
    /// trigger — so a trigger never goes hunting for components itself and can be written as pure "what
    /// happens next". Any field may be null (a Zoe with no weapon, a static SpriteView with no animation
    /// player); every trigger degrades to doing nothing rather than throwing.</summary>
    [MovedFrom(true, null, null, "MirageStepContext")]
    public class MirageTriggerContext
    {
        public MirageSubject Subject;
        public Zoe Zoe;
        public GameObject Spawned;
        /// Null unless the Zoe's view is a Zoned Launimator view.
        public ZonedAnimationPlayer Player;
        public ZoeEventPlayer Events;
        public WeaponSwitcher Switcher;
        public ProjectileWeapon Weapon;
        public Health Health;
        /// Null unless the spawned character has one (every ZoeSpawner-built character does).
        public MotionStateSource Motion;
    }

    /// <summary>ONE thing sent to the character: raise an event, play a laumination, fire, wait, deal a hit.
    /// Pluggable (<c>[SerializeReference]</c>) for the same reason <see cref="IEffect"/> is — the kinds carry
    /// genuinely different data and are mutually exclusive, and a project can add one without this module
    /// learning about it.
    ///
    /// <para>These are the TRIGGERS a character would receive in a real game, in an order you choose, so a Zoe
    /// can be exercised without writing game code or aiming a second shooter at it. Loop / play / pause /
    /// step-once are properties of the LIST, never of a trigger.</para>
    ///
    /// <para><b>Play mode only.</b> <see cref="ZonedAnimationPlayer"/> is not <c>[ExecuteAlways]</c>, so
    /// nothing here ticks in Edit mode — Edit mode shows the spawned idle pose, static.</para></summary>
    public interface IMirageTrigger
    {
        /// One short line naming what this does, for the card header and the running status. Includes the
        /// trigger's own picked target ("Raise \"Hurt\"") so a folded list reads as the sequence itself.
        string Label { get; }

        /// Run it. Yield until finished — a trigger with nothing to wait for simply returns without yielding,
        /// and the list moves straight on.
        IEnumerator Run(MirageTriggerContext ctx);
    }

    /// <summary>Which weapon a firing trigger pulls, and which way that shot goes.
    ///
    /// <para><b>Direction belongs to the trigger, not to the previewable.</b> One direction for a whole entry
    /// means every shot in a list goes the same way, which is not a preview of anything a game does. The
    /// entry's own aim stays the character's RESTING aim — where it points while nothing fires; a shot says
    /// where IT goes.</para>
    ///
    /// <para><b>Where the shot leaves FROM is deliberately not here.</b> That is the weapon's own
    /// configuration — its muzzle MetaLayer, tracked live on the laumination by <c>MuzzleTracker</c>. A
    /// trigger naming its own muzzle point was a second place to answer a question the weapon already
    /// answers, and two answers to one question is how they drift.</para></summary>
    [Serializable]
    public class MirageFireConfig
    {
        [Tooltip("Which of the Zoe's OWN equipped weapon slots fires. Below zero = whichever this previewable " +
                 "already has active.")]
        public int slot = -1;

        [Tooltip("Which way this shot goes. Zero = use the character's own resting aim.")]
        public Vector2 direction = Vector2.right;

        public string SlotLabel => slot < 0 ? "active weapon" : "slot " + slot;
    }

    /// <summary>Raise one of the Zoe's own declared events by id — the primary way events are meant to run
    /// (game code says what happened), reproduced here without any game code. The id is PICKED from the
    /// character's declared list, never typed.</summary>
    [Serializable]
    [MovedFrom(true, null, null, "MirageRaiseEventStep")]
    public class MirageRaiseEventTrigger : IMirageTrigger
    {
        [Tooltip("Which of this Zoe's declared events to raise. Picked from the Zoe's own list — the id is " +
                 "typed once where the event is declared.")]
        public string eventId = "";

        [Tooltip("Hold the list until the event's laumination finishes. Off = raise it and move straight on, " +
                 "so the next trigger overlaps it (a flash while a walk keeps playing).")]
        public bool waitForFinish = true;

        public string Label => string.IsNullOrEmpty(eventId) ? "Raise Event" : $"Raise \"{eventId}\"";

        public IEnumerator Run(MirageTriggerContext ctx)
        {
            if (ctx.Events == null || string.IsNullOrEmpty(eventId)) yield break;

            // Whether anything will ever CALL BACK is knowable up front: the player only signals a finish for
            // an event that armed a laumination. Waiting on an event with no clip would hang the list forever,
            // which reads as the whole tool being broken rather than as one mis-authored trigger.
            var e = ctx.Zoe != null ? ctx.Zoe.EventNamed(eventId) : null;
            bool willSignal = waitForFinish && e != null && !string.IsNullOrEmpty(e.clip) && ctx.Player != null;

            bool done = false;
            Action<string> onFinished = id => { if (id == eventId) done = true; };
            if (willSignal) ctx.Events.EventFinished += onFinished;

            bool raised = ctx.Events.Raise(eventId);
            if (willSignal && raised) while (!done) yield return null;

            if (willSignal) ctx.Events.EventFinished -= onFinished;
        }
    }

    /// <summary>Play one of the view's lauminations directly — the absorbed <c>ClipStep</c>. Optionally fires
    /// as it starts, which is how you preview a shoot animation actually shooting.</summary>
    [Serializable]
    [MovedFrom(true, null, null, "MiragePlayLauminationStep")]
    public class MiragePlayLauminationTrigger : IMirageTrigger
    {
        [Tooltip("Which laumination to play. Picked from the ones this Zoe's view actually has.")]
        public string laumination = "";

        [Tooltip("Hold the list until it finishes playing once. Off = start it and move straight on, so a " +
                 "following Wait or Damage happens while it plays.")]
        public bool waitForFinish = true;

        [Tooltip("Loop it instead of playing once. Only available when this does not wait — a looping " +
                 "laumination never finishes, so the list could never advance past it.")]
        public bool loop;

        [Tooltip("Also fire as this starts. The shot leaves from the weapon's own muzzle point, tracked live " +
                 "on the laumination, so there is nothing to configure here about WHERE.")]
        public bool fireWeapon;

        [Tooltip("Which weapon fires, and which way, when Fire is on.")]
        public MirageFireConfig fire = new MirageFireConfig();

        public string Label => string.IsNullOrEmpty(laumination) ? "Play Laumination" : $"Play \"{laumination}\"";

        public IEnumerator Run(MirageTriggerContext ctx)
        {
            var player = ctx.Player;
            if (player == null || string.IsNullOrEmpty(laumination)) yield break;

            bool waiting = waitForFinish && !loop;
            bool done = false;
            Action onComplete = () => done = true;
            if (waiting) player.OnComplete += onComplete;

            if (!player.Play(laumination, loop))
            {
                if (waiting) player.OnComplete -= onComplete;
                yield break;
            }

            if (fireWeapon) MirageFire.Now(ctx, fire);

            if (waiting) while (!done) yield return null;
            if (waiting) player.OnComplete -= onComplete;
        }
    }

    /// <summary>Fire a weapon — the trigger a game's own code would pull. The weapon is picked from the Zoe's
    /// OWN equipped slots (Mirage never equips one the Zoe does not have), and the shot leaves from that
    /// weapon's muzzle, which <c>MuzzleTracker</c> keeps on the laumination's painted point.</summary>
    [Serializable]
    [MovedFrom(true, null, null, "MirageFireWeaponStep")]
    public class MirageFireWeaponTrigger : IMirageTrigger
    {
        [Tooltip("Which weapon fires, and which way.")]
        public MirageFireConfig fire = new MirageFireConfig();

        public string Label => "Fire (" + fire.SlotLabel + ")";

        public IEnumerator Run(MirageTriggerContext ctx)
        {
            MirageFire.Now(ctx, fire);
            yield break;   // firing is instant; the projectile lives its own life
        }
    }

    /// <summary>A dwell. Without one you cannot hold on Idle, or leave a gap between two hits.</summary>
    [Serializable]
    [MovedFrom(true, null, null, "MirageWaitStep")]
    public class MirageWaitTrigger : IMirageTrigger
    {
        [Tooltip("Seconds to hold before the next trigger.")]
        [Min(0f)] public float seconds = 1f;

        public string Label => $"Wait {seconds:0.##}s";

        public IEnumerator Run(MirageTriggerContext ctx)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }
    }

    /// <summary>Pretend this character is moving/aiming a certain way — the "Mirage trigger" ZOE_MOVEMENT_DESIGN.md
    /// section 10 step 7 calls for: preview a <see cref="MotionPose"/>'s Heading/Aim resolution (a torso aiming
    /// in 16, legs running in 4) with no game driving the transform or Combatant at all. Publishes a FULL
    /// <see cref="MotionState"/> override on the subject's <see cref="MotionStateSource"/> for the trigger's own
    /// duration, then clears it so the list (and real transform-based inference) resumes normally.</summary>
    [Serializable]
    public class MirageSimulateMotionTrigger : IMirageTrigger
    {
        [Tooltip("Speed in world units/second along the heading below. 0 = stationary (heading channel goes " +
                 "quiet and latches, same as a real stationary character).")]
        [Min(0f)] public float speed = 3f;

        [Tooltip("Heading angle in degrees — 0 = up, increasing clockwise (matches LauminationSetResolver's " +
                 "convention). What a MotionPose's Heading channel resolves to.")]
        public float headingDeg = 0f;

        [Tooltip("Aim angle, same convention, independent of heading (e.g. strafing one way while aiming " +
                 "another). What a MotionPose's Aim channel resolves to.")]
        public float aimDeg = 0f;

        public bool grounded = true;
        [Tooltip("Only meaningful while not grounded.")]
        public float verticalVelocity = 0f;
        [Tooltip("Moving under its own power vs being shoved — affects anything reading MotionState.selfWilled.")]
        public bool selfWilled = true;

        [Tooltip("How long to hold this simulated state before the list continues and real transform-based " +
                 "inference resumes.")]
        [Min(0f)] public float seconds = 2f;

        public string Label => $"Simulate Motion ({speed:0.#}u/s @ {headingDeg:0}°, {seconds:0.#}s)";

        public IEnumerator Run(MirageTriggerContext ctx)
        {
            if (ctx.Motion == null) yield break;

            Vector2 headingDir = AngleToVector(headingDeg);
            bool moving = speed > 0.0001f;
            var state = new MotionState
            {
                velocity = headingDir * speed,
                speed = speed,
                heading = moving ? headingDir : Vector2.zero,
                facing = moving ? headingDir : Vector2.zero,
                aim = AngleToVector(aimDeg),
                grounded = grounded,
                verticalVelocity = verticalVelocity,
                selfWilled = selfWilled,
            };
            ctx.Motion.PublishOverride(state, seconds);

            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }

            ctx.Motion.ClearOverride();
        }

        static Vector2 AngleToVector(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }
    }

    /// <summary>Apply a hit with no second shooter — the thing that makes a Hit or Death event testable at
    /// all, instead of hoping something in the scene shoots the Hero.</summary>
    [Serializable]
    [MovedFrom(true, null, null, "MirageDamageStep")]
    public class MirageDamageTrigger : IMirageTrigger
    {
        [Tooltip("How much damage to deal. Ignored when Lethal is on (the character's remaining health is used).")]
        [Min(0f)] public float amount = 10f;

        [Tooltip("Kill outright: deals exactly the remaining health and ignores any i-frames, so a forced " +
                 "death always resolves — while still carrying the type/point below, so the right death event answers.")]
        public bool lethal;

        [Tooltip("Where the hit lands, relative to the character — what a Hit-Position effect spawns at.")]
        public Vector2 offset;

        [Tooltip("Which way the hit pushes — what a directional hit effect aims along.")]
        public Vector2 direction = Vector2.left;

        [Tooltip("Mark it a critical hit, so an event filtered on crits answers this one.")]
        public bool crit;

        [Tooltip("What KIND of damage this is, so an event filtered by damage type answers it. None = unspecified.")]
        public DamageType type;

        public string Label => lethal ? "Damage (lethal)" : $"Damage {amount:0.##}";

        public IEnumerator Run(MirageTriggerContext ctx)
        {
            var health = ctx.Health;
            if (health == null) yield break;

            Vector2 point = (Vector2)(ctx.Spawned != null ? ctx.Spawned.transform.position
                                                          : ctx.Subject.transform.position) + offset;
            var info = new DamageInfo(amount, source: null, faction: null, point: point,
                                      direction: direction, crit: crit, type: type);
            if (lethal) health.Kill(info);
            else health.ApplyDamage(info);
        }
    }

    /// Shared firing, so the two trigger kinds that can fire cannot drift apart.
    static class MirageFire
    {
        /// <summary>Pull the trigger. WHERE the shot leaves from is the weapon's own muzzle transform, which
        /// <c>MuzzleTracker</c> holds on the laumination's painted point, so nothing here has to know about
        /// meta-layers; WHICH WAY it goes is the trigger's own.</summary>
        public static void Now(MirageTriggerContext ctx, MirageFireConfig fire)
        {
            if (fire == null) return;
            if (fire.slot >= 0 && ctx.Switcher != null)
            {
                ctx.Switcher.SwitchTo(fire.slot);
                ctx.Weapon = ctx.Switcher.ActiveWeapon;
            }
            var weapon = ctx.Weapon;
            if (weapon == null) return;

            if (fire.direction.sqrMagnitude > 1e-6f) weapon.TryFire(fire.direction.normalized);
            else weapon.TryFire();   // no direction of its own — the character's resting aim decides
        }
    }

    /// <summary>The one place a legacy <see cref="ClipStep"/> list becomes triggers, shared by the asset entry
    /// and the scene component so they cannot migrate differently.</summary>
    public static class MirageTriggerList
    {
        /// <summary>Fold a pre-trigger Clips list into triggers. A firing clip keeps firing; its painted
        /// muzzle id is DROPPED rather than carried, because the weapon's own configuration answers where a
        /// shot leaves from now, and that field was a second answer to the same question.</summary>
        public static void Migrate(List<ClipStep> legacy, List<IMirageTrigger> triggers)
        {
            if (legacy == null || triggers == null) return;
            foreach (var c in legacy)
            {
                if (c == null || string.IsNullOrEmpty(c.clip)) continue;
                triggers.Add(new MiragePlayLauminationTrigger
                {
                    laumination = c.clip,
                    waitForFinish = true,
                    fireWeapon = c.fireWeapon,
                });
            }
        }
    }
}

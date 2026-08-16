using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// Which condition a <see cref="MotionPoseRule"/> matches against. Deliberately just two live checks plus
    /// the unconditional fallback — the same "first match wins, unconditional last" idiom <see cref="Zoe.EventFor"/>
    /// already uses, rather than fixed named slots (ZOE_MOVEMENT_DESIGN.md section 8.1: "when accelerating" is
    /// not idle-or-moving, so a fixed Idle/Move pair is the wrong shape even for the simplest case).
    public enum MotionCondition
    {
        /// Matches unconditionally — the fallback a rule list should end with.
        Always,
        /// Matches when MotionState.speed is above the pose's moveThreshold.
        Moving,
        /// Matches when MotionState.speed is at or below the pose's moveThreshold.
        Idle,
    }

    /// Which MotionState channel steers this pose's resolved direction. This is where "legs follow movement,
    /// torso follows the crosshair" is expressed (ZOE_MOVEMENT_DESIGN.md section 7.3) — one enum per pose.
    public enum DirectionChannel
    {
        /// Where the body is travelling (MotionState.heading). Latches when the channel goes quiet (a
        /// stationary tank keeps facing where it last drove).
        Heading,
        /// Where the body is aimed (MotionState.aim, from Combatant.aimDirection). Latches the same way.
        Aim,
        /// A fixed authored angle — a turret with no free aim, a part that never turns.
        Fixed,
    }

    /// One condition -> set rule in a <see cref="MotionPose"/>'s ordered list.
    [System.Serializable]
    public class MotionPoseRule
    {
        public MotionCondition condition = MotionCondition.Always;

        [Tooltip("Name of a LauminationSet declared on the view's LauminaryVersion. Picked from that version's " +
                 "sets by an editor, never typed free-hand — this field is the underlying runtime reference.")]
        public string setName = "";

        [Tooltip("Steer THIS rule by a different channel than the pose's own default — e.g. legs that follow " +
                 "Heading while moving but should match the torso's Aim while idle (an idle character's legs " +
                 "read as \"paired with\" whichever way it's facing, not frozen on wherever it last walked). " +
                 "Off = use the pose's channel, same behaviour as before this field existed.")]
        public bool overrideChannel = false;
        public DirectionChannel channel = DirectionChannel.Heading;
    }

    /// <summary>
    /// Per-part (a single-body Zoe has exactly one, on <see cref="Zoe.motionPose"/>) — which channel aims it,
    /// and which <see cref="LauminationSet"/> plays for which condition. Generalises <see cref="Locomotion"/>'s
    /// fixed idle/move pair into an ordered rule list so a later condition (under-power, airborne) can be
    /// inserted without redesigning the shape — the same reason <see cref="Zoe"/>'s event list moved off fixed
    /// hit/death slots.
    ///
    /// An unauthored pose (empty <see cref="rules"/>) resolves nothing, so an existing Zoe with only the old
    /// <see cref="Locomotion"/> field authored is completely unaffected — <see cref="ZoeSpawner"/> only attaches
    /// <see cref="MotionPoseAnimator"/> when this is actually authored.
    /// </summary>
    [System.Serializable]
    public class MotionPose
    {
        [Tooltip("Which MotionState channel steers this pose's direction.")]
        public DirectionChannel channel = DirectionChannel.Heading;

        [Tooltip("Speed, in world units per second, above which MotionCondition.Moving matches (Idle matches " +
                 "at or below it). Same idea as Locomotion.moveThreshold.")]
        [Min(0f)] public float moveThreshold = 0.15f;

        [Tooltip("Angle used when channel == Fixed. Convention: 0 = up, increasing clockwise in screen space " +
                 "(matches LauminationSetResolver).")]
        public float fixedAngleDeg = 0f;

        [Tooltip("Ordered condition -> set rules. FIRST MATCH WINS; put an Always rule last as the fallback.")]
        public List<MotionPoseRule> rules = new List<MotionPoseRule>();

        public bool IsAuthored => rules != null && rules.Count > 0;

        /// The first rule whose condition matches <paramref name="state"/>, or null if none do (an unauthored
        /// pose, or a rule list with no unconditional fallback that happens not to match right now).
        public MotionPoseRule Match(in MotionState state)
        {
            if (rules == null) return null;
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null) continue;
                bool hit = r.condition switch
                {
                    MotionCondition.Always => true,
                    MotionCondition.Moving => state.speed > moveThreshold,
                    MotionCondition.Idle => state.speed <= moveThreshold,
                    _ => false,
                };
                if (hit) return r;
            }
            return null;
        }

        /// The raw direction vector this pose's channel points at right now — zero for Heading/Aim when that
        /// channel has nothing to say (not moving, no aim set), which is exactly the signal a caller latches
        /// against (ZOE_MOVEMENT_DESIGN.md section 8.1, break 3: "a stationary tank has no heading"). Never
        /// zero for Fixed — that channel has no "quiet" state to latch through.
        public Vector2 ResolveDirectionVector(in MotionState state) => ResolveDirectionVector(state, channel);

        /// Same as above but steered by an EXPLICIT channel — for a matched rule with
        /// <see cref="MotionPoseRule.overrideChannel"/> set, which resolves by its own channel instead of the
        /// pose's default.
        public Vector2 ResolveDirectionVector(in MotionState state, DirectionChannel effectiveChannel)
        {
            switch (effectiveChannel)
            {
                case DirectionChannel.Aim: return state.aim;
                case DirectionChannel.Fixed: return AngleToVector(fixedAngleDeg);
                case DirectionChannel.Heading:
                default: return state.heading;
            }
        }

        /// Whether this pose's channel should latch through a quiet frame rather than snapping to angle 0.
        /// Fixed never latches — a fixed angle has no notion of "no current direction".
        public bool UsesLatch => channel != DirectionChannel.Fixed;

        /// Same as above but for an EXPLICIT channel (a rule's override).
        public static bool ChannelUsesLatch(DirectionChannel c) => c != DirectionChannel.Fixed;

        static Vector2 AngleToVector(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }
    }
}

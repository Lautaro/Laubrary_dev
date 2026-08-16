using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// Pure resolver: (<see cref="MotionPose"/>, <see cref="LauminaryVersion"/>, <see cref="MotionState"/>) →
    /// which <see cref="LauminationResolution"/> to show right now. Lives in this bridge (not core Zoetrope)
    /// for the same reason <see cref="ZonedLauminaryView"/> and <see cref="CompositeZonedPlayer"/> do — it
    /// directly touches Launimator's <see cref="LauminaryVersion"/>/<see cref="LauminationSetResolver"/>, which
    /// core Zoetrope must not reference.
    /// </summary>
    public static class MotionPoseResolver
    {
        /// Resolve <paramref name="pose"/> against <paramref name="state"/>: pick the first matching rule, look
        /// its named set up on <paramref name="version"/>, then resolve that set for the pose's channel angle.
        /// Returns <see cref="LauminationResolution.None"/> if the pose is unauthored, no rule matches, or the
        /// matched rule names a set the version doesn't declare.
        public static LauminationResolution Resolve(MotionPose pose, LauminaryVersion version,
                                                     in MotionState state, LatchedDirection latch)
        {
            if (pose == null || version == null) return LauminationResolution.None;

            var rule = pose.Match(state);
            if (rule == null || string.IsNullOrEmpty(rule.setName)) return LauminationResolution.None;

            var set = FindSet(version, rule.setName);
            if (set == null) return LauminationResolution.None;

            var effectiveChannel = rule.overrideChannel ? rule.channel : pose.channel;
            Vector2 dir = pose.ResolveDirectionVector(state, effectiveChannel);
            bool usesLatch = MotionPose.ChannelUsesLatch(effectiveChannel);
            float angle = usesLatch && latch != null ? latch.Update(dir) : AngleOf(dir);
            return LauminationSetResolver.Resolve(set, angle);
        }

        static float AngleOf(Vector2 v) => v.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;

        static LauminationSet FindSet(LauminaryVersion version, string name)
        {
            if (version.sets == null) return null;
            for (int i = 0; i < version.sets.Count; i++)
                if (version.sets[i] != null && version.sets[i].name == name) return version.sets[i];
            return null;
        }
    }
}

using System;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>Where a followed effect should be, this frame: the point its placement resolved to, the angle its
    /// forward should point at (NaN = upright) and whether it is mirrored — the same three answers
    /// <see cref="EventContext.TryResolvePosition(FxEntry, out Vector2)"/> +
    /// <see cref="EventContext.ResolveOrientation"/> give at spawn time, re-asked while the effect plays.</summary>
    public readonly struct FxPose
    {
        public readonly Vector3 Position;
        public readonly float AimDeg;
        public readonly bool FlipX;

        public FxPose(Vector3 position, float aimDeg, bool flipX)
        {
            Position = position;
            AimDeg = aimDeg;
            FlipX = flipX;
        }
    }

    /// <summary>Attached to a spawned FX instance when its <see cref="FxEntry.follow"/> is true — re-samples the
    /// placement source every frame and re-places the instance with it (a muzzle flash riding the gun barrel,
    /// rather than one fire-and-forget spawn).
    ///
    /// <para>It follows the WHOLE pose, not just the point: position, aim and mirror. A flash that spawned at the
    /// muzzle pointing right keeps pointing where the gun points if the character turns mid-flash, instead of
    /// sliding along with the barrel while still aimed at the angle the shot left at.</para>
    ///
    /// <para>Pooling-safe: the follow is re-armed per spawn (the host reuses an existing component rather than
    /// stacking a second one onto a pooled instance) and cleared in <see cref="OnDisable"/>, which is exactly
    /// when <c>ComponentPool</c> deactivates a released instance — so a pooled object reused later by an effect
    /// that does NOT follow is not still being dragged around by the previous user's sampler.</para></summary>
    public class FxFollowTarget : MonoBehaviour
    {
        Func<FxPose> _sample;
        Action<FxPose> _apply;

        /// <summary>Follow a moving POINT only, leaving whatever orientation the effect spawned with — the
        /// behaviour every follow had before pose-following existed.</summary>
        public void Init(Func<Vector3> samplePosition)
        {
            if (samplePosition == null) { Stop(); return; }
            Init(() => new FxPose(samplePosition(), float.NaN, false),
                 pose => transform.position = pose.Position);
        }

        /// <summary>Follow the full pose: <paramref name="sample"/> re-resolves it each frame and
        /// <paramref name="apply"/> writes it through the effect's OWN placement maths (a Pyre re-places through
        /// its anchor, so an anchored flash keeps its anchor on the muzzle as it turns).</summary>
        public void Init(Func<FxPose> sample, Action<FxPose> apply)
        {
            _sample = sample;
            _apply = apply;
        }

        /// Stop following. The instance keeps whatever pose it last had.
        public void Stop()
        {
            _sample = null;
            _apply = null;
        }

        void LateUpdate()
        {
            if (_sample == null || _apply == null) return;
            _apply(_sample());
        }

        // A pooled instance is deactivated on release; dropping the sampler here is what keeps the NEXT user of
        // that pooled object from inheriting this one's follow.
        void OnDisable() => Stop();
    }
}

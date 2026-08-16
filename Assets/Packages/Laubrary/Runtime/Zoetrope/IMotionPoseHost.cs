using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Optional capability a spawned character's view MAY provide (discovered via GetComponent, same pattern
    /// as <see cref="IAnimatedView"/> / <c>ICueSink</c>) — lets <see cref="ZoeSpawner"/> attach a directional
    /// <see cref="MotionPose"/> animator without core Zoetrope depending on Launimator (<see cref="MotionPose"/>
    /// resolution needs <c>LauminaryVersion</c>/<c>LauminationSetResolver</c>, which live in the
    /// Zoetrope.Launimator bridge — the same reason <c>AnimatedViewRelay</c>/<c>CueRelay</c> exist there
    /// instead of here). A plain <see cref="SpriteView"/> host has no component implementing this, so an
    /// authored <see cref="MotionPose"/> on a non-directional view is silently a no-op — nothing to animate,
    /// not an error, the rule <see cref="IAnimatedView"/> already follows.
    /// </summary>
    public interface IMotionPoseHost
    {
        /// Wire <paramref name="pose"/> up as this host's motion-pose animator. Implementations add and
        /// configure whatever component actually drives it (a Launimator-backed one resolves the pose's rules
        /// against its LauminaryVersion's declared sets).
        void BindMotionPose(GameObject go, MotionPose pose);
    }
}

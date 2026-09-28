using System;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// A Klip's looping settings — what makes a Klip a **Looper** (T-0473). A Looper plays its trimmed region over and
    /// over until it is stopped. With a crossmix, a new copy starts that long before the end, from the loop's start,
    /// and the two are cross-faded over exactly that length, so the seam is smoothed over instead of cut.
    ///
    /// **Why this is a setting on the Klip and not a separate kind of zound.** Everything a Looper needs — its audio,
    /// trim points (which are the loop points), effect chain, speed, pitch and volume ranges, its editor — is exactly
    /// what a Klip already has. As a Klip it keeps all of that, stays in the same list and save format, and can be an
    /// entry in a Zequence of any mode (parallel, round robin, randomizer, playlist), which is how those stay
    /// choosable alongside it. The editors show it as a Looper.
    ///
    /// **Fixed or random crossmix.** Stored as a range like the Klip's volume and pitch: equal ends mean one fixed
    /// length, different ends mean every crossfade draws its own length within the range. The draw is made by the
    /// engine per loop cycle from a hash, never from a managed random source.
    /// </summary>
    [Serializable]
    public class ZoundLoop {

        /// <summary>This Klip is a Looper.</summary>
        public bool enabled;

        /// <summary>The crossmix length range, in seconds of the source. Both 0: the loop simply starts over at the end.</summary>
        public float crossmixMin;
        public float crossmixMax;

        /// <summary>
        /// The longest crossmix a loop of <paramref name="loopSeconds"/> can have: half of it. The incoming copy fades in
        /// over the first X seconds of the loop while the next seam's copy starts X seconds before its end; above half
        /// the loop those two would overlap within one pass. The engine applies this limit to the top of the range.
        /// </summary>
        public static float MaxCrossmix(float loopSeconds) => Mathf.Max(0f, loopSeconds * 0.5f);

        /// <summary>The range as it will be heard for a loop of <paramref name="loopSeconds"/>: ordered, and each end
        /// limited to half the loop.</summary>
        public void Effective(float loopSeconds, out float min, out float max) {
            float limit = MaxCrossmix(loopSeconds);
            min = Mathf.Clamp(Mathf.Min(crossmixMin, crossmixMax), 0f, limit);
            max = Mathf.Clamp(Mathf.Max(crossmixMin, crossmixMax), 0f, limit);
        }

        public ZoundLoop DeepCopy() => (ZoundLoop)MemberwiseClone();
    }
}

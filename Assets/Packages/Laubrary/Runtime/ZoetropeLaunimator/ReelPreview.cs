using UnityEngine;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// The shared "which animation should a browser show" rule for every Reel-backed character view.
    ///
    /// One place on purpose: ReelView and ZonedReelView both answer it, and answering it differently would
    /// mean the same character previewed differently depending on which view happened to be authored on it.
    internal static class ReelPreview
    {
        /// The preferred clip when it exists, otherwise the first animation that actually has frames.
        ///
        /// That fallback is the whole point. A reel routinely has no clip literally called "Idle" —
        /// OutBurner's own characters have Run and Shot — so honouring only the named clip left every such
        /// Zoe blank in a picker, which is exactly the bug this fixes. It also matches what the views promise
        /// at runtime: "falls back to the first clip".
        public static AnimationDef Pick(ReelVersion version, string preferredClip)
        {
            if (version == null || version.animations == null) return null;

            if (!string.IsNullOrEmpty(preferredClip))
                foreach (var a in version.animations)
                    if (a != null && a.frames != null && a.frames.Count > 0 &&
                        string.Equals(a.name, preferredClip, System.StringComparison.OrdinalIgnoreCase))
                        return a;

            foreach (var a in version.animations)
                if (a != null && a.frames != null && a.frames.Count > 0) return a;

            return null;
        }

        public static Sprite[] Frames(ReelVersion version, string preferredClip)
        {
            var a = Pick(version, preferredClip);
            return a != null ? a.frames.ToArray() : System.Array.Empty<Sprite>();
        }

        public static float Fps(ReelVersion version, string preferredClip)
        {
            var a = Pick(version, preferredClip);
            return a != null ? a.fps : 0f;
        }
    }
}

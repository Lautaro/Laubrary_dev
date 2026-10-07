using UnityEngine;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// The shared "which animation should a browser show" rule for every Lauminary-backed character view.
    ///
    /// One place on purpose: LauminaryView and ZonedLauminaryView both answer it, and answering it differently would
    /// mean the same character previewed differently depending on which view happened to be authored on it.
    internal static class LauminaryPreview
    {
        /// The preferred clip when it exists, otherwise the first animation that actually has frames.
        ///
        /// That fallback is the whole point. A lauminary routinely has no clip literally called "Idle" —
        /// OutBurner's own characters have Run and Shot — so honouring only the named clip left every such
        /// Zoe blank in a picker, which is exactly the bug this fixes. It also matches what the views promise
        /// at runtime: "falls back to the first clip".
        public static Laumination Pick(LauminaryVersion version, string preferredClip)
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

        public static Sprite[] Frames(LauminaryVersion version, string preferredClip)
        {
            var a = Pick(version, preferredClip);
            return a != null ? a.frames.ToArray() : System.Array.Empty<Sprite>();
        }

        public static float Fps(LauminaryVersion version, string preferredClip)
        {
            var a = Pick(version, preferredClip);
            return a != null ? a.fps : 0f;
        }

        /// Each frame's own time in seconds, or null when the animation has no per-frame durations.
        public static float[] FrameSeconds(LauminaryVersion version, string preferredClip)
        {
            var a = Pick(version, preferredClip);
            if (a == null || !a.HasFrameTimings) return null;
            var s = new float[a.frames.Count];
            for (int i = 0; i < s.Length; i++) s[i] = a.FrameSeconds(i);
            return s;
        }
    }
}

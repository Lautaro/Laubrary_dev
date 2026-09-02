using UnityEngine;

namespace Laubrary.Chunks
{
    /// How long a recipe lasts. The ONE home for that rule: the runtime sizes a burst container from it, the
    /// window's transport scrubs over it, and the timing lanes are drawn against it — three readings of "how
    /// long is this?" that must agree, and did not before (the emitter summed module delays while the timeline
    /// reported its own duration, so a lane could sit past the end of the container that was meant to hold it).
    public static class ChunkClock
    {
        /// A recipe with nothing timed in it still needs a scrubbable span, or the transport has no range to
        /// move over and reads as broken rather than as empty.
        public const float MinimumLength = 0.25f;

        /// Seconds from the recipe's start to the moment the last thing it produces is gone.
        ///
        /// Every timed capability counts, ENABLED OR NOT, on purpose: switching one off to look at the rest
        /// must not make the ruler jump and re-scale every lane under the cursor. It is the same reason the
        /// window keeps a disabled capability's lane on screen, drawn dim.
        ///
        /// Cues count too — a marker a second after the last visible thing is a real part of the recipe's
        /// length (a sound that plays after the smoke clears), and a clock that ended before it would put that
        /// marker off the end of its own ruler.
        public static float Length(ChunkSpec spec)
        {
            if (spec == null) return MinimumLength;

            float length = 0f;
            var stack = spec.capabilities;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                {
                    var c = stack[i];
                    if (c == null) continue;
                    if (c.OccupiesTime)
                        length = Mathf.Max(length, Mathf.Max(0f, c.delay) + Mathf.Max(0f, c.DurationSeconds(spec)));
                    if (c is Cues cues) length = Mathf.Max(length, cues.LatestCueTime);
                }

            return Mathf.Max(MinimumLength, length);
        }

        /// How many capabilities would draw a lane — what decides whether the recipe has a timing surface at
        /// all (two or more, or any cue) rather than a single card with no Delay dial.
        public static int TimedCount(ChunkSpec spec)
        {
            var stack = spec != null ? spec.capabilities : null;
            if (stack == null) return 0;
            int n = 0;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null && stack[i].OccupiesTime) n++;
            return n;
        }

        /// Whether the recipe needs a timing surface: more than one thing competing for the clock, or a cue
        /// that has to be placed on it. One lone capability owns the whole clock and has nothing to be timed
        /// against, so it shows no Delay dial and no ruler.
        public static bool NeedsTimingSurface(ChunkSpec spec)
        {
            if (TimedCount(spec) > 1) return true;
            var stack = spec != null ? spec.capabilities : null;
            if (stack == null) return false;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] is Cues cues && cues.CueCount > 0) return true;
            return false;
        }
    }
}

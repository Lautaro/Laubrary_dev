using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0144 -- the document's animation clock: the ONE place that answers "what phase is frame N?" and
    /// "how many frames have elapsed in this much wall time?".
    ///
    /// <b>Why this file exists when the T-0115 node cache's own <c>PhaseOfFrame</c> already answered the
    /// first question.</b> It did, and its answer is kept verbatim -- but it lived on a transient CACHE object
    /// that is constructed with a <c>frameCount</c> handed to it from outside. Design C1 says the DOCUMENT "has a
    /// canvas size, a frame count and a rate" (<c>SHAPER_THE_DESIGN.md:299</c>), so the authored frame count
    /// belongs on <see cref="ShaperDocument"/>, and once two types can answer the same question there must be
    /// exactly one implementation behind both. That cache (and its own <c>PhaseOfFrame</c>) has since been
    /// deleted (T-0253); this is now the ONLY implementation.
    ///
    /// <b>The frame-to-phase convention, and why it was NOT changed.</b> Frame 0 is phase 0 and the LAST frame
    /// is phase 1: <c>phase = i / (N - 1)</c>, with a single-frame document pinned at phase 0. That convention
    /// was set by T-0115 and is already baked into every cache key ever produced
    /// (<see cref="ShaperLayerKey"/> folds <c>phase01</c> into a node's identity), so it is not an open
    /// choice any more -- it is a shipped one.
    ///
    /// It is worth recording the argument AGAINST it, because it is a real one and a future reader will
    /// re-derive it otherwise. The Pyre forms this engine hosts as composite generators are periodic by
    /// construction on <c>frame / frameCount</c> -- see <c>TorchForm.cs:26</c> and <c>JetForm.cs:18</c>, both
    /// of which say "exactly periodic by construction" about that exact expression. Under <c>i / (N - 1)</c> a
    /// looping document's first and last frames land on phase 0 and phase 1, which for a cyclic function are
    /// the same point, so a seamless loop shows one duplicated frame per cycle. Under <c>i / N</c> that
    /// duplicate disappears, but phase never reaches 1, so the end of every authored Curve dial becomes
    /// unreachable.
    ///
    /// Changing it now would silently invalidate every cached key and re-point every authored curve at a
    /// different value -- a breaking change to a shipped system, to trade one artefact for another. So the
    /// existing convention stands, and callers that need exact periodicity from a hosted Pyre form should
    /// author the loop to meet at its ends. Flagged here rather than quietly settled, because it is the kind
    /// of thing that reads as an oversight when it is actually a decision.
    /// </summary>
    public static class ShaperClock
    {
        /// <summary>The rate a new document starts at, matching Pyre's own <c>previewFps</c> default (12).</summary>
        public const float DefaultFrameRate = 12f;

        /// <summary>The slowest and fastest authorable rate. Same 1..30 band Pyre's transport offers.</summary>
        public const float MinFrameRate = 1f;
        public const float MaxFrameRate = 30f;

        /// <summary>
        /// Frame index to <c>phase01</c>. Frame 0 is phase 0, the LAST frame is phase 1; a single-frame
        /// document (<paramref name="frameCount"/> &lt;= 1) is phase 0 throughout, which is exactly the
        /// default <c>ShaperCompiler.Compile</c> already uses for a non-animated document. See the class doc
        /// for why this convention is fixed rather than chosen.
        /// </summary>
        public static float PhaseOfFrame(int frameIndex, int frameCount)
        {
            if (frameCount <= 1) return 0f;
            return Mathf.Clamp01((float)frameIndex / (frameCount - 1));
        }

        /// <summary>Wrap a frame index into <c>[0, frameCount)</c>, looping. Negative indices wrap forward, so
        /// stepping backwards off frame 0 lands on the last frame rather than clamping there.</summary>
        public static int WrapFrame(int frameIndex, int frameCount)
        {
            int n = Mathf.Max(1, frameCount);
            int i = frameIndex % n;
            return i < 0 ? i + n : i;
        }

        /// <summary>
        /// The playback accumulator -- the engine half of a transport's per-tick update, with no
        /// <c>EditorApplication</c> or <c>Time</c> dependency of its own so the same rule drives an editor
        /// preview, a runtime coroutine or a test.
        ///
        /// Feeds <paramref name="deltaSeconds"/> of wall time in at <paramref name="frameRate"/> and returns
        /// how many WHOLE frames to step, leaving the sub-frame remainder in <paramref name="accumulator"/>
        /// for the next call. <paramref name="deltaSeconds"/> is clamped to <paramref name="maxDeltaSeconds"/>
        /// first: without that, one long editor stall (a domain reload, a compile, a breakpoint) arrives as a
        /// single huge delta and the transport "catches up" by skipping a chunk of the animation, which reads
        /// as a glitch rather than as playback. Pyre clamps at the same 0.1 s for the same reason
        /// (<c>PyreWindow.cs:181</c>).
        /// </summary>
        public static int AdvanceFrames(ref float accumulator, float deltaSeconds, float frameRate,
                                        float maxDeltaSeconds = 0.1f)
        {
            float dt = Mathf.Clamp(deltaSeconds, 0f, Mathf.Max(0f, maxDeltaSeconds));
            accumulator += dt * Mathf.Max(MinFrameRate, frameRate);
            if (accumulator < 1f) return 0;

            int whole = Mathf.FloorToInt(accumulator);
            accumulator -= whole;
            return whole;
        }
    }
}

using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// What a Zoe event LOOKS like and how long it LASTS — resolved once, here, for every editor that needs
    /// the answer.
    ///
    /// Two editors ask this question and they must never give different answers: the Zoe window, which shows
    /// the author how long the event they are configuring actually runs, and the SpriteFx preview link, which
    /// hands the stack window a visual to play the effect over. When each worked it out for itself the stack
    /// window previewed a flash at one length while the event played it at another, which is the whole class
    /// of bug this type exists to close.
    ///
    /// The visual is whatever the character's own view is configured for, which covers both realistic cases
    /// without either caller branching: a clip-aware view (a Launimator reel) yields the event's named clip,
    /// and a plain sprite view yields its one picture. A still has no length of its own, so an event drawn on
    /// one can only get a duration from the reaction's Fixed-seconds mode — and reports 0, meaning "unknown",
    /// rather than inventing one.
    /// </summary>
    public struct ZoeEventVisual
    {
        /// The event's frames, in order. One is a still; more animate. Never null.
        public Sprite[] Frames;
        /// The rate those frames play at. 0 for a still, or a view with no meaningful animation speed.
        public float Fps;
        /// One play-through of the visual, in seconds. 0 when it cannot be measured (a still, or no art).
        public float ClipSeconds;
        /// How long the whole EVENT lasts — ClipSeconds put through the reaction's own duration model (N
        /// loops, or an explicit number of seconds). 0 = unknown, which is what makes a timed playback
        /// binding degrade to a single play instead of guessing.
        public float EventSeconds;

        public bool HasFrames => Frames != null && Frames.Length > 0;
        /// True when there is nothing to animate — one frame, or no rate to animate it at.
        public bool IsStill => Fps <= 0f || Frames == null || Frames.Length <= 1;

        /// <summary>Resolve the visual and the timebase of one reaction on one character.</summary>
        public static ZoeEventVisual Of(Zoe zoe, ReactionFx reaction)
        {
            var v = new ZoeEventVisual { Frames = System.Array.Empty<Sprite>() };
            if (zoe == null) return v;

            if (zoe.view is IClipPreviewableView byClip)
            {
                string clip = reaction != null ? reaction.clip : "";
                v.Frames = byClip.PreviewFrames(clip) ?? System.Array.Empty<Sprite>();
                v.Fps = byClip.PreviewFpsOf(clip);
            }
            else if (zoe.view is IPreviewableView plain)
            {
                v.Frames = plain.PreviewFrames() ?? System.Array.Empty<Sprite>();
                v.Fps = plain.PreviewFps;
            }

            if (v.Fps > 0f && v.Frames.Length > 0) v.ClipSeconds = v.Frames.Length / v.Fps;
            v.EventSeconds = reaction != null ? reaction.DurationSeconds(v.ClipSeconds) : 0f;
            return v;
        }
    }
}

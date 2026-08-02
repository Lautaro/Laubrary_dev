using System;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// Optional capability a spawned character's view MAY provide (discovered via GetComponent, same pattern
    /// as <see cref="ICueSink"/>) — lets core Zoetrope drive named-clip playback (hit reactions, Target
    /// Practice's idle loop) without depending on any specific animation system. A plain <see cref="SpriteView"/>
    /// Zoe has no component implementing this, so clip playback is silently a no-op for it (nothing to play,
    /// not an error) — a Zoned Launimator view provides it via the ZoetropeLaunimator bridge's
    /// <c>AnimatedViewRelay</c>, wrapping <c>ZonedAnimationPlayer</c>.
    /// </summary>
    public interface IAnimatedView
    {
        /// Play a named clip. When loop is false, onComplete fires once when it finishes; ignored when looping.
        /// Returns false if the clip name is unknown to this view.
        bool PlayClip(string clip, bool loop, Action onComplete = null);

        /// Length of a named clip in seconds, or 0 when unknown — an unrecognised name, or a clip whose end
        /// is not fixed (a zoned strip can hold/loop indefinitely). Lets reaction effects time themselves to
        /// the event (loop-for-its-duration, run-at-its-end); 0 makes them degrade to a single play.
        float GetClipSeconds(string clip);

        /// Stop playback and show NOTHING (blank the view) — the "no clip" state, so a clip-less death can be
        /// replaced by an explosion FX instead of freezing on the last idle frame. A later <see cref="PlayClip"/>
        /// re-shows it. (A plain SpriteView has no IAnimatedView, so its caller hides the SpriteRenderer directly.)
        void Hide();

        /// Fires (eventName, frame) as playback enters a frame carrying an authored FrameEvent — lets core
        /// Zoetrope (<see cref="ReactionFxPlayer"/>) sync FX to a reaction clip's own authored frames without
        /// depending on any specific animation system.
        event Action<string, int> OnFrameEvent;
        /// Fires once per frame-entry with the frame index, for EVERY frame — annotated or not. The plain
        /// counterpart to <see cref="OnFrameEvent"/>: that one says "something named happens here", this one
        /// says "we are on frame N", which is what a reaction timing itself to the animation needs when the
        /// animator has not authored an event to hang off. 0-based, matching the reel's own indexing.
        event Action<int> OnFrameEntered;
        /// The frame showing right now, or -1 if nothing is playing.
        int CurrentFrame { get; }

        /// Sample a Point (or Shape) MetaLayer's current painted position, in world space. Returns false if the
        /// layer has nothing painted on the current frame, or this view has no meta-layer data at all.
        bool TryGetMetaPoint(string layerId, out Vector2 worldPos);
    }
}

using UnityEngine;

namespace Laubrary.Chunks
{
    /// Minimal playback contract for animated chunk content: anything that can hand Chunks a sequence of frames
    /// to cycle through instead of a static/procedural sprite (e.g. a Pyre blast instance, a Zoe animation).
    /// Chunks never references Pyre or Zoetrope directly — those tools instead implement this interface and
    /// reference Chunks, so Chunks itself stays a standalone, zero-dependency package.
    public interface IChunkAnimation
    {
        /// The frames to play, in order. May be freshly built or a cached/shared array — callers must not mutate it.
        Sprite[] GetFrames();
        float Fps { get; }
        /// Loop when the frames run out, or freeze on the last frame (e.g. a one-shot fireball instance).
        bool Loop { get; }
    }
}

using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A pluggable look for a character: builds the visual onto the spawned GameObject and reports its bounds (used
    /// to size the hurtbox). Concrete implementations are <c>[Serializable]</c> and carry their own data.
    /// <see cref="SpriteView"/> is the dependency-free default that ships in core; richer views (a Launimator Lauminary, a
    /// Lazor vector shape) come from OPTIONAL bridge modules so Zoetrope core stays Combat2D-only. Assigned via
    /// <c>[SerializeReference]</c> on <see cref="Zoe"/>.
    /// </summary>
    public interface ICharacterView
    {
        /// Attach the view to <paramref name="host"/>; return the visual's world-space size (for hurtbox sizing).
        Vector2 Build(GameObject host);
    }

    /// Optional capability: a view that can say what it LOOKS like without being spawned, so a browser, a
    /// picker or a Zoe's own thumbnail can show it.
    ///
    /// Separate from ICharacterView on purpose, and optional on purpose. Core Zoetrope must not learn what a
    /// Lauminary is — a view that cannot preview itself (or has not been given art yet) simply does not implement
    /// this, and its consumers fall back to a blank. That keeps the pluggable-view rule intact: the concrete
    /// view knows its own frames, the core only knows to ask.
    public interface IPreviewableView
    {
        /// Frames worth showing, in order. One entry is a still; more can animate. Empty or null = nothing to
        /// show, which is a legitimate answer for a view with no art assigned.
        Sprite[] PreviewFrames();

        /// Playback rate for those frames. 0 when the view has no meaningful animation speed.
        float PreviewFps { get; }
    }

    /// A view that can preview a NAMED clip, not just its default frames.
    ///
    /// Separate from IPreviewableView on purpose, and gated structurally rather than by a bool: a plain
    /// SpriteView has one picture and no notion of a clip, so asking it for "the death clip" is meaningless.
    /// A caller tests for this interface and gets a compile-checked answer about what the view can actually
    /// do, instead of calling a method that silently returns the wrong frames.
    ///
    /// The consumer that needs it: previewing a Zoe EVENT's own visual, where the event names the clip.
    public interface IClipPreviewableView
    {
        /// Frames of `clip`, in order. An unknown or empty clip name falls back to the view's default
        /// frames rather than returning nothing — a preview that goes blank because a clip was renamed is
        /// less useful than one showing the idle pose.
        Sprite[] PreviewFrames(string clip);

        /// Playback rate for that clip.
        float PreviewFpsOf(string clip);
    }

    /// <summary>The default look: one static sprite. No dependency beyond UnityEngine, so it lives in core.</summary>
    [System.Serializable]
    public class SpriteView : ICharacterView, IPreviewableView
    {
        public Sprite sprite;
        [Min(0.01f)] public float scale = 1f;

        public Sprite[] PreviewFrames() => sprite != null ? new[] { sprite } : System.Array.Empty<Sprite>();
        public float PreviewFps => 0f;

        public Vector2 Build(GameObject host)
        {
            var sr = host.AddComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sr.sprite = sprite;
                host.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                return sprite.bounds.size;
            }
            return Vector2.one;
        }
    }
}

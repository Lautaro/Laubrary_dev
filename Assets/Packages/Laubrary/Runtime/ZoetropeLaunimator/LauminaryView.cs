using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// An <see cref="ICharacterView"/> that renders a character as an animated Launimator lauminary — plays an idle clip on
    /// spawn, optionally fitted to a target height. The OPTIONAL Launimator presentation bridge: pulled in only by
    /// projects that have Launimator, so Zoetrope core stays Combat2D-only. Assign it to a <c>Zoe.view</c>
    /// in place of the default <c>SpriteView</c> to give an enemy real animation.
    /// </summary>
    [System.Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, null, "ReelView")]
    public class LauminaryView : ICharacterView, IPreviewableView, IClipPreviewableView
    {
        [Tooltip("The lauminary version to render.")]
        public LauminaryVersion version;
        [Tooltip("Animation to play while idle (case-insensitive; falls back to the first clip).")]
        public string idleClip = "Idle";
        [Tooltip("Scale the lauminary so its resting frame is this many world units tall (0 = leave native size).")]
        public float height = 0f;

        // ── IPreviewableView ── shared with ZonedLauminaryView, so the same character never previews two ways.
        // Note idleClip defaults to "Idle" here, and most lauminaries have no clip by that name — the fallback to
        // the first animation with frames is what makes these previewable at all.
        public Sprite[] PreviewFrames() => LauminaryPreview.Frames(version, idleClip);
        public float PreviewFps => LauminaryPreview.Fps(version, idleClip);

        // Clip-aware preview: an empty or unknown clip falls back to the idle frames, so a renamed clip
        // degrades to "shows the wrong animation" rather than "shows nothing".
        public Sprite[] PreviewFrames(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return PreviewFrames();
            var f = LauminaryPreview.Frames(version, clip);
            return f != null && f.Length > 0 ? f : PreviewFrames();
        }

        public float PreviewFpsOf(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return PreviewFps;
            float fps = LauminaryPreview.Fps(version, clip);
            return fps > 0f ? fps : PreviewFps;
        }

        public float[] PreviewFrameSecondsOf(string clip) =>
            LauminaryPreview.FrameSeconds(version, string.IsNullOrEmpty(clip) ? idleClip : clip);

        public Vector2 Build(GameObject host)
        {
            var p = host.AddComponent<LauminaryPlayer>();   // RequireComponent adds the SpriteRenderer
            p.SetVersion(version);
            p.startClip = idleClip;
            p.playOnStart = true;
            if (height > 0f) p.FitToHeight(height);
            var rest = p.RestingSprite;
            return rest != null ? (Vector2)rest.bounds.size : Vector2.one;
        }
    }
}

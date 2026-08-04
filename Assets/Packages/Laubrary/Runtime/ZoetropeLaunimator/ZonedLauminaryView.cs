using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// An <see cref="ICharacterView"/> that renders a character via <see cref="ZonedAnimationPlayer"/> instead
    /// of the simpler <see cref="LauminaryPlayer"/> that <see cref="LauminaryView"/> uses — for characters that need its
    /// extra capabilities: a one-shot clip (<c>loop: false</c>) that automatically holds on its last frame
    /// instead of looping or resetting (no zone authoring needed — that's ZonedAnimationPlayer's plain-clip
    /// behavior out of the box), and <see cref="ZonedAnimationPlayer.TryGetMetaPoint"/> to read a named
    /// MetaLayer's painted point each frame (a muzzle exit point, a blade position, ...). Assign it to a
    /// <c>Zoe.view</c> in place of <see cref="LauminaryView"/> when a character's animation needs either.
    /// </summary>
    [System.Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, null, "ZonedReelView")]
    public class ZonedLauminaryView : ICharacterView, IPreviewableView, IClipPreviewableView
    {
        [Tooltip("The lauminary version to render.")]
        public LauminaryVersion version;
        [Tooltip("Animation to play while idle (case-insensitive). Left empty = whatever frame 0 of the first " +
                 "animation is; the game logic is expected to drive Play/EnterAt itself for anything beyond that.")]
        public string idleClip = "";
        [Tooltip("Scale the lauminary so its resting frame is this many world units tall (0 = leave native size).")]
        public float height = 0f;

        // ── IPreviewableView ── shared with LauminaryView, so the same character never previews two ways.
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

        public Vector2 Build(GameObject host)
        {
            var p = host.AddComponent<ZonedAnimationPlayer>();   // RequireComponent adds the SpriteRenderer
            p.SetVersion(version);
            if (!string.IsNullOrEmpty(idleClip)) p.Play(idleClip);

            host.AddComponent<CueRelay>();   // the ICueSink ZoeSpawner/EquipWeapon look for — RequireComponent
                                              // is satisfied since the ZonedAnimationPlayer above already exists
            host.AddComponent<AnimatedViewRelay>();   // the IAnimatedView HitReactionPlayer/TargetPracticeController look for

            var sprite = p.CurrentSprite;
            if (height > 0f && sprite != null)
            {
                float h = sprite.bounds.size.y;
                if (h > 1e-4f) host.transform.localScale = Vector3.one * (height / h);
            }
            return sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
        }
    }
}

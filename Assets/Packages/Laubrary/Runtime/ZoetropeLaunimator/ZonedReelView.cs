using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// An <see cref="ICharacterView"/> that renders a character via <see cref="ZonedAnimationPlayer"/> instead
    /// of the simpler <see cref="ReelPlayer"/> that <see cref="ReelView"/> uses — for characters that need its
    /// extra capabilities: a one-shot clip (<c>loop: false</c>) that automatically holds on its last frame
    /// instead of looping or resetting (no zone authoring needed — that's ZonedAnimationPlayer's plain-clip
    /// behavior out of the box), and <see cref="ZonedAnimationPlayer.TryGetMetaPoint"/> to read a named
    /// MetaLayer's painted point each frame (a muzzle exit point, a blade position, ...). Assign it to a
    /// <c>Zoe.view</c> in place of <see cref="ReelView"/> when a character's animation needs either.
    /// </summary>
    [System.Serializable]
    public class ZonedReelView : ICharacterView
    {
        [Tooltip("The reel version to render.")]
        public ReelVersion version;
        [Tooltip("Animation to play while idle (case-insensitive). Left empty = whatever frame 0 of the first " +
                 "animation is; the game logic is expected to drive Play/EnterAt itself for anything beyond that.")]
        public string idleClip = "";
        [Tooltip("Scale the reel so its resting frame is this many world units tall (0 = leave native size).")]
        public float height = 0f;

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

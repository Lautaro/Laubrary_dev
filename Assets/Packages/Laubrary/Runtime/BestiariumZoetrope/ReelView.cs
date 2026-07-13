using UnityEngine;
using Laubrary.Bestiarium;
using Laubrary.Launimator;

namespace Laubrary.BestiariumZoetrope
{
    /// <summary>
    /// An <see cref="ICharacterView"/> that renders a character as an animated Launimator reel — plays an idle clip on
    /// spawn, optionally fitted to a target height. The OPTIONAL Launimator presentation bridge: pulled in only by
    /// projects that have Launimator, so Bestiarium core stays Combat2D-only. Assign it to a <c>CharacterDef.view</c>
    /// in place of the default <c>SpriteView</c> to give an enemy real animation.
    /// </summary>
    [System.Serializable]
    public class ReelView : ICharacterView
    {
        [Tooltip("The reel version to render.")]
        public ReelVersion version;
        [Tooltip("Animation to play while idle (case-insensitive; falls back to the first clip).")]
        public string idleClip = "Idle";
        [Tooltip("Scale the reel so its resting frame is this many world units tall (0 = leave native size).")]
        public float height = 0f;

        public Vector2 Build(GameObject host)
        {
            var p = host.AddComponent<ReelPlayer>();   // RequireComponent adds the SpriteRenderer
            p.SetVersion(version);
            p.startClip = idleClip;
            p.playOnStart = true;
            if (height > 0f) p.FitToHeight(height);
            var rest = p.RestingSprite;
            return rest != null ? (Vector2)rest.bounds.size : Vector2.one;
        }
    }
}

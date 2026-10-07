using System.Linq;
using UnityEngine;
using Laubrary.Chunks;
using Laubrary.PreviewKit;

namespace Laubrary.Launimator
{
    /// Adapts one named animation of a LauminaryVersion so Chunks can spawn instances of it. Picked by name (not a
    /// direct Laumination reference) so it keeps working across rebakes/re-versioning of the lauminary. Implements
    /// IVisualPreview by cropping the animation's own baked frames out of its atlas — the same frames LauminaryPlayer
    /// shows in play mode, no separate render path.
    [CreateAssetMenu(menuName = "Laubrary/Launimator/Lauminary Chunk Animation", fileName = "LauminaryChunkAnim")]
    public class LauminaryAnimationChunkAdapter : ScriptableObject, IChunkAnimation, IVisualPreview
    {
        [Tooltip("The lauminary version to pull the animation from.")]
        public LauminaryVersion version;
        [Tooltip("Name of the animation within that version to play on each chunk (case-insensitive).")]
        public string animationName;
        [Tooltip("Loop the animation for the chunk's whole lifetime, or play once and freeze on the last frame.")]
        public bool loop = false;

        Laumination Find()
        {
            if (version == null || version.animations == null || string.IsNullOrEmpty(animationName)) return null;
            foreach (var a in version.animations)
                if (a != null && string.Equals(a.name, animationName, System.StringComparison.OrdinalIgnoreCase))
                    return a;
            return null;
        }

        public Sprite[] GetFrames()
        {
            var def = Find();
            return def != null && def.frames != null ? def.frames.ToArray() : System.Array.Empty<Sprite>();
        }

        public float Fps { get { var d = Find(); return d != null ? d.fps : 12f; } }
        public bool Loop => loop;

        public Texture2D RenderPreviewTexture()
        {
            var def = Find();
            if (def == null || def.frames == null || def.frames.Count == 0) return null;
            return CropSpriteTexture(def.frames[def.frames.Count / 2]);
        }

        public bool CanAnimatePreview { get { var d = Find(); return d != null && d.frames != null && d.frames.Count > 1; } }
        public float PreviewFps => Fps;

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            var def = Find();
            if (def == null || def.frames == null || def.frames.Count <= 1 || tex == null) return;
            int frame = def.HasFrameTimings
                ? def.FrameAt((float)time)
                : Mathf.FloorToInt((float)(time * Mathf.Max(1f, def.fps))) % def.frames.Count;
            var s = def.frames[frame];
            if (s == null || s.texture == null) return;
            var r = s.textureRect;
            if (tex.width != (int)r.width || tex.height != (int)r.height) tex.Reinitialize((int)r.width, (int)r.height);
            tex.SetPixels(s.texture.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height));
            tex.Apply();
        }

        static Texture2D CropSpriteTexture(Sprite s)
        {
            if (s == null || s.texture == null) return null;
            var r = s.textureRect;
            var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(s.texture.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height));
            tex.Apply();
            return tex;
        }
    }
}

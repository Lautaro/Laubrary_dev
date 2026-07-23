using UnityEngine;
using Laubrary.PreviewKit;

namespace Laubrary.Chunks
{
    /// The trivial IChunkAnimation: a single static sprite, no bridge module required. For the common case
    /// where a chunk (or an AmmoDef's visual) just needs a plain sprite rather than a Pyre blast or a Launimator
    /// reel — Chunks ships this itself, keeping it a standalone, zero-dependency package (see IChunkAnimation's
    /// own doc comment).
    [CreateAssetMenu(menuName = "Laubrary/Chunks/Sprite Chunk Animation", fileName = "SpriteChunkAnim")]
    public class SpriteChunkAnimation : ScriptableObject, IChunkAnimation, IVisualPreview
    {
        public Sprite sprite;

        public Sprite[] GetFrames() => new[] { sprite };
        public float Fps => 0f;
        public bool Loop => true;

        // A plain sprite has nothing to animate — one static frame, cropped from the sprite's own rect so the
        // preview matches what actually renders (not the whole source sheet it might live in).
        public Texture2D RenderPreviewTexture()
        {
            if (sprite == null || sprite.texture == null || !sprite.texture.isReadable) return null;
            var srcTex = sprite.texture;
            var r = sprite.rect;
            var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(srcTex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height));
            tex.Apply();
            return tex;
        }

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}

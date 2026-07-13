using UnityEngine;

namespace Laubrary.Chunks
{
    /// The trivial IChunkAnimation: a single static sprite, no bridge module required. For the common case
    /// where a chunk (or an AmmoDef's visual) just needs a plain sprite rather than a Pyre blast or a Launimator
    /// reel — Chunks ships this itself, keeping it a standalone, zero-dependency package (see IChunkAnimation's
    /// own doc comment).
    [CreateAssetMenu(menuName = "Laubrary/Chunks/Sprite Chunk Animation", fileName = "SpriteChunkAnim")]
    public class SpriteChunkAnimation : ScriptableObject, IChunkAnimation
    {
        public Sprite sprite;

        public Sprite[] GetFrames() => new[] { sprite };
        public float Fps => 0f;
        public bool Loop => true;
    }
}

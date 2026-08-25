using UnityEngine;
using Laubrary.Chunks;
using Laubrary.PreviewKit;

namespace Laubrary.Pyre
{
    /// Adapts a Pyre so any [RequireInterface(typeof(IChunkAnimation))] field (Chunks, Zoetrope's
    /// AmmoDef/WeaponDef, etc.) can pick it as a visual — e.g. a Pyre explosion reused as flung chunk
    /// debris, or a weapon's projectile FX. Byte-identical structure to Pyre1's own PyreChunkAnimation: reuses
    /// PyreRenderer's own frame cache, so it renders identically to (and shares the cache with) anything
    /// else asking PyreRenderer for this spec's frames. This is deliberately the ONLY path other Laubrary
    /// tools consume a Pyre through — it never touches PyreBaker's baked PNG/sub-sprites, which
    /// stay a separate, standalone export. A Pyre is picked here (via LauAsset's type-constrained
    /// picker), never a raw baked sprite.
    [CreateAssetMenu(menuName = "Laubrary/Pyre/Pyre Chunk Animation", fileName = "PyreChunkAnim")]
    public class PyreChunkAnimation : ScriptableObject, IChunkAnimation, IVisualPreview
    {
        [Tooltip("The Pyre effect to play on each chunk.")]
        public Pyre spec;
        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;
        [Tooltip("Loop the effect for the chunk's whole lifetime, or play once and freeze on the last frame.")]
        public bool loop = false;

        public Sprite[] GetFrames() => PyreRenderer.GetFrames(spec);
        public float Fps => fps;
        public bool Loop => loop;

        public Texture2D RenderPreviewTexture()
        {
            if (spec == null) return null;
            int mid = Mathf.Clamp(spec.frameCount / 2, 0, Mathf.Max(0, spec.frameCount - 1));
            return PyreRenderer.RenderFrameTexture(spec, mid);
        }

        public bool CanAnimatePreview => spec != null && spec.frameCount > 1;
        public float PreviewFps => fps;

        // Same fix as PyreWindow's own thumbnail animation: pull from GetFrames' cached Sprite[] (each frame
        // rendered once, lazily, and reused) instead of calling RenderFrame fresh every tick.
        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (spec == null || spec.frameCount <= 1 || tex == null) return;
            var frames = PyreRenderer.GetFrames(spec);
            if (frames == null || frames.Length == 0) return;
            int frame = Mathf.FloorToInt((float)(time * Mathf.Max(1f, fps))) % frames.Length;
            var frameTex = frames[frame]?.texture;
            if (frameTex == null) return;
            tex.SetPixels32(frameTex.GetPixels32());
            tex.Apply();
        }
    }
}

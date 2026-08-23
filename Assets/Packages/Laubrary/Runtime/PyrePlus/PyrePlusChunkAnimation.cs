using UnityEngine;
using Laubrary.Chunks;
using Laubrary.PreviewKit;

namespace Laubrary.PyrePlus
{
    /// Adapts a PyrePlusSpec so any [RequireInterface(typeof(IChunkAnimation))] field (Chunks, Zoetrope's
    /// AmmoDef/WeaponDef, etc.) can pick it as a visual — e.g. a PyrePlus explosion reused as flung chunk
    /// debris, or a weapon's projectile FX. Byte-identical structure to Pyre1's own PyreChunkAnimation: reuses
    /// PyrePlusRenderer's own frame cache, so it renders identically to (and shares the cache with) anything
    /// else asking PyrePlusRenderer for this spec's frames. This is deliberately the ONLY path other Laubrary
    /// tools consume a PyrePlusSpec through — it never touches PyrePlusBaker's baked PNG/sub-sprites, which
    /// stay a separate, standalone export. A PyrePlusSpec is picked here (via LauAsset's type-constrained
    /// picker), never a raw baked sprite.
    [CreateAssetMenu(menuName = "Laubrary/PyrePlus/PyrePlus Chunk Animation", fileName = "PyrePlusChunkAnim")]
    public class PyrePlusChunkAnimation : ScriptableObject, IChunkAnimation, IVisualPreview
    {
        [Tooltip("The PyrePlus effect to play on each chunk.")]
        public PyrePlusSpec spec;
        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;
        [Tooltip("Loop the effect for the chunk's whole lifetime, or play once and freeze on the last frame.")]
        public bool loop = false;

        public Sprite[] GetFrames() => PyrePlusRenderer.GetFrames(spec);
        public float Fps => fps;
        public bool Loop => loop;

        public Texture2D RenderPreviewTexture()
        {
            if (spec == null) return null;
            int mid = Mathf.Clamp(spec.frameCount / 2, 0, Mathf.Max(0, spec.frameCount - 1));
            return PyrePlusRenderer.RenderFrameTexture(spec, mid);
        }

        public bool CanAnimatePreview => spec != null && spec.frameCount > 1;
        public float PreviewFps => fps;

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (spec == null || spec.frameCount <= 1 || tex == null) return;
            int frame = Mathf.FloorToInt((float)(time * Mathf.Max(1f, fps))) % spec.frameCount;
            tex.SetPixels32(PyrePlusRenderer.RenderFrame(spec, frame));
            tex.Apply();
        }
    }
}

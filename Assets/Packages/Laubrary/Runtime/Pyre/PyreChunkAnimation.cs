using UnityEngine;
using Laubrary.Chunks;
using Laubrary.PreviewKit;

namespace Laubrary.Pyre
{
    /// Adapts a Pyre so Chunks can spawn instances of it — e.g. fireballs shot in a line from a jet engine,
    /// or an explosion sprite reused as flung debris. Reuses BlastPlayer's own frame cache, so it renders
    /// identically to (and shares the cache with) a normal BlastPlayer showing the same spec. Implements
    /// IVisualPreview via BlastRenderer directly — the same renderer PyreWindow's own thumbnails use.
    [CreateAssetMenu(menuName = "Laubrary/Pyre/Blast Chunk Animation", fileName = "BlastChunkAnim")]
    public class PyreChunkAnimation : ScriptableObject, IChunkAnimation, IVisualPreview
    {
        [Tooltip("The blast to play on each chunk.")]
        public Pyre spec;
        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;
        [Tooltip("Loop the blast for the chunk's whole lifetime, or play once and freeze on the last frame.")]
        public bool loop = false;

        public Sprite[] GetFrames() => BlastPlayer.GetFrames(spec);
        public float Fps => fps;
        public bool Loop => loop;

        public Texture2D RenderPreviewTexture()
        {
            if (spec == null) return null;
            int mid = Mathf.Clamp(spec.frameCount / 2, 0, Mathf.Max(0, spec.frameCount - 1));
            return BlastRenderer.RenderFrameTexture(spec, mid);
        }

        public bool CanAnimatePreview => spec != null && spec.frameCount > 1;
        public float PreviewFps => fps;

        public void UpdateAnimatedPreview(Texture2D tex, double time)
        {
            if (spec == null || spec.frameCount <= 1 || tex == null) return;
            int frame = Mathf.FloorToInt((float)(time * Mathf.Max(1f, fps))) % spec.frameCount;
            tex.SetPixels32(BlastRenderer.RenderFrame(spec, frame));
            tex.Apply();
        }
    }
}

using UnityEngine;
using Laubrary.Chunks;
using Laubrary.PreviewKit;

namespace Laubrary.Pyre
{
    /// Adapts a Pyre so anything holding an <see cref="IChunkEffectSpawner"/> slot (Chunks' Pyre Spawn module,
    /// its Spawn Formation) can SPAWN a live blast — the spawning counterpart of
    /// <see cref="PyreChunkAnimation"/>, which only hands over FRAMES for somebody else to play on a chunk.
    /// Both exist for the same hard reason: Pyre references Chunks (PyreChunkAnimation implements
    /// IChunkAnimation), so Chunks can never reference Pyre back — the Pyre-side implementation of the
    /// interface has to live here, in Pyre, and Chunks only ever sees the interface.
    ///
    /// The spawn itself is byte-for-byte the lifetime discipline SpawnPyreFx already uses: take a pooled
    /// PyreBlastPlayer, configure it, subscribe Finished ONCE, and in that handler unsubscribe, restore the
    /// pooled object to unit scale / no rotation, and release it. Nothing outside this class ever destroys or
    /// releases what SpawnEffect returns — callers only read and move it while it is alive.
    [CreateAssetMenu(menuName = "Laubrary/Pyre/Pyre Spawn Source", fileName = "PyreSpawnSource")]
    public class PyreSpawnSource : ScriptableObject, IChunkEffectSpawner, IVisualPreview
    {
        [Tooltip("The Pyre effect spawned at each point. Nothing spawns while this is empty.")]
        public Pyre spec;

        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;

        [Tooltip("Loop instead of playing once. A looping blast never finishes on its own, so it is released " +
                 "after Loop For seconds instead.")]
        public bool loop = false;

        [Tooltip("Loop only: roughly how long a looping blast keeps going. It stops looping after this and " +
                 "plays out the cycle it is in, so the effect always ends on its last frame rather than " +
                 "cutting mid-animation. 0 means never stop — the instance is then held for the whole " +
                 "session, which is only right for something the game tears down another way.")]
        [Min(0f)] public float loopSeconds = 2f;

        /// Spawn one blast at worldPos, angled by rotationDeg, at a uniform scale. Returns the spawned
        /// transform so a caller (Chunks' Pyre Movement module) can fly it, or null when there is no spec to
        /// play. A NaN rotation means "no rotation", matching how an omni-directional burst reports itself.
        /// The body lives in PyreDirectSpawn so this wrapper and a DIRECTLY-picked Pyre spawn through one
        /// implementation. It is not shared for tidiness: the pooled lifetime discipline it carries is the
        /// part that goes wrong quietly (a held instance is a leak nobody sees until the pool has grown for
        /// hours), and two copies of it would drift. This wrapper's job is now purely to supply the fps /
        /// loop / loopSeconds overrides a directly-picked Pyre cannot carry.
        public Transform SpawnEffect(Vector3 worldPos, float rotationDeg, float scale,
                                     string sortingLayerName, int sortingOrder)
            => PyreDirectSpawn.Spawn(spec, fps, loop, loopSeconds,
                                     worldPos, rotationDeg, scale, sortingLayerName, sortingOrder);

        // ── IVisualPreview ───────────────────────────────────────────────────────
        // Identical to PyreChunkAnimation's: this is a VISUAL asset, so its thumbnail must always resolve in a
        // LauAsset picker or browser (the UI guide's "a blank thumbnail is worse than none"), and it must come
        // from the SAME deterministic renderer Pyre's own editing preview uses — never a second render path.

        public Texture2D RenderPreviewTexture()
        {
            if (spec == null) return null;
            int mid = Mathf.Clamp(spec.frameCount / 2, 0, Mathf.Max(0, spec.frameCount - 1));
            return PyreRenderer.RenderFrameTexture(spec, mid);
        }

        public bool CanAnimatePreview => spec != null && spec.frameCount > 1;
        public float PreviewFps => fps;

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

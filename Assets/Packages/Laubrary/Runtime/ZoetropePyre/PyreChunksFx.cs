using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Pyre;
using Laubrary.Chunks;
using ChunksFx = Laubrary.Chunks.Chunks;   // the class is shadowed by the namespace inside a Laubrary.* namespace

namespace Laubrary.ZoetropePyre
{
    /// <summary>
    /// An <see cref="ICombatFx"/> that plays a Pyre blast + a Chunks debris burst at a world point — the OPTIONAL
    /// presentation bridge that lets a Zoetrope Def use Pyre/Chunks effects. Only projects that include Pyre +
    /// Chunks pull this module in; Zoetrope core stays Combat2D-only. (This is the old coupled <c>CombatVfx</c>,
    /// now a pluggable effect in its own module — the whole point of the decouple.)
    /// </summary>
    [System.Serializable]
    public class PyreChunksFx : ICombatFx
    {
        [Tooltip("Pyre explosion to play once at the point (optional).")]
        public BlastSpec blast;
        [Tooltip("Playback speed for the blast, frames per second.")]
        public float blastFps = 24f;
        [Tooltip("Chunks debris burst to throw at the point (optional). Uses the hit direction when given.")]
        public ChunkSpec chunks;
        [Tooltip("Sorting order for the spawned blast sprite (above the characters).")]
        public int sortingOrder = 10;

        public bool IsEmpty => blast == null && chunks == null;

        /// Spawn the blast + debris at a world point. directionDeg (NaN = omni) aims directional chunk bursts.
        public void Play(Vector2 worldPos, float directionDeg = float.NaN)
        {
            if (blast != null)
            {
                var go = new GameObject("PyreBlast");
                go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
                var bp = go.AddComponent<BlastPlayer>();   // RequireComponent adds the SpriteRenderer
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sortingOrder = sortingOrder;
                bp.spec = blast;
                bp.fps = blastFps > 0f ? blastFps : 24f;
                bp.loop = false;
                bp.destroyOnFinish = true;
                bp.Play();   // spec was null at Awake (playOnAwake no-op), so kick it off now
            }
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, directionDeg);
        }
    }
}

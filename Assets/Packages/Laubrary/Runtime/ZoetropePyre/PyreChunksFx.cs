using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Pyre;
using PyreAsset = Laubrary.Pyre.Pyre;   // the class is shadowed by the namespace inside a Laubrary.* namespace
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
        public PyreAsset blast;
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
            SpawnBlast(worldPos);
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, directionDeg);
        }

        /// Same as <see cref="Play"/>, but returns the blast's Transform for follow-tracking (see
        /// <see cref="FxFollowTarget"/>). Chunks still burst once at the initial point regardless — a scatter
        /// of independently-moving debris has no single Transform to hand back. Null if no blast is configured.
        public Transform PlayFollowable(Vector2 worldPos, float directionDeg = float.NaN)
        {
            var bp = SpawnBlast(worldPos);
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, directionDeg);
            return bp != null ? bp.transform : null;
        }

        BlastPlayer SpawnBlast(Vector2 worldPos)
        {
            if (blast == null) return null;

            var bp = PyreBlastPool.Get();   // pooled: pooled=true already set by the pool's factory
            bp.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            var sr = bp.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = sortingOrder;
            bp.spec = blast;
            bp.fps = blastFps > 0f ? blastFps : 24f;
            bp.loop = false;

            System.Action onFinished = null;
            onFinished = () => { bp.Finished -= onFinished; PyreBlastPool.Release(bp); };
            bp.Finished += onFinished;
            bp.Play();
            return bp;
        }
    }
}

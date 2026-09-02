using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Shaper;
using Laubrary.Chunks;
using ChunksFx = Laubrary.Chunks.Chunks;   // the class is shadowed by the namespace inside a Laubrary.* namespace

namespace Laubrary.ZoetropeShaper
{
    /// <summary>
    /// An <see cref="ICombatFx"/> that plays a baked Shaper animation + a Chunks debris burst at a world
    /// point — the OPTIONAL presentation bridge that lets a Zoetrope Def use Shaper/Chunks effects together,
    /// the Shaper counterpart of <see cref="Laubrary.ZoetropePyre.PyreChunksFx"/>. Copied field-for-field and
    /// behaviour-for-behaviour from that class. Only projects that include Shaper + Chunks pull this module
    /// in; Zoetrope core stays Combat2D-only.
    /// </summary>
    [System.Serializable]
    public class ShaperChunksFx : ICombatFx
    {
        [Tooltip("Baked Shaper animation to play once at the point (optional).")]
        public ShaperClip clip;
        [Tooltip("Playback speed override for the animation, frames per second. 0 uses the clip's own authored rate.")]
        public float clipFpsOverride;
        [Tooltip("Chunks debris burst to throw at the point (optional). Uses the hit direction when given.")]
        public ChunkSpec chunks;
        [Tooltip("Sorting order for the spawned sprite (above the characters).")]
        public int sortingOrder = 10;

        public bool IsEmpty => clip == null && chunks == null;

        /// Spawn the animation + debris at a world point. directionDeg (NaN = omni) aims directional chunk bursts.
        public void Play(Vector2 worldPos, float directionDeg = float.NaN)
        {
            SpawnClip(worldPos);
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, directionDeg);
        }

        /// Same as <see cref="Play"/>, but returns the animation's Transform for follow-tracking (see
        /// <see cref="FxFollowTarget"/>). Chunks still burst once at the initial point regardless — a scatter
        /// of independently-moving debris has no single Transform to hand back. Null if no clip is configured.
        public Transform PlayFollowable(Vector2 worldPos, float directionDeg = float.NaN)
        {
            var sp = SpawnClip(worldPos);
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, directionDeg);
            return sp != null ? sp.transform : null;
        }

        ShaperPlayer SpawnClip(Vector2 worldPos)
        {
            if (clip == null) return null;

            var sp = ShaperPlayerPool.Get();   // pooled: pooled=true already set by the pool's factory
            sp.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            var sr = sp.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = sortingOrder;
            sp.clip = clip;
            sp.fps = clipFpsOverride;
            sp.loop = false;

            System.Action onFinished = null;
            onFinished = () => { sp.Finished -= onFinished; ShaperPlayerPool.Release(sp); };
            sp.Finished += onFinished;
            sp.Play();
            return sp;
        }
    }
}

using UnityEngine;
using Laubrary.Pyre;
using Laubrary.Chunks;
using ChunksFx = Laubrary.Chunks.Chunks;   // the class is shadowed by the namespace inside a Laubrary.* namespace

namespace Laubrary.Codex
{
    /// One reusable "visual event": a Pyre blast + a Chunks burst played together at a world point (with an optional
    /// direction for directional debris). It's the shared building block for every Def's VFX slot — a Character's hit
    /// and death, a Weapon's muzzle, a Projectile's impact. Pure data + a Play() that spawns the effects; later this
    /// grows a Zound ref so audio drops in when the Zounds engine is present.
    [System.Serializable]
    public class CombatVfx
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

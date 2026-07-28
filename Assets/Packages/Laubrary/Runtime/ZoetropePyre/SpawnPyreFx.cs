using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Pyre;
using PyreAsset = Laubrary.Pyre.Pyre;   // the class is shadowed by the namespace inside a Laubrary.* namespace

namespace Laubrary.ZoetropePyre
{
    /// <summary>
    /// An <see cref="IEffect"/> that spawns ONE Pyre blast at the event's resolved position, SCALED by the event's
    /// resolved scalar param — the Spawn-Pyre entry of the Zoe-event effect palette (ZOE_EVENTS_DESIGN.md step 3).
    /// It is the Pyre-only counterpart of the older bundled <see cref="PyreChunksFx"/> (which STAYS as the combined
    /// blast+debris <see cref="ICombatFx"/>, still referenced by committed assets), promoted to a first-class
    /// palette effect that reads its spawn point AND its size from the <see cref="EventContext"/> the trigger fills
    /// — the new scalar-sizing capability over the fixed-size PyreChunksFx. Lives in the ZoetropePyre bridge module
    /// so Zoetrope core stays Pyre-free.
    /// </summary>
    [System.Serializable]
    public class SpawnPyreFx : IEffect, IEventParamUser
    {
        /// Reads a POSITION (where to spawn the blast), a DIRECTION (which way to ANGLE it — so an asymmetric blast
        /// can glance off a surface, and the Centre-Angle option can point it at the Zoe's middle) and a SCALAR (how
        /// big). So the Zoe-event editor shows all three pickers. A symmetric blast simply looks the same rotated.
        public EventParam UsedParams => EventParam.Position | EventParam.Direction | EventParam.Scalar;

        [Tooltip("Pyre explosion to play once at the resolved position (optional).")]
        public PyreAsset blast;
        [Tooltip("Playback speed for the blast, frames per second.")]
        public float blastFps = 24f;
        [Tooltip("Sorting order for the spawned blast sprite (above the characters).")]
        public int sortingOrder = 10;

        [Tooltip("Base uniform scale applied to the blast when the scalar param contributes nothing " +
                 "(Scale per amount = 0). 1 = the blast's authored size.")]
        public float baseScale = 1f;
        [Tooltip("Extra uniform scale added per unit of the event's resolved SCALAR param (the scalar the entry " +
                 "picks, e.g. the damage amount). 0 = fixed size (identical to the old PyreChunksFx blast). " +
                 "Final scale = baseScale + scalePerAmount * scalar, clamped to a small minimum.")]
        public float scalePerAmount = 0f;

        public bool IsEmpty => blast == null;

        /// The uniform scale this effect spawns the blast at for a given resolved scalar value:
        /// baseScale + scalePerAmount * scalar, clamped to a small minimum. Its own method so the sizing
        /// contract is unit-testable without a live (play-mode-only) blast pool.
        public float ResolveScale(float scalar) => Mathf.Max(0.01f, baseScale + scalePerAmount * scalar);

        public void Apply(EventContext ctx)
        {
            if (blast == null) return;
            float scale = ResolveScale(ctx.Scalar);

            var bp = PyreBlastPool.Get();   // pooled: pooled=true already set by the pool's factory
            bp.transform.position = new Vector3(ctx.Position.x, ctx.Position.y, 0f);
            // Angle the blast by the resolved direction (task #12/#5): NaN (omni / None) leaves it upright.
            bp.transform.rotation = float.IsNaN(ctx.DirectionDeg) ? Quaternion.identity
                                                                  : Quaternion.Euler(0f, 0f, ctx.DirectionDeg);
            bp.transform.localScale = Vector3.one * scale;
            var sr = bp.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = sortingOrder;
            bp.spec = blast;
            bp.fps = blastFps > 0f ? blastFps : 24f;
            bp.loop = false;

            System.Action onFinished = null;
            onFinished = () =>
            {
                bp.Finished -= onFinished;
                bp.transform.localScale = Vector3.one;      // hand the pooled blast back at unit scale
                bp.transform.rotation = Quaternion.identity; // …and unrotated
                PyreBlastPool.Release(bp);
            };
            bp.Finished += onFinished;
            bp.Play();
        }
    }
}

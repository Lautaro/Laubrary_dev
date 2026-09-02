using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Shaper;

namespace Laubrary.ZoetropeShaper
{
    /// <summary>
    /// An <see cref="IEffect"/> that spawns ONE baked Shaper animation at the event's resolved position,
    /// SCALED by the event's resolved scalar param — the Spawn-Shaper entry of the Zoe-event effect palette
    /// (ZOE_EVENTS_DESIGN.md step 3), the Shaper counterpart of <see cref="Laubrary.ZoetropePyre.SpawnPyreFx"/>.
    /// Copied field-for-field and behaviour-for-behaviour from that class so a Laubrary user who already
    /// knows Spawn-Pyre gets Spawn-Shaper for free. Lives in the ZoetropeShaper bridge module so Zoetrope core
    /// stays Shaper-free (same reasoning as ZoetropePyre keeping core Pyre-free).
    ///
    /// Per the Zoe-palette rule (project CLAUDE.md, "Zoe palette — a character shows things only through its
    /// declared list"): this is a declared-list ENTRY a Zoe raises by name, never a component bolted onto a
    /// character or a raw ShaperPlayer spawn reached around the palette.
    /// </summary>
    [System.Serializable]
    public class SpawnShaperFx : IEffect, IEventParamUser
    {
        /// Reads a POSITION (where to spawn the animation), a DIRECTION (which way to ANGLE it — so an
        /// asymmetric shape can glance off a surface, and the Centre-Angle option can point it at the Zoe's
        /// middle) and a SCALAR (how big). So the Zoe-event editor shows all three pickers. A symmetric shape
        /// simply looks the same rotated.
        public EventParam UsedParams => EventParam.Position | EventParam.Direction | EventParam.Scalar;

        [Tooltip("Baked Shaper animation to play once at the resolved position (optional).")]
        public ShaperClip clip;
        [Tooltip("Playback speed override, frames per second. 0 uses the clip's own authored rate.")]
        public float fpsOverride;
        [Tooltip("Sorting order for the spawned sprite (above the characters).")]
        public int sortingOrder = 10;

        [Tooltip("Base uniform scale applied to the animation when the scalar param contributes nothing " +
                 "(Scale per amount = 0). 1 = the clip's baked size.")]
        public float baseScale = 1f;
        [Tooltip("Extra uniform scale added per unit of the event's resolved SCALAR param (the scalar the entry " +
                 "picks, e.g. the damage amount). 0 = fixed size. " +
                 "Final scale = baseScale + scalePerAmount * scalar, clamped to a small minimum.")]
        public float scalePerAmount = 0f;

        public bool IsEmpty => clip == null;

        /// The uniform scale this effect spawns the animation at for a given resolved scalar value:
        /// baseScale + scalePerAmount * scalar, clamped to a small minimum. Its own method so the sizing
        /// contract is unit-testable without a live (play-mode-only) player pool.
        public float ResolveScale(float scalar) => Mathf.Max(0.01f, baseScale + scalePerAmount * scalar);

        public void Apply(EventContext ctx)
        {
            if (clip == null) return;
            float scale = ResolveScale(ctx.Scalar);

            var sp = ShaperPlayerPool.Get();   // pooled: pooled=true already set by the pool's factory
            sp.transform.position = new Vector3(ctx.Position.x, ctx.Position.y, 0f);
            // Angle by the resolved direction: NaN (omni / None) leaves it upright.
            sp.transform.rotation = float.IsNaN(ctx.DirectionDeg) ? Quaternion.identity
                                                                  : Quaternion.Euler(0f, 0f, ctx.DirectionDeg);
            sp.transform.localScale = Vector3.one * scale;
            var sr = sp.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = sortingOrder;
            sp.clip = clip;
            sp.fps = fpsOverride;
            sp.loop = false;

            System.Action onFinished = null;
            onFinished = () =>
            {
                sp.Finished -= onFinished;
                sp.transform.localScale = Vector3.one;      // hand the pooled player back at unit scale
                sp.transform.rotation = Quaternion.identity; // …and unrotated
                ShaperPlayerPool.Release(sp);
            };
            sp.Finished += onFinished;
            sp.Play();
        }
    }
}

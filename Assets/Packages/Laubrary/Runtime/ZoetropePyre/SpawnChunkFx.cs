using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Chunks;
using ChunksFx = Laubrary.Chunks.Chunks;   // the class is shadowed by the namespace inside a Laubrary.* namespace

namespace Laubrary.ZoetropePyre
{
    /// <summary>
    /// An <see cref="IEffect"/> that throws a Chunks debris burst at the event's resolved position, aimed by the
    /// resolved direction, SIZED by the resolved scalar param, and COLOUR-SAMPLED from the Zoe's CURRENT lauminary
    /// sprite — the Spawn-Chunk entry of the Zoe-event effect palette (ZOE_EVENTS_DESIGN.md step 3). It is the
    /// Chunks-only counterpart of the bundled <see cref="PyreChunksFx"/> (which STAYS), promoted to a first-class
    /// palette effect. Two new capabilities over the fixed PyreChunksFx debris:
    /// (1) the whole burst is uniformly SCALED by the event's scalar param (a bigger hit throws bigger debris), and
    /// (2) its base colours are sampled LIVE off the Zoe's on-screen sprite via the <see cref="EventContext"/>'s
    /// renderer — never authored data — so the shrapnel flies off in whatever colours the enemy is showing THIS
    /// frame. Lives in the ZoetropePyre bridge module so Zoetrope core stays Chunks-optional.
    ///
    /// Sizing is done by scaling the returned burst CONTAINER's transform: each <see cref="Chunk"/> writes its
    /// WORLD position but its LOCAL scale every frame, so a scaled parent multiplies the visual size (and the
    /// per-chunk trigger radius) without disturbing the world-space motion — and it never mutates the shared,
    /// committed <see cref="ChunkSpec"/> asset.
    /// </summary>
    [System.Serializable]
    public class SpawnChunkFx : IEffect, IEventParamUser
    {
        /// Reads all three params: a POSITION (where the debris bursts), a DIRECTION (which way it's thrown) and a
        /// SCALAR (how big the burst is). So the Zoe-event editor shows this effect Position + Direction + Scalar.
        public EventParam UsedParams => EventParam.Position | EventParam.Direction | EventParam.Scalar;

        [Tooltip("Chunks debris burst to throw at the resolved position. Aimed by the event's direction param.")]
        public ChunkSpec chunks;

        [Tooltip("Sample the Zoe's CURRENT lauminary sprite for the debris' base colours, so the shrapnel flies off in " +
                 "the enemy's own on-screen colours (read live from the event context's renderer each fire, never " +
                 "authored). Off = the spec's own colours (a white base).")]
        public bool sampleLauminaryColours = true;
        [Tooltip("How many colours to sample from the live sprite when 'Sample lauminary colours' is on.")]
        [Min(1)] public int sampleCount = 6;

        [Tooltip("Base uniform scale applied to the whole burst when the scalar param contributes nothing. " +
                 "1 = the spec's authored size.")]
        public float baseScale = 1f;
        [Tooltip("Extra uniform scale added per unit of the event's resolved SCALAR param. 0 = fixed size " +
                 "(identical to the old PyreChunksFx debris). Final scale = baseScale + scalePerAmount * scalar, " +
                 "clamped to a small minimum.")]
        public float scalePerAmount = 0f;

        public bool IsEmpty => chunks == null;

        /// The uniform scale this effect scales the whole burst by for a given resolved scalar value:
        /// baseScale + scalePerAmount * scalar, clamped to a small minimum. Its own method so the sizing
        /// contract is unit-testable without a live (play-mode-only) chunk pool.
        public float ResolveScale(float scalar) => Mathf.Max(0.01f, baseScale + scalePerAmount * scalar);

        public void Apply(EventContext ctx)
        {
            if (chunks == null) return;

            List<Color32> palette = sampleLauminaryColours ? ZoeLiveSampler.SampleColours(ctx.Renderer, sampleCount) : null;
            // The Zoe's own live current sprite, handed on as the burst's SampleSourceOverride — a Sampled-visual
            // DebrisScatter in the recipe cuts its pieces from THIS frame instead of its own authored sampleSource,
            // same live-off-the-renderer spirit as the colour palette above. Always forwarded (not gated by
            // sampleLauminaryColours, which is the palette's own toggle) — DebrisScatter decides for itself
            // whether it wants a sample source at all.
            Sprite liveSprite = ZoeLiveSampler.LiveSprite(ctx.Renderer);
            var container = ChunksFx.Burst(ctx.Position, chunks, palette, ctx.DirectionDeg, sampleSourceOverride: liveSprite);

            if (container != null)
            {
                float scale = ResolveScale(ctx.Scalar);
                if (!Mathf.Approximately(scale, 1f))
                    container.localScale = Vector3.one * scale;
            }
        }

    }
}

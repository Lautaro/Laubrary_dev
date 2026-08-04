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

            List<Color32> palette = sampleLauminaryColours ? SampleRenderer(ctx.Renderer, sampleCount) : null;
            var container = ChunksFx.Burst(ctx.Position, chunks, palette, ctx.DirectionDeg);

            if (container != null)
            {
                float scale = ResolveScale(ctx.Scalar);
                if (!Mathf.Approximately(scale, 1f))
                    container.localScale = Vector3.one * scale;
            }
        }

        static bool _warnedUnreadable;

        /// Grab up to <paramref name="count"/> opaque colours from the renderer's CURRENT sprite (its live lauminary
        /// frame), pre-multiplied by the renderer's tint so the debris matches what's on screen. Returns null when
        /// there is nothing sampleable (no renderer/sprite, or an unreadable texture) — the burst then falls back
        /// to the spec's own colours. Deterministic stride sampling, so the same frame yields the same palette.
        static List<Color32> SampleRenderer(SpriteRenderer sr, int count)
        {
            if (sr == null || sr.sprite == null) return null;
            var sprite = sr.sprite;
            var tex = sprite.texture;
            if (tex == null) return null;
            if (!tex.isReadable)
            {
                if (!_warnedUnreadable)
                {
                    _warnedUnreadable = true;
                    Debug.LogWarning($"[SpawnChunkFx] Sprite texture '{tex.name}' is not Read/Write enabled — cannot " +
                                     "sample its pixels for debris colours, so the burst uses the spec's own colours. " +
                                     "Tick 'Read/Write Enabled' on the sprite's import settings.");
                }
                return null;
            }

            Rect tr = sprite.textureRect;
            int x = Mathf.RoundToInt(tr.x), y = Mathf.RoundToInt(tr.y);
            int w = Mathf.RoundToInt(tr.width), h = Mathf.RoundToInt(tr.height);
            if (w <= 0 || h <= 0) return null;

            Color32[] block;
            if (x == 0 && y == 0 && w == tex.width && h == tex.height)
            {
                block = tex.GetPixels32();   // whole-texture fast path
            }
            else
            {
                Color[] cols = tex.GetPixels(x, y, w, h);   // atlased sub-rect
                block = new Color32[cols.Length];
                for (int i = 0; i < cols.Length; i++) block[i] = (Color32)cols[i];
            }
            if (block.Length == 0) return null;

            Color tint = sr.color;
            var list = new List<Color32>(count);
            int stride = Mathf.Max(1, block.Length / (count * 8));
            for (int i = 0; i < block.Length && list.Count < count; i += stride)
            {
                Color32 c = block[i];
                if (c.a <= 40) continue;           // skip (near-)transparent pixels
                list.Add((Color32)((Color)c * tint));
            }
            return list.Count > 0 ? list : null;
        }
    }
}

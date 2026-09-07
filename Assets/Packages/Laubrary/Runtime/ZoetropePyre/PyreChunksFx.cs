using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Pyre;
using PyreAsset = Laubrary.Pyre.Pyre;   // the class is shadowed by the namespace inside a Laubrary.* namespace
using Laubrary.Chunks;
using ChunksFx = Laubrary.Chunks.Chunks;   // the class is shadowed by the namespace inside a Laubrary.* namespace

namespace Laubrary.ZoetropePyre
{
    /// <summary>
    /// An <see cref="ICombatFx"/> that plays a Pyre blast + a Chunks debris burst at a world point — the
    /// OPTIONAL presentation bridge that lets a Zoetrope Def use Pyre/Chunks effects. Only projects that
    /// include Pyre + Chunks pull this module in; Zoetrope core stays Combat2D-only. (This is the old
    /// coupled <c>CombatVfx</c>, now a pluggable effect in its own module — the whole point of the decouple.)
    /// Migrated 2026-08-23 from Pyre1 (BlastPlayer/PyreBlastPool) to Pyre (PyreBlastPlayer/Pool) as
    /// part of Pyre's retirement — real committed content (Hero Gun, ProtoGuy Gun) referenced this via `blast`
    /// pointing at a Pyre1 asset; those references were converted to equivalent Pyre assets and
    /// repointed, not left dangling.
    ///
    /// <para>The blast is placed through <see cref="PyreAnchor"/>: a Pyre with an anchor lands that anchor on
    /// the point, and a Vector anchor's direction is what the event's aim rotates. A Pyre without one is
    /// centred and rotated by its +X, exactly as before anchors existed.</para>
    /// </summary>
    [System.Serializable]
    public class PyreChunksFx : ICombatFx, IEffectOrientationHint
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

        public string OrientationHint => PyreOrientationHint.For(blast);

        /// Spawn the blast + debris at a world point. directionDeg (NaN = omni) aims directional chunk bursts.
        public void Play(Vector2 worldPos, float directionDeg = float.NaN)
        {
            SpawnBlast(worldPos);
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, directionDeg);
        }

        /// The blast turned to face <paramref name="aimDeg"/> (through its anchor) and mirrored when asked; the
        /// debris burst aims the same way. This is what a weapon's own muzzle slot plays.
        public void PlayOriented(Vector2 worldPos, float aimDeg, bool flipX)
        {
            SpawnBlast(worldPos, aimDeg, flipX);
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, aimDeg);
        }

        void IEffect.Apply(EventContext ctx)
        {
            SpawnBlast(ctx.Position, ctx.DirectionDeg, ctx.FlipX);
            if (chunks != null) ChunksFx.Burst(ctx.Position, chunks, ctx.DirectionDeg);
        }

        /// Same as <see cref="Play"/>, but returns the blast's Transform for follow-tracking (see
        /// <see cref="FxFollowTarget"/>). Chunks still burst once at the initial point regardless — a scatter
        /// of independently-moving debris has no single Transform to hand back. Null if no blast is configured.
        public Transform PlayFollowable(Vector2 worldPos, float directionDeg = float.NaN, bool flipX = false)
        {
            var bp = SpawnBlast(worldPos, directionDeg, flipX);
            if (chunks != null) ChunksFx.Burst(worldPos, chunks, directionDeg);
            return bp != null ? bp.transform : null;
        }

        PyreBlastPlayer SpawnBlast(Vector2 worldPos) => SpawnBlast(worldPos, float.NaN, false);
        PyreBlastPlayer SpawnBlast(Vector2 worldPos, float directionDeg, bool flipX)
        {
            if (blast == null) return null;

            var bp = PyreBlastPool.Get();   // pooled: pooled=true already set by the pool's factory
            PyreAnchor.Place(bp.transform, blast, worldPos, directionDeg, flipX);
            var sr = bp.GetComponent<SpriteRenderer>();
            if (sr != null) { sr.sortingOrder = sortingOrder; sr.flipX = flipX; }
            bp.spec = blast;
            bp.fps = blastFps > 0f ? blastFps : 24f;
            bp.loop = false;

            System.Action onFinished = null;
            onFinished = () => { bp.Finished -= onFinished; bp.transform.rotation = Quaternion.identity; if (sr != null) sr.flipX = false; PyreBlastPool.Release(bp); };
            bp.Finished += onFinished;
            bp.Play();
            return bp;
        }
    }
}

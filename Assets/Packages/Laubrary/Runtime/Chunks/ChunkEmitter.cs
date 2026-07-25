using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Chunks
{
    /// The game-facing component: hold a <see cref="ChunkSpec"/> and call <see cref="Burst()"/> when something
    /// explodes. It spawns the randomised swarm of <see cref="Chunk"/> GameObjects (a SpriteRenderer + a Chunk each)
    /// that then fly and clean themselves up. For a fire-and-forget burst with no component in the scene, use the
    /// static <see cref="Chunks.Burst(Vector2,ChunkSpec,float)"/>. A caller that has sampled the colours of the thing
    /// that exploded (à la Larder's WareDebris) can pass a <c>tintPalette</c> so the debris matches those colours.
    [DisallowMultipleComponent]
    public class ChunkEmitter : MonoBehaviour
    {
        [Tooltip("The burst recipe this emitter fires.")]
        public ChunkSpec spec;
        [Tooltip("Sorting order applied to every spawned chunk's SpriteRenderer.")]
        public int sortingOrder = 500;
        [Tooltip("Combatant dealing damage via chunks that have spec.useHitDetection on (its faction decides who " +
                 "can be hit — see Combat2D.Hitbox). Auto-found in parents if null. Unused when the spec doesn't " +
                 "use hit detection.")]
        public Combatant owner;

        void Reset() { owner = GetComponentInParent<Combatant>(); }
        void Awake() { if (owner == null) owner = GetComponentInParent<Combatant>(); }

        /// Burst at this emitter's own position, using the spec's direction.
        public void Burst() => Burst((Vector2)transform.position, float.NaN);

        /// Burst at a world position; pass a direction (degrees) to override the spec's directionDeg for this burst.
        /// animationOverride, if supplied, plays instead of the spec's own animationSource for this burst only.
        /// Signature deliberately stays Combatant-free (unlike SpawnBurst) — this.owner is forwarded internally —
        /// so existing callers in assemblies that don't reference Combat2D keep compiling unchanged; a Combatant
        /// PARAMETER here would force every caller's assembly to reference Combat2D just to resolve the overload,
        /// even when they never touch it (a real C#/Unity gotcha: unused defaulted params still need their type
        /// resolvable). ZoetropePyre's Chunks.Burst(...) call is exactly the case that broke before this fix.
        public void Burst(Vector2 worldPos, float directionDegOverride = float.NaN, IChunkAnimation animationOverride = null)
            => SpawnBurst(worldPos, spec, null, directionDegOverride, transform, sortingOrder, animationOverride, owner);

        /// Burst tinted to a supplied palette (e.g. colours sampled off the exploded object).
        public void Burst(Vector2 worldPos, IList<Color32> tintPalette, float directionDegOverride = float.NaN,
                          IChunkAnimation animationOverride = null)
            => SpawnBurst(worldPos, spec, tintPalette, directionDegOverride, transform, sortingOrder, animationOverride, owner);

        /// The one place chunks are actually created. Shared by the component and the static API.
        /// parent may be null (a temporary self-destroying container is made). Returns the container transform.
        /// animationOverride wins over spec.AnimationSource when both are set. owner is only used when
        /// spec.useHitDetection is on (see Combat2D.Hitbox.owner) — harmless to leave null otherwise.
        public static Transform SpawnBurst(Vector2 worldPos, ChunkSpec spec, IList<Color32> palette,
                                           float directionDegOverride, Transform parent, int sortingOrder,
                                           IChunkAnimation animationOverride = null, Combatant owner = null)
        {
            if (spec == null) return null;

            var container = new GameObject("ChunkBurst").transform;
            container.position = worldPos;
            if (parent != null) container.SetParent(parent, true);

            int count = Random.Range(spec.countMin, spec.countMax + 1);
            float centerDeg = float.IsNaN(directionDegOverride) ? spec.directionDeg : directionDegOverride;
            bool haveSprites = spec.sprites != null && spec.sprites.Count > 0;
            bool havePalette = palette != null && palette.Count > 0;
            IChunkAnimation anim = animationOverride ?? spec.AnimationSource;

            for (int i = 0; i < count; i++)
            {
                var chunk = ChunkPool.Get();
                var go = chunk.gameObject;
                go.transform.SetParent(container, false);
                go.transform.localPosition = Vector3.zero;

                var sr = go.GetComponent<SpriteRenderer>();
                sr.sortingOrder = sortingOrder;
                bool sampled = false;
                if (anim == null)
                {
                    Sprite sampledSprite = spec.UsesSampledDebris
                        ? SampledChunkSprites.Sample(spec.sampleSource, spec.samplePxMin, spec.samplePxMax, spec.pixelsPerUnit,
                            spec.tintMode, spec.tintColor, spec.tintStrength, spec.edgeThicknessPx, spec.modifiers)
                        : null;
                    if (sampledSprite != null)
                    {
                        sr.sprite = sampledSprite;
                        sampled = true;
                    }
                    else
                    {
                        sr.sprite = haveSprites
                            ? spec.sprites[Random.Range(0, spec.sprites.Count)]
                            : ChunkSprites.Random(spec.pixelsPerUnit);
                    }
                }
                bool tumbling = sampled && spec.tumble;

                Color baseColor = havePalette ? (Color)palette[Random.Range(0, palette.Count)] : Color.white;

                float angleDeg = centerDeg + Random.Range(-spec.spreadDeg, spec.spreadDeg);
                float angleRad = angleDeg * Mathf.Deg2Rad;
                float speed = Random.Range(spec.speedMin, spec.speedMax);
                Vector2 vel = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * speed;
                vel.y += spec.upwardBias;

                float angular = tumbling
                    ? Random.Range(spec.tumbleSpeedMin, spec.tumbleSpeedMax) * (Random.value < 0.5f ? -1f : 1f)
                    : Random.Range(spec.angularSpeedMin, spec.angularSpeedMax) * (Random.value < 0.5f ? -1f : 1f);
                float life = Random.Range(spec.lifeMin, spec.lifeMax);
                float size = Random.Range(spec.sizeMin, spec.sizeMax);

                chunk.Init(spec, vel, angular, life, size, baseColor, anim, tumbling, owner);

                System.Action onFinished = null;
                onFinished = () => { chunk.Finished -= onFinished; ChunkPool.Release(chunk); };
                chunk.Finished += onFinished;
            }

            // If we own the container, tear it down after the longest chunk could possibly live (plus slack).
            if (parent == null)
                Destroy(container.gameObject, spec.lifeMax + 2f);

            return container;
        }
    }

    /// Fire-and-forget static API so a game can throw a burst without wiring a component.
    /// Note: in this pure-runtime assembly bare <c>Random</c> is <c>UnityEngine.Random</c>, which is what we want —
    /// runtime debris wants variety, not determinism.
    public static class Chunks
    {
        /// Throw a burst at a world point. Pass directionDeg to override the spec's direction. Returns the
        /// container. No owner param here on purpose (see ChunkEmitter.Burst's own comment on why) — a burst
        /// fired via this fire-and-forget API with useHitDetection on just deals damage as an unowned hit
        /// (Hitbox.owner null); use ChunkEmitter (component, with its own owner field) or call
        /// ChunkEmitter.SpawnBurst directly if you need an attributed owner.
        public static Transform Burst(Vector2 worldPos, ChunkSpec spec, float directionDeg = float.NaN,
                                      IChunkAnimation animationOverride = null)
            => ChunkEmitter.SpawnBurst(worldPos, spec, null, directionDeg, null, 500, animationOverride);

        /// Throw a burst tinted to a supplied palette (e.g. colours sampled off the exploded object).
        public static Transform Burst(Vector2 worldPos, ChunkSpec spec, IList<Color32> tintPalette, float directionDeg = float.NaN,
                                      IChunkAnimation animationOverride = null)
            => ChunkEmitter.SpawnBurst(worldPos, spec, tintPalette, directionDeg, null, 500, animationOverride);
    }
}

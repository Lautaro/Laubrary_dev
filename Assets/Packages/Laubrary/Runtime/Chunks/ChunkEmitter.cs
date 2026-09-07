using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Chunks
{
    /// The game-facing component: hold a <see cref="ChunkSpec"/> and call <see cref="Burst()"/> when something
    /// explodes. It owns the container the burst lives in and its lifetime, and nothing else — what actually
    /// appears is entirely the recipe's own stack of capabilities. For a fire-and-forget burst with no
    /// component in the scene, use the static <see cref="Chunks.Burst(Vector2,ChunkSpec,float)"/>. A caller
    /// that has sampled the colours of the thing that exploded (à la Larder's WareDebris) can pass a
    /// <c>tintPalette</c> so the debris matches those colours.
    [DisallowMultipleComponent]
    public class ChunkEmitter : MonoBehaviour
    {
        /// Where an unslotted output's draw order starts from. Named rather than typed in three places
        /// because the editor preview has to assume the same number to predict what will be in front.
        public const int DefaultSortingOrder = 500;

        [Tooltip("The recipe this emitter fires.")]
        public ChunkSpec spec;
        [Tooltip("Draw order for anything the recipe does not put in a named layer slot.")]
        public int sortingOrder = DefaultSortingOrder;
        [Tooltip("Combatant dealing damage through a recipe that has a Hits capability (its faction decides " +
                 "who can be hit — see Combat2D.Hitbox). Auto-found in parents if null.")]
        public Combatant owner;

        void Reset() { owner = GetComponentInParent<Combatant>(); }
        void Awake() { if (owner == null) owner = GetComponentInParent<Combatant>(); }

        /// Burst at this emitter's own position, using the recipe's direction.
        public void Burst() => Burst((Vector2)transform.position, float.NaN);

        /// Burst at a world position; pass a direction (degrees) to override the recipe's own for this burst.
        /// animationOverride, if supplied, plays instead of the recipe's own animated debris content.
        /// Signature deliberately stays Combatant-free (unlike SpawnBurst) — this.owner is forwarded
        /// internally — so existing callers in assemblies that don't reference Combat2D keep compiling; a
        /// Combatant PARAMETER here would force every caller's assembly to reference Combat2D just to resolve
        /// the overload, even when they never touch it (unused defaulted params still need their type
        /// resolvable). ZoetropePyre's Chunks.Burst(...) call is exactly the case that broke before this fix.
        public void Burst(Vector2 worldPos, float directionDegOverride = float.NaN, IChunkAnimation animationOverride = null)
            => SpawnBurst(worldPos, spec, null, directionDegOverride, transform, sortingOrder, animationOverride, owner);

        /// Burst tinted to a supplied palette (e.g. colours sampled off the exploded object). sampleSourceOverride,
        /// if supplied, is the sprite a Sampled-visual DebrisScatter cuts its pieces from instead of its own
        /// authored sampleSource — a Zoe's live current sprite, say.
        public void Burst(Vector2 worldPos, IList<Color32> tintPalette, float directionDegOverride = float.NaN,
                          IChunkAnimation animationOverride = null, Sprite sampleSourceOverride = null)
            => SpawnBurst(worldPos, spec, tintPalette, directionDegOverride, transform, sortingOrder, animationOverride, owner, sampleSourceOverride);

        /// The one place a burst is actually created. Shared by the component and the static API. parent may
        /// be null (a temporary self-destroying container is made). Returns the container transform.
        /// sampleSourceOverride, when supplied, is forwarded as the burst's ChunkModuleContext.SampleSourceOverride.
        public static Transform SpawnBurst(Vector2 worldPos, ChunkSpec spec, IList<Color32> palette,
                                           float directionDegOverride, Transform parent, int sortingOrder,
                                           IChunkAnimation animationOverride = null, Combatant owner = null,
                                           Sprite sampleSourceOverride = null)
        {
            if (spec == null) return null;

            var container = new GameObject("ChunkBurst").transform;
            container.position = worldPos;
            if (parent != null) container.SetParent(parent, true);

            // The composition's own aim, unless this particular burst was told otherwise. Producers that
            // inherit the burst direction read exactly this.
            float centerDeg = float.IsNaN(directionDegOverride) ? spec.directionDeg : directionDegOverride;

            ChunkModules.Run(spec, worldPos, container, sortingOrder, centerDeg, palette, animationOverride, owner,
                             sampleSourceOverride);

            // If we own the container, tear it down once the recipe's clock has run out.
            if (parent == null)
                Destroy(container.gameObject, ContainerLifetime(spec));

            return container;
        }

        /// How long a self-owned burst container has to stay alive: the recipe's own clock plus slack.
        ///
        /// Destroying it early silently cancels whatever the recipe still had scheduled — a blast a second
        /// after the debris dies simply never appears, with nothing logged to explain it — so the slack is
        /// deliberate and erring long costs one idle empty GameObject for a moment. The clock itself is
        /// <see cref="ChunkClock"/>'s to compute, and only its: the window scrubs over the same number, and
        /// two answers to "how long is this?" is how a lane ends up past the end of its own container.
        public static float ContainerLifetime(ChunkSpec spec)
            => spec == null ? 2f : ChunkClock.Length(spec) + 2f;
    }

    /// Fire-and-forget static API so a game can throw a burst without wiring a component.
    public static class Chunks
    {
        /// Throw a burst at a world point. Pass directionDeg to override the recipe's direction. Returns the
        /// container. No owner param here on purpose (see ChunkEmitter.Burst's own comment on why) — a burst
        /// fired through this API by a recipe with a Hits capability just deals damage as an unowned hit; use
        /// ChunkEmitter (component, with its own owner field) or call SpawnBurst directly for an attributed one.
        public static Transform Burst(Vector2 worldPos, ChunkSpec spec, float directionDeg = float.NaN,
                                      IChunkAnimation animationOverride = null)
            => ChunkEmitter.SpawnBurst(worldPos, spec, null, directionDeg, null, ChunkEmitter.DefaultSortingOrder, animationOverride);

        /// Throw a burst tinted to a supplied palette (e.g. colours sampled off the exploded object).
        /// sampleSourceOverride, if supplied, is the sprite a Sampled-visual DebrisScatter cuts its pieces
        /// from instead of its own authored sampleSource (e.g. a Zoe's live current sprite).
        public static Transform Burst(Vector2 worldPos, ChunkSpec spec, IList<Color32> tintPalette, float directionDeg = float.NaN,
                                      IChunkAnimation animationOverride = null, Sprite sampleSourceOverride = null)
            => ChunkEmitter.SpawnBurst(worldPos, spec, tintPalette, directionDeg, null, ChunkEmitter.DefaultSortingOrder,
                                       animationOverride, null, sampleSourceOverride);
    }
}

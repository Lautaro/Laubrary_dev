using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// Which way a spawned effect is angled. Small on purpose: anything more elaborate is the recipe choosing
    /// a direction, not the blast deciding what to do with one.
    public enum PyreSpawnRotation
    {
        /// Use the recipe's own direction, so an angled burst angles what it spawns with it.
        InheritBurst = 0,
        /// Always the same authored angle, whichever way the burst went.
        Fixed = 1,
        /// A random angle inside an authored range, rerolled per spawn (or fixed by the seed).
        RandomRange = 2,
    }

    /// How many blasts one capability puts out, and where.
    public enum PyreBlastPattern
    {
        /// Exactly one, at the offset.
        Single = 0,
        /// Several along a straight segment centred on the offset.
        Line = 1,
        /// Several around a circle, or an arc of one, centred on the offset.
        Ring = 2,
    }

    /// One blast — a Pyre, in practice — or a whole pattern of them, spawned at the recipe's origin.
    ///
    /// It spawns through <see cref="IChunkEffectSpawner"/> and never through a Pyre type: Pyre already
    /// references Chunks, so the arrow back would be a cycle Unity refuses to compile. A recipe holds one of
    /// these per depth band — "this blast behind the fragments, those in front of them" is three of these with
    /// three layer slots, not one blast with a list of exceptions.
    [System.Serializable]
    public class PyreBlast : ChunkCapability
    {
        public override string KindName => "Pyre Blast";

        [Tooltip("Which layer-stack slot the blasts draw in. Empty leaves them out of the plan, drawing " +
                 "in stack order in FRONT of every slotted output.")]
        public string layerName = "";

        public override string LayerName => layerName;

        // ── what gets spawned ─────────────────────────────────────────────────────
        // A serialized reference to an INTERFACE is not something Unity can do, so this is an Object field
        // plus a cast property — the same shape IChunkAnimation already needs.
        [Tooltip("The effect spawned at each point. Ignored while the pool below has any entry.")]
        public Object source;

        [Tooltip("A pool to draw from instead: every spawn picks ONE entry at random. While this has any entry " +
                 "it wins outright; empty it to go back to the single source above.")]
        public List<Object> pool = new List<Object>();

        // ── where, how many ───────────────────────────────────────────────────────
        [Tooltip("Lay several blasts out in a shape instead of one at the offset.")]
        public bool useFormation = false;

        [Tooltip("Where the blasts sit and how long after this fires each one goes off.")]
        public SpawnFormation formation = new SpawnFormation();

        [Tooltip("Where the pattern's centre sits relative to the recipe's origin, in world units.")]
        public Vector2 offset = Vector2.zero;

        /// One authored control over the two fields that really decide the layout. They are stored separately
        /// because a Ring's dials and a Line's dials are different sets and the shape has to survive being
        /// switched back to Single and out again — but nobody should ever be shown "several?" and "which
        /// shape?" as two questions, since Single is simply the answer "neither".
        public PyreBlastPattern Pattern
        {
            get => !useFormation || formation == null
                ? PyreBlastPattern.Single
                : (formation.shape == FormationShape.Line ? PyreBlastPattern.Line : PyreBlastPattern.Ring);
            set
            {
                useFormation = value != PyreBlastPattern.Single;
                if (formation == null) formation = new SpawnFormation();
                if (value == PyreBlastPattern.Line) formation.shape = FormationShape.Line;
                else if (value == PyreBlastPattern.Ring) formation.shape = FormationShape.Ring;
            }
        }

        // ── how each one comes out ────────────────────────────────────────────────
        [Tooltip("Whether each blast follows the recipe's direction, sits at a fixed angle, or picks a random one.")]
        public PyreSpawnRotation rotationMode = PyreSpawnRotation.InheritBurst;

        [Tooltip("Fixed-angle mode: the angle every blast is rotated to. 0 = right, 90 = up.")]
        public float fixedAngleDeg = 0f;

        [Tooltip("Random-angle mode: the lowest angle a blast can be rotated to.")]
        public float randomAngleMinDeg = 0f;

        [Tooltip("Random-angle mode: the highest angle a blast can be rotated to.")]
        public float randomAngleMaxDeg = 360f;

        [Tooltip("Smallest uniform scale a blast comes out at. 1 = the effect's authored size.")]
        [Min(0.01f)] public float scaleMin = 1f;

        [Tooltip("Largest uniform scale a blast comes out at. Equal to the minimum = every blast the same size.")]
        [Min(0.01f)] public float scaleMax = 1f;

        [Tooltip("Fixes the random picking, angle and scale so every play resolves identically. 0 = reroll.")]
        public int seed = 0;

        // Chunks cannot ask a blast asset how long it plays for: IChunkEffectSpawner deliberately says nothing
        // beyond "spawn one and hand back its transform", because widening it would make every implementer
        // answer a question only the recipe's clock cares about. So the length one blast reads as is authored
        // here, and it is used ONLY to size the clock — never to cut playback short.
        [Min(0f)]
        [Tooltip("Roughly how long one blast stays on screen. Sizes the recipe's clock only; it never cuts a " +
                 "blast short.")]
        public float blastSeconds = 0.6f;

        /// source cast to the contract Chunks actually needs, or null if unset/incompatible.
        public IChunkEffectSpawner Source => source as IChunkEffectSpawner;

        /// Whether anything at all could be spawned — a usable pool entry or a usable single source. Asked by
        /// the UI and by the placement pass, so neither re-derives the pool-beats-source precedence.
        public bool HasSpawner
        {
            get
            {
                if (pool != null)
                    for (int i = 0; i < pool.Count; i++)
                        if (pool[i] is IChunkEffectSpawner) return true;
                return Source != null;
            }
        }

        /// Whether this lays out several points rather than one.
        public bool UsesPattern => Pattern != PyreBlastPattern.Single;

        /// The last point of the pattern goes off this long after the capability fires, and the blast it
        /// spawns then plays for its own length.
        public override float DurationSeconds(ChunkSpec spec) => StaggerTail + Mathf.Max(0f, blastSeconds);

        /// Seconds between the pattern firing and its LAST point going off. The jitter term matches what
        /// SpawnFormation.Resolve actually applies — jitter is clamped to staggerSeconds there, so adding the
        /// raw value would over-state a wildly authored number rather than describe the tail.
        public float StaggerTail
        {
            get
            {
                if (!UsesPattern || formation == null || formation.staggerSeconds <= 0f) return 0f;
                return formation.staggerSeconds * Mathf.Max(0, formation.count - 1)
                       + Mathf.Min(formation.staggerJitter, formation.staggerSeconds);
            }
        }

        // ── randomness ────────────────────────────────────────────────────────────
        // Not serialized: a seed is authored data, the generator it produces is not. One generator per fire,
        // shared by every point of a pattern, so N points fan out instead of all drawing the same first value.
        [System.NonSerialized] ChunkRng _rng;

        /// Restart this capability's randomness. <see cref="Fire"/> calls it; a driver that calls
        /// <see cref="SpawnOne"/> itself (a Follow Emitter re-firing on a timer) must call it ONCE at start,
        /// or a seeded blast would restart its sequence on every tick and every flare come out identical.
        public void ResetRandom() => _rng = Rng(seed);

        /// The spawner ONE spawn should use: a random usable entry from the pool when it has any, else the
        /// single source. Null when neither is usable. Public so a caller can ask what it is about to get.
        public IChunkEffectSpawner PickSpawner()
        {
            if (pool != null && pool.Count > 0)
            {
                // Only entries that actually implement the contract are counted, so one empty slot in an
                // otherwise good pool costs that slot rather than a proportion of the spawns.
                int usable = 0;
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i] is IChunkEffectSpawner) usable++;
                if (usable > 0)
                {
                    int pick = _rng.Next(usable);
                    for (int i = 0; i < pool.Count; i++)
                    {
                        if (!(pool[i] is IChunkEffectSpawner s)) continue;
                        if (pick == 0) return s;
                        pick--;
                    }
                }
            }
            return Source;
        }

        /// The angle one spawn comes out at, given the recipe's own direction. NaN in (an omni-directional
        /// burst) stays NaN out in inherit mode, which every spawner reads as "no rotation".
        public float ResolveRotation(float burstDirectionDeg)
        {
            switch (rotationMode)
            {
                case PyreSpawnRotation.Fixed: return fixedAngleDeg;
                case PyreSpawnRotation.RandomRange:
                    float lo = Mathf.Min(randomAngleMinDeg, randomAngleMaxDeg);
                    float hi = Mathf.Max(randomAngleMinDeg, randomAngleMaxDeg);
                    return _rng.Range(lo, hi);
                default: return burstDirectionDeg;
            }
        }

        /// The uniform scale one spawn comes out at.
        public float ResolveScale()
        {
            float lo = Mathf.Max(0.01f, Mathf.Min(scaleMin, scaleMax));
            float hi = Mathf.Max(lo, scaleMax);
            return hi <= lo ? lo : _rng.Range(lo, hi);
        }

        /// Spawn exactly one blast at worldPos and return its transform (null when nothing could be spawned).
        /// The placement pass calls this once per point, so picking, rotation and scaling exist in ONE place
        /// and the pattern only has to know where its points are.
        ///
        /// orderOffset sub-orders several blasts of one fire within their shared layer slot, and doubles as
        /// the per-instance index so a Trajectory can fan them out instead of flying them identically.
        public Transform SpawnOne(in ChunkModuleContext ctx, Vector3 worldPos, int orderOffset = 0)
        {
            var spawner = PickSpawner();
            if (spawner == null) return null;

            float rotation = ResolveRotation(ctx.DirectionDeg);
            float scale = ResolveScale();
            // The layer stack owns draw order — this passes the slot's name and lets the context decide, which
            // is what keeps "layering is optional" true without a fallback living in every capability.
            string sortingLayer = ctx.Layers != null ? ctx.Layers.sortingLayerName : null;
            var spawned = spawner.SpawnEffect(worldPos, rotation, scale, sortingLayer,
                                              ctx.OrderFor(LayerName, orderOffset));

            // Flight is a DECORATION on a spawn rather than a spawn of its own, so it is applied at the one
            // point in Chunks that ever holds a freshly spawned blast's transform.
            ctx.Spec?.FindModifier<Trajectory>(this)?.Apply(ctx, spawned, orderOffset);

            return spawned;
        }

        public override void Fire(in ChunkModuleContext ctx)
        {
            ResetRandom();

            Vector3 centre = ctx.Origin + (Vector3)offset;
            if (!UsesPattern || formation == null)
            {
                SpawnOne(ctx, centre);
                return;
            }

            SpawnFormationRunner.Fire(ctx, this, formation, centre);
        }
    }
}

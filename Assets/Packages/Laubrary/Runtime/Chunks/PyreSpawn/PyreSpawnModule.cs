using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// Which way a spawned effect is angled. Small on purpose: anything more elaborate is the burst choosing a
    /// direction, not the spawner deciding what to do with one.
    public enum PyreSpawnRotation
    {
        /// Use the burst's own centre direction, so an angled burst angles what it spawns with it.
        InheritBurst = 0,
        /// Always the same authored angle, whichever way the burst went.
        Fixed = 1,
        /// A random angle inside an authored range, rerolled per spawn (or fixed by the seed).
        RandomRange = 2,
    }

    /// <summary>
    /// Spawns ONE effect — a Pyre blast, in practice — at a point, or picks one at random from a pool of them.
    /// Standalone by design: nothing else in the spec needs to be switched on, no layer stack, no timeline.
    ///
    /// It spawns through <see cref="IChunkEffectSpawner"/> and never through a Pyre type; see that interface
    /// for the assembly cycle that makes it necessary. It is also strictly the PICKING half — it spawns at a
    /// point it is handed and computes nothing about where that point is. Placement belongs to the Spawn
    /// Formation module, which calls <see cref="SpawnOne"/> once per placement rather than duplicating any of
    /// the picking, rotation or scaling logic here.
    /// </summary>
    [System.Serializable]
    public class PyreSpawnModule : IChunkModule
    {
        [Tooltip("Spawn a Pyre blast (or one picked at random from a pool) when this burst fires.")]
        public bool enabled = false;

        [Tooltip("Which layer-stack slot the spawned blast draws in. Ignored when no layer stack is configured.")]
        public string layerName = "Blast";

        // ── group identity ───────────────────────────────────────────────────────
        // A spec can hold SEVERAL of these (ChunkSpec.blastGroups), which is the only way to author "this
        // blast behind the fragments, those blasts in front of them" — one reference per depth. Once there is
        // more than one, they need telling apart in the window's section headers and on the timeline's lanes,
        // and an index would be a reference-by-position that renumbers itself the moment one is removed.
        [Tooltip("A short name for this blast group, shown on its section header and its timeline lane. " +
                 "Only used to tell several groups apart — it never affects what is spawned.")]
        public string label = "";

        // ── several at once (optional) ───────────────────────────────────────────
        // The same placement maths the standalone Spawn Formation module uses, available per group. Off by
        // default, so a group is exactly one blast at its offset unless it is asked for more.
        [Tooltip("Place SEVERAL spawns of this group in a shape and stagger when each fires, instead of one " +
                 "at the offset below.")]
        public bool useFormation = false;

        [Tooltip("Where each of this group's spawn points sits and how long after burst-start it fires. " +
                 "Only used while 'several' is on.")]
        public SpawnFormation formation = new SpawnFormation();

        // ── what gets spawned ────────────────────────────────────────────────────
        // A serialized reference to an INTERFACE is not a thing Unity can do, so this is an Object field plus a
        // cast property — exactly how ChunkSpec.animationSource / AnimationSource already handles the same
        // problem for IChunkAnimation.
        [Tooltip("The effect spawned at the point — an asset implementing IChunkEffectSpawner (e.g. a Pyre " +
                 "Spawn Source). Ignored while the pool below has any entry.")]
        public Object source;

        [Tooltip("A pool to pick from instead: every spawn draws ONE entry at random. While this has any " +
                 "entry it wins outright and the single source above is not used; empty the pool to go back " +
                 "to the single source.")]
        public List<Object> pool = new List<Object>();

        // ── where, how big, which way ────────────────────────────────────────────
        [Tooltip("Offset from the burst's own origin, in world units. (0, 0) spawns exactly on it.")]
        public Vector2 offset = Vector2.zero;

        [Tooltip("Whether the spawned effect follows the burst's direction, sits at a fixed angle, or picks a " +
                 "random one per spawn.")]
        public PyreSpawnRotation rotationMode = PyreSpawnRotation.InheritBurst;

        [Tooltip("Fixed-angle mode: the angle every spawn is rotated to. 0 = right, 90 = up.")]
        public float fixedAngleDeg = 0f;

        [Tooltip("Random-angle mode: the lowest angle a spawn can be rotated to. 0 = right, 90 = up.")]
        public float randomAngleMinDeg = 0f;

        [Tooltip("Random-angle mode: the highest angle a spawn can be rotated to. 0 = right, 90 = up.")]
        public float randomAngleMaxDeg = 360f;

        [Tooltip("Smallest uniform scale a spawn comes out at. 1 = the effect's authored size.")]
        [Min(0.01f)] public float scaleMin = 1f;

        [Tooltip("Largest uniform scale a spawn comes out at. Equal to the minimum = every spawn the same size.")]
        [Min(0.01f)] public float scaleMax = 1f;

        [Tooltip("Fixes the random picking, angle and scale so every play resolves identically. 0 = reroll " +
                 "every time.")]
        public int seed = 0;

        /// source cast to the contract Chunks actually needs, or null if unset/incompatible.
        public IChunkEffectSpawner Source => source as IChunkEffectSpawner;

        public bool Enabled => enabled;
        public string LayerName => layerName;

        /// Whether anything at all could be spawned — a usable pool entry or a usable single source. The UI
        /// and any caller wanting to skip a pointless placement pass can ask this instead of re-deriving the
        /// pool-beats-source precedence.
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

        // ── randomness ───────────────────────────────────────────────────────────
        // Non-serialized: a seed is authored data, the generator it produces is not. Null means "use Unity's
        // shared generator", i.e. reroll every play, which is the right default for debris.
        [System.NonSerialized] System.Random _rng;

        /// Restart this module's randomness from its seed. Fire() calls it, so a plain one-shot spawn is
        /// deterministic with no ceremony; a caller that drives SpawnOne itself (the Spawn Formation module,
        /// once per placement) must call this ONCE at burst start, or a seeded formation would carry on from
        /// wherever the previous burst's sequence had got to.
        public void ResetRandom() => _rng = seed != 0 ? new System.Random(seed) : null;

        float Next01() => _rng != null ? (float)_rng.NextDouble() : Random.value;
        int NextIndex(int count) => _rng != null ? _rng.Next(count) : Random.Range(0, count);

        /// The spawner ONE spawn should use: a random usable entry from the pool when the pool has any, else
        /// the single source. Null when neither is usable. Public so a caller can ask what it is about to get
        /// without spawning it.
        public IChunkEffectSpawner PickSpawner()
        {
            if (pool != null && pool.Count > 0)
            {
                // Only count entries that actually implement the contract, so one empty or wrong-typed slot in
                // an otherwise good pool costs that slot rather than a proportion of the spawns.
                int usable = 0;
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i] is IChunkEffectSpawner) usable++;
                if (usable > 0)
                {
                    int pick = NextIndex(usable);
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

        /// The angle one spawn comes out at, given the burst's own direction. NaN in (an omni-directional
        /// burst) stays NaN out in inherit mode, which every spawner reads as "no rotation".
        public float ResolveRotation(float burstDirectionDeg)
        {
            switch (rotationMode)
            {
                case PyreSpawnRotation.Fixed: return fixedAngleDeg;
                case PyreSpawnRotation.RandomRange:
                    float lo = Mathf.Min(randomAngleMinDeg, randomAngleMaxDeg);
                    float hi = Mathf.Max(randomAngleMinDeg, randomAngleMaxDeg);
                    return lo + (hi - lo) * Next01();
                default: return burstDirectionDeg;
            }
        }

        /// The uniform scale one spawn comes out at.
        public float ResolveScale()
        {
            float lo = Mathf.Max(0.01f, Mathf.Min(scaleMin, scaleMax));
            float hi = Mathf.Max(lo, scaleMax);
            return hi <= lo ? lo : lo + (hi - lo) * Next01();
        }

        /// <summary>
        /// Spawn exactly one effect at worldPos and return its transform (null when nothing could be spawned).
        /// This is the whole of this module's job, and the entry point the Spawn Formation module calls once
        /// per placement — so picking, rotation and scaling exist in ONE place and a formation only has to
        /// know where its points are.
        /// </summary>
        /// <param name="orderOffset">Sub-order within the module's layer slot, for telling several spawns of
        /// one burst apart in depth. The concrete sortingOrder is always resolved by the context, never here.</param>
        /// layerNameOverride lets a CALLING module put the spawn in its OWN layer slot instead of this one's.
        /// The Spawn Formation is why: a formation is its own visual band ("a line of blasts behind the
        /// fragments"), and routing every spawn through this module's slot would have made the formation's own
        /// slot silently inert — a control that does nothing. Null means "use my own slot", which is what a
        /// plain Fire wants.
        public Transform SpawnOne(in ChunkModuleContext ctx, Vector3 worldPos, int orderOffset = 0,
                                  string layerNameOverride = null)
        {
            var spawner = PickSpawner();
            if (spawner == null) return null;

            float rotation = ResolveRotation(ctx.DirectionDeg);
            float scale = ResolveScale();
            // The layer stack owns draw order — this module passes the slot's name and lets the context decide,
            // which is what keeps "layering is optional" true without a fallback living in every module.
            string sortingLayer = ctx.Layers != null ? ctx.Layers.sortingLayerName : null;
            string slot = string.IsNullOrEmpty(layerNameOverride) ? LayerName : layerNameOverride;
            var spawned = spawner.SpawnEffect(worldPos, rotation, scale, sortingLayer,
                                              ctx.OrderFor(slot, orderOffset));

            // Movement is a DECORATION on a spawn, not a spawn of its own, so it is applied here rather than
            // dispatched as its own module: this is the one place in Chunks that ever holds a freshly spawned
            // blast's transform, and it is also the point both callers (a plain Fire and the Spawn Formation's
            // per-placement loop) pass through. Off by default, so a spawn without it plays where it landed
            // exactly as before. orderOffset doubles as the per-instance index, so N blasts in one formation
            // fly differently instead of identically.
            ctx.Spec?.pyreMotion?.Apply(ctx, spawned, orderOffset);

            return spawned;
        }

        /// How this group reads in a section header or on a timeline lane. The authored label when there is
        /// one, else the blast's own asset name, else a plain fallback — never an empty strip of UI.
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(label)) return label;
                if (source != null) return source.name;
                if (pool != null)
                    for (int i = 0; i < pool.Count; i++)
                        if (pool[i] != null) return pool[i].name + "…";
                return "Blast";
            }
        }

        public void Fire(in ChunkModuleContext ctx)
        {
            // Several: hand the whole group to the shared placement loop, which reseeds and staggers for us.
            // The offset still applies — it moves the formation's CENTRE, so "a ring of blasts, slightly
            // above the impact point" is one authored value rather than a rebuilt formation.
            if (useFormation && formation != null)
            {
                SpawnFormationRunner.Fire(ctx, this, formation, LayerName, ctx.Origin + (Vector3)offset);
                return;
            }

            ResetRandom();
            SpawnOne(ctx, ctx.Origin + (Vector3)offset);
        }
    }
}

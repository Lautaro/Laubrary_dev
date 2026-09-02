using UnityEngine;

namespace Laubrary.Chunks
{
    /// One authored unit of a chunk recipe. A recipe is an ordered STACK of these and shows only the surfaces
    /// its own stack brings, which is why every kind carries its own fields instead of reading a shared bag of
    /// dials on the spec: a recipe holding one Debris Scatter has no blast dials anywhere, and a recipe holding
    /// three blasts has three independent sets rather than one set plus a list of exceptions.
    ///
    /// Three roles share this base, distinguished by behaviour rather than by a flag:
    ///   * producers put something on screen (Debris Scatter, Fragment Fracture, Palette Splash, Pyre Blast);
    ///   * modifiers decorate what a producer made and are resolved BY that producer, never fired (see
    ///     <see cref="ChunkModifier"/>);
    ///   * coordinators contribute shared context (Layer Plan) or fire only their own markers (Cues).
    [System.Serializable]
    public abstract class ChunkCapability
    {
        /// The stable key everything else addresses this capability by: a modifier's target, a timing lane, a
        /// card's view state. Minted once and never reused, so reordering the stack, renaming a capability or
        /// swapping its asset can never orphan what points at it — which an index or a display name would.
        [HideInInspector] public string id = "";

        [Tooltip("Take part in the recipe. Off keeps every authored value but puts nothing on screen.")]
        public bool enabled = true;

        [Tooltip("What this capability is called in the recipe. Empty reads as its kind's own name.")]
        public string displayName = "";

        [Min(0f)]
        [Tooltip("Seconds from the START of the recipe at which this fires. Capabilities overlap freely.")]
        public float delay = 0f;

        /// The kind's own user-facing name. Abstract so a new kind cannot forget to name itself.
        public abstract string KindName { get; }

        /// Whether this capability has a moment of its own on the recipe's clock. False for anything whose
        /// timing belongs to something else — a coordinator that never fires, a modifier that rides its
        /// producer — so those never claim a timing lane or a Delay dial.
        public virtual bool OccupiesTime => true;

        /// How long this capability's output stays on screen once it has fired. Together with
        /// <see cref="delay"/> this is what sizes the recipe's clock; see <see cref="ChunkClock"/>, which is
        /// the only place that rule lives.
        public virtual float DurationSeconds(ChunkSpec spec) => 0f;

        /// Which named slot of the recipe's Layer Plan this draws in, or null when it draws nothing.
        public virtual string LayerName => null;

        /// Do the thing, once, at this capability's moment. Empty by default because coordinators and
        /// modifiers legitimately fire nothing.
        public virtual void Fire(in ChunkModuleContext ctx) { }

        /// What the capability reads as: its authored name, else its kind's.
        public string Title => string.IsNullOrEmpty(displayName) ? KindName : displayName;

        /// Mints this capability's id if it has none, and returns it. Safe to call repeatedly.
        public string EnsureId()
        {
            if (string.IsNullOrEmpty(id)) id = NewId();
            return id;
        }

        public static string NewId() => System.Guid.NewGuid().ToString("N");

        /// The generator one fire of this capability draws from: the authored seed when there is one (so an
        /// authored effect hits the same marks every play), else a single fresh draw so runtime debris keeps
        /// its variety. Every subsequent roll comes off <see cref="ChunkRng"/> rather than Unity's shared
        /// generator, which is what lets a preview reproduce the whole sequence from that one integer.
        protected static ChunkRng Rng(int seed)
            => new ChunkRng(seed != 0 ? seed : Random.Range(1, int.MaxValue));
    }

    /// A capability that acts on what a PRODUCER made rather than producing anything itself. It is never
    /// fired: the producer asks the recipe for the modifiers pointed at it and applies them as it spawns, so a
    /// modifier can never run against a spawn that does not exist.
    [System.Serializable]
    public abstract class ChunkModifier : ChunkCapability
    {
        /// The <see cref="ChunkCapability.id"/> of the producer this acts on. EMPTY means every compatible
        /// producer, which is what a recipe migrated from the old fixed-slot layout gets — the old modules had
        /// no notion of a target and applied to everything.
        [HideInInspector] public string targetId = "";

        /// A modifier has no moment of its own; it happens whenever its producer does.
        public override bool OccupiesTime => false;

        /// Whether this KIND of modifier can act on that kind of producer at all — what the target picker
        /// offers, and the guard that keeps a stale targetId from applying a trail to a blast.
        public abstract bool CanTarget(ChunkCapability producer);

        /// Whether this modifier applies to that particular producer, target choice included.
        public bool Targets(ChunkCapability producer)
            => producer != null && enabled && CanTarget(producer)
               && (string.IsNullOrEmpty(targetId) || targetId == producer.id);
    }
}

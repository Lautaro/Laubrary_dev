using System.Collections.Generic;
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

        /// Which entry of the authoring window's card palette this capability wears — on its card, its timing
        /// lane and its outline on the preview stage. Stored rather than derived from the stack position so a
        /// card keeps its colour through a reorder, and stored rather than hashed from the id so two cards in
        /// one recipe never land on the same colour by chance. -1 = not assigned yet; the window resolves it
        /// in stack order and writes it on the next structural edit. Nothing at runtime reads it.
        [HideInInspector] public int colorSlot = -1;

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

        /// One-time upgrade for a capability that used to hold a native <see cref="AnimationCurve"/> and now
        /// holds a <c>List&lt;ZUIEnvelopePoint&gt;</c> instead (T-0261 — CurveField is banned project-wide, ZUI
        /// Envelope is the only authored curve control). No-op by default; a capability that carries a legacy
        /// curve field overrides this, converts it once (guarded by its own serialized "migrated" flag) and
        /// returns true the one time it actually changed something, so <see cref="ChunkSpec.UpgradeIfNeeded"/>
        /// knows whether the asset needs re-saving. Called on every load — cheap once migrated, since the guard
        /// short-circuits — so it needs no schema-version bump of its own.
        public virtual bool MigrateLegacyCurves() => false;

        /// Turns an old AnimationCurve into an equivalent ZUI Envelope by DENSELY SAMPLING it rather than
        /// trying to reproduce its Hermite tangents as per-segment exponents — ZUIEnvelopePoint's bend is a
        /// simple Lerp(a, b, Pow(t, exponent)), which cannot represent an arbitrary tangent exactly, but many
        /// linear segments between close samples read as visually identical to the smooth original. A null or
        /// empty curve becomes a flat two-point envelope at <paramref name="fallbackValue"/>, matching how the
        /// runtime evaluator already treats a missing curve/envelope.
        protected static List<ZUIEnvelopePoint> SampleCurveToEnvelope(AnimationCurve curve, float fallbackValue = 1f,
                                                                       int sampleCount = 9)
        {
            var points = new List<ZUIEnvelopePoint>(Mathf.Max(2, sampleCount));
            if (curve == null || curve.length == 0)
            {
                points.Add(new ZUIEnvelopePoint(0f, fallbackValue));
                points.Add(new ZUIEnvelopePoint(1f, fallbackValue));
                return points;
            }
            sampleCount = Mathf.Max(2, sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / (sampleCount - 1);
                points.Add(new ZUIEnvelopePoint(t, curve.Evaluate(t)));
            }
            return points;
        }
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

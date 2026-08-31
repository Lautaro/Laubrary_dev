using System;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0113 — which of the two swarm implementations actually ran for a node. Computed by the compiler and
    /// published on <see cref="ShaperProgram"/>, never authored: a node cannot REQUEST Native, a generator can
    /// only OFFER it. Same "declared reason, not declared exemption" stance <see cref="ShaperCompositeReason"/>
    /// takes (T-0112 §6.2) — the whole point is that which implementation ran is SHOWN, never silently hidden,
    /// because it changes what <see cref="ShaperSwarmDef.interact"/> means (task body, verbatim).
    /// APPEND-ONLY: serialized as an int.
    /// </summary>
    public enum ShaperSwarmImplementation
    {
        /// <summary>No swarm authored, or <see cref="ShaperSwarmDef.count"/> ≤ 1 — an exact no-op, the node's
        /// own content untouched.</summary>
        None = 0,
        /// <summary>
        /// The generic wrapper (T-0113 default/fallback): the node's own content is compiled <see
        /// cref="ShaperSwarmDef.count"/> times, each with its own jittered transform/phase/seed, unioned like a
        /// Bag's members. Works on EVERY node kind — Primitive, Bag, Composite — with zero extra code on the
        /// generator's part, because it is built once, here, and never inside a generator.
        /// </summary>
        Generic = 1,
        /// <summary>
        /// A generator-declared faster or interacting path. Offered today by a <see cref="ShaperNodeKind.Composite"/>
        /// node whose <see cref="ShaperCompositeDef.source"/> implements <see cref="IShaperSwarmNativeSource"/>
        /// and currently reports <see cref="IShaperSwarmNativeSource.SupportsNativeSwarm"/> true. Primitive and
        /// Bag nodes never resolve to Native today — see SWARM-SPEC.md §4 for why domain-repeat was
        /// deliberately not built as a second native path in this task, an honest scope limit, not an oversight.
        /// </summary>
        Native = 2,
    }

    /// <summary>
    /// T-0113 — a Composite source that can serve a swarm NATIVELY: all <c>N</c> instances rendered in ONE call
    /// sharing one internal buffer/grid/state, instead of the generic wrapper's <c>N</c> independent renders.
    /// Implementing this is what a generator DOES to offer Native — a <see cref="ShaperNode"/> can never force it
    /// (mirrors <see cref="ShaperCompositeReason"/>'s declared-reason stance, T-0112 §6.2). A source that does not
    /// implement this interface is swarmed by the generic wrapper only, which is a fully legitimate, honestly
    /// labelled outcome — Generic is not a degraded mode, it is the correct answer for a source with nothing to
    /// share between instances.
    /// </summary>
    public interface IShaperSwarmNativeSource
    {
        /// <summary>
        /// Checked by the compiler on EVERY compile, never assumed from the interface alone — a source may be
        /// able to batch only below some internal limit, or only once some other asset is assigned, exactly the
        /// posture <c>ShaperCompositeDef.source == null</c> already takes ("a legal, if useless, authoring
        /// state, never a crash").
        /// </summary>
        bool SupportsNativeSwarm { get; }

        /// <summary>
        /// Free-text, REQUIRED, non-blank — shown verbatim by <c>ShaperSwarmAudit</c>'s compliance pass and by
        /// the future authoring window next to "Implementation: Native" (SWARM-SPEC.md §6), the same
        /// "monolithic must be a declared reason" obligation <see cref="ShaperCompositeDef.reasonNote"/> already
        /// carries for the composite escape hatch.
        /// </summary>
        string NativeSwarmReason { get; }

        /// <summary>
        /// Render every instance into ONE <paramref name="target"/> in a single pass — e.g. one shared
        /// simulation grid stepped once, with <c>N</c> seed/injection points, instead of <c>N</c> independent
        /// grids each stepped once. The four per-instance arrays are the EXACT values the generic wrapper would
        /// have used for the same authored swarm (same hash, same jitter ranges), so a native source's output
        /// is comparable to — not a different effect from — the generic path it replaces.
        /// </summary>
        /// <param name="instanceOffsets">Per-instance position offset, in the node's own local canvas units,
        /// relative to the node's own authored transform (index-parallel with the other three arrays).</param>
        /// <param name="instancePhases">Per-instance normalised life 0..1, already carrying the swarm's
        /// lifetime stagger — this is the fix for the shared-clock bug (task body), passed straight through.</param>
        /// <param name="interact">The swarm's own <see cref="ShaperSwarmDef.interact"/> flag, meaningful ONLY
        /// here — a source decides what "instances influence each other" means for itself (heat diffusing
        /// between injection points, say); false renders each instance's own contribution in isolation within
        /// the SAME shared buffer, still at native's O(1)-buffer cost, just without cross-instance influence.</param>
        void RenderSwarm(int width, int height, float phase01, uint[] instanceSeeds, Vector2[] instanceOffsets,
                          float[] instancePhases, bool interact, Color32[] target);
    }

    /// <summary>
    /// T-0113 — a marker a Composite source may carry to declare itself a STATEFUL per-frame simulation whose
    /// cost is not linear-cheap to run <c>N</c> independent times (task body: "measured at roughly 200x cell
    /// updates for a realistic swarm size" — see SWARM-SPEC.md §5 for the actual measured number). A source that
    /// declares this AND does not also implement <see cref="IShaperSwarmNativeSource"/> has its swarm
    /// <see cref="ShaperSwarmDef.count"/> clamped to <see cref="ShaperSwarmDef.SimulationHardCap"/> by the
    /// compiler, with the clamp published on <see cref="ShaperProgram.swarmCapped"/> — the "explicit hard cap
    /// with a visible warning" the task body names as one of three acceptable real answers.
    /// </summary>
    public interface IShaperSimulationSource
    {
        bool IsStatefulSimulation { get; }
    }

    /// <summary>
    /// T-0113 — the swarm modifier, authored on ANY <see cref="ShaperNode"/> exactly like <see
    /// cref="ShaperSweep"/>/<see cref="ShaperShell"/>: a node GAINS it, it does not replace the node's own kind.
    /// Its identity setting is <c>enabled == false</c> (or <c>count ≤ 1</c>), which returns the node's own
    /// content untouched — no swarm authored is bit-identical to the un-swarmed tree, verified by
    /// <c>ShaperSwarmAudit</c> CT-0.
    ///
    /// Two implementations sit behind these SAME authored fields (task body): a GENERIC wrapper that compiles
    /// the node's own content <see cref="count"/> times with per-instance clocks/seeds/positions/parameter
    /// jitter and unions them, and a NATIVE path a generator may offer instead. Which one actually ran is never
    /// decided here — it is read back off <see cref="ShaperProgram"/> after a compile, so a swarm can never
    /// silently mis-declare its own implementation.
    /// </summary>
    [Serializable]
    public class ShaperSwarmDef
    {
        public bool enabled = false;

        /// <summary>Instance count. 1 is a legal identity — one instance, itself.</summary>
        [Range(1, 64)] public int count = 5;

        /// <summary>Base seed. Each instance draws from <c>Hash(seed, instanceIndex)</c> — never
        /// <c>System.Random</c> (BC-1.3, restated for swarm).</summary>
        public uint seed = 0u;

        /// <summary>Half-range, in the node's own PARENT-local canvas units, of a per-instance random position
        /// offset. (0,0) is the identity — every instance lands exactly on the node's own authored position,
        /// still a legal (if pointless) swarm rather than a forced minimum spread.</summary>
        public Vector2 positionJitter = new Vector2(24f, 24f);

        /// <summary>Degrees, half-range, of a per-instance random rotation offset added to the node's own.</summary>
        public float rotationJitterDegrees = 0f;

        /// <summary>0..1, half-range, of a per-instance random uniform scale multiplier around 1 (so 0.3 draws
        /// each instance's scale from 0.7×..1.3× the node's own).</summary>
        [Range(0f, 1f)] public float scaleJitter = 0f;

        /// <summary>
        /// THE FIX this task's body requires ("most big effects start every instance on the shared clock, so
        /// instances pop in mid-animation instead of having their own lifetimes"). 0 = every instance shares the
        /// node's own phase — the OLD broken behaviour, kept selectable (never deleted) so the bug it fixes stays
        /// reproducible on demand rather than becoming a claim nobody can check. 1 (the default) = each
        /// instance's phase is independently drawn across the WHOLE 0..1 cycle by hash — N instances started in
        /// the same frame land at N different points in their own lifetime, exactly as N independently spawned
        /// effects would. Values between are a partial stagger.
        /// </summary>
        [Range(0f, 1f)] public float lifetimeStagger = 1f;

        /// <summary>How swarm instances fold into one field. Reused rather than re-invented — the same knobs a
        /// Bag member's own <see cref="ShaperBlend"/> already exposes for organic merging.</summary>
        public ShaperBlend merge = new ShaperBlend();

        /// <summary>
        /// Native-path-only control: instances influence EACH OTHER'S result rather than being independently
        /// evaluated and unioned (task body: "something the generic wrapper's independent-instance model can't
        /// do"). Read by <see cref="ShaperCompiler"/> only when <see cref="ShaperProgram.swarmImplementation"/>
        /// resolves to <see cref="ShaperSwarmImplementation.Native"/> for this node — under Generic it is
        /// authored data with no effect, and <c>ShaperSwarmAudit</c> measures that it has none, so a stray
        /// checked box on a Primitive can never be mistaken for something working.
        /// </summary>
        public bool interact = false;

        /// <summary>
        /// The clamp a stateful-simulation source (<see cref="IShaperSimulationSource.IsStatefulSimulation"/>)
        /// with no <see cref="IShaperSwarmNativeSource"/> is held to — see that interface's doc for the
        /// reasoning and SWARM-SPEC.md §5 for the measured cost this protects against.
        /// </summary>
        public const int SimulationHardCap = 6;
    }
}

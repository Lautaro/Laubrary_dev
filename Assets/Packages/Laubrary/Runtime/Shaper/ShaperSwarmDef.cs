using System;
using UnityEngine;
using UnityEngine.Serialization;

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
    public class ShaperSwarmDef : ISerializationCallbackReceiver, IShaperDialOwner, IShaperPreviewOverlay
    {
        const uint FldPosJitterX = 0x7E00_0001u, FldPosJitterY = 0x7E00_0002u;
        const uint FldRotJitter = 0x7E00_0003u, FldScaleJitter = 0x7E00_0004u;
        const uint FldRadius = 0x7E00_0005u, FldOffsetX = 0x7E00_0006u, FldOffsetY = 0x7E00_0007u;
        const uint FldSpawnerRot = 0x7E00_0008u, FldSpawnerPitch = 0x7E00_0009u, FldSpawnerYaw = 0x7E00_000Au;
        const uint FldProgress = 0x7E00_000Bu, FldSpawnTiming = 0x7E00_000Cu, FldScaleByIndex = 0x7E00_000Du;

        public bool enabled = false;

        /// <summary>Instance count. 1 is a legal identity — one instance, itself.</summary>
        [Range(1, 64)] public int count = 5;

        /// <summary>Base seed. Each instance draws from <c>Hash(seed, instanceIndex)</c> — never
        /// <c>System.Random</c> (BC-1.3, restated for swarm).</summary>
        public uint seed = 0u;

        /// <summary>Half-range, in the node's own PARENT-local canvas units, of a per-instance random position
        /// offset. (0,0) is the identity — every instance lands exactly on the node's own authored position,
        /// still a legal (if pointless) swarm rather than a forced minimum spread.</summary>
        public ZUIValue positionJitterX = new ZUIValue(24f);
        public ZUIValue positionJitterY = new ZUIValue(24f);

        /// <summary>Degrees, half-range, of a per-instance random rotation offset added to the node's own.</summary>
        public ZUIValue rotationJitterDegreesDial = new ZUIValue(0f);

        /// <summary>0..1, half-range, of a per-instance random uniform scale multiplier around 1 (so 0.3 draws
        /// each instance's scale from 0.7×..1.3× the node's own).</summary>
        public ZUIValue scaleJitterDial = new ZUIValue(0f);

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

        // ── T-0169 spawn shape ───────────────────────────────────────────────────────────────────────────
        // Every field below defaults to an exact identity, and the whole block is gated on `shape` being
        // something other than None, so a swarm authored before this task renders bit-for-bit what it did.

        /// <summary>The figure the instances arrange themselves on. <see cref="ShaperSwarmShape.None"/> — the
        /// default — leaves every instance on the node's own origin, moved only by the jitter dials.</summary>
        public ShaperSwarmShape shape = ShaperSwarmShape.None;

        /// <summary>Whether instances fill the shape's interior or ride its outline.</summary>
        public ShaperSwarmSpawnMode spawnMode = ShaperSwarmSpawnMode.Area;

        /// <summary>The shape's radius in the node's own parent-local canvas units — for a polygon, the
        /// circumradius; for <see cref="ShaperSwarmShape.Line"/>, half its length. Animatable, so a ring can
        /// expand over the document's frames.</summary>
        public ZUIValue spawnerRadius = new ZUIValue(24f);

        /// <summary>Where the shape's centre sits, relative to the node's own authored position.</summary>
        public ZUIValue spawnerOffsetX = new ZUIValue(0f);
        public ZUIValue spawnerOffsetY = new ZUIValue(0f);

        /// <summary>Degrees, counter-clockwise, turning the whole arrangement in the canvas plane.</summary>
        public ZUIValue spawnerRotationDegrees = new ZUIValue(0f);
        /// <summary>Pseudo-3D tilt about the horizontal axis: at 90° the shape collapses to a horizontal
        /// line, so a ring reads as a ring seen edge-on rather than as a flattened oval.</summary>
        public ZUIValue spawnerPitchDegrees = new ZUIValue(0f);
        /// <summary>Pseudo-3D tilt about the vertical axis.</summary>
        public ZUIValue spawnerYawDegrees = new ZUIValue(0f);

        /// <summary>Area mode: 0 places instances on an even, ordered ring layout; 1 scatters them at random
        /// inside the shape; between blends the two placements.</summary>
        [Range(0f, 1f)] public float distribution = 0f;

        /// <summary>Which end the ordered layout builds from — innermost ring first, or outermost. Only
        /// visible in its effect while <see cref="distribution"/> is below 1.</summary>
        public bool gridReverse = false;

        /// <summary>Which position each spawn moment reveals: 0 walks spatial neighbours, 1 is a full seeded
        /// shuffle. Observable only when <see cref="timing"/> gives instances distinct birth moments.</summary>
        [Range(0f, 1f)] public float spawnOrderChaos = 0f;

        /// <summary>Path mode: where along the outline the arrangement sits. Closed shapes WRAP, so animating
        /// this past 1 rides the instances around further laps rather than pinning them at the end.</summary>
        public ZUIValue pathProgress = new ZUIValue(0f);

        /// <summary>Path mode: spread the instances evenly along the outline by index, so
        /// <see cref="pathProgress"/> becomes the whole string's shared ride rather than each instance's own
        /// position. Defaults ON — unlike Pyre, where it defaults off for byte-identity with an older
        /// renderer — because a Path shape picked with it off puts every instance on the same point, and an
        /// authoring default that produces one visible instance is a dead end, not a neutral state.</summary>
        public bool evenSpacing = true;

        /// <summary>How much of the outline the evenly-spaced string covers. 1 is the whole shape; 0.5 is a
        /// half-shape arc.</summary>
        [Range(0f, 1f)] public float pathSpread = 1f;

        /// <summary>Which way each instance faces once placed.</summary>
        public ShaperSwarmOrient orient = ShaperSwarmOrient.None;

        /// <summary>A per-instance size multiplier read at <c>index/(count-1)</c> rather than at the document
        /// phase — so a Curve tapers the swarm from one end to the other, and Min-Max gives each instance its
        /// own random size. Static 1, the default, is an exact no-op.</summary>
        public ZUIValue scaleByIndex = new ZUIValue(1f);

        // ── T-0169 spawn timing ──────────────────────────────────────────────────────────────────────────

        /// <summary>How instances are distributed in time. <see cref="ShaperSwarmTiming.Stagger"/> is the
        /// default and the pre-T-0169 model.</summary>
        public ShaperSwarmTiming timing = ShaperSwarmTiming.Stagger;

        /// <summary>Window timing: maps an instance's number (0 = first, 1 = last) to the document phase it is
        /// BORN at. A linear ramp spreads the births evenly; an eased curve bursts then trickles; a flat
        /// Static value births the whole swarm at once; Min-Max births each instance at a random moment.</summary>
        public ZUIValue spawnTiming = DefaultSpawnTiming();

        /// <summary>FrameStep timing: the document phase the FIRST instance is born at. Stored in phase, not
        /// in frames, so the engine needs no knowledge of the document's frame count — the window authors it
        /// in frames and converts through <see cref="ShaperClock"/>, which stays the one home for that
        /// mapping.</summary>
        [Range(0f, 1f)] public float firstSpawnPhase = 0f;

        /// <summary>FrameStep timing: how much later in the document each next instance is born. 0 births the
        /// whole swarm on the first frame.</summary>
        [Range(0f, 1f)] public float spawnPhaseStep = 0.1f;

        /// <summary>Window/FrameStep timing: how much of the document each instance is alive for, measured
        /// from its own birth. Outside that span the instance is not emitted at all — it has not appeared yet,
        /// or it is gone.</summary>
        [Range(0.01f, 1f)] public float instanceLife = 0.5f;

        /// <summary>Window/FrameStep timing: every instance dies at the SAME moment — the last birth plus one
        /// life — instead of one life after its own birth, so the swarm vanishes as one burst. An instance
        /// born early therefore runs its own 0..1 more slowly, because a Shaper instance has no duration of
        /// its own, only a normalised phase to be spread across whatever span it is alive for.</summary>
        public bool dieTogether = false;

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

        [SerializeField, FormerlySerializedAs("positionJitter")]        Vector2 legacyPositionJitter = new Vector2(24f, 24f);
        [SerializeField, FormerlySerializedAs("rotationJitterDegrees")] float legacyRotationJitter = 0f;
        [SerializeField, FormerlySerializedAs("scaleJitter")]           float legacyScaleJitter = 0f;
        [SerializeField] bool dialsPromoted;

        public void OnBeforeSerialize() => dialsPromoted = true;

        public void OnAfterDeserialize()
        {
            if (dialsPromoted) { EnsureDials(); return; }
            positionJitterX = new ZUIValue(legacyPositionJitter.x);
            positionJitterY = new ZUIValue(legacyPositionJitter.y);
            rotationJitterDegreesDial = new ZUIValue(legacyRotationJitter);
            scaleJitterDial = new ZUIValue(legacyScaleJitter);
            dialsPromoted = true;
        }

        public void EnsureDials()
        {
            if (positionJitterX == null) positionJitterX = new ZUIValue(24f);
            if (positionJitterY == null) positionJitterY = new ZUIValue(24f);
            if (rotationJitterDegreesDial == null) rotationJitterDegreesDial = new ZUIValue(0f);
            if (scaleJitterDial == null) scaleJitterDial = new ZUIValue(0f);
            if (merge == null) merge = new ShaperBlend();

            if (spawnerRadius == null) spawnerRadius = new ZUIValue(24f);
            if (spawnerOffsetX == null) spawnerOffsetX = new ZUIValue(0f);
            if (spawnerOffsetY == null) spawnerOffsetY = new ZUIValue(0f);
            if (spawnerRotationDegrees == null) spawnerRotationDegrees = new ZUIValue(0f);
            if (spawnerPitchDegrees == null) spawnerPitchDegrees = new ZUIValue(0f);
            if (spawnerYawDegrees == null) spawnerYawDegrees = new ZUIValue(0f);
            if (pathProgress == null) pathProgress = new ZUIValue(0f);
            if (scaleByIndex == null) scaleByIndex = new ZUIValue(1f);
            if (spawnTiming == null) spawnTiming = DefaultSpawnTiming();
        }

        /// <summary>A linear (0,0)→(1,1) ramp: the first instance is born at the document's start and the last
        /// at its end. Built as a Curve rather than left Static so the dial opens already showing the mapping
        /// it represents — a Static default would birth every instance at one moment, which is a legal but
        /// misleading first impression of what Window timing does.</summary>
        static ZUIValue DefaultSpawnTiming()
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 1f));
            return v;
        }

        public Vector2 positionJitter
        {
            get => new Vector2(ShaperDial.Get(positionJitterX, 24f), ShaperDial.Get(positionJitterY, 24f));
            set { ShaperDial.Set(ref positionJitterX, value.x); ShaperDial.Set(ref positionJitterY, value.y); }
        }

        public float rotationJitterDegrees
        {
            get => ShaperDial.Get(rotationJitterDegreesDial);
            set => ShaperDial.Set(ref rotationJitterDegreesDial, value);
        }

        public float scaleJitter
        {
            get => ShaperDial.Get(scaleJitterDial);
            set => ShaperDial.Set(ref scaleJitterDial, value);
        }

        /// <summary>The jitter RANGES at <paramref name="phase01"/> — the ranges themselves are animatable, so a
        /// swarm can start tight and spread out; the per-instance draw within them stays a pure hash of
        /// <see cref="seed"/> and the instance index.</summary>
        public void SampleJitter(float phase01, uint dialSeed, out Vector2 position, out float rotation, out float scale)
        {
            EnsureDials();
            position = new Vector2(
                ShaperValue.Sample(positionJitterX, phase01, dialSeed ^ FldPosJitterX, 24f),
                ShaperValue.Sample(positionJitterY, phase01, dialSeed ^ FldPosJitterY, 24f));
            rotation = ShaperValue.Sample(rotationJitterDegreesDial, phase01, dialSeed ^ FldRotJitter, 0f);
            scale = Mathf.Clamp01(ShaperValue.Sample(scaleJitterDial, phase01, dialSeed ^ FldScaleJitter, 0f));
        }

        /// <summary>The spawner's own dials resolved at one document phase — sampled ONCE per compile, never
        /// per instance, because the spawner is one figure the whole swarm sits on rather than a per-instance
        /// value (BC-1.2's "evaluate into the program, not inside the loop", applied to a swarm).</summary>
        public ShaperSpawner SampleSpawner(float phase01, uint dialSeed)
        {
            EnsureDials();
            return new ShaperSpawner
            {
                radius = Mathf.Max(0f, ShaperValue.Sample(spawnerRadius, phase01, dialSeed ^ FldRadius, 24f)),
                offset = new Vector2(ShaperValue.Sample(spawnerOffsetX, phase01, dialSeed ^ FldOffsetX, 0f),
                                     ShaperValue.Sample(spawnerOffsetY, phase01, dialSeed ^ FldOffsetY, 0f)),
                rotationDegrees = ShaperValue.Sample(spawnerRotationDegrees, phase01, dialSeed ^ FldSpawnerRot, 0f),
                pitchDegrees = ShaperValue.Sample(spawnerPitchDegrees, phase01, dialSeed ^ FldSpawnerPitch, 0f),
                yawDegrees = ShaperValue.Sample(spawnerYawDegrees, phase01, dialSeed ^ FldSpawnerYaw, 0f),
            };
        }

        /// <summary>The document phase instance number <paramref name="i"/> of <paramref name="count"/> is
        /// BORN at, under whichever timing mode is authored. Meaningless under
        /// <see cref="ShaperSwarmTiming.Stagger"/>, where instances have no birth at all — the caller checks
        /// the mode before asking.</summary>
        public float SpawnPhase(int i, int count, uint dialSeed)
        {
            if (timing == ShaperSwarmTiming.FrameStep)
                return Mathf.Clamp01(firstSpawnPhase + i * spawnPhaseStep);
            float t = count > 1 ? i / (float)(count - 1) : 0f;
            return Mathf.Clamp01(ShaperValue.Sample(spawnTiming, t, dialSeed ^ FldSpawnTiming, t));
        }

        /// <summary>The per-instance size multiplier read at <c>index/(count-1)</c>. 1 when the dial is at its
        /// default, which the caller folds in as a zero scale bias.</summary>
        public float ScaleAtIndex(int i, int count, uint dialSeed)
        {
            float t = count > 1 ? i / (float)(count - 1) : 0f;
            return ShaperValue.Sample(scaleByIndex, t, dialSeed ^ FldScaleByIndex, 1f);
        }

        /// <summary>Where instance <paramref name="posIdx"/> lands, and which way it faces — the whole
        /// placement pipeline for one instance, in the node's own parent-local units with the node's authored
        /// position as the origin. <paramref name="instancePhase"/> is the phase the outline progress is read
        /// at, so instances at different points in their own life ride different points of an animated path.
        /// </summary>
        public void PlaceInstance(int posIdx, int count, in ShaperSpawner spawner, float instancePhase,
                                  uint dialSeed, out Vector2 offset, out float orientDegrees)
        {
            offset = Vector2.zero;
            orientDegrees = 0f;
            if (shape == ShaperSwarmShape.None) return;

            float progress = 0f;
            if (spawnMode == ShaperSwarmSpawnMode.Path)
            {
                progress = ShaperValue.Sample(pathProgress, instancePhase, dialSeed ^ FldProgress, 0f);
                if (evenSpacing && count > 1)
                {
                    // A FULL lap divides by the count, not by count-1: the outline is closed, so progress 0
                    // and progress 1 are the same point and dividing by count-1 lands the first and last
                    // instance on top of each other. A partial arc is open, so it divides by count-1 and its
                    // ends sit exactly on the ends of the arc. (Pyre always divides by count-1 and therefore
                    // doubles up on a full lap — the maths is ported, the wart is not.)
                    float spread = Mathf.Clamp01(pathSpread);
                    int denom = spread >= 1f ? count : count - 1;
                    progress += posIdx / (float)denom * spread;
                }
            }

            Vector2 local = ShaperSwarmPlacement.Place(shape, spawnMode, spawner.radius, count, posIdx,
                                                       distribution, gridReverse, progress, seed);
            Vector2 placed = ShaperSwarmPlacement.ApplySpawnerTransform(
                local, spawner.radius, spawner.rotationDegrees, spawner.yawDegrees, spawner.pitchDegrees, out _);
            offset = placed + spawner.offset;

            if (orient == ShaperSwarmOrient.None) return;

            // The angle is a MATH angle (0 = +X). A Shaper primitive is drawn pointing +Y, so the instance's
            // own rotation is that angle less a quarter turn — which is what makes an Outward-oriented star
            // point away from the centre rather than lying on its side.
            bool wantTangent = orient == ShaperSwarmOrient.PathTangent
                               && spawnMode == ShaperSwarmSpawnMode.Path;
            if (wantTangent)
            {
                Vector2 tangent = ShaperSwarmPlacement.PathTangent(shape, spawner.radius, progress);
                if (spawner.rotationDegrees != 0f)
                {
                    float a = spawner.rotationDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    tangent = new Vector2(tangent.x * c - tangent.y * s, tangent.x * s + tangent.y * c);
                }
                if (tangent.sqrMagnitude > 1e-8f)
                {
                    orientDegrees = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90f;
                    return;
                }
            }

            // Outward — and the honest fallback for a PathTangent asked for in Area mode, or on a degenerate
            // tangent: a facing the user can see is better than silently no facing at all.
            if (placed.sqrMagnitude > 1e-8f)
                orientDegrees = Mathf.Atan2(placed.y, placed.x) * Mathf.Rad2Deg - 90f;
        }

        // ── preview overlay (IShaperPreviewOverlay) ──────────────────────────────────────────────────────
        // A swarm is the one authored thing in Shaper whose result cannot be read off the picture: the
        // instances are UNIONED into a single field, so a cloud of eight overlapping shapes and a cloud of
        // three look the same, and moving a spawner dial changes a silhouette without saying where anything
        // actually went. Marking the spawner figure and every real placement is what makes the dials
        // author-able by eye instead of by trial.
        //
        // See ShaperPreviewOverlay.cs for why this is an interface the swarm implements rather than a toggle
        // in the preview chrome.

        public string OverlayLabel => "Swarm spawns";

        public string OverlayTooltip =>
            "Marks this swarm's spawn figure — a cyan outline — and a warm dot at every place it puts an "
            + "instance, sampled at the frame on screen, so the marks turn and grow with an animated spawner "
            + "instead of freezing at frame 0.\n\n"
            + "The dots are PLACEMENTS, before the position-jitter dials scatter each instance off its own "
            + "spot: a dot says where the swarm decided to put something, and how far the drawn shape sits "
            + "from its dot is how much jitter you have authored. Preview only — nothing drawn here can reach "
            + "a bake.";

        /// A configuration question, not an enabled one: a swarm with no shape leaves every instance on the
        /// node's own origin, so the whole overlay would be one dot under one dot. Offering a toggle for that
        /// is worse than offering none.
        public bool WantsPreviewOverlay => enabled && count > 1 && shape != ShaperSwarmShape.None;

        public void DrawPreviewOverlay(ShaperOverlayCanvas canvas)
        {
            if (canvas?.pixels == null || !WantsPreviewOverlay) return;

            // The SAME sampling call the compiler makes (ShaperCompiler.cs:463, 643), at the frame on screen —
            // so a rotating or expanding spawner is drawn where it is now, not where it started. The seed is
            // the document's, which is what the compile state carries at a layer root; a MinMax spawner dial
            // under a differently-seeded ancestor would draw a placement one draw away from the rendered one,
            // which is a mark being approximate rather than a mark being wrong.
            var spawner = SampleSpawner(canvas.phase01, canvas.seed);
            Vector2 centre = canvas.origin + spawner.offset;

            var outline = new Color32(90, 220, 255, 190);
            var dotColor = new Color32(255, 190, 70, 230);

            // The figure. Only the two closed forms have an outline worth tracing at canvas resolution; every
            // other shape is described honestly by its own placements, which are drawn below either way.
            if (spawnMode == ShaperSwarmSpawnMode.Area || shape != ShaperSwarmShape.Line)
                canvas.Circle(centre, spawner.radius, outline);
            if (shape == ShaperSwarmShape.Line)
            {
                float rad = spawner.rotationDegrees * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * spawner.radius;
                canvas.Line(centre - dir, centre + dir, outline);
            }

            // The placements themselves — the truth the outline only approximates once distribution, spawn
            // order or an animated path progress is authored. Each is asked for exactly as the compiler asks
            // (same PlaceInstance, same permutation) so a dot marks a real instance, never an estimate.
            int n = Mathf.Clamp(count, 1, 64);
            int[] perm = ShaperSwarmPlacement.BuildSpawnPermutation(shape, spawnMode, spawner.radius, n,
                Mathf.Clamp01(spawnOrderChaos), distribution, gridReverse, seed);
            for (int i = 0; i < n; i++)
            {
                PlaceInstance(perm != null ? perm[i] : i, n, spawner, canvas.phase01, canvas.seed,
                              out Vector2 place, out _);
                canvas.Dot(canvas.origin + place, 1.5f, dotColor);
            }
        }
    }

    /// <summary>The swarm's spawner figure resolved at one document phase — see
    /// <see cref="ShaperSwarmDef.SampleSpawner"/>.</summary>
    public struct ShaperSpawner
    {
        public float radius;
        public Vector2 offset;
        public float rotationDegrees;
        public float pitchDegrees;
        public float yawDegrees;
    }
}

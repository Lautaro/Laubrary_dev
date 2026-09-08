using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// Flattens the authored tree into a <see cref="ShaperProgram"/>. Run once per frame-time, never per
    /// sample.
    ///
    /// It does three things the reference app does not, and each fixes a named defect:
    /// <list type="number">
    /// <item>It accumulates the full root→leaf inverse transform per leaf, so a leaf is evaluated by mapping
    /// the canvas point straight into that leaf's own local space with one precomputed 2×3. No point stack,
    /// no per-node re-transform.</item>
    /// <item>It stores, per leaf, <c>distanceScale = σ_min(M)</c> of the accumulated forward linear part, and
    /// the leaf's local distance is multiplied by it on the way out. The reference app's <c>evalShape</c>
    /// returns the child's raw value across a transform, which is the whole of its 5.00× over-report at
    /// <c>scale.x = 0.2</c>.</item>
    /// <item>It emits post-order RPN, so evaluation is a flat loop over ops against a small value stack.</item>
    /// </list>
    /// </summary>
    public static class ShaperCompiler
    {
        struct Box
        {
            public bool valid;
            public float minX, minY, maxX, maxY;

            public static Box Invalid => default;

            public static Box FromCentre(float cx, float cy, float hw, float hh)
                => new Box { valid = true, minX = cx - hw, minY = cy - hh, maxX = cx + hw, maxY = cy + hh };

            public Box Union(in Box o)
            {
                if (!valid) return o;
                if (!o.valid) return this;
                return new Box
                {
                    valid = true,
                    minX = Mathf.Min(minX, o.minX), minY = Mathf.Min(minY, o.minY),
                    maxX = Mathf.Max(maxX, o.maxX), maxY = Mathf.Max(maxY, o.maxY),
                };
            }

            public Box Intersect(in Box o)
            {
                if (!valid || !o.valid) return Invalid;
                float nx = Mathf.Max(minX, o.minX), ny = Mathf.Max(minY, o.minY);
                float xx = Mathf.Min(maxX, o.maxX), xy = Mathf.Min(maxY, o.maxY);
                if (nx > xx || ny > xy) return Invalid;
                return new Box { valid = true, minX = nx, minY = ny, maxX = xx, maxY = xy };
            }

            public Box Grow(float m)
            {
                if (!valid) return this;
                return new Box { valid = true, minX = minX - m, minY = minY - m, maxX = maxX + m, maxY = maxY + m };
            }

            public float CentreX => (minX + maxX) * 0.5f;
            public float CentreY => (minY + maxY) * 0.5f;
            public float HalfW => (maxX - minX) * 0.5f;
            public float HalfH => (maxY - minY) * 0.5f;

            /// <summary>
            /// Carry this box through an affine map by the absolute-value corner sum, producing the smallest
            /// axis-aligned box that contains the mapped one.
            ///
            /// Used ONLY with a RELATIVE (child-to-parent) matrix when folding the node-local support box of
            /// FC-1.5a. That restriction is the whole point: applied with the ACCUMULATED forward it would
            /// reproduce the canvas box's rotation-dependent inflation, which is exactly what the local box
            /// exists to avoid. A child rotated inside its bag genuinely does change the bag's local extent, so
            /// the inflation is correct at this scope; the node's OWN rotation never enters, so its own box is
            /// stable.
            /// </summary>
            public Box Map(in ShaperMatrix m)
            {
                if (!valid) return this;
                float cx = CentreX, cy = CentreY, hw = HalfW, hh = HalfH;
                float nx = m.m00 * cx + m.m01 * cy + m.m02;
                float ny = m.m10 * cx + m.m11 * cy + m.m12;
                float nhw = Mathf.Abs(m.m00) * hw + Mathf.Abs(m.m01) * hh;
                float nhh = Mathf.Abs(m.m10) * hw + Mathf.Abs(m.m11) * hh;
                return FromCentre(nx, ny, nhw, nhh);
            }
        }

        struct Emitted
        {
            public float bound;
            public Box box;
            public ShaperSweepAxis sweepAxis;

            /// <summary>The support box in the emitting node's OWN local frame (FC-1.5a).</summary>
            public Box localBox;
            /// <summary>This node's own transform block, i.e. the map from its local frame to its parent's.</summary>
            public ShaperMatrix localToParent;
            /// <summary>The accumulated root→node inverse, for the fill stage's Stamped anchor (FC-1.5).</summary>
            public ShaperMatrix inverse;
            public bool invertible;

            /// <summary>
            /// The worst ratio <c>σ_max / σ_min</c> of the accumulated forward map anywhere in this subtree —
            /// the factor by which the published distance can UNDER-report the true canvas distance.
            ///
            /// <b>Why it exists, and what it is for.</b> Every node publishes <c>σ_min · d_local</c>
            /// (<see cref="ShaperMatrix.SigmaMin"/>), which is a conservative under-estimate: on an
            /// anisotropically scaled member a REPORTED distance of <c>r</c> is reached as far as
            /// <c>r · σ_max/σ_min</c> canvas pixels from the boundary along the stretched axis. So any operator
            /// that grows the support box by a distance expressed in the FIELD's units — <c>Shell</c>'s
            /// thickness, a joining border's reach (BD-2.4) — must grow the CANVAS box by that distance times
            /// this factor, or the box excludes real samples. Measured before this was carried: an Outward
            /// border of reach 8 on a member scaled (2.0, 0.5) put 808 samples of its own silhouette OUTSIDE
            /// the declared box, overshooting it by 23.5 px.
            ///
            /// It is a MAX over the subtree rather than the node's own transform, because a bag with an
            /// identity transform inherits its child's anisotropy: the same measurement on a bag wrapping that
            /// member reproduced the same 808 samples and the same 23.5 px.
            ///
            /// Read through <see cref="Spread"/>, never directly: <c>Emitted</c> is a struct, so an
            /// unset field is 0, and a 0 here would silently multiply every box growth to nothing.
            /// </summary>
            public float spreadRaw;
            /// <summary>Never below 1. See <see cref="spreadRaw"/> for why the property and not the field.</summary>
            public float Spread => spreadRaw >= 1f ? spreadRaw : 1f;

            /// <summary>
            /// <c>σ_min</c> of the accumulated forward map at THIS node — the number that converts a canvas
            /// distance into this node's own local units. Published so a strip can grow its node-local anchor
            /// box the way <see cref="EmitShell"/> already grows a shell's (BD-2.4 / FC-1.5a).
            /// Read through <see cref="SigmaMin"/>, for the same struct-default reason as above.
            /// </summary>
            public float sigmaMinRaw;
            /// <summary>Never zero or negative — it is divided by. See <see cref="sigmaMinRaw"/>.</summary>
            public float SigmaMin => sigmaMinRaw > 1e-9f ? sigmaMinRaw : 1f;
        }

        class State
        {
            public List<ShaperOp> ops = new List<ShaperOp>();
            public ShaperProgram program = new ShaperProgram();
            /// <summary>T-0112 — every composite node's baked picture, collected in emission order.</summary>
            public List<ShaperCompiledComposite> composites = new List<ShaperCompiledComposite>();
            /// <summary>T-0175 — every Sprite primitive's baked distance raster, collected in emission order.</summary>
            public List<ShaperCompiledSpriteField> spriteFields = new List<ShaperCompiledSpriteField>();
            /// <summary>T-0174 — every Text primitive's baked distance raster, collected in emission order.</summary>
            public List<ShaperCompiledTextField> textFields = new List<ShaperCompiledTextField>();
            public int depth;
            public int maxDepth;
            public float phase01;
            public uint seed;

            public void Push(int n = 1) { depth += n; if (depth > maxDepth) maxDepth = depth; }
            public void Pop(int n = 1) { depth -= n; }
        }

        /// <summary>
        /// Compile a tree for one normalised frame time, in the layer frame.
        ///
        /// Kept EXACTLY as shipped — signature, defaults and behaviour — because the field audit and every
        /// existing caller use it. It now delegates to the <c>parentForward</c> overload with
        /// <see cref="ShaperMatrix.Identity"/>, which is the value it used to hardcode inline.
        /// </summary>
        public static ShaperProgram Compile(ShaperNode root, float phase01 = 0f, uint seed = 0u)
            => Compile(root, ShaperMatrix.Identity, phase01, seed);

        /// <summary>
        /// Compile a tree for one normalised frame time, seeded with a starting forward matrix.
        ///
        /// <b>The one addition the fill stage requires of the shape stage (FILL-CONTRACT FC-3.9).</b> Resolving
        /// a fill-owning node's own coverage means evaluating that node's subtree standalone, and
        /// <c>ShaperEvaluator.Distance</c> returns only <c>stack[0]</c> (<c>ShaperEvaluator.cs:113</c>) while
        /// <c>ShaperOp</c> carries no tap — so an intermediate node's coverage cannot be extracted from a
        /// whole-tree program. <c>Compile</c> already accepts ANY node as a root, which makes one program per
        /// fill-owning node nearly free; the only catch was that it seeded Identity, so a subtree compiled
        /// standalone landed in its own local frame rather than the layer frame. This overload takes the
        /// accumulated forward of the node's PARENT, which is the same thing <c>EmitNode</c> already accepts.
        ///
        /// The route not taken was tapping the RPN stack at named ops. It is cheaper at evaluation time — one
        /// walk instead of k — but it costs a per-op tap index, a tap output array and a rule about what the
        /// stack holds at each op, and it couples the fill stage to the RPN layout. This route needs one
        /// parameter instead of a mechanism, each program is independently cacheable under R7's rules, and each
        /// program carries its own support box so a fill's paint pass is bounded by the pixels it can affect.
        /// The honest cost: k fill-owning nodes means k+1 field evaluations at a sample covered by all of them.
        /// </summary>
        public static ShaperProgram Compile(ShaperNode root, in ShaperMatrix parentForward,
                                            float phase01 = 0f, uint seed = 0u)
        {
            var st = new State { phase01 = Mathf.Clamp01(phase01), seed = seed };
            st.program.phase01 = st.phase01;

            Emitted top;
            if (root == null || !root.enabled)
            {
                EmitEmpty(st);
                top = new Emitted
                {
                    bound = 1f,
                    box = Box.Invalid,
                    sweepAxis = ShaperSweepAxis.Radial,
                    localBox = Box.Invalid,
                    localToParent = ShaperMatrix.Identity,
                    inverse = ShaperMatrix.Identity,
                    invertible = false,
                    spreadRaw = 1f,
                    sigmaMinRaw = 1f,
                };
            }
            else
            {
                top = EmitNodeMaybeSwarm(root, parentForward, st, true);
            }

            st.program.ops = st.ops.ToArray();
            st.program.composites = st.composites.ToArray();
            st.program.spriteFields = st.spriteFields.ToArray();
            st.program.textFields = st.textFields.ToArray();   // T-0174
            st.program.stackDepth = Mathf.Max(1, st.maxDepth);
            st.program.bound = top.bound;
            if (top.box.valid)
            {
                st.program.supportCx = top.box.CentreX;
                st.program.supportCy = top.box.CentreY;
                st.program.supportHalfW = top.box.HalfW;
                st.program.supportHalfH = top.box.HalfH;
            }

            // BD-2.4: the factor by which a distance expressed in the FIELD's units must be multiplied to bound
            // the same distance in CANVAS units, and this root's own σ_min. Both are published so that a strip
            // compiled OUTSIDE the compiler (ShaperBorder.CompileStrip, the unjoined path) grows the same two
            // boxes by the same two numbers the compiler would have used.
            st.program.supportSpread = top.Spread;
            st.program.rootSigmaMin = top.SigmaMin;

            // FC-1.5a: the node-local support box and the accumulated inverse, for the fill stage's anchor.
            st.program.rootInverse = top.inverse;
            st.program.rootInvertible = top.invertible;
            st.program.hasLocalSupport = top.localBox.valid;
            if (top.localBox.valid)
            {
                st.program.localSupportCx = top.localBox.CentreX;
                st.program.localSupportCy = top.localBox.CentreY;
                st.program.localSupportHalfW = top.localBox.HalfW;
                st.program.localSupportHalfH = top.localBox.HalfH;
            }
            return st.program;
        }

        static void EmitEmpty(State st)
        {
            st.ops.Add(new ShaperOp { kind = ShaperOpKind.Empty, bound = 1f, distanceScale = 1f });
            st.Push();
        }

        /// <param name="isRoot">
        /// True only for the node <see cref="Compile"/> was handed. It is needed for exactly one reason —
        /// BD-3.7's refusal of a border on a Subtract MEMBER, where a root's <see cref="ShaperNode.mode"/> is
        /// ignored data and must not silence the root's own border.
        /// </param>
        static Emitted EmitNode(ShaperNode node, in ShaperMatrix parentForward, State st, bool isRoot = false)
        {
            var block = node.transform ?? new ShaperTransformBlock();
            ShaperMatrix localToParent = block.ToMatrix(st.phase01, st.seed);
            ShaperMatrix forward = ShaperMatrix.Mul(parentForward, localToParent);
            bool invertible = forward.TryInvert(out ShaperMatrix inverse);
            forward.SingularValues(out float sigmaMin, out float sigmaMax);

            Emitted e;
            if (!invertible || sigmaMin <= 1e-9f)
            {
                // A singular linear part publishes the empty field and is flagged. It must not divide.
                // EVERY offender is counted; only the first supplies the name.
                st.program.singularTransformCount++;
                if (!st.program.hasSingularTransform)
                {
                    st.program.hasSingularTransform = true;
                    st.program.singularNode = node.name;
                }
                EmitEmpty(st);
                e = new Emitted { bound = 1f, box = Box.Invalid, sweepAxis = ShaperSweepAxis.Radial,
                                  localBox = Box.Invalid, spreadRaw = 1f, sigmaMinRaw = 1f };
            }
            else if (node.kind == ShaperNodeKind.Primitive)
            {
                e = EmitLeaf(node, forward, inverse, sigmaMin, st);
                // BD-2.4: a leaf publishes σ_min · d_local, so a reported distance r is reached as far as
                // r · σ_max/σ_min canvas pixels out. That ratio is this subtree's whole under-report factor.
                e.spreadRaw = sigmaMax / sigmaMin;
            }
            else if (node.kind == ShaperNodeKind.Composite)
            {
                e = EmitComposite(node, forward, inverse, sigmaMin, st);
                // Same reasoning as a Leaf immediately above: this is this subtree's own under-report factor,
                // even though a composite's reported value is a pseudo-distance rather than a true SDF.
                e.spreadRaw = sigmaMax / sigmaMin;
            }
            else if (node.kind == ShaperNodeKind.Solid)
            {
                e = EmitSolid(node, forward, inverse, sigmaMin, st);
                e.spreadRaw = sigmaMax / sigmaMin;
            }
            else
            {
                e = EmitBag(node, forward, st);
                // EmitBag already carries the MAX over its members: a bag with an identity transform still
                // inherits an anisotropic child's under-report, which is the case that first exposed this.
            }

            // σ_min at THIS node, for the node-local box growth a strip needs (FC-1.5a).
            e.sigmaMinRaw = sigmaMin;

            e = EmitSweep(node, forward, inverse, sigmaMin, e, st);
            e = EmitShell(node, sigmaMin, e, st);
            e = EmitBorderJoin(node, isRoot, e, st);

            // Carried on the way out so a parent bag can fold this node's LOCAL box into its own frame, and so
            // the top-level Compile can publish the accumulated inverse for the fill anchor (FC-1.5a).
            e.localToParent = localToParent;
            e.inverse = invertible ? inverse : ShaperMatrix.Identity;
            e.invertible = invertible && sigmaMin > 1e-9f;
            return e;
        }

        // ── T-0113 swarm ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>An avalanche hash combining two inputs — never <c>System.Random</c>/<c>UnityEngine.Random</c>
        /// (BC-1.3), same posture as <see cref="ShaperValue"/>'s own hash, restated here so swarm jitter is
        /// deterministic per (seed, instance) and re-evaluates identically every compile.</summary>
        static uint Hash(uint a, uint b)
        {
            uint x = a ^ (b * 0x9E3779B9u + 0x7f4a7c15u + (a << 6) + (a >> 2));
            x ^= x >> 16; x *= 0x7feb352du;
            x ^= x >> 15; x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        static float Unit(uint h) => (h & 0x00FFFFFFu) * (1f / 16777216f);

        /// <summary>The one call site every ordinary caller of <c>EmitNode</c> now goes through — checks the
        /// node's own <see cref="ShaperNode.swarm"/> and routes to <see cref="EmitSwarm"/> when it is enabled
        /// with more than one instance; otherwise this is an exact pass-through to <see cref="EmitNode"/>, so an
        /// un-swarmed tree compiles bit-identically to before this task (<c>ShaperSwarmAudit</c> CT-0).</summary>
        static Emitted EmitNodeMaybeSwarm(ShaperNode node, in ShaperMatrix parentForward, State st, bool isRoot)
        {
            var swarm = node.swarm;
            if (swarm == null || !swarm.enabled || swarm.count <= 1) return EmitNode(node, parentForward, st, isRoot);
            return EmitSwarm(node, parentForward, st, isRoot);
        }

        /// <summary>
        /// Decides which of the two implementations runs and publishes the decision onto
        /// <see cref="ShaperProgram"/> (never authored — <see cref="ShaperSwarmImplementation"/>'s class doc),
        /// then delegates. A Composite node whose source offers <see cref="IShaperSwarmNativeSource"/> AND
        /// currently reports it supported gets Native; every other node kind, and every Composite source that
        /// does not offer it, gets the Generic wrapper — a fully legitimate, not a degraded, outcome.
        ///
        /// A Composite source that also declares itself a stateful simulation
        /// (<see cref="IShaperSimulationSource.IsStatefulSimulation"/>) and does NOT offer Native has its count
        /// clamped to <see cref="ShaperSwarmDef.SimulationHardCap"/> — the explicit hard-cap-with-a-visible-
        /// warning fallback the task body names, protecting against the measured non-linear cost of running an
        /// independent full simulation per instance (SWARM-SPEC.md §5).
        /// </summary>
        static Emitted EmitSwarm(ShaperNode node, in ShaperMatrix parentForward, State st, bool isRoot)
        {
            var swarm = node.swarm;
            int count = Mathf.Clamp(swarm.count, 1, 64);

            var source = node.kind == ShaperNodeKind.Composite ? node.composite?.source : null;
            bool isSim = source is IShaperSimulationSource simSrc && simSrc.IsStatefulSimulation;
            bool hasNative = source is IShaperSwarmNativeSource nativeSrc && nativeSrc.SupportsNativeSwarm;

            bool capped = false;
            string capReason = null;
            if (isSim && !hasNative && count > ShaperSwarmDef.SimulationHardCap)
            {
                capped = true;
                capReason = "stateful simulation source with no native batched swarm path -- N independent " +
                            "full simulations cost roughly N times one (SWARM-SPEC.md Part 5), so count is " +
                            "held at " + ShaperSwarmDef.SimulationHardCap + " instead of the authored " + count + ".";
                count = ShaperSwarmDef.SimulationHardCap;
            }

            ShaperSwarmImplementation impl = hasNative ? ShaperSwarmImplementation.Native : ShaperSwarmImplementation.Generic;

            st.program.swarmNodeCount++;
            if (!st.program.hasSwarm)
            {
                st.program.hasSwarm = true;
                st.program.swarmImplementation = impl;
                st.program.swarmImplementationNode = node.name;
                st.program.swarmCount = count;
                st.program.swarmCapped = capped;
                st.program.swarmCapReason = capReason;
            }

            Emitted e = hasNative
                ? EmitNativeSwarm(node, parentForward, count, st)
                : EmitGenericSwarm(node, parentForward, count, st, isRoot);

            // A swarm has no single coherent local frame (N differently-jittered instances) — the node's own
            // BASE (un-jittered) transform stands in for the local-frame fields a parent bag's box fold or a
            // fill-anchor subtree compile may read off this node, exactly as EmitNode publishes them for an
            // ordinary node. Documented approximation, not a bug: SWARM-SPEC.md §4.
            ShaperMatrix baseLocalToParent = (node.transform ?? new ShaperTransformBlock()).ToMatrix(st.phase01, st.seed);
            ShaperMatrix baseForward = ShaperMatrix.Mul(parentForward, baseLocalToParent);
            bool baseInvertible = baseForward.TryInvert(out ShaperMatrix baseInverse);
            baseForward.SingularValues(out float baseSigmaMin, out _);
            e.localToParent = baseLocalToParent;
            e.inverse = baseInvertible ? baseInverse : ShaperMatrix.Identity;
            e.invertible = baseInvertible;
            e.sigmaMinRaw = baseSigmaMin;
            return e;
        }

        /// <summary>
        /// The generic wrapper (default/fallback): compiles <paramref name="count"/> full copies of
        /// <paramref name="node"/>'s own content -- kind dispatch, sweep, shell, border, everything a plain
        /// <see cref="EmitNode"/> call would produce -- each with its own jittered transform/phase/seed, and
        /// unions them exactly the way <see cref="EmitBag"/> unions sibling members. Works on EVERY node kind
        /// with ZERO extra code from the generator, because the jitter is applied to the node's own
        /// <see cref="ShaperNode.transform"/> and to the compiler's own <see cref="State.phase01"/>/<c>seed</c>
        /// -- both already generic plumbing every node kind reads.
        ///
        /// Implementation note: mutates <paramref name="node"/>'s transform and its own <c>swarm.enabled</c> for
        /// the duration of each instance's <see cref="EmitNode"/> call and restores them in a <c>finally</c> --
        /// safe under Shaper's ownership model (a node is not shared by reference across two parents in one
        /// tree) and the compiler's single-threaded, synchronous, non-reentrant-per-node call shape. The
        /// <c>swarm.enabled</c> guard is what stops instance i's own <c>EmitNode</c> call from re-entering
        /// <see cref="EmitSwarm"/> on the SAME node.
        /// </summary>
        static Emitted EmitGenericSwarm(ShaperNode node, in ShaperMatrix parentForward, int count, State st, bool isRoot)
        {
            var swarm = node.swarm;
            var transform = node.transform ?? (node.transform = new ShaperTransformBlock());

            EmitEmpty(st);
            float bound = 1f;
            Box box = Box.Invalid;
            Box localBox = Box.Invalid;
            bool anyAxis = false;
            ShaperSweepAxis axis = ShaperSweepAxis.Radial;
            bool axisAgrees = true;
            float spread = 1f;

            // The per-instance offset is written to the transform block's OWN jitter slots rather than onto its
            // authored dials: a dial may be a Curve, and the only way to write a jittered number onto one would
            // be to overwrite its static value, which silently throws the curve away.
            Vector2 originalInstanceTranslate = transform.instanceTranslate;
            float originalInstanceRotation = transform.instanceRotationDegrees;
            float originalInstanceScaleBias = transform.instanceScaleBias;
            bool originalSwarmEnabled = swarm.enabled;

            swarm.SampleJitter(st.phase01, st.seed, out Vector2 jitterRange, out float rotJitterRange,
                               out float scaleJitterRange);
            float originalPhase = st.phase01;
            uint originalSeed = st.seed;

            // T-0169 — the spawner is ONE figure the whole swarm sits on, so its dials are sampled once here
            // rather than per instance, and the spawn-order permutation is built once for the same reason.
            // Both collapse to an exact no-op when no shape is authored, which is the default.
            ShaperSpawner spawner = swarm.SampleSpawner(st.phase01, st.seed);
            bool placed = swarm.shape != ShaperSwarmShape.None;
            int[] spawnPerm = (placed || swarm.spawnOrderChaos > 0f)
                ? ShaperSwarmPlacement.BuildSpawnPermutation(swarm.shape, swarm.spawnMode, spawner.radius, count,
                    Mathf.Clamp01(swarm.spawnOrderChaos), swarm.distribution, swarm.gridReverse, swarm.seed)
                : null;

            // Die-together needs the LAST birth before any instance is placed, so it is computed up front:
            // every instance then shares that one death moment instead of each carrying its own.
            bool timed = swarm.timing != ShaperSwarmTiming.Stagger;
            float sharedDeath = 0f;
            if (timed && swarm.dieTogether)
            {
                float lastSpawn = 0f;
                for (int i = 0; i < count; i++) lastSpawn = Mathf.Max(lastSpawn, swarm.SpawnPhase(i, count, st.seed));
                sharedDeath = Mathf.Min(1f, lastSpawn + Mathf.Max(0.01f, swarm.instanceLife));
            }

            // -1 marks an instance that is not alive at this document phase and was therefore never emitted.
            var phasesUsed = new float[count];

            swarm.enabled = false;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    uint h0 = Hash(swarm.seed, (uint)i * 2654435761u + 1u);
                    uint h1 = Hash(h0, 0x9E3779B1u);
                    uint h2 = Hash(h1, 0x85EBCA77u);
                    uint h3 = Hash(h2, 0xC2B2AE3Du);
                    uint h4 = Hash(h3, 0x27D4EB2Fu);

                    float jx = (Unit(h0) * 2f - 1f) * jitterRange.x;
                    float jy = (Unit(h1) * 2f - 1f) * jitterRange.y;
                    float jrot = (Unit(h2) * 2f - 1f) * rotJitterRange;
                    float jscale = (Unit(h3) * 2f - 1f) * scaleJitterRange;
                    float jphaseRaw = Unit(h4);

                    // T-0169 — under Window/FrameStep an instance has a BIRTH and a DEATH, and outside that
                    // span it is not emitted at all: the swarm genuinely appears and clears rather than
                    // fading a permanently-present instance, which is the thing a spawn timing is for.
                    float instancePhase;
                    if (timed)
                    {
                        float birth = swarm.SpawnPhase(i, count, originalSeed);
                        float death = swarm.dieTogether
                            ? sharedDeath
                            : Mathf.Min(1f, birth + Mathf.Max(0.01f, swarm.instanceLife));
                        // `death < birth` only — an instance born ON the last frame has a zero-length life and
                        // must still be drawn for that one frame, or the last of a spread-out swarm never
                        // appears at all.
                        if (originalPhase < birth || originalPhase > death || death < birth)
                        {
                            phasesUsed[i] = -1f;
                            continue;
                        }
                        instancePhase = Mathf.Clamp01((originalPhase - birth) / Mathf.Max(1e-4f, death - birth));
                    }
                    else
                    {
                        // THE shared-clock fix: at lifetimeStagger=1 each instance's phase is drawn
                        // independently across the whole cycle; at 0 every instance shares the node's own
                        // phase, the old broken behaviour, kept selectable rather than deleted
                        // (ShaperSwarmDef.lifetimeStagger doc).
                        instancePhase = Mathf.Clamp01(Mathf.Lerp(originalPhase, jphaseRaw,
                                                                 Mathf.Clamp01(swarm.lifetimeStagger)));
                    }

                    int posIdx = spawnPerm != null ? spawnPerm[i] : i;
                    Vector2 place = Vector2.zero;
                    float orientDeg = 0f;
                    float sizeBias = 0f;
                    if (placed)
                    {
                        swarm.PlaceInstance(posIdx, count, spawner, instancePhase, originalSeed,
                                            out place, out orientDeg);
                        sizeBias = swarm.ScaleAtIndex(posIdx, count, originalSeed) - 1f;
                    }

                    transform.instanceTranslate = new Vector2(place.x + jx, place.y + jy);
                    transform.instanceRotationDegrees = jrot + orientDeg;
                    transform.instanceScaleBias = jscale + sizeBias;

                    st.phase01 = instancePhase;
                    st.seed = Hash(originalSeed, h0);
                    phasesUsed[i] = st.phase01;

                    Emitted ce = EmitNode(node, parentForward, st, isRoot);

                    if (!anyAxis) { axis = ce.sweepAxis; anyAxis = true; }
                    else if (axis != ce.sweepAxis) axisAgrees = false;

                    var merge = swarm.merge ?? new ShaperBlend();
                    merge.Sample(st.phase01, st.seed, out float width, out float mergeSharpness, out float strength);

                    Box combined = box.Union(ce.box);
                    Box childLocal = ce.localBox.Map(ce.localToParent);
                    Box combinedLocal = localBox.Union(childLocal);

                    Box reachBox = box.Union(ce.box);
                    float reach = reachBox.valid ? 0.55f * Mathf.Min(reachBox.HalfW * 2f, reachBox.HalfH * 2f) : 0f;

                    st.ops.Add(new ShaperOp
                    {
                        kind = ShaperOpKind.Combine,
                        mode = ShaperCombineMode.Add,
                        p0 = width,
                        p1 = ShaperOps.BlendExponent(mergeSharpness),
                        p2 = strength,
                        p3 = Mathf.Max(0f, reach),
                        bound = ShaperBound.Combine(ShaperCombineMode.Add, bound, ce.bound, width, strength),
                        distanceScale = 1f,
                        boxCx = combined.valid ? combined.CentreX : 0f,
                        boxCy = combined.valid ? combined.CentreY : 0f,
                        boxHalfW = combined.valid ? combined.HalfW : 0f,
                        boxHalfH = combined.valid ? combined.HalfH : 0f,
                    });
                    st.Pop();

                    bound = ShaperBound.Combine(ShaperCombineMode.Add, bound, ce.bound, width, strength);
                    box = combined;
                    localBox = combinedLocal;
                    if (ce.Spread > spread) spread = ce.Spread;
                }
            }
            finally
            {
                transform.instanceTranslate = originalInstanceTranslate;
                transform.instanceRotationDegrees = originalInstanceRotation;
                transform.instanceScaleBias = originalInstanceScaleBias;
                swarm.enabled = originalSwarmEnabled;
                st.phase01 = originalPhase;
                st.seed = originalSeed;
            }

            if (st.program.swarmInstancePhases == null) st.program.swarmInstancePhases = phasesUsed;

            return new Emitted
            {
                bound = bound,
                box = box,
                sweepAxis = (anyAxis && axisAgrees) ? axis : ShaperSweepAxis.Radial,
                localBox = localBox,
                spreadRaw = spread,
            };
        }

        /// <summary>
        /// The native path (T-0113): valid only for a <see cref="ShaperNodeKind.Composite"/> node whose source
        /// implements <see cref="IShaperSwarmNativeSource"/> and currently supports it. All <paramref
        /// name="count"/> instances are rendered by ONE <see cref="IShaperSwarmNativeSource.RenderSwarm"/> call
        /// into ONE raster, baked into ONE <see cref="ShaperCompiledComposite"/> and sampled by exactly ONE
        /// <see cref="ShaperOpKind.CompositeSample"/> op -- O(1) ops regardless of <paramref name="count"/>,
        /// against the generic path's O(count). Mirrors <see cref="EmitComposite"/>'s tail (box/localBox
        /// computation) exactly, duplicated rather than shared to keep T-0112's working path untouched.
        /// </summary>
        static Emitted EmitNativeSwarm(ShaperNode node, in ShaperMatrix parentForward, int count, State st)
        {
            var swarm = node.swarm;
            var def = node.composite ?? new ShaperCompositeDef();
            var native = (IShaperSwarmNativeSource)def.source;

            float hx = Mathf.Max(1e-4f, def.halfExtentX);
            float hy = Mathf.Max(1e-4f, def.halfExtentY);
            int bw = Mathf.Max(1, def.bakeWidth);
            int bh = Mathf.Max(1, def.bakeHeight);

            var offsets = new Vector2[count];
            var seeds = new uint[count];
            var phases = new float[count];
            // The jitter RANGES are read once, at the node's own phase — the per-instance draw inside them stays
            // a pure hash, so an animated range widens or tightens the cloud without re-rolling which instance
            // sits where.
            swarm.SampleJitter(st.phase01, st.seed, out Vector2 jitterRange, out _, out _);

            // T-0169 — the spawn shape belongs to the SWARM, not to either implementation, so a native source
            // is handed the same placed offsets the generic wrapper would have used (the interface's own
            // contract: "the EXACT values the generic wrapper would have used for the same authored swarm").
            // A native source cannot express a per-instance ROTATION or SIZE, so orient and scale-by-index are
            // dropped here rather than silently half-applied; the window says which path is in use.
            ShaperSpawner spawner = swarm.SampleSpawner(st.phase01, st.seed);
            bool placed = swarm.shape != ShaperSwarmShape.None;
            int[] spawnPerm = (placed || swarm.spawnOrderChaos > 0f)
                ? ShaperSwarmPlacement.BuildSpawnPermutation(swarm.shape, swarm.spawnMode, spawner.radius, count,
                    Mathf.Clamp01(swarm.spawnOrderChaos), swarm.distribution, swarm.gridReverse, swarm.seed)
                : null;

            for (int i = 0; i < count; i++)
            {
                uint h0 = Hash(swarm.seed, (uint)i * 2654435761u + 1u);
                uint h1 = Hash(h0, 0x9E3779B1u);
                uint h4 = Hash(Hash(Hash(h1, 0x85EBCA77u), 0xC2B2AE3Du), 0x27D4EB2Fu);
                phases[i] = Mathf.Clamp01(Mathf.Lerp(st.phase01, Unit(h4), Mathf.Clamp01(swarm.lifetimeStagger)));
                Vector2 place = Vector2.zero;
                if (placed)
                    swarm.PlaceInstance(spawnPerm != null ? spawnPerm[i] : i, count, spawner, phases[i], st.seed,
                                        out place, out _);
                offsets[i] = new Vector2(place.x + (Unit(h0) * 2f - 1f) * jitterRange.x,
                                         place.y + (Unit(h1) * 2f - 1f) * jitterRange.y);
                seeds[i] = Hash(st.seed, h0);
            }
            if (st.program.swarmInstancePhases == null) st.program.swarmInstancePhases = phases;

            ShaperMatrix localToParent = (node.transform ?? new ShaperTransformBlock()).ToMatrix(st.phase01, st.seed);
            ShaperMatrix forward = ShaperMatrix.Mul(parentForward, localToParent);
            bool invertible = forward.TryInvert(out ShaperMatrix inverse);
            forward.SingularValues(out float sigmaMin, out _);
            if (!invertible || sigmaMin <= 1e-9f)
            {
                st.program.singularTransformCount++;
                if (!st.program.hasSingularTransform)
                {
                    st.program.hasSingularTransform = true;
                    st.program.singularNode = node.name;
                }
                EmitEmpty(st);
                return new Emitted { bound = 1f, box = Box.Invalid, sweepAxis = ShaperSweepAxis.Radial,
                                      localBox = Box.Invalid, spreadRaw = 1f, sigmaMinRaw = 1f };
            }

            var baked = new ShaperCompiledComposite { width = bw, height = bh, coverage = new float[bw * bh] };
            var pixels = new Color32[bw * bh];
            // T-0198 — same posture as the single-instance path (see SafeRender): a generator that throws
            // publishes an empty swarm and a status message, never an exception through the whole render.
            try
            {
                native.RenderSwarm(bw, bh, st.phase01, seeds, offsets, phases, swarm.interact, pixels);
                def.lastRenderError = null;
            }
            catch (System.Exception ex)
            {
                System.Array.Clear(pixels, 0, pixels.Length);
                string where = !string.IsNullOrEmpty(node.name) ? node.name : "composite";
                string msg = $"“{where}” ({def.source.SourceLabel}) drew no swarm at {bw}×{bh}: "
                           + ex.GetType().Name + ". The rest of the document still renders.";
                if (def.lastRenderError != msg) Debug.LogException(ex);
                def.lastRenderError = msg;
            }
            baked.pixels = pixels;
            for (int i = 0; i < pixels.Length; i++) baked.coverage[i] = pixels[i].a / 255f;

            int index = st.composites.Count;
            st.composites.Add(baked);

            float halfBand = 0.5f * Mathf.Max((2f * hx) / bw, (2f * hy) / bh);
            var op = new ShaperOp
            {
                kind = ShaperOpKind.CompositeSample,
                count = index,
                p0 = hx, p1 = hy, p2 = halfBand,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                bound = 1f,
            };

            float chw = Mathf.Abs(forward.m00) * hx + Mathf.Abs(forward.m01) * hy;
            float chh = Mathf.Abs(forward.m10) * hx + Mathf.Abs(forward.m11) * hy;
            Box box = Box.FromCentre(forward.m02, forward.m12, chw, chh);
            op.boxCx = box.CentreX; op.boxCy = box.CentreY; op.boxHalfW = box.HalfW; op.boxHalfH = box.HalfH;

            st.ops.Add(op);
            st.Push();
            return new Emitted
            {
                bound = 1f,
                box = box,
                sweepAxis = ShaperSweepAxis.Radial,
                localBox = Box.FromCentre(0f, 0f, hx, hy),
            };
        }

        static Emitted EmitLeaf(ShaperNode node, in ShaperMatrix forward, in ShaperMatrix inverse,
                                float sigmaMin, State st)
            => EmitLeafFrom(node.primitive, forward, inverse, sigmaMin, st);

        /// <summary>
        /// T-0155 — <see cref="EmitLeaf"/>'s body, taking the primitive DEFINITION rather than reading it off a
        /// node, so a <see cref="ShaperNodeKind.Solid"/> node can emit a carrier leaf from a synthesized
        /// primitive without either duplicating this code or growing a phantom <c>primitive</c> the user can see
        /// and edit. Behaviour for the ordinary Primitive path is unchanged — same bake, same op, same box.
        /// </summary>
        static Emitted EmitLeafFrom(ShaperPrimitiveDef primitive, in ShaperMatrix forward, in ShaperMatrix inverse,
                                    float sigmaMin, State st)
        {
            // T-0175 — a Sprite has no flat-number SDF form at all, so it forks around ShaperPrimitives.Bake
            // entirely, the same way EmitComposite and EmitSolid already fork around the ordinary Leaf op for
            // their own non-analytic or synthesized content.
            if (primitive != null && primitive.kind == ShaperPrimitiveKind.Sprite)
                return EmitSpriteLeaf(primitive, forward, inverse, sigmaMin, st);

            // T-0174 — Text forks for the same reason, at the same point.
            if (primitive != null && primitive.kind == ShaperPrimitiveKind.Text)
                return EmitTextLeaf(primitive, forward, inverse, sigmaMin, st);

            ShaperBakedPrimitive baked = ShaperPrimitives.Bake(primitive, st.phase01, st.seed);

            var op = new ShaperOp
            {
                kind = ShaperOpKind.Leaf,
                primitive = baked.kind,
                count = baked.count,
                p0 = baked.p0, p1 = baked.p1, p2 = baked.p2, p3 = baked.p3,
                p4 = baked.p4, p5 = baked.p5, p6 = baked.p6, p7 = baked.p7,
                p8 = baked.p8, p9 = baked.p9, p10 = baked.p10, p11 = baked.p11,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                bound = baked.bound,
            };

            // Canvas support box: the local half-extent box carried through the forward corner map.
            float hw = Mathf.Abs(forward.m00) * baked.halfExtentX + Mathf.Abs(forward.m01) * baked.halfExtentY;
            float hh = Mathf.Abs(forward.m10) * baked.halfExtentX + Mathf.Abs(forward.m11) * baked.halfExtentY;
            Box box = Box.FromCentre(forward.m02, forward.m12, hw, hh);

            op.boxCx = box.CentreX; op.boxCy = box.CentreY;
            op.boxHalfW = box.HalfW; op.boxHalfH = box.HalfH;

            st.ops.Add(op);
            st.Push();
            return new Emitted
            {
                bound = baked.bound,
                box = box,
                sweepAxis = baked.sweepAxis,
                // FC-1.5a: the primitive's OWN declared half-extents, centred on its own origin, with no
                // transform applied. These are fixed before any transform touches them, which is precisely what
                // makes the fill anchor rotation-invariant and scale-stable.
                localBox = Box.FromCentre(0f, 0f, baked.halfExtentX, baked.halfExtentY),
            };
        }

        // Field ids for the sprite dials' own reproducible Min-Max sampling — mirrors ShaperPrimitives' own
        // Fld* constants, kept local because only this method samples them (ShaperPrimitives.Bake never sees a
        // Sprite primitive — see its own class doc).
        const uint FldSpriteHalfW = 0x51A21E20u, FldSpriteHalfH = 0x51A21E21u;
        const uint FldSpriteThreshold = 0x51A21E22u, FldSpriteSoftness = 0x51A21E23u;

        /// <summary>
        /// T-0175 — a Sprite primitive's leaf. Resolves its dials at this compile's own phase/seed (the same
        /// funnel-once-per-compile rule every other dial follows, BC-1.2), fetches (or builds) its cached distance
        /// raster, and emits a <see cref="ShaperOpKind.SpriteSample"/> op — structurally identical to
        /// <see cref="EmitLeafFrom"/>'s own tail (canvas support box, local box, Push) with the raster lookup
        /// standing in for <see cref="ShaperSdf.Evaluate"/>.
        /// </summary>
        static Emitted EmitSpriteLeaf(ShaperPrimitiveDef primitive, in ShaperMatrix forward, in ShaperMatrix inverse,
                                      float sigmaMin, State st)
        {
            float hw = Mathf.Max(1e-4f, ShaperValue.Sample(primitive.spriteHalfWDial, st.phase01, st.seed ^ FldSpriteHalfW, 50f));
            float hh = Mathf.Max(1e-4f, ShaperValue.Sample(primitive.spriteHalfHDial, st.phase01, st.seed ^ FldSpriteHalfH, 50f));
            float threshold = Mathf.Clamp01(ShaperValue.Sample(primitive.spriteThresholdDial, st.phase01, st.seed ^ FldSpriteThreshold, 0.5f));
            float softness = Mathf.Max(0f, ShaperValue.Sample(primitive.spriteSoftnessDial, st.phase01, st.seed ^ FldSpriteSoftness, 0f));

            ShaperCompiledSpriteField raster = ShaperSpritePrepassCache.Get(primitive, hw, hh, threshold, softness, primitive.spriteFitMode);
            if (raster == null)
            {
                // No sprite assigned — a legal, if useless, authoring state (FC-6.5's posture): empty everywhere,
                // same as an unassigned Texture/Gradient fill or a Composite node with no source.
                EmitEmpty(st);
                return new Emitted { bound = 1f, box = Box.Invalid, sweepAxis = ShaperSweepAxis.Radial, localBox = Box.Invalid };
            }

            var op = new ShaperOp
            {
                kind = ShaperOpKind.SpriteSample,
                count = st.spriteFields.Count,
                p0 = raster.halfExtentX, p1 = raster.halfExtentY,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                bound = 1f,
            };
            st.spriteFields.Add(raster);

            float chw = Mathf.Abs(forward.m00) * raster.halfExtentX + Mathf.Abs(forward.m01) * raster.halfExtentY;
            float chh = Mathf.Abs(forward.m10) * raster.halfExtentX + Mathf.Abs(forward.m11) * raster.halfExtentY;
            Box box = Box.FromCentre(forward.m02, forward.m12, chw, chh);
            op.boxCx = box.CentreX; op.boxCy = box.CentreY; op.boxHalfW = box.HalfW; op.boxHalfH = box.HalfH;

            st.ops.Add(op);
            st.Push();
            return new Emitted
            {
                bound = 1f,
                box = box,
                sweepAxis = ShaperSweepAxis.Radial,
                localBox = Box.FromCentre(0f, 0f, raster.halfExtentX, raster.halfExtentY),
            };
        }

        // ── T-0174 Text ──────────────────────────────────────────────────────────────────────────────────────
        // Field ids for the text dials' own reproducible Min-Max sampling, in the same space as the sprite ones
        // above (ShaperPrimitives.Bake never sees a Text primitive either).
        const uint FldTextSize = 0x51A21E30u, FldTextLetterSpacing = 0x51A21E31u;
        const uint FldTextLineSpacing = 0x51A21E32u, FldTextWeight = 0x51A21E33u;

        /// <summary>
        /// T-0174 — a Text primitive's leaf. Resolves its dials once at this compile's own phase/seed (BC-1.2),
        /// fetches (or builds) its cached glyph distance raster, and emits a <see cref="ShaperOpKind.TextSample"/>
        /// op. Structurally the same method as <see cref="EmitSpriteLeaf"/>; the raster's OWN half-extents (which
        /// the text sizes for itself from the font's metrics, rather than being told by an authored box) become
        /// both the op's sampling box and the node's local box.
        /// </summary>
        static Emitted EmitTextLeaf(ShaperPrimitiveDef primitive, in ShaperMatrix forward, in ShaperMatrix inverse,
                                    float sigmaMin, State st)
        {
            float size = Mathf.Max(1e-3f, ShaperValue.Sample(primitive.textSizeDial, st.phase01, st.seed ^ FldTextSize, 40f));
            float letterSpacing = ShaperValue.Sample(primitive.textLetterSpacingDial, st.phase01, st.seed ^ FldTextLetterSpacing, 0f);
            float lineSpacing = ShaperValue.Sample(primitive.textLineSpacingDial, st.phase01, st.seed ^ FldTextLineSpacing, 0f);
            float weight = Mathf.Clamp(ShaperValue.Sample(primitive.textWeightDial, st.phase01, st.seed ^ FldTextWeight, 0.5f), 0.05f, 0.95f);

            ShaperCompiledTextField raster = ShaperTextPrepassCache.Get(
                primitive, size, letterSpacing, lineSpacing, weight, primitive.textAlign);
            if (raster == null)
            {
                // No font, no string, or nothing in the string this font can draw — a legal, if useless,
                // authoring state, the same posture EmitSpriteLeaf takes for an unassigned sprite.
                EmitEmpty(st);
                return new Emitted { bound = 1f, box = Box.Invalid, sweepAxis = ShaperSweepAxis.Radial, localBox = Box.Invalid };
            }

            var op = new ShaperOp
            {
                kind = ShaperOpKind.TextSample,
                count = st.textFields.Count,
                p0 = raster.halfExtentX, p1 = raster.halfExtentY,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                bound = 1f,
            };
            st.textFields.Add(raster);

            float chw = Mathf.Abs(forward.m00) * raster.halfExtentX + Mathf.Abs(forward.m01) * raster.halfExtentY;
            float chh = Mathf.Abs(forward.m10) * raster.halfExtentX + Mathf.Abs(forward.m11) * raster.halfExtentY;
            Box box = Box.FromCentre(forward.m02, forward.m12, chw, chh);
            op.boxCx = box.CentreX; op.boxCy = box.CentreY; op.boxHalfW = box.HalfW; op.boxHalfH = box.HalfH;

            st.ops.Add(op);
            st.Push();
            return new Emitted
            {
                bound = 1f,
                box = box,
                sweepAxis = ShaperSweepAxis.Radial,
                localBox = Box.FromCentre(0f, 0f, raster.halfExtentX, raster.halfExtentY),
            };
        }

        /// <summary>
        /// T-0155 — a <see cref="ShaperNodeKind.Solid"/> node's CARRIER leaf.
        ///
        /// A Solids node does not emit its own silhouette here, and it deliberately cannot: LR-6.1 says the
        /// generator REPLACES the shape stage for its owner, and it does that downstream —
        /// <see cref="ShaperFillResolver.PaintTile"/> hands this owner's slab to
        /// <see cref="ShaperSolids.FillTile"/>, which writes coverage, edge distance and the surface normal as
        /// its own closed-form facet geometry over EVERY sample of the tile. Anything this method emitted as a
        /// silhouette would be overwritten in full before a single pixel was painted.
        ///
        /// So what this leaf is FOR is the two things the pipeline still needs and the generator does not
        /// supply: an entry in <see cref="ShaperFillDocument.owners"/> (no owner, no slab, no paint — the
        /// generator would never be reached), and a canvas support box for the stages that bound their work by
        /// it. The box is therefore deliberately CONSERVATIVE rather than tight: half-extents are the solid's
        /// own radius scaled by its aspect, plus the absolute centre offset, so the box contains the solid for
        /// any authored centre without this method having to re-derive the facet projection that
        /// <see cref="ShaperSolids.Build"/> already owns. A box that is too large costs bounding work; one that
        /// is too small would clip a generator that is entitled to the whole tile.
        ///
        /// The dials are read through <see cref="ShaperSolids.Compile"/> rather than off the def, so the
        /// envelope sampling (phase, seed) is the SAME one the renderer will use when it binds this solid —
        /// two samplings of one ZUIValue at one phase must not disagree about how big the shape is.
        /// </summary>
        static Emitted EmitSolid(ShaperNode node, in ShaperMatrix forward, in ShaperMatrix inverse,
                                 float sigmaMin, State st)
        {
            ShaperSolidOp op = ShaperSolids.Compile(node.solid ?? new ShaperSolidDef(), st.phase01, st.seed);

            float r = Mathf.Max(1e-4f, op.r);
            float reach = r * Mathf.Max(1f, Mathf.Abs(op.aspect));

            // T-0265 — SQUARE, and sized by the DISTANCE of the centre rather than by each axis separately.
            // The solid occupies a disc of radius `reach` about (centreX, centreY), so it lies inside the disc
            // of radius |centre| + reach about the node's own origin, and a square of that half-side contains
            // that disc under ANY rotation of the node — which the per-axis box did not, so a Position
            // Rotation could clip the very solid it was turning. A box that is too large costs bounding work;
            // one that is too small loses pixels.
            float half = Mathf.Sqrt(op.centreX * op.centreX + op.centreY * op.centreY) + reach;

            var carrier = new ShaperPrimitiveDef
            {
                kind = ShaperPrimitiveKind.Rect,
                rectHalfW = half,
                rectHalfH = half,
            };
            return EmitLeafFrom(carrier, forward, inverse, sigmaMin, st);
        }

        /// <summary>
        /// T-0198 — run a generator's own render, and treat a throw as an EMPTY PICTURE rather than as the end
        /// of the frame. A composite source is, by the escape hatch's own premise, code the shape engine does
        /// not own and cannot verify: it may be handed a canvas shape it was never written for. Before this, one
        /// such generator threw straight out through <c>RenderPhaseInto</c> and the preview stopped rendering
        /// altogether — a document that looked broken everywhere because one node was. The node now publishes no
        /// coverage and says why on the transport status line (<see cref="ShaperCompositeDef.lastRenderError"/>),
        /// which is the same posture a null source already had: useless, visible, and survivable.
        ///
        /// The catch is deliberately broad. Narrowing it to the exception a known-bad generator happens to raise
        /// today would only mean the NEXT generator's different exception takes the preview down instead.
        /// </summary>
        static void SafeRender(ShaperCompositeDef def, ShaperNode node, int bw, int bh, Color32[] pixels, State st)
        {
            try
            {
                def.source.Render(bw, bh, st.phase01, st.seed, pixels);
                def.lastRenderError = null;
            }
            catch (System.Exception ex)
            {
                System.Array.Clear(pixels, 0, pixels.Length);
                string where = node != null && !string.IsNullOrEmpty(node.name) ? node.name : "composite";
                string msg = $"“{where}” ({def.source.SourceLabel}) drew nothing at {bw}×{bh}: "
                           + ex.GetType().Name + ". The rest of the document still renders.";
                // A preview recompiles every frame, so the console is told once per DISTINCT failure — the
                // status line is the live channel, and a stack trace repeated sixty times a second is noise
                // that buries the one occurrence anybody would read.
                if (def.lastRenderError != msg) Debug.LogException(ex);
                def.lastRenderError = msg;
            }
        }

        /// <summary>
        /// T-0112 — a composite generator's picture, hosted unmodified. Renders <see cref="node"/>'s
        /// <see cref="ShaperNode.composite"/> source ONCE, at this compile's own <c>phase01</c>/<c>seed</c>, into
        /// a fixed-resolution raster (<see cref="ShaperCompositeDef.bakeWidth"/>/<c>Height</c>), decodes its
        /// alpha channel as coverage, and emits a <see cref="ShaperOpKind.CompositeSample"/> leaf that reads it
        /// back per sample — mirroring <see cref="EmitLeaf"/>'s shape (local transform, one value pushed, a
        /// canvas support box carried out) with a raster lookup in place of an analytic SDF.
        /// </summary>
        static Emitted EmitComposite(ShaperNode node, in ShaperMatrix forward, in ShaperMatrix inverse,
                                     float sigmaMin, State st)
        {
            ShaperCompositeDef def = node.composite ?? new ShaperCompositeDef();
            float hx = Mathf.Max(1e-4f, def.halfExtentX);
            float hy = Mathf.Max(1e-4f, def.halfExtentY);
            int bw = Mathf.Max(1, def.bakeWidth);
            int bh = Mathf.Max(1, def.bakeHeight);

            var baked = new ShaperCompiledComposite { width = bw, height = bh, coverage = new float[bw * bh] };
            var pixels = new Color32[bw * bh];
            // A null source is a legal, if useless, authoring state (FC-6.5's posture) — it renders as empty
            // coverage everywhere rather than throwing, the same way an unassigned Texture/Gradient fill does.
            if (def.source != null) SafeRender(def, node, bw, bh, pixels, st);
            baked.pixels = pixels;
            for (int i = 0; i < pixels.Length; i++) baked.coverage[i] = pixels[i].a / 255f;

            int index = st.composites.Count;
            st.composites.Add(baked);

            // Half a texel in local units — the finest antialiasing this raster can resolve, ShaperField's own
            // "half a pixel" convention (ShaperField.HalfBand) applied to the BAKE grid instead of the canvas
            // grid. This is the number that makes the derived pseudo-distance saturate a texel or so out from
            // the generator's own edge — see ShaperEvaluator's CompositeSample case for the consequence.
            float halfBand = 0.5f * Mathf.Max((2f * hx) / bw, (2f * hy) / bh);

            var op = new ShaperOp
            {
                kind = ShaperOpKind.CompositeSample,
                count = index,
                p0 = hx, p1 = hy, p2 = halfBand,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                // The declared bound is only meaningful within halfBand of the surface (see the op-kind doc
                // comment) — 1 is the same value every exact primitive declares, kept for uniformity with the
                // rest of the compiled program rather than invented meaning for a case with no Lipschitz proof.
                bound = 1f,
            };

            float chw = Mathf.Abs(forward.m00) * hx + Mathf.Abs(forward.m01) * hy;
            float chh = Mathf.Abs(forward.m10) * hx + Mathf.Abs(forward.m11) * hy;
            Box box = Box.FromCentre(forward.m02, forward.m12, chw, chh);
            op.boxCx = box.CentreX; op.boxCy = box.CentreY; op.boxHalfW = box.HalfW; op.boxHalfH = box.HalfH;

            st.ops.Add(op);
            st.Push();
            return new Emitted
            {
                bound = 1f,
                box = box,
                sweepAxis = ShaperSweepAxis.Radial,
                localBox = Box.FromCentre(0f, 0f, hx, hy),
            };
        }

        static Emitted EmitBag(ShaperNode node, in ShaperMatrix forward, State st)
        {
            // The accumulator starts EMPTY. That single fact is what makes R1's leading-member rule fall out
            // with no special case: min(Empty, d) == d, max(Empty, −d) == Empty, max(Empty, d) == Empty.
            EmitEmpty(st);

            float bound = 1f;
            Box box = Box.Invalid;
            // The same fold, in the BAG's own local frame: each member's local box carried up through that
            // member's own transform block only (FC-1.5a). The bag's own transform never enters, so the bag's
            // local box does not change when the bag rotates.
            Box localBox = Box.Invalid;
            bool anyAxis = false;
            ShaperSweepAxis axis = ShaperSweepAxis.Radial;
            bool axisAgrees = true;
            bool first = true;
            float spread = 1f;                       // BD-2.4, the max over the members; see the fold below.

            var children = node.children;
            int n = children != null ? children.Count : 0;
            for (int i = 0; i < n; i++)
            {
                ShaperNode child = children[i];
                if (child == null || !child.enabled) continue;

                if (first)
                {
                    first = false;
                    if (child.mode != ShaperCombineMode.Add)
                    {
                        // EVERY offending bag is counted; only the first supplies the name. Recording just the
                        // first meant a tree with three offenders surfaced one, which defeats the flag.
                        st.program.leadingNonAddCount++;
                        if (!st.program.hasLeadingNonAdd)
                        {
                            st.program.hasLeadingNonAdd = true;
                            st.program.leadingNonAddNode = child.name;
                        }
                    }
                }

                Emitted ce = EmitNodeMaybeSwarm(child, forward, st, false);

                if (!anyAxis) { axis = ce.sweepAxis; anyAxis = true; }
                else if (axis != ce.sweepAxis) axisAgrees = false;

                var blend = child.blend ?? new ShaperBlend();
                blend.Sample(st.phase01, st.seed, out float width, out float blendSharpness, out float strength);

                Box combined;
                Box childLocal = ce.localBox.Map(ce.localToParent);
                Box combinedLocal;
                switch (child.mode)
                {
                    case ShaperCombineMode.Add:
                        combined = box.Union(ce.box);
                        combinedLocal = localBox.Union(childLocal);
                        break;
                    case ShaperCombineMode.Intersect:
                        combined = box.Intersect(ce.box);
                        combinedLocal = localBox.Intersect(childLocal);
                        break;
                    default:
                        combined = box;                 // Subtract never enlarges the accumulated box
                        combinedLocal = localBox;
                        break;
                }

                // The soft-subtract band is authored as a fraction of the operands' own size, matching the
                // reference app's reach = 0.55 · min(width, height).
                Box reachBox = box.Union(ce.box);
                float reach = reachBox.valid
                    ? 0.55f * Mathf.Min(reachBox.HalfW * 2f, reachBox.HalfH * 2f)
                    : 0f;

                st.ops.Add(new ShaperOp
                {
                    kind = ShaperOpKind.Combine,
                    mode = child.mode,
                    p0 = width,
                    p1 = ShaperOps.BlendExponent(blendSharpness),
                    p2 = strength,
                    p3 = Mathf.Max(0f, reach),
                    bound = ShaperBound.Combine(child.mode, bound, ce.bound, width, strength),
                    distanceScale = 1f,
                    boxCx = combined.valid ? combined.CentreX : 0f,
                    boxCy = combined.valid ? combined.CentreY : 0f,
                    boxHalfW = combined.valid ? combined.HalfW : 0f,
                    boxHalfH = combined.valid ? combined.HalfH : 0f,
                });
                st.Pop();

                bound = ShaperBound.Combine(child.mode, bound, ce.bound, width, strength);
                box = combined;
                localBox = combinedLocal;
                // BD-2.4: the bag inherits the WORST under-report factor of any member. A max is conservative
                // for every mode: Add's union needs the worst of the two, and both Subtract's and Intersect's
                // results are SUBSETS of an operand, so a box bounded for the union bounds them too.
                if (ce.Spread > spread) spread = ce.Spread;
            }

            return new Emitted
            {
                bound = bound,
                box = box,
                sweepAxis = (anyAxis && axisAgrees) ? axis : ShaperSweepAxis.Radial,
                localBox = localBox,
                spreadRaw = spread,
            };
        }

        static Emitted EmitSweep(ShaperNode node, in ShaperMatrix forward, in ShaperMatrix inverse,
                                 float sigmaMin, Emitted child, State st)
        {
            var sweep = node.sweep;
            if (sweep == null || !sweep.enabled) return child;

            sweep.Sample(st.phase01, st.seed, out float sweepStartDeg, out float sweepExtentDeg,
                         out float sweepStartFrac, out float sweepExtentFrac);

            var op = new ShaperOp
            {
                kind = ShaperOpKind.Sweep,
                sweepAxis = child.sweepAxis,
                m00 = inverse.m00, m01 = inverse.m01, m02 = inverse.m02,
                m10 = inverse.m10, m11 = inverse.m11, m12 = inverse.m12,
                distanceScale = sigmaMin,
                bound = ShaperBound.Sweep(child.bound),
                boxCx = child.box.valid ? child.box.CentreX : 0f,
                boxCy = child.box.valid ? child.box.CentreY : 0f,
                boxHalfW = child.box.valid ? child.box.HalfW : 0f,
                boxHalfH = child.box.valid ? child.box.HalfH : 0f,
            };

            if (child.sweepAxis == ShaperSweepAxis.Radial)
            {
                float extent = sweepExtentDeg;
                if (extent >= 360f)
                {
                    op.p5 = 1f;   // identity — the evaluator returns the child's float untouched
                }
                else
                {
                    float s = sweepStartDeg * Mathf.Deg2Rad;
                    float e = Mathf.Max(0f, extent) * Mathf.Deg2Rad;
                    float end = s + e;
                    // Half-plane on the counter-clockwise side of the start ray, negative inside:
                    //   −cross(d_s, p) = sin(s)·x − cos(s)·y
                    op.p0 = Mathf.Sin(s); op.p1 = -Mathf.Cos(s);
                    // ...and on the clockwise side of the end ray: cross(d_e, p) = cos(e)·y − sin(e)·x
                    op.p2 = -Mathf.Sin(end); op.p3 = Mathf.Cos(end);
                    op.p4 = e > Mathf.PI ? 1f : 0f;   // past a half-turn the wedge is a union, not an intersection
                }
            }
            else
            {
                // The child's extent along its own local X, taken conservatively by carrying the canvas
                // support box back down through this node's inverse.
                float lo = 0f, hi = 0f;
                if (child.box.valid)
                {
                    float cx = inverse.m00 * child.box.CentreX + inverse.m01 * child.box.CentreY + inverse.m02;
                    float hw = Mathf.Abs(inverse.m00) * child.box.HalfW + Mathf.Abs(inverse.m01) * child.box.HalfH;
                    lo = cx - hw; hi = cx + hw;
                }
                float start = sweepStartFrac;
                float extent = sweepExtentFrac;
                if (start <= 0f && start + extent >= 1f)
                {
                    op.p5 = 1f;   // identity
                }
                else
                {
                    float span = hi - lo;
                    op.p0 = lo + span * start;
                    op.p1 = lo + span * Mathf.Min(1f, start + extent);
                }
            }

            st.ops.Add(op);
            // A sweep carves the child away; it never enlarges either box, canvas or local.
            return new Emitted { bound = op.bound, box = child.box, sweepAxis = child.sweepAxis,
                                 localBox = child.localBox,
                                 // A sweep is a max against a half-plane in the SAME rescaled units, so it
                                 // neither improves nor worsens the under-report factor: carried through.
                                 spreadRaw = child.spreadRaw, sigmaMinRaw = child.sigmaMinRaw };
        }

        static Emitted EmitShell(ShaperNode node, float sigmaMin, Emitted child, State st)
        {
            var shell = node.shell;
            if (shell == null || !shell.enabled) return child;   // identity by construction: nothing is emitted

            float thickness = shell.SampleThickness(st.phase01, st.seed);
            Box box = child.box;
            Box localBox = child.localBox;
            if (shell.alignment != ShaperShellAlignment.Inward)
            {
                // BD-2.4's argument, applied to the operator the border stage reuses: the canvas box must grow
                // by the thickness measured in CANVAS units, and the field's units are canvas units only when
                // the map is isotropic. Growing by `thickness` alone under-bounded an Outward shell on a member
                // scaled (2.0, 0.5) by 23.5 px over 808 samples — measured, T-0107 verification.
                box = box.Grow(thickness * child.Spread);
                // Thickness is authored in CANVAS pixels, and the local box is in local units, so the growth
                // must be divided by the accumulated σ_min that maps local distance to canvas
                // (ShaperProgram.distanceScale). Guarded rather than clamped: a singular node never reaches
                // here — EmitNode publishes the empty field for it and flags — but a near-singular σ_min would
                // otherwise blow the box up, and an over-large ANCHOR box only under-uses the ramp, whereas a
                // divide by zero would NaN every sample.
                localBox = localBox.Grow(sigmaMin > 1e-9f ? thickness / sigmaMin : 0f);
            }

            st.ops.Add(new ShaperOp
            {
                kind = ShaperOpKind.Shell,
                shellAlignment = shell.alignment,
                p0 = thickness,
                // NO second early-out here. `thickness <= 0 → return the child untouched` used to sit in this
                // slot, and it was wrong twice over: spec §7 declares exactly ONE identity, `enabled == false`,
                // and the value it produced was not merely undeclared but backwards. A wall thinned to nothing
                // is an EMPTY interior — |d| − 0 = |d| ≥ 0 for Centred, and max(d, −d − 0) = |d| for both
                // Inward and Outward — so dragging thickness to zero must make the wall vanish. What the
                // early-out did instead was make the whole solid REAPPEAR, bit-identical to no shell at all.
                distanceScale = 1f,
                bound = ShaperBound.Shell(child.bound),
                boxCx = box.valid ? box.CentreX : 0f,
                boxCy = box.valid ? box.CentreY : 0f,
                boxHalfW = box.valid ? box.HalfW : 0f,
                boxHalfH = box.valid ? box.HalfH : 0f,
            });

            return new Emitted { bound = ShaperBound.Shell(child.bound), box = box,
                                 sweepAxis = child.sweepAxis, localBox = localBox,
                                 spreadRaw = child.spreadRaw, sigmaMinRaw = child.sigmaMinRaw };
        }

        /// <summary>
        /// BORDER-CONTRACT BD-2.2 and BD-2.4 — a joining border's DILATION of the node's published field.
        ///
        /// <b>Why this lives in the compiler and not in the border stage.</b> "Joined" means the rest of the tree
        /// SEES the outline: a bag containing the node fuses with it, a mask made from the layer includes it. The
        /// only place a node's field is visible to its parent is the fold, so the dilation has to be an
        /// instruction in the node's own subtree, emitted at the point its content is finished — after
        /// <see cref="EmitSweep"/> and <see cref="EmitShell"/>, before it folds into the bag. Emitted here it
        /// composes automatically, and the node's OWN standalone program (the one
        /// <see cref="ShaperFillResolver"/> compiles per owner) and every ancestor's program publish the same
        /// number with no second rule about which is authoritative.
        ///
        /// <b>Three identities, all structural rather than arithmetic</b> — nothing is emitted at all when:
        /// the border is null, disabled or zero-width (BD-1.5, decided by
        /// <see cref="ShaperBorder.Resolve"/>); the author opted out of joining (BD-2.3, "drawn, not counted");
        /// or the reach is zero, which is every Inward border. BD-1.5 requires the zero-width case to be BITWISE
        /// identical to no border at all, and an emitted <c>d − 0</c> would be identical only by the arithmetic's
        /// good behaviour, which is exactly what that clause declines to rely on.
        ///
        /// <b>BD-3.7's refusal is applied here too.</b> A Subtract MEMBER may not own a border, so it may not
        /// dilate either — a subtractor that quietly grew by its own outline width would carve a bigger hole than
        /// the author sees anywhere on screen, and the refusal diagnostic
        /// (<see cref="ShaperFillDocument.hasSubtractBorder"/>) would name a border that nonetheless changed the
        /// silhouette. The resolver refuses the OWNER for the same node on the same test, so the two halves
        /// cannot disagree.
        ///
        /// <b>The box growth is the whole of BD-2.4, and it touches ONE of the two boxes.</b> <c>box</c> — the
        /// conservative CULLING box — grows by the reach on all four sides, because a bound that excludes real
        /// samples is a correctness failure. <c>localBox</c> — the node-local ANCHOR box a fill normalises
        /// against (FC-1.5) — is returned untouched, because growing it would rescale every gradient on the node
        /// the moment an outline appeared and rescale it again on every frame the width animated. Enabling an
        /// outline must never repaint the thing it outlines. Contrast <see cref="EmitShell"/> directly above,
        /// which grows BOTH — correctly, because a shell REPLACES the node's field and its band genuinely is the
        /// node's new extent, whereas a border keeps the node and adds a strip beside it.
        /// </summary>
        static Emitted EmitBorderJoin(ShaperNode node, bool isRoot, Emitted child, State st)
        {
            // T-0112 — a composite generator gives up the border stage structurally, the same way BD-3.7 refuses
            // a Subtract member's border a few lines below: SHAPER_THE_DESIGN.md B1 states it as one of exactly
            // three things a composite loses (a swappable fill, the border stage, shape-local position), and the
            // real reason is fidelity, not policy — a composite's field is only a trustworthy distance within
            // about one bake texel of its own edge (see ShaperEvaluator's CompositeSample case), and a border can
            // reach many pixels out. Refusing here means an authored border on a Composite node is INERT rather
            // than drawing a strip from a saturated, meaningless field.
            if (node.kind == ShaperNodeKind.Composite) return child;
            // T-0271 — IsAuthored rather than a null test: a saved node carries a phantom ShaperBorderDef.
            // Its `enabled` already defaults to false so this changes no pixels, but the engine and the card
            // must answer "does this node have an edge?" the same way, or one of them is lying.
            if (!ShaperBorderDef.IsAuthored(node.border)) return child;
            if (!isRoot && node.mode == ShaperCombineMode.Subtract) return child;   // BD-3.7

            ShaperResolvedBorder border = ShaperBorder.Resolve(node.border, st.phase01, st.seed);
            if (!border.Joins) return child;

            // BD-2.4 — the CULLING box grows by the reach measured in CANVAS units, which is the reach times
            // this subtree's under-report factor (Emitted.Spread). Growing by the bare reach is what a reader
            // of BD-2.4 writes first and it is an UNDER-bound: on a member scaled (2.0, 0.5), reach 8, it left
            // 808 samples of the dilated silhouette outside the declared box, overshooting by 23.5 px. On an
            // isotropic node Spread is exactly 1 and this is the bare reach, bit for bit.
            Box box = child.box.Grow(border.reach * child.Spread);

            st.ops.Add(ShaperBorder.JoinOp(border.reach, child.bound,
                                           box.valid ? box.CentreX : 0f,
                                           box.valid ? box.CentreY : 0f,
                                           box.valid ? box.HalfW : 0f,
                                           box.valid ? box.HalfH : 0f));

            // bound unchanged: adding a constant does not change a gradient.
            // localBox unchanged: BD-2.4, and it is the half of this method most easily got wrong.
            return new Emitted { bound = child.bound, box = box,
                                 sweepAxis = child.sweepAxis, localBox = child.localBox,
                                 spreadRaw = child.spreadRaw, sigmaMinRaw = child.sigmaMinRaw };
        }
    }
}

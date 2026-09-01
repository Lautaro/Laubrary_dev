using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0115 -- design B9, requirement 1 ("a dial moves, the preview moves... every node remembers what it
    /// produced, labelled by its own settings and by the labels of everything feeding into it"). This is the
    /// ONE place a <see cref="ShaperNode"/>'s own identity is turned into a <see cref="ShaperCacheKey"/> --
    /// every cache (<see cref="ShaperNodeCache"/>, <see cref="ShaperCachedEvaluator"/>, <see cref="ShaperFrameCache"/>)
    /// calls in here rather than re-deriving its own hash, so the fields that matter are declared exactly once.
    ///
    /// <b>What is, and is not, part of a node's OWN identity.</b> <see cref="ShaperNode.mode"/> and
    /// <see cref="ShaperNode.blend"/> describe how this node folds into its PARENT -- they change the PARENT's
    /// output, not this node's own buffer, so they are deliberately excluded from <see cref="OwnHash"/> and are
    /// instead mixed in by the parent (<see cref="ShaperCachedEvaluator"/>'s fold step) when it builds ITS OWN
    /// key from each child's key + that child's own mode/blend. This is what lets editing a blend width on
    /// member 7 dirty the bag (the fold changed) while leaving member 7's OWN cached buffer untouched (its
    /// shape did not change) -- a real, deliberate refinement over "just hash the whole node recursively",
    /// which would conflate the two and throw away that reuse.
    ///
    /// <see cref="ShaperNode.name"/> is excluded on purpose: it is authoring metadata, never a rendering input.
    ///
    /// <b>Animation.</b> <see cref="ShaperCompiler.Compile"/> takes <c>phase01</c>/<c>seed</c> as compile-time
    /// parameters threaded into every primitive's own bake (<c>ShaperPrimitives.Bake(node.primitive, phase01,
    /// seed)</c>) and every composite's own render, so this file folds <c>phase01</c> and <c>seed</c> into
    /// EVERY node's own hash unconditionally, rather than trying to detect per-node whether a given dial
    /// actually reads phase01 (a real optimisation that was considered and cut -- see SPEC.md Part 5 for why:
    /// getting that detection wrong in either direction is worse than the honest, simple, always-correct
    /// choice of "assume every node COULD depend on phase and key it that way"). The cost is a cache entry per
    /// (node, phase) pair instead of one entry for a genuinely phase-invariant node; the benefit is that this
    /// file can never silently return a stale value for the wrong frame.
    /// </summary>
    public static class ShaperNodeIdentity
    {
        // ── per-kind salts, so a Primitive's hash space can never collide with a Bag's or a Composite's even
        // if every other authored field happened to coincide ─────────────────────────────────────────────────
        const string SaltPrimitive = "shaper.node.primitive.v1";
        const string SaltBag = "shaper.node.bag.v1";
        const string SaltComposite = "shaper.node.composite.v1";
        const string SaltSolid = "shaper.node.solid.v1";
        const string SaltFold = "shaper.fold.v1";
        const string SaltSwarmInstance = "shaper.swarm.instance.v1";
        const string SaltSwarmWhole = "shaper.swarm.whole.v1";

        /// <summary>
        /// This node's OWN identity: its kind-specific content (primitive dials, or composite declaration, or
        /// a bag's own transform/sweep/shell/swarm settings) plus <paramref name="phase01"/>/<paramref name="seed"/>.
        /// Deliberately excludes <see cref="ShaperNode.mode"/>/<see cref="ShaperNode.blend"/> (parent-fold
        /// concerns, see class doc) and a Bag's <see cref="ShaperNode.children"/> (the caller folds those in
        /// separately, member by member, via <see cref="FoldChild"/> -- that incremental fold is what lets an
        /// unchanged prefix of members stay cached when only a later member changes).
        /// </summary>
        public static ShaperCacheKey OwnHash(ShaperNode node, float phase01, uint seed)
        {
            if (node == null || !node.enabled) return ShaperCacheKey.Empty;

            switch (node.kind)
            {
                case ShaperNodeKind.Primitive: return PrimitiveHash(node, phase01, seed);
                case ShaperNodeKind.Composite: return CompositeHash(node, phase01, seed);
                case ShaperNodeKind.Bag: return BagOwnHash(node, phase01, seed);
                case ShaperNodeKind.Solid: return SolidHash(node, phase01, seed);
                default: return ShaperCacheKey.Empty;
            }
        }

        static ShaperCacheMixer MixCommon(ShaperCacheMixer m, ShaperNode node, float phase01, uint seed)
        {
            m.MixVector2(node.transform.translate);
            m.MixFloat(node.transform.rotation);
            m.MixVector2(node.transform.scale);
            m.MixVector2(node.transform.skewDegrees);
            m.MixVector2(node.transform.origin);
            MixSweep(ref m, node.sweep);
            MixShell(ref m, node.shell);
            m.MixFloat(phase01);
            m.MixUInt(seed);
            return m;
        }

        static void MixSweep(ref ShaperCacheMixer m, ShaperSweep s)
        {
            if (s == null) { m.MixBool(false); return; }
            m.MixBool(s.enabled);
            if (!s.enabled) return;
            m.MixFloat(s.startDegrees); m.MixFloat(s.extentDegrees);
            m.MixFloat(s.startFraction); m.MixFloat(s.extentFraction);
        }

        static void MixShell(ref ShaperCacheMixer m, ShaperShell s)
        {
            if (s == null) { m.MixBool(false); return; }
            m.MixBool(s.enabled);
            if (!s.enabled) return;
            m.MixFloat(s.thickness);
            m.MixInt((int)s.alignment);
        }

        static void MixSwarmDef(ref ShaperCacheMixer m, ShaperSwarmDef s)
        {
            if (s == null) { m.MixBool(false); return; }
            m.MixBool(s.enabled);
            if (!s.enabled || s.count <= 1) return;
            m.MixInt(s.count);
            m.MixUInt(s.seed);
            m.MixVector2(s.positionJitter);
            m.MixFloat(s.rotationJitterDegrees);
            m.MixFloat(s.scaleJitter);
            m.MixFloat(s.lifetimeStagger);
            m.MixBool(s.interact);
            m.MixFloat(s.merge != null ? s.merge.width : 0f);
            m.MixFloat(s.merge != null ? s.merge.sharpness : 0f);
            m.MixFloat(s.merge != null ? s.merge.carveStrength : 0f);
        }

        /// <summary>
        /// T-0155 — a Solids node's own identity.
        ///
        /// <b>This method is load-bearing, not bookkeeping.</b> Adding <see cref="ShaperNodeKind.Solid"/>
        /// without it would have left <see cref="OwnHash"/> falling through to <c>default</c> and returning
        /// <see cref="ShaperCacheKey.Empty"/>, while <see cref="IsCacheable"/> — which only ever special-cases
        /// Primitive — kept returning TRUE. Every Solid in a document would then have shared one cache key:
        /// two different solids would render as whichever of them compiled first, and it would look like a
        /// plausible picture rather than a crash. Every authored dial is mixed here for that reason.
        ///
        /// The colours are mixed too. They are not geometry, but they ARE resolved into
        /// <see cref="ShaperSolidOp"/> at compile and painted from it, so two solids differing only in line or
        /// glow colour are genuinely different pictures and must not collide.
        /// </summary>
        static ShaperCacheKey SolidHash(ShaperNode node, float phase01, uint seed)
        {
            var m = ShaperCacheMixer.Begin(SaltSolid);
            m = MixCommon(m, node, phase01, seed);
            MixSwarmDef(ref m, node.swarm);

            var s = node.solid ?? new ShaperSolidDef();
            m.MixInt((int)s.form);
            MixZuiValue(ref m, s.size, phase01);
            MixZuiValue(ref m, s.centreX, phase01);
            MixZuiValue(ref m, s.centreY, phase01);
            MixZuiValue(ref m, s.aspect, phase01);
            MixZuiValue(ref m, s.depth, phase01);
            MixZuiValue(ref m, s.gemSides, phase01);
            MixZuiValue(ref m, s.gemCrown, phase01);
            MixZuiValue(ref m, s.gemPavilion, phase01);
            MixZuiValue(ref m, s.ringInner, phase01);
            MixZuiValue(ref m, s.yaw, phase01);
            MixZuiValue(ref m, s.tilt, phase01);
            MixZuiValue(ref m, s.roll, phase01);
            MixZuiValue(ref m, s.lineWidth, phase01);
            MixZuiValue(ref m, s.edgeGlow, phase01);
            MixZuiValue(ref m, s.innerGlow, phase01);
            MixColour(ref m, s.lineColour);
            MixColour(ref m, s.edgeGlowColour);
            MixColour(ref m, s.innerGlowColour);
            return m.Key;
        }

        static void MixColour(ref ShaperCacheMixer m, Color c)
        {
            m.MixFloat(c.r); m.MixFloat(c.g); m.MixFloat(c.b); m.MixFloat(c.a);
        }

        static ShaperCacheKey PrimitiveHash(ShaperNode node, float phase01, uint seed)
        {
            var m = ShaperCacheMixer.Begin(SaltPrimitive);
            m = MixCommon(m, node, phase01, seed);
            MixSwarmDef(ref m, node.swarm);
            var p = node.primitive ?? new ShaperPrimitiveDef();
            m.MixInt((int)p.kind);
            m.MixFloat(p.rectHalfW); m.MixFloat(p.rectHalfH); m.MixFloat(p.rectCornerRadius);
            m.MixFloat(p.ellipseRx); m.MixFloat(p.ellipseRy);
            m.MixFloat(p.diamondRx); m.MixFloat(p.diamondRy);
            m.MixFloat(p.triangleBase); m.MixFloat(p.triangleHeight);
            m.MixFloat(p.capsuleHalfLength); m.MixFloat(p.capsuleRadius);
            m.MixInt(p.ngonSides); m.MixFloat(p.ngonRadius); m.MixFloat(p.ngonRotation); m.MixFloat(p.ngonCornerRadius);
            m.MixInt(p.starArms); m.MixFloat(p.starRadius);
            MixZuiValue(ref m, p.starLength, phase01);
            MixZuiValue(ref m, p.starBaseWidth, phase01);
            MixZuiValue(ref m, p.starSkew, phase01);
            return m.Key;
        }

        static ShaperCacheKey CompositeHash(ShaperNode node, float phase01, uint seed)
        {
            var m = ShaperCacheMixer.Begin(SaltComposite);
            m = MixCommon(m, node, phase01, seed);
            MixSwarmDef(ref m, node.swarm);
            var c = node.composite ?? new ShaperCompositeDef();
            m.MixInt((int)c.reason);
            m.MixString(c.reasonNote);
            m.MixFloat(c.halfExtentX); m.MixFloat(c.halfExtentY);
            m.MixInt(c.bakeWidth); m.MixInt(c.bakeHeight);
            m.MixKey(SourceContentHash(c.source, phase01, seed));
            return m.Key;
        }

        /// <summary>
        /// A composite's hosted source is an arbitrary <see cref="IShaperCompositeSource"/> -- this file has no
        /// generic way to see inside one. A source MAY implement <see cref="IShaperCacheableSource"/> to
        /// publish a real content hash (the correct, precise answer); one that does not falls back to its own
        /// reference identity plus phase/seed -- correct for the common case (a fixed asset reference whose
        /// OWN dials are edited by swapping in a differently-configured asset), wrong for the case named
        /// honestly in SPEC.md Part 5: a source object mutated IN PLACE (its own fields edited without the
        /// reference itself changing) goes stale-cached under the fallback, because nothing here can see that
        /// mutation happened.
        /// </summary>
        static ShaperCacheKey SourceContentHash(IShaperCompositeSource source, float phase01, uint seed)
        {
            var m = ShaperCacheMixer.Begin("shaper.composite.source.v1");
            if (source == null) { m.MixBool(false); return m.Key; }
            m.MixBool(true);
            if (source is IShaperCacheableSource cacheable)
            {
                m.MixBool(true);
                m.MixKey(cacheable.ContentHash());
            }
            else
            {
                m.MixBool(false);
                m.MixInt(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source));
                m.MixFloat(phase01);
                m.MixUInt(seed);
            }
            return m.Key;
        }

        static ShaperCacheKey BagOwnHash(ShaperNode node, float phase01, uint seed)
        {
            var m = ShaperCacheMixer.Begin(SaltBag);
            m = MixCommon(m, node, phase01, seed);
            MixSwarmDef(ref m, node.swarm);
            // Deliberately NOT children here -- see FoldChild/BuildBagKey.
            return m.Key;
        }

        static void MixZuiValue(ref ShaperCacheMixer m, ZUIValue v, float phase01)
        {
            if (v == null) { m.MixBool(false); return; }
            m.MixBool(true);
            m.MixInt((int)v.mode);
            m.MixFloat(v.staticValue);
            if (v.mode == ZUIValue.Mode.Curve || v.mode == ZUIValue.Mode.Steps || v.mode == ZUIValue.Mode.Oscillation)
            {
                m.MixFloat(v.yMin); m.MixFloat(v.yMax); m.MixFloat(v.duration);
                m.MixFloat(v.warmup); m.MixFloat(v.cooldown); m.MixFloat(v.smoothness);
                m.MixFloat(phase01);   // the timed modes' output genuinely depends on where phase01 lands
            }
            if (v.mode == ZUIValue.Mode.MinMax) { m.MixFloat(v.min); m.MixFloat(v.max); }
            m.MixString(v.multiplierId);
        }

        /// <summary>
        /// True when <paramref name="node"/>'s OWN dials (not its children's) are a pure function of
        /// (settings, phase, seed) -- false when any authored <see cref="ZUIValue"/> is in
        /// <see cref="ZUIValue.Mode.MinMax"/>, which draws a fresh <c>UnityEngine.Random</c> sample on every
        /// <c>Evaluate</c> call and is therefore NOT reproducible from its own settings alone. Caching such a
        /// node would either freeze the random roll forever (a cache hit replays the FIRST draw, not a fresh
        /// one -- arguably worse than today's every-compile-reroll) or require excluding it, which is what this
        /// flag lets a caller do. A real, found issue (SPEC.md Part 5), not a hypothetical.
        /// </summary>
        public static bool IsCacheable(ShaperNode node)
        {
            if (node == null || !node.enabled) return true;   // the Empty-key fast path is always safe to cache
            if (node.kind == ShaperNodeKind.Primitive && node.primitive != null)
            {
                var p = node.primitive;
                if (IsNonDeterministic(p.starLength) || IsNonDeterministic(p.starBaseWidth) || IsNonDeterministic(p.starSkew))
                    return false;
            }
            // T-0155 — the same rule for Solids, and it needs every dial rather than a chosen few: unlike a
            // primitive, where only three fields are ZUIValue, EVERY authored dial on a ShaperSolidDef is one,
            // so any of them can be MinMax. A MinMax dial re-draws per evaluation, which is exactly what a
            // cache must not memoise.
            if (node.kind == ShaperNodeKind.Solid && node.solid != null)
            {
                var s = node.solid;
                if (IsNonDeterministic(s.size) || IsNonDeterministic(s.centreX) || IsNonDeterministic(s.centreY) ||
                    IsNonDeterministic(s.aspect) || IsNonDeterministic(s.depth) ||
                    IsNonDeterministic(s.gemSides) || IsNonDeterministic(s.gemCrown) ||
                    IsNonDeterministic(s.gemPavilion) || IsNonDeterministic(s.ringInner) ||
                    IsNonDeterministic(s.yaw) || IsNonDeterministic(s.tilt) || IsNonDeterministic(s.roll) ||
                    IsNonDeterministic(s.lineWidth) || IsNonDeterministic(s.edgeGlow) ||
                    IsNonDeterministic(s.innerGlow))
                    return false;
            }
            return true;
        }

        static bool IsNonDeterministic(ZUIValue v) => v != null && v.mode == ZUIValue.Mode.MinMax;

        /// <summary>
        /// The PARENT's fold step: combine an already-accumulated key with one more child, folding in the
        /// child's own <see cref="ShaperCacheKey"/> (its shape) AND the child's <see cref="ShaperNode.mode"/>/
        /// <see cref="ShaperNode.blend"/> (how it folds) -- see class doc for why those two live here rather
        /// than in the child's own <see cref="OwnHash"/>. Order-sensitive by construction (mixing is sequential
        /// and a bag's fold is order-dependent -- SHAPE-TREE-RULES R1, "no stage may reorder a bag's members").
        /// </summary>
        /// <summary>
        /// Folds one more child into an already-accumulated bag key. <paramref name="childKey"/> is the
        /// child's OWN full key -- for a Primitive/Composite child that is just <see cref="OwnHash"/>; for a
        /// Bag child it is the RESULT of that child's own complete fold chain (its own <see cref="OwnHash"/>
        /// mixed with all of ITS children in order) -- <see cref="ShaperCachedEvaluator"/> computes that
        /// recursively and passes the finished key in here, so this method never needs to know how a nested
        /// bag's key was built, only that it correctly identifies "this child's whole subtree, unioned".
        /// </summary>
        public static ShaperCacheKey FoldChild(ShaperCacheKey accumulated, ShaperNode child, ShaperCacheKey childKey)
        {
            var m = ShaperCacheMixer.Begin(SaltFold);
            m.MixKey(accumulated);
            m.MixKey(childKey);
            m.MixInt((int)child.mode);
            var b = child.blend ?? new ShaperBlend();
            m.MixFloat(b.width); m.MixFloat(b.sharpness); m.MixFloat(b.carveStrength);
            return m.Key;
        }

        /// <summary>Root/whole-swarm-node key: a swarm-enabled node's OWN settings plus its swarm def, used as
        /// the single atomic cache unit for the whole swarm (see SPEC.md Part 5 -- per-instance sub-caching
        /// within one swarm was designed but not built; this is the coarser, honestly-scoped alternative that
        /// still gets dirty-propagation right at the node level).</summary>
        public static ShaperCacheKey SwarmWholeNodeKey(ShaperCacheKey baseOwnHashExcludingSwarm, ShaperSwarmDef swarm)
        {
            var m = ShaperCacheMixer.Begin(SaltSwarmWhole);
            m.MixKey(baseOwnHashExcludingSwarm);
            MixSwarmDef(ref m, swarm);
            return m.Key;
        }

        /// <summary>
        /// A whole subtree's key computed structurally -- own hash folded with every enabled child's OWN
        /// structural key, recursively, in fold order -- WITHOUT evaluating a single pixel. Used wherever a
        /// caller needs a correct cache key for a subtree it is about to compile MONOLITHICALLY (a swarm node,
        /// per <see cref="ShaperCachedEvaluator"/>'s honestly-scoped atomic-swarm-unit design) rather than via
        /// the incremental per-member fold, so the key still reflects every authored dial anywhere in the
        /// subtree even though no member's buffer is separately cached in that path. Also doubles as a
        /// consistency check: for a subtree with NO swarm anywhere, this must equal the <c>.key</c> the
        /// incremental evaluator returns for the same subtree, since both use <see cref="OwnHash"/> +
        /// <see cref="FoldChild"/> in the same order -- <c>ShaperCacheAudit</c> asserts this equality directly.
        /// </summary>
        public static ShaperCacheKey FullSubtreeStructuralKey(ShaperNode node, float phase01, uint seed)
        {
            if (node == null || !node.enabled) return ShaperCacheKey.Empty;
            var own = OwnHash(node, phase01, seed);
            if (node.kind != ShaperNodeKind.Bag) return own;
            var key = own;
            var children = node.children;
            int n = children != null ? children.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var c = children[i];
                if (c == null || !c.enabled) continue;
                var childKey = FullSubtreeStructuralKey(c, phase01, seed);
                key = FoldChild(key, c, childKey);
            }
            return key;
        }

        /// <summary>One swarm instance's own key -- a pure function of the node's base settings (excluding
        /// swarm.count/enabled bookkeeping) plus the instance index and the swarm's own seed/jitter ranges, so
        /// instance i's key is IDENTICAL whether the swarm has 5 instances or 50: growing count reuses every
        /// existing instance's cache entry untouched (SPEC.md Part 3).</summary>
        public static ShaperCacheKey SwarmInstanceKey(ShaperCacheKey baseOwnHashExcludingSwarm, ShaperSwarmDef swarm, int instanceIndex)
        {
            var m = ShaperCacheMixer.Begin(SaltSwarmInstance);
            m.MixKey(baseOwnHashExcludingSwarm);
            m.MixUInt(swarm != null ? swarm.seed : 0u);
            m.MixVector2(swarm != null ? swarm.positionJitter : Vector2.zero);
            m.MixFloat(swarm != null ? swarm.rotationJitterDegrees : 0f);
            m.MixFloat(swarm != null ? swarm.scaleJitter : 0f);
            m.MixFloat(swarm != null ? swarm.lifetimeStagger : 0f);
            m.MixInt(instanceIndex);
            return m.Key;
        }
    }
}

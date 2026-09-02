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
            var t = node.transform;
            if (t != null)
            {
                t.EnsureDials();
                MixZuiValue(ref m, t.translateX, phase01);
                MixZuiValue(ref m, t.translateY, phase01);
                MixZuiValue(ref m, t.rotationDegrees, phase01);
                MixZuiValue(ref m, t.scaleX, phase01);
                MixZuiValue(ref m, t.scaleY, phase01);
                MixZuiValue(ref m, t.skewX, phase01);
                MixZuiValue(ref m, t.skewY, phase01);
                MixZuiValue(ref m, t.originX, phase01);
                MixZuiValue(ref m, t.originY, phase01);
            }
            MixSweep(ref m, node.sweep, phase01);
            MixShell(ref m, node.shell, phase01);
            m.MixFloat(phase01);
            m.MixUInt(seed);
            return m;
        }

        static void MixSweep(ref ShaperCacheMixer m, ShaperSweep s, float phase01)
        {
            if (s == null) { m.MixBool(false); return; }
            m.MixBool(s.enabled);
            if (!s.enabled) return;
            s.EnsureDials();
            MixZuiValue(ref m, s.startDegreesDial, phase01); MixZuiValue(ref m, s.extentDegreesDial, phase01);
            MixZuiValue(ref m, s.startFractionDial, phase01); MixZuiValue(ref m, s.extentFractionDial, phase01);
        }

        static void MixShell(ref ShaperCacheMixer m, ShaperShell s, float phase01)
        {
            if (s == null) { m.MixBool(false); return; }
            m.MixBool(s.enabled);
            if (!s.enabled) return;
            s.EnsureDials();
            MixZuiValue(ref m, s.thicknessDial, phase01);
            m.MixInt((int)s.alignment);
        }

        static void MixBlend(ref ShaperCacheMixer m, ShaperBlend b, float phase01)
        {
            if (b == null) { m.MixBool(false); return; }
            m.MixBool(true);
            b.EnsureDials();
            MixZuiValue(ref m, b.widthDial, phase01);
            MixZuiValue(ref m, b.sharpnessDial, phase01);
            MixZuiValue(ref m, b.carveStrengthDial, phase01);
        }

        static void MixSwarmDef(ref ShaperCacheMixer m, ShaperSwarmDef s, float phase01)
        {
            if (s == null) { m.MixBool(false); return; }
            m.MixBool(s.enabled);
            if (!s.enabled || s.count <= 1) return;
            s.EnsureDials();
            m.MixInt(s.count);
            m.MixUInt(s.seed);
            MixZuiValue(ref m, s.positionJitterX, phase01);
            MixZuiValue(ref m, s.positionJitterY, phase01);
            MixZuiValue(ref m, s.rotationJitterDegreesDial, phase01);
            MixZuiValue(ref m, s.scaleJitterDial, phase01);
            m.MixFloat(s.lifetimeStagger);
            m.MixBool(s.interact);
            MixBlend(ref m, s.merge, phase01);
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
            MixSwarmDef(ref m, node.swarm, phase01);

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
            MixSwarmDef(ref m, node.swarm, phase01);
            var p = node.primitive ?? new ShaperPrimitiveDef();
            p.EnsureDials();
            m.MixInt((int)p.kind);
            MixZuiValue(ref m, p.rectHalfWDial, phase01); MixZuiValue(ref m, p.rectHalfHDial, phase01);
            MixZuiValue(ref m, p.rectCornerRadiusDial, phase01);
            MixZuiValue(ref m, p.ellipseRxDial, phase01); MixZuiValue(ref m, p.ellipseRyDial, phase01);
            MixZuiValue(ref m, p.diamondRxDial, phase01); MixZuiValue(ref m, p.diamondRyDial, phase01);
            MixZuiValue(ref m, p.triangleBaseDial, phase01); MixZuiValue(ref m, p.triangleHeightDial, phase01);
            MixZuiValue(ref m, p.capsuleHalfLengthDial, phase01); MixZuiValue(ref m, p.capsuleRadiusDial, phase01);
            m.MixInt(p.ngonSides);
            MixZuiValue(ref m, p.ngonRadiusDial, phase01); MixZuiValue(ref m, p.ngonRotationDial, phase01);
            MixZuiValue(ref m, p.ngonCornerRadiusDial, phase01);
            m.MixInt(p.starArms);
            MixZuiValue(ref m, p.starRadiusDial, phase01);
            MixZuiValue(ref m, p.starLength, phase01);
            MixZuiValue(ref m, p.starBaseWidth, phase01);
            MixZuiValue(ref m, p.starSkew, phase01);
            // ── T-0174 Text ────────────────────────────────────────────────────────────────────────────────
            // Every dial that changes the baked glyph raster must be in the key: the node cache keys a whole
            // rendered node on this hash, so a dial left out would let an edited string keep drawing the old one.
            m.MixInt(p.textFont != null ? p.textFont.GetInstanceID() : 0);
            m.MixInt(p.textString != null ? ShaperTextPrepassCache.StableHash(p.textString) : 0);
            m.MixInt((int)p.textAlign);
            MixZuiValue(ref m, p.textSizeDial, phase01);
            MixZuiValue(ref m, p.textLetterSpacingDial, phase01);
            MixZuiValue(ref m, p.textLineSpacingDial, phase01);
            MixZuiValue(ref m, p.textWeightDial, phase01);
            // ── T-0175 fix (T-0174-noticed) Sprite ────────────────────────────────────────────────────────
            // Same reasoning as Text immediately above: every dial that changes the baked distance raster must
            // be in the key, or two Sprite primitives differing only in these keep sharing one node cache key
            // and the preview cache serves a stale field. The sprite ASSET is identity, not a dial, but its
            // own rect is part of the authoring state too — the same source texture re-sliced (an atlas
            // re-pack, a different sub-sprite at the same GUID) bakes a different raster from the same
            // instance id.
            m.MixInt(p.spriteAsset != null ? p.spriteAsset.GetInstanceID() : 0);
            if (p.spriteAsset != null)
            {
                Rect sr = p.spriteAsset.rect;
                m.MixFloat(sr.x); m.MixFloat(sr.y); m.MixFloat(sr.width); m.MixFloat(sr.height);
            }
            m.MixInt((int)p.spriteFitMode);
            MixZuiValue(ref m, p.spriteHalfWDial, phase01);
            MixZuiValue(ref m, p.spriteHalfHDial, phase01);
            MixZuiValue(ref m, p.spriteThresholdDial, phase01);
            MixZuiValue(ref m, p.spriteSoftnessDial, phase01);
            return m.Key;
        }

        static ShaperCacheKey CompositeHash(ShaperNode node, float phase01, uint seed)
        {
            var m = ShaperCacheMixer.Begin(SaltComposite);
            m = MixCommon(m, node, phase01, seed);
            MixSwarmDef(ref m, node.swarm, phase01);
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
            MixSwarmDef(ref m, node.swarm, phase01);
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

            // T-0168 — the transform, sweep, shell, fold blend and swarm jitter ranges are all animatable now,
            // so the Min-Max exclusion has to cover them too: any of them can be the one dial that re-draws.
            var t = node.transform;
            if (t != null &&
                (IsNonDeterministic(t.translateX) || IsNonDeterministic(t.translateY) ||
                 IsNonDeterministic(t.rotationDegrees) ||
                 IsNonDeterministic(t.scaleX) || IsNonDeterministic(t.scaleY) ||
                 IsNonDeterministic(t.skewX) || IsNonDeterministic(t.skewY) ||
                 IsNonDeterministic(t.originX) || IsNonDeterministic(t.originY)))
                return false;

            var sw = node.sweep;
            if (sw != null && sw.enabled &&
                (IsNonDeterministic(sw.startDegreesDial) || IsNonDeterministic(sw.extentDegreesDial) ||
                 IsNonDeterministic(sw.startFractionDial) || IsNonDeterministic(sw.extentFractionDial)))
                return false;

            var sh = node.shell;
            if (sh != null && sh.enabled && IsNonDeterministic(sh.thicknessDial)) return false;

            if (IsBlendNonDeterministic(node.blend)) return false;

            var sm = node.swarm;
            if (sm != null && sm.enabled && sm.count > 1 &&
                (IsNonDeterministic(sm.positionJitterX) || IsNonDeterministic(sm.positionJitterY) ||
                 IsNonDeterministic(sm.rotationJitterDegreesDial) || IsNonDeterministic(sm.scaleJitterDial) ||
                 IsBlendNonDeterministic(sm.merge)))
                return false;

            if (node.kind == ShaperNodeKind.Primitive && node.primitive != null)
            {
                var p = node.primitive;
                if (IsNonDeterministic(p.starLength) || IsNonDeterministic(p.starBaseWidth) || IsNonDeterministic(p.starSkew) ||
                    IsNonDeterministic(p.rectHalfWDial) || IsNonDeterministic(p.rectHalfHDial) || IsNonDeterministic(p.rectCornerRadiusDial) ||
                    IsNonDeterministic(p.ellipseRxDial) || IsNonDeterministic(p.ellipseRyDial) ||
                    IsNonDeterministic(p.diamondRxDial) || IsNonDeterministic(p.diamondRyDial) ||
                    IsNonDeterministic(p.triangleBaseDial) || IsNonDeterministic(p.triangleHeightDial) ||
                    IsNonDeterministic(p.capsuleHalfLengthDial) || IsNonDeterministic(p.capsuleRadiusDial) ||
                    IsNonDeterministic(p.ngonRadiusDial) || IsNonDeterministic(p.ngonRotationDial) ||
                    IsNonDeterministic(p.ngonCornerRadiusDial) || IsNonDeterministic(p.starRadiusDial) ||
                    // T-0174 — a Text dial re-drawn per evaluation rebakes a different glyph raster, which is
                    // exactly the thing a cache must not memoise.
                    IsNonDeterministic(p.textSizeDial) || IsNonDeterministic(p.textLetterSpacingDial) ||
                    IsNonDeterministic(p.textLineSpacingDial) || IsNonDeterministic(p.textWeightDial) ||
                    // T-0175 fix — same reasoning: a Sprite dial re-drawn per evaluation rebakes a different
                    // distance raster (a different threshold/softness mask, or a different fitted box), which
                    // this cache must not memoise either.
                    IsNonDeterministic(p.spriteHalfWDial) || IsNonDeterministic(p.spriteHalfHDial) ||
                    IsNonDeterministic(p.spriteThresholdDial) || IsNonDeterministic(p.spriteSoftnessDial))
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

        static bool IsBlendNonDeterministic(ShaperBlend b)
            => b != null && (IsNonDeterministic(b.widthDial) || IsNonDeterministic(b.sharpnessDial) ||
                             IsNonDeterministic(b.carveStrengthDial));

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
        public static ShaperCacheKey FoldChild(ShaperCacheKey accumulated, ShaperNode child, ShaperCacheKey childKey,
                                               float phase01)
        {
            var m = ShaperCacheMixer.Begin(SaltFold);
            m.MixKey(accumulated);
            m.MixKey(childKey);
            m.MixInt((int)child.mode);
            MixBlend(ref m, child.blend ?? new ShaperBlend(), phase01);
            return m.Key;
        }

        /// <summary>Root/whole-swarm-node key: a swarm-enabled node's OWN settings plus its swarm def, used as
        /// the single atomic cache unit for the whole swarm (see SPEC.md Part 5 -- per-instance sub-caching
        /// within one swarm was designed but not built; this is the coarser, honestly-scoped alternative that
        /// still gets dirty-propagation right at the node level).</summary>
        public static ShaperCacheKey SwarmWholeNodeKey(ShaperCacheKey baseOwnHashExcludingSwarm, ShaperSwarmDef swarm,
                                                       float phase01)
        {
            var m = ShaperCacheMixer.Begin(SaltSwarmWhole);
            m.MixKey(baseOwnHashExcludingSwarm);
            MixSwarmDef(ref m, swarm, phase01);
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
                key = FoldChild(key, c, childKey, phase01);
            }
            return key;
        }

        /// <summary>One swarm instance's own key -- a pure function of the node's base settings (excluding
        /// swarm.count/enabled bookkeeping) plus the instance index and the swarm's own seed/jitter ranges, so
        /// instance i's key is IDENTICAL whether the swarm has 5 instances or 50: growing count reuses every
        /// existing instance's cache entry untouched (SPEC.md Part 3).</summary>
        public static ShaperCacheKey SwarmInstanceKey(ShaperCacheKey baseOwnHashExcludingSwarm, ShaperSwarmDef swarm,
                                                      int instanceIndex, float phase01)
        {
            var m = ShaperCacheMixer.Begin(SaltSwarmInstance);
            m.MixKey(baseOwnHashExcludingSwarm);
            m.MixUInt(swarm != null ? swarm.seed : 0u);
            if (swarm != null)
            {
                swarm.EnsureDials();
                MixZuiValue(ref m, swarm.positionJitterX, phase01);
                MixZuiValue(ref m, swarm.positionJitterY, phase01);
                MixZuiValue(ref m, swarm.rotationJitterDegreesDial, phase01);
                MixZuiValue(ref m, swarm.scaleJitterDial, phase01);
            }
            m.MixFloat(swarm != null ? swarm.lifetimeStagger : 0f);
            m.MixInt(instanceIndex);
            return m.Key;
        }
    }
}

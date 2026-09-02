using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// T-0115 -- the caching audit: measured verification of the grid/budget numbers, per-node dirty
    /// propagation (an edited dial recomputes only that node and downstream), the composite-hit-avoids-re-render
    /// fix, swarm-instance key stability under a growing count, the frame cache's first-loop-computes /
    /// later-loop-replays behaviour, the MinMax non-cacheable bypass, the tilted-cache's separate key/budget
    /// namespace, and a realistic multi-layer document's cost with and without caching.
    ///
    /// Same posture as <c>ShaperSwarmAudit</c>/<c>ShaperFillAudit</c>: plain static methods, no
    /// <c>[MenuItem]</c>, no <c>EditorWindow</c>, invoked through the Unity CLI. Every result is measured, not
    /// asserted on the strength of the code compiling.
    /// </summary>
    public static class ShaperCacheAudit
    {
        const int W = 96, H = 64;   // the design's own "typical" grid (design B9's worked example)
        const float Px = 1f;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Shaper CACHE audit (T-0115) ===");
            sb.AppendLine(CT0_BudgetNumbers());
            sb.AppendLine(CT1_DisabledNodeCollapsesToEmpty());
            sb.AppendLine(CT2_DirtyPropagationOnBagMember());
            sb.AppendLine(CT3_FrameCacheFirstLoopComputesSecondReplays());
            sb.AppendLine(CT4_SwarmInstanceKeyStableUnderGrowingCount());
            sb.AppendLine(CT5_CompositeHitNeverReRenders());
            sb.AppendLine(CT6_TiltedCacheSeparateNamespace());
            sb.AppendLine(CT7_StructuralKeyMatchesEvaluatorKey());
            sb.AppendLine(CT8_MinMaxNodeBypassesCache());
            sb.AppendLine(CT9_RealisticDocumentCostModel());
            sb.AppendLine(CT10_ReachDivergenceFromMonolithicCompiler());
            return sb.ToString();
        }

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";
        static ShaperSampleGrid Grid() => ShaperSampleGrid.Centred(W, H, Px);

        static ShaperNode Disc(string name, float r, float x = 0f, float y = 0f)
        {
            var n = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r }, name);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        static ShaperNode Rect(string name, float hw, float hh, float x = 0f, float y = 0f)
        {
            var n = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh }, name);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        // ── CT0 — the grid/budget numbers, computed by the ported formula and checked against the design's
        // own worked example and its own citations ────────────────────────────────────────────────────────────

        static string CT0_BudgetNumbers()
        {
            ShaperCacheBudget.ComputeGrid(ShaperCacheBudget.SchemaResolutionDefault,
                ShaperCacheBudget.DefaultCanvasWidth, ShaperCacheBudget.DefaultCanvasHeight,
                out int gw, out int gh, out int cells);
            bool defaultOk = gw == 96 && gh == 64 && cells == 6144 && cells == ShaperCacheBudget.TypicalCells;

            ShaperCacheBudget.WorstCaseGrid(out int wgw, out int wgh, out int wcells);
            bool worstOk = wcells <= ShaperCacheBudget.MaxGridCells && wcells > 60000; // "about 68,000", not "64 square"

            return $"CT0 budget numbers: default grid {gw}x{gh}={cells} cells (expect 96x64=6144) " +
                   $"[{Verdict(defaultOk)}]; worst-case grid {wgw}x{wgh}={wcells} cells, cap={ShaperCacheBudget.MaxGridCells} " +
                   $"[{Verdict(worstOk)}] -- schema {ShaperCacheBudget.SchemaResolutionMin}-{ShaperCacheBudget.SchemaResolutionMax} " +
                   $"default {ShaperCacheBudget.SchemaResolutionDefault}, renderer clamp {ShaperCacheBudget.RendererClampMin}-{ShaperCacheBudget.RendererClampMax} " +
                   $"(D:\\CODEZ\\AgentHQ\\3D Shaper\\project_document.py:79,152; public\\index.html:982,1372-1374)";
        }

        // ── CT1 — two disabled nodes with wildly different dials collapse to the SAME key ──────────────────────

        static string CT1_DisabledNodeCollapsesToEmpty()
        {
            var a = Disc("A", 10f); a.enabled = false;
            var b = Rect("B", 99f, 3f); b.enabled = false;
            var ka = ShaperNodeIdentity.OwnHash(a, 0.3f, 7u);
            var kb = ShaperNodeIdentity.OwnHash(b, 0.9f, 42u);
            bool ok = ka == ShaperCacheKey.Empty && kb == ShaperCacheKey.Empty && ka == kb;
            return $"CT1 disabled-node identity: A.key==Empty {ka == ShaperCacheKey.Empty}, B.key==Empty {kb == ShaperCacheKey.Empty}, equal {ka == kb} [{Verdict(ok)}]";
        }

        // ── CT2 — the central claim: editing one bag member's dial recomputes only that member and downstream ──

        static string CT2_DirtyPropagationOnBagMember()
        {
            ShaperNode BuildTree(float r2)
            {
                return ShaperNode.Bag("Layer", ShaperCombineMode.Add,
                    Disc("m0", 10f, -30f, 0f),
                    Disc("m1", 12f, -10f, 0f),
                    Disc("m2", r2, 10f, 0f),
                    Disc("m3", 14f, 30f, 0f));
            }

            var cache = new ShaperNodeCache();
            var grid = Grid();

            var tree1 = BuildTree(13f);
            ShaperCachedEvaluator.Evaluate(tree1, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            long coldComputes = cache.ComputeCount; // full cold build: 4 leaves + 4 fold steps = 8

            cache.ResetCounters();
            var treeSame = BuildTree(13f);
            var resultSame = ShaperCachedEvaluator.Evaluate(treeSame, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            long noOpComputes = cache.ComputeCount; // identical settings: expect 0 new computes, all hits

            cache.ResetCounters();
            var treeEdited = BuildTree(20f); // ONLY member 2's own radius changed
            var resultEdited = ShaperCachedEvaluator.Evaluate(treeEdited, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            long editComputes = cache.ComputeCount; // expect: 1 leaf (m2) + fold steps from position 2 onward (2) = 3, not 8

            bool outputActuallyDiffers = !BuffersEqual(resultSame.buffer, resultEdited.buffer);
            bool ok = coldComputes == 8 && noOpComputes == 0 && editComputes > 0 && editComputes < coldComputes && outputActuallyDiffers;

            return $"CT2 dirty propagation: cold build={coldComputes} computes, unchanged re-evaluate={noOpComputes} computes, " +
                   $"one-member-edited re-evaluate={editComputes} computes (expect < {coldComputes}, and > 0), output changed={outputActuallyDiffers} [{Verdict(ok)}]";
        }

        static bool BuffersEqual(ShaperFieldBuffer a, ShaperFieldBuffer b)
        {
            if (a.distance.Length != b.distance.Length) return false;
            for (int i = 0; i < a.distance.Length; i++) if (a.distance[i] != b.distance[i]) return false;
            return true;
        }

        // ── CT3 — animation: first loop through N frames computes, second loop replays from cache ──────────────

        static string CT3_FrameCacheFirstLoopComputesSecondReplays()
        {
            var root = ShaperNode.Bag("Anim", ShaperCombineMode.Add,
                Disc("m0", 10f, -20f, 0f), Disc("m1", 12f, 0f, 0f), Disc("m2", 10f, 20f, 0f));

            var cache = new ShaperNodeCache();
            var grid = Grid();
            const int frameCount = 12;
            var frameCache = new ShaperFrameCache(root, cache, grid, W, H, frameCount);

            int cachedBefore = frameCache.CountCachedFrames();

            var sw1 = Stopwatch.StartNew();
            for (int f = 0; f < frameCount; f++) frameCache.ComputeFrame(f);
            sw1.Stop();
            long computesAfterFirstLoop = cache.ComputeCount;
            int cachedAfterFirstLoop = frameCache.CountCachedFrames();

            long computesBeforeSecondLoop = cache.ComputeCount;
            var sw2 = Stopwatch.StartNew();
            for (int f = 0; f < frameCount; f++) frameCache.ComputeFrame(f);
            sw2.Stop();
            long computesAfterSecondLoop = cache.ComputeCount;

            bool ok = cachedBefore == 0 && cachedAfterFirstLoop == frameCount &&
                     computesAfterSecondLoop == computesBeforeSecondLoop && // zero NEW computes on replay
                     computesAfterFirstLoop > 0;

            return $"CT3 frame cache: 0/{frameCount} cached before, {cachedAfterFirstLoop}/{frameCount} cached after first loop " +
                   $"({computesAfterFirstLoop} computes, {sw1.Elapsed.TotalMilliseconds:F2}ms wall); second loop added " +
                   $"{computesAfterSecondLoop - computesBeforeSecondLoop} NEW computes ({sw2.Elapsed.TotalMilliseconds:F2}ms wall) [{Verdict(ok)}] " +
                   $"-- wall-clock ratio is measured on THIS machine via the Unity CLI, not inside a live interactive editor session (SPEC.md Part 5)";
        }

        // ── CT4 — a swarm instance's own key does not move when the swarm's count grows ────────────────────────

        static string CT4_SwarmInstanceKeyStableUnderGrowingCount()
        {
            var baseHash = ShaperNodeIdentity.OwnHash(Disc("base", 10f), 0f, 0u);
            var swarm5 = new ShaperSwarmDef { enabled = true, count = 5, seed = 3u };
            var swarm8 = new ShaperSwarmDef { enabled = true, count = 8, seed = 3u };

            bool allMatch = true;
            for (int i = 0; i < 5; i++)
            {
                var k5 = ShaperNodeIdentity.SwarmInstanceKey(baseHash, swarm5, i);
                var k8 = ShaperNodeIdentity.SwarmInstanceKey(baseHash, swarm8, i);
                if (k5 != k8) allMatch = false;
            }
            var wholeA = ShaperNodeIdentity.SwarmWholeNodeKey(baseHash, swarm5);
            var wholeB = ShaperNodeIdentity.SwarmWholeNodeKey(baseHash, swarm8);
            bool wholeDiffers = wholeA != wholeB; // the ATOMIC whole-node key SHOULD differ (this task's built path uses this one)

            return $"CT4 swarm instance keys: instances 0-4 identical across count=5 vs count=8 growth: {allMatch} [{Verdict(allMatch)}]; " +
                   $"whole-node atomic key correctly differs when count changes: {wholeDiffers} [{Verdict(wholeDiffers)}] " +
                   $"-- per-instance sub-caching (using the per-instance keys) was designed but not wired into the built evaluator, " +
                   $"which uses the atomic whole-node path instead (SPEC.md Part 3, honest scope cut)";
        }

        // ── CT5 — a cache HIT on a Composite node must not call Render() again ──────────────────────────────────

        sealed class CountingSource : IShaperCompositeSource
        {
            public int renderCalls;
            public string SourceLabel => "Counting fixture";
            public void Render(int width, int height, float phase01, uint seed, Color32[] target)
            {
                renderCalls++;
                for (int i = 0; i < target.Length; i++) target[i] = new Color32(255, 255, 255, 255);
            }
        }

        static string CT5_CompositeHitNeverReRenders()
        {
            var source = new CountingSource();
            var node = ShaperNode.Composite(new ShaperCompositeDef
            {
                source = source, reason = ShaperCompositeReason.AuthoredData, reasonNote = "T-0115 fixture",
                halfExtentX = 32f, halfExtentY = 32f, bakeWidth = 32, bakeHeight = 32,
            }, "CompositeFixture");

            var cache = new ShaperNodeCache();
            var grid = Grid();
            ShaperCachedEvaluator.Evaluate(node, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            int callsAfterFirst = source.renderCalls;
            ShaperCachedEvaluator.Evaluate(node, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            ShaperCachedEvaluator.Evaluate(node, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            int callsAfterThree = source.renderCalls;

            bool ok = callsAfterFirst == 1 && callsAfterThree == 1;
            return $"CT5 composite cache hit: Render() calls after 1st evaluate={callsAfterFirst} (expect 1), after 3 evaluates={callsAfterThree} (expect 1) [{Verdict(ok)}]";
        }

        // ── CT6 — the tilted cache is a structurally separate type/namespace/budget ──────────────────────────

        sealed class FakeTiltedPayload { public int value; }

        static string CT6_TiltedCacheSeparateNamespace()
        {
            var flat = new ShaperNodeCache { MaxEntries = 4 };
            var tilted = new ShaperTiltedResolveCache { MaxEntries = 4 };

            for (int i = 0; i < 4; i++)
                flat.Store(new ShaperCacheKey((ulong)i, (ulong)i), new ShaperFieldBuffer(1, 1, new float[] { i }));

            for (int i = 0; i < 4; i++)
                tilted.Store(new ShaperTiltedCacheKey((ulong)i + 1000, (ulong)i + 1000), new FakeTiltedPayload { value = i });

            bool flatUnaffected = flat.Count == 4;
            bool tiltedUnaffected = tilted.Count == 4;

            // Filling the tilted cache PAST its own cap must not touch the flat cache's contents or count.
            tilted.Store(new ShaperTiltedCacheKey(9999, 9999), new FakeTiltedPayload { value = 99 });
            bool flatStillUnaffectedAfterTiltedEviction = flat.Count == 4 && tilted.Count == 4 && tilted.EvictCount == 1;

            // Compile-time separation: ShaperTiltedCacheKey is a distinct struct, not the same type as
            // ShaperCacheKey -- demonstrated by reflection here as a runtime-checkable proxy for the
            // compile-time fact (the two are literally different System.Type values).
            bool differentTypes = typeof(ShaperCacheKey) != typeof(ShaperTiltedCacheKey);

            bool ok = flatUnaffected && tiltedUnaffected && flatStillUnaffectedAfterTiltedEviction && differentTypes;
            return $"CT6 tilted-cache separation: flat count={flat.Count}, tilted count={tilted.Count}, " +
                   $"tilted eviction did not touch flat: {flatStillUnaffectedAfterTiltedEviction}, distinct key types: {differentTypes} [{Verdict(ok)}]";
        }

        // ── CT7 — the structural-only key (used by the swarm/atomic path) agrees with the incremental
        // evaluator's own returned key for a plain (non-swarm) tree ──────────────────────────────────────────

        static string CT7_StructuralKeyMatchesEvaluatorKey()
        {
            var tree = ShaperNode.Bag("L", ShaperCombineMode.Add, Disc("a", 8f, -5f), Disc("b", 9f, 5f));
            var cache = new ShaperNodeCache();
            var grid = Grid();
            var result = ShaperCachedEvaluator.Evaluate(tree, ShaperMatrix.Identity, 0.42f, 3u, grid, W, H, cache);
            var structural = ShaperNodeIdentity.FullSubtreeStructuralKey(tree, 0.42f, 3u);
            bool ok = result.key == structural;
            return $"CT7 structural-vs-evaluator key agreement (non-swarm tree): equal={ok} [{Verdict(ok)}]";
        }

        // ── CT8 — a MinMax-mode dial (non-deterministic) is correctly refused caching, not silently frozen ────

        static string CT8_MinMaxNodeBypassesCache()
        {
            var star = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Star, starArms = 5, starRadius = 30f };
            star.starLength.mode = ZUIValue.Mode.MinMax; star.starLength.min = 0.3f; star.starLength.max = 0.9f;
            var node = ShaperNode.Primitive(star, "StarMinMax");

            bool cacheableFlag = ShaperNodeIdentity.IsCacheable(node);
            var cache = new ShaperNodeCache();
            var grid = Grid();
            ShaperCachedEvaluator.Evaluate(node, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            ShaperCachedEvaluator.Evaluate(node, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            bool neverStored = cache.Count == 0;
            bool bypassCounted = cache.BypassCount == 2;

            bool ok = !cacheableFlag && neverStored && bypassCounted;
            return $"CT8 MinMax bypass: IsCacheable={cacheableFlag} (expect false), entries stored={cache.Count} (expect 0), " +
                   $"BypassCount={cache.BypassCount} (expect 2) [{Verdict(ok)}]";
        }

        // ── CT9 — a realistic document: a dozen Bag-of-4-subshapes layers, measured with and without caching ──

        static ShaperNode BuildRealisticDocument()
        {
            var root = ShaperNode.Bag("Root", ShaperCombineMode.Add);
            for (int layer = 0; layer < 12; layer++)
            {
                float baseX = (layer % 4) * 40f - 60f;
                float baseY = (layer / 4) * 40f - 40f;
                var bag = ShaperNode.Bag($"Layer{layer}", ShaperCombineMode.Add,
                    Disc($"L{layer}_0", 6f, baseX - 4f, baseY),
                    Disc($"L{layer}_1", 5f, baseX + 4f, baseY),
                    Rect($"L{layer}_2", 3f, 3f, baseX, baseY - 4f),
                    Rect($"L{layer}_3", 3f, 3f, baseX, baseY + 4f));
                root.children.Add(bag);
            }
            return root;
        }

        static string CT9_RealisticDocumentCostModel()
        {
            var grid = Grid();

            // Without caching: one monolithic compile+evaluate per "frame", repeated, as production's
            // ShaperEvaluator path already does today (no caching involved at all).
            var docForMonolithic = BuildRealisticDocument();
            var swMonolithic = Stopwatch.StartNew();
            const int repeats = 24; // one loop of a 24-frame animation with NOTHING changing between frames
            for (int r = 0; r < repeats; r++)
            {
                var program = ShaperCompiler.Compile(docForMonolithic, ShaperMatrix.Identity, 0f, 0u);
                var dist = new float[W * H];
                ShaperEvaluator.FillTile(program, grid, 0, 0, W, H, dist, null, 0, W, program.NewStack());
            }
            swMonolithic.Stop();

            // With caching: the SAME repeated evaluate, through ShaperCachedEvaluator -- first pass computes,
            // every later pass should be almost entirely hits since nothing changed between "frames" here.
            var docForCached = BuildRealisticDocument();
            var cache = new ShaperNodeCache { MaxEntries = 4096 };
            var swCached = Stopwatch.StartNew();
            for (int r = 0; r < repeats; r++)
                ShaperCachedEvaluator.Evaluate(docForCached, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);
            swCached.Stop();

            int nodeCount = CountNodes(docForCached);
            long computesForOnePass = 0;
            {
                var freshCache = new ShaperNodeCache();
                var freshDoc = BuildRealisticDocument();
                ShaperCachedEvaluator.Evaluate(freshDoc, ShaperMatrix.Identity, 0f, 0u, grid, W, H, freshCache);
                computesForOnePass = freshCache.ComputeCount;
            }

            bool nearEstimate = nodeCount >= 48 && nodeCount <= 72; // "roughly 60 passes/frame" -- 12 bags + 48 primitives = 60 nodes
            bool cachingCollapsesRepeatCost = cache.ComputeCount == computesForOnePass; // no NEW computes after the first pass across 24 repeats
            bool cachingFaster = swCached.Elapsed.TotalMilliseconds < swMonolithic.Elapsed.TotalMilliseconds;

            bool ok = nearEstimate && cachingCollapsesRepeatCost;
            return $"CT9 realistic document: {nodeCount} nodes (design's own estimate: ~60 passes/frame before effects) [{Verdict(nearEstimate)}]; " +
                   $"{repeats} repeats with nothing changed -- one-pass computes={computesForOnePass}, cached-total-computes-after-{repeats}-passes={cache.ComputeCount} " +
                   $"(expect equal -- zero NEW work after the first pass) [{Verdict(cachingCollapsesRepeatCost)}]; " +
                   $"wall time: monolithic(no cache)={swMonolithic.Elapsed.TotalMilliseconds:F2}ms, cached={swCached.Elapsed.TotalMilliseconds:F2}ms over {repeats} passes, " +
                   $"cached faster: {cachingFaster} [{Verdict(ok)}]";
        }

        static int CountNodes(ShaperNode n)
        {
            if (n == null) return 0;
            int c = 1;
            if (n.kind == ShaperNodeKind.Bag && n.children != null)
                foreach (var child in n.children) c += CountNodes(child);
            return c;
        }

        // ── CT10 — the one piece of production math ShaperCachedEvaluator duplicates (the soft-Subtract
        // "reach" band, which depends on an accumulated bounding box) is tracked with a SIMPLER, own AABB-union
        // rather than the real compiler's private Box bookkeeping. Measured, not assumed: compare the cached
        // incremental evaluator's output against the real monolithic ShaperCompiler+ShaperEvaluator path for a
        // scene that actually exercises a soft Subtract, and report the max per-pixel divergence. ──────────────

        static string CT10_ReachDivergenceFromMonolithicCompiler()
        {
            ShaperNode BuildSoftSubtractScene()
            {
                var cutter = Disc("cutter", 14f, 6f, 0f);
                cutter.mode = ShaperCombineMode.Subtract;
                cutter.blend = new ShaperBlend { width = 6f, sharpness = 0.4f, carveStrength = 0.7f };
                return ShaperNode.Bag("SoftCut", ShaperCombineMode.Add,
                    Disc("base", 20f, 0f, 0f),
                    cutter);
            }

            var grid = Grid();

            var monolithicTree = BuildSoftSubtractScene();
            var program = ShaperCompiler.Compile(monolithicTree, ShaperMatrix.Identity, 0f, 0u);
            var monolithicDistance = new float[W * H];
            ShaperEvaluator.FillTile(program, grid, 0, 0, W, H, monolithicDistance, null, 0, W, program.NewStack());

            var cachedTree = BuildSoftSubtractScene();
            var cache = new ShaperNodeCache();
            var cachedResult = ShaperCachedEvaluator.Evaluate(cachedTree, ShaperMatrix.Identity, 0f, 0u, grid, W, H, cache);

            float maxAbsDiff = 0f;
            int diffCountOverHalfPixel = 0;
            for (int i = 0; i < monolithicDistance.Length; i++)
            {
                float d = Mathf.Abs(monolithicDistance[i] - cachedResult.buffer.distance[i]);
                if (d > maxAbsDiff) maxAbsDiff = d;
                if (d > 0.5f) diffCountOverHalfPixel++;
            }

            // Not a pass/fail gate -- there is no a-priori "acceptable" threshold declared anywhere in the
            // design for a PREVIEW path's fidelity against the final bake. This is reported as a measured
            // number for the record, honestly, rather than silently assumed to be zero.
            return $"CT10 reach-formula divergence (soft Subtract, own-AABB reach vs real compiler): " +
                   $"max |Δdistance| = {maxAbsDiff:F4} canvas px across {monolithicDistance.Length} samples, " +
                   $"{diffCountOverHalfPixel} samples differ by >0.5px [MEASURED, not a pass/fail gate -- see SPEC.md Part 4]";
        }
    }
}

// TEMP PROBE T-0165 — PM deletes after running
//
// Measures the before/after this task's brief asked for: uncached ms/frame (ShaperDocumentRenderer.RenderFrame
// called fresh every time, exactly as the window did before this task) vs a cached hit through
// ShaperPreviewFrameCache (a repeat visit to an already-computed frame, and a full second pass over a document
// whose frames were all visited once already — the steady state of scrubbing back over an animation, or of
// Play looping past its first pass). Also isolates the ShaperFillBuffers allocation fix (T-0146 T21) on its
// own: the SAME layer rendered many times with pooling vs without.
//
// No [MenuItem], no EditorWindow — call Laubrary.Shaper.Editor.T0165_CachePerfProbe.RunAll() from the Unity
// CLI / Coplay execute_script, matching the posture of every other Shaper*Audit.cs probe in this folder.
using System.Diagnostics;
using System.Text;
using Laubrary.PyreShaper;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class T0165_CachePerfProbe
    {
        const int W = 96, H = 64;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== T-0165 cache perf probe ===");
            sb.AppendLine(P1_UncachedVsCachedFrameCache());
            sb.AppendLine(P2_FillBufferPoolAlone());
            return sb.ToString();
        }

        static ShaperDocument BuildDoc(int frameCount)
        {
            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            doc.canvasWidth = W;
            doc.canvasHeight = H;
            doc.pixelSize = 1f;
            doc.frameCount = frameCount;
            doc.frameRate = 12;
            doc.seed = 1234u;

            for (int layer = 0; layer < 4; layer++)
            {
                float baseX = (layer % 2) * 30f - 15f;
                float baseY = (layer / 2) * 30f - 15f;
                var bag = ShaperNode.Bag($"Layer{layer}", ShaperCombineMode.Add,
                    Disc($"L{layer}_0", 8f, baseX - 4f, baseY),
                    Disc($"L{layer}_1", 6f, baseX + 4f, baseY),
                    Rect($"L{layer}_2", 4f, 4f, baseX, baseY - 5f));

                // A Min-Max ZUIValue somewhere in the tree is what makes different frames actually differ —
                // an all-static document would let a naive cache look faster than it really is by never
                // needing a second distinct compute at all.
                var star = new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Star, starArms = 5, starRadius = 10f };
                star.starLength.mode = ZUIValue.Mode.MinMax;
                star.starLength.min = 0.3f;
                star.starLength.max = 0.9f;
                bag.children.Add(ShaperNode.Primitive(star, $"L{layer}_star"));

                doc.layers.Add(new ShaperLayer { name = $"Layer{layer}", enabled = true, root = bag });
            }
            return doc;
        }

        static ShaperNode Disc(string name, float r, float x, float y)
        {
            var n = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r }, name);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        static ShaperNode Rect(string name, float hw, float hh, float x, float y)
        {
            var n = ShaperNode.Primitive(new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh }, name);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        // ── P1 — the number the brief asked for: ms/frame uncached vs ms/frame on a cache hit ────────────────

        static string P1_UncachedVsCachedFrameCache()
        {
            const int frameCount = 24;
            var doc = BuildDoc(frameCount);

            // Uncached baseline: exactly what ShaperPreviewStage.Refresh did before T-0165 — a fresh
            // ShaperDocumentRenderer.RenderFrame call for every frame, no cache, no pool.
            var swUncached = Stopwatch.StartNew();
            for (int f = 0; f < frameCount; f++)
                ShaperDocumentRenderer.RenderFrame(doc, f, ShaperEffectApplier.Instance);
            swUncached.Stop();
            double uncachedMsPerFrame = swUncached.Elapsed.TotalMilliseconds / frameCount;

            // Cold pass through the cache: first-ever visit to each frame, so this should cost about the
            // same per frame as the uncached baseline (the pool only pays off on a REPEAT visit).
            var cache = new ShaperPreviewFrameCache();
            cache.EnsureShape(frameCount, W, H);
            var swCold = Stopwatch.StartNew();
            for (int f = 0; f < frameCount; f++)
                cache.ComputeFrame(f, doc);
            swCold.Stop();
            double coldMsPerFrame = swCold.Elapsed.TotalMilliseconds / frameCount;

            // Warm pass: every frame already resident — the steady state of scrubbing back over an
            // animation, or Play looping past its first lap. This is the number that matters.
            var swWarm = Stopwatch.StartNew();
            for (int f = 0; f < frameCount; f++)
                cache.ComputeFrame(f, doc);
            swWarm.Stop();
            double warmMsPerFrame = swWarm.Elapsed.TotalMilliseconds / frameCount;

            bool warmIsHit = cache.CountCachedFrames() == frameCount;
            bool warmFasterThanUncached = warmMsPerFrame < uncachedMsPerFrame * 0.1; // expect ~three orders of magnitude, demand at least 10x
            bool ok = warmIsHit && warmFasterThanUncached;

            Object.DestroyImmediate(doc);

            return $"P1 {frameCount}-frame document, {W}x{H} canvas, 4 layers (one Min-Max star each):\n"
                 + $"  uncached (no cache, matches pre-T-0165 window):        {uncachedMsPerFrame:F3} ms/frame\n"
                 + $"  cache COLD (first visit, pool warms up):               {coldMsPerFrame:F3} ms/frame\n"
                 + $"  cache WARM (every frame already resident):             {warmMsPerFrame:F5} ms/frame\n"
                 + $"  frames resident after warm pass: {cache.CountCachedFrames()}/{frameCount} [{(warmIsHit ? "PASS" : "FAIL")}]; "
                 + $"warm at least 10x faster than uncached: {warmFasterThanUncached} [{(ok ? "PASS" : "FAIL")}]";
        }

        // ── P2 — the ShaperFillBuffers allocation fix (T-0146 T21) isolated from the pixel cache above ────────

        static string P2_FillBufferPoolAlone()
        {
            const int repeats = 200;
            var doc = BuildDoc(1); // a still document -- every call renders the identical phase, isolating the
                                    // buffer-allocation cost from any actual pixel-content difference.
            var dst = new float[W * H * ShaperDocumentRenderer.FloatsPerSample];

            var swNoPool = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                ShaperDocumentRenderer.RenderPhaseInto(doc, 0f, dst, 0, null);
            swNoPool.Stop();

            var pool = new ShaperRenderBufferPool();
            var swPooled = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                ShaperDocumentRenderer.RenderPhaseInto(doc, 0f, dst, 0, pool);
            swPooled.Stop();

            Object.DestroyImmediate(doc);

            bool pooledFaster = swPooled.Elapsed.TotalMilliseconds < swNoPool.Elapsed.TotalMilliseconds;
            return $"P2 ShaperFillBuffers pooling alone, {repeats} repeats of the same still-document render:\n"
                 + $"  no pool (pre-T-0165: new ShaperFillBuffers per layer per call): {swNoPool.Elapsed.TotalMilliseconds:F2} ms total, "
                 + $"{swNoPool.Elapsed.TotalMilliseconds / repeats:F4} ms/call\n"
                 + $"  pooled (ShaperRenderBufferPool, exact-match reuse):             {swPooled.Elapsed.TotalMilliseconds:F2} ms total, "
                 + $"{swPooled.Elapsed.TotalMilliseconds / repeats:F4} ms/call\n"
                 + $"  pooled faster: {pooledFaster} [{(pooledFaster ? "PASS" : "FAIL")}]";
        }
    }
}

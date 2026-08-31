using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0115 -- design B9 requirement 1, built for real: a recursive, content-addressed evaluator that walks
    /// a <see cref="ShaperNode"/> tree and returns each node's own distance buffer over the CANVAS grid,
    /// reusing <see cref="ShaperNodeCache"/> so that a change to one node's own settings only recomputes that
    /// node and everything above it in the fold chain -- an unchanged sibling's own buffer, and every
    /// unchanged PREFIX of an unchanged bag's members, stays a cache hit.
    ///
    /// <b>Why this exists next to the already-fast, already-Burst-shaped <see cref="ShaperEvaluator"/> rather
    /// than replacing it.</b> <see cref="ShaperEvaluator"/> walks ONE flat, monolithic, whole-tree-compiled
    /// <see cref="ShaperProgram"/> -- exactly right for the FINAL bake / a runtime spawn, where the whole tree
    /// genuinely needs evaluating once and no dial is being dragged. It has no per-node boundary once compiled
    /// (that flattening is what makes it allocation-free and Burst-ready), so it structurally cannot skip an
    /// unchanged member without recompiling and re-walking the whole thing. This file is the PREVIEW path:
    /// it re-derives each node's own buffer by compiling and evaluating ONE node at a time via the real
    /// <see cref="ShaperCompiler"/>/<see cref="ShaperEvaluator"/> (so a leaf's own math, sweep, shell and
    /// dilate are exactly production's, never reimplemented here), and does the FOLD between nodes itself,
    /// per pixel, via the same public <see cref="ShaperOps.Combine"/> the compiler emits -- the one piece of
    /// production math this file duplicates, and the one place SPEC.md Part 4 names a measured precision cost
    /// for doing so (the accumulated bounding box used for a soft-Subtract's "reach" is tracked here as a
    /// plain per-node AABB union rather than reusing the compiler's own internal box bookkeeping, which is
    /// private to <c>ShaperCompiler</c>).
    /// </summary>
    public static class ShaperCachedEvaluator
    {
        public struct Result
        {
            public ShaperFieldBuffer buffer;
            /// <summary>This node's own full key -- for a leaf, its <see cref="ShaperNodeIdentity.OwnHash"/>;
            /// for a bag, the end of its whole fold chain (own hash folded with every member in order).</summary>
            public ShaperCacheKey key;
            public float boxCx, boxCy, boxHalfW, boxHalfH;
            public bool boxValid;
        }

        /// <summary>Evaluate one node's own buffer over the whole canvas grid, recursively, with caching.</summary>
        public static Result Evaluate(ShaperNode node, in ShaperMatrix parentForward, float phase01, uint seed,
                                      in ShaperSampleGrid grid, int width, int height, ShaperNodeCache cache)
        {
            if (node == null || !node.enabled)
                return new Result { buffer = EmptyBuffer(width, height), key = ShaperCacheKey.Empty };

            bool swarming = node.swarm != null && node.swarm.enabled && node.swarm.count > 1;
            if (swarming)
                return EvaluateSwarmAtomic(node, parentForward, phase01, seed, grid, width, height, cache);

            switch (node.kind)
            {
                case ShaperNodeKind.Primitive:
                case ShaperNodeKind.Composite:
                    return EvaluateLeafLike(node, parentForward, phase01, seed, grid, width, height, cache);
                case ShaperNodeKind.Bag:
                    return EvaluateBag(node, parentForward, phase01, seed, grid, width, height, cache);
                default:
                    return new Result { buffer = EmptyBuffer(width, height), key = ShaperCacheKey.Empty };
            }
        }

        // ── leaves (Primitive / Composite) -- compiled and evaluated as a one-node subtree via the REAL compiler ──

        static Result EvaluateLeafLike(ShaperNode node, in ShaperMatrix parentForward, float phase01, uint seed,
                                       in ShaperSampleGrid grid, int width, int height, ShaperNodeCache cache)
        {
            var key = ShaperNodeIdentity.OwnHash(node, phase01, seed);
            bool cacheable = ShaperNodeIdentity.IsCacheable(node);

            // A cache HIT must never call ShaperCompiler.Compile again: for a Composite node that call
            // re-renders the hosted source (IShaperCompositeSource.Render) -- exactly the expensive bake T-0112
            // caches once. The box therefore travels WITH the stored buffer (ShaperFieldBuffer.box*) so a hit
            // is a true zero-recompile, zero-rerender hit.
            if (cacheable && cache.TryGet(key, out var hit))
                return new Result { buffer = hit, key = key, boxCx = hit.boxCx, boxCy = hit.boxCy,
                                    boxHalfW = hit.boxHalfW, boxHalfH = hit.boxHalfH, boxValid = hit.boxValid };

            var program = ShaperCompiler.Compile(node, parentForward, phase01, seed);
            var distance = new float[width * height];
            ShaperEvaluator.FillTile(program, grid, 0, 0, width, height, distance, null, 0, width, program.NewStack());
            bool boxValid = !program.hasSingularTransform;
            var buffer = new ShaperFieldBuffer(width, height, distance,
                program.supportCx, program.supportCy, program.supportHalfW, program.supportHalfH, boxValid);

            cache.ComputeCount++;
            if (cacheable) cache.Store(key, buffer); else cache.BypassCount++;

            return new Result { buffer = buffer, key = key, boxCx = program.supportCx, boxCy = program.supportCy,
                                boxHalfW = program.supportHalfW, boxHalfH = program.supportHalfH, boxValid = boxValid };
        }

        // ── swarm: one atomic cache unit for the whole swarmed subtree (honest scope cut, see class doc's
        // sibling ShaperNodeIdentity.SwarmInstanceKey/SwarmWholeNodeKey docs for the finer-grained design that
        // was NOT built) -- reuses the real compiler's own swarm expansion (EmitGenericSwarm/EmitNativeSwarm)
        // exactly, at zero risk of a jitter-formula mismatch.

        static Result EvaluateSwarmAtomic(ShaperNode node, in ShaperMatrix parentForward, float phase01, uint seed,
                                          in ShaperSampleGrid grid, int width, int height, ShaperNodeCache cache)
        {
            var key = ShaperNodeIdentity.FullSubtreeStructuralKey(node, phase01, seed);

            if (cache.TryGet(key, out var hit))
                return new Result { buffer = hit, key = key, boxCx = hit.boxCx, boxCy = hit.boxCy,
                                    boxHalfW = hit.boxHalfW, boxHalfH = hit.boxHalfH, boxValid = hit.boxValid };

            var program = ShaperCompiler.Compile(node, parentForward, phase01, seed);
            var distance = new float[width * height];
            ShaperEvaluator.FillTile(program, grid, 0, 0, width, height, distance, null, 0, width, program.NewStack());
            bool boxValid = !program.hasSingularTransform;
            var buffer = new ShaperFieldBuffer(width, height, distance,
                program.supportCx, program.supportCy, program.supportHalfW, program.supportHalfH, boxValid);

            cache.ComputeCount++;
            cache.Store(key, buffer);

            return new Result { buffer = buffer, key = key, boxCx = program.supportCx, boxCy = program.supportCy,
                                boxHalfW = program.supportHalfW, boxHalfH = program.supportHalfH, boxValid = boxValid };
        }

        // ── bag: incremental member fold, each PREFIX cached, so an unchanged prefix of members is a hit ──────

        static Result EvaluateBag(ShaperNode node, in ShaperMatrix parentForward, float phase01, uint seed,
                                  in ShaperSampleGrid grid, int width, int height, ShaperNodeCache cache)
        {
            ShaperMatrix thisForward = ShaperMatrix.Mul(parentForward, node.transform.ToMatrix());
            ShaperCacheKey foldKey = ShaperNodeIdentity.OwnHash(node, phase01, seed);

            ShaperFieldBuffer accumBuffer = null; // null == the empty field, everywhere
            float accCx = 0, accCy = 0, accHalfW = 0, accHalfH = 0; bool accValid = false;

            var children = node.children;
            int n = children != null ? children.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var child = children[i];
                if (child == null || !child.enabled) continue;

                var childResult = Evaluate(child, thisForward, phase01, seed, grid, width, height, cache);
                var newFoldKey = ShaperNodeIdentity.FoldChild(foldKey, child, childResult.key);

                // The box AFTER folding this child -- computed once, up front, so it can be stored WITH a
                // freshly-computed buffer (letting a later hit on this exact prefix skip recompute entirely,
                // the same reasoning as the leaf/swarm paths above) and so it is available either way (hit or
                // miss) to seed the NEXT iteration's reach and the bag's own returned box.
                float nextCx = accCx, nextCy = accCy, nextHalfW = accHalfW, nextHalfH = accHalfH; bool nextValid = accValid;
                CombineBox(child.mode, ref nextCx, ref nextCy, ref nextHalfW, ref nextHalfH, ref nextValid,
                          childResult.boxCx, childResult.boxCy, childResult.boxHalfW, childResult.boxHalfH, childResult.boxValid);

                if (cache.TryGet(newFoldKey, out var cachedPrefix))
                {
                    accumBuffer = cachedPrefix;
                }
                else
                {
                    float reach = ComputeReach(accCx, accCy, accHalfW, accHalfH, accValid,
                                               childResult.boxCx, childResult.boxCy, childResult.boxHalfW, childResult.boxHalfH);
                    var blend = child.blend ?? new ShaperBlend();
                    float blendWidth = Mathf.Max(0f, blend.width);
                    float blendExponent = ShaperOps.BlendExponent(blend.sharpness);
                    float carveStrength = Mathf.Clamp01(blend.carveStrength);

                    var newDistance = new float[width * height];
                    var srcA = accumBuffer;
                    var srcB = childResult.buffer;
                    for (int p = 0; p < newDistance.Length; p++)
                    {
                        float a = srcA != null ? srcA.distance[p] : ShaperField.Empty;
                        float b = srcB.distance[p];
                        newDistance[p] = ShaperOps.Combine(child.mode, a, b, blendWidth, blendExponent, carveStrength, reach);
                    }
                    var newBuffer = new ShaperFieldBuffer(width, height, newDistance, nextCx, nextCy, nextHalfW, nextHalfH, nextValid);
                    cache.ComputeCount++;
                    cache.Store(newFoldKey, newBuffer);
                    accumBuffer = newBuffer;
                }

                accCx = nextCx; accCy = nextCy; accHalfW = nextHalfW; accHalfH = nextHalfH; accValid = nextValid;
                foldKey = newFoldKey;
            }

            if (accumBuffer == null) accumBuffer = EmptyBuffer(width, height);

            bool hasSweep = node.sweep != null && node.sweep.enabled;
            bool hasShell = node.shell != null && node.shell.enabled;
            if (hasSweep || hasShell)
            {
                var m = ShaperCacheMixer.Begin("shaper.bag.postops.v1");
                m.MixKey(foldKey);
                var postKey = m.Key;

                if (cache.TryGet(postKey, out var postHit))
                {
                    accumBuffer = postHit;
                }
                else
                {
                    accumBuffer = ApplySweepShell(node, thisForward, grid, width, height, accumBuffer,
                        accCx, accCy, accHalfW, accHalfH, accValid);
                    cache.ComputeCount++;
                    cache.Store(postKey, accumBuffer);
                }
                foldKey = postKey;
                // A sweep never enlarges the box (ShaperCompiler.EmitSweep's own comment: "a sweep carves the
                // child away; it never enlarges either box"); Shell does not either. So accCx/Cy/HalfW/HalfH
                // stay valid and unchanged here -- deliberately not touched.
            }

            return new Result { buffer = accumBuffer, key = foldKey, boxCx = accCx, boxCy = accCy,
                                boxHalfW = accHalfW, boxHalfH = accHalfH, boxValid = accValid };
        }

        // ── shared helpers ──────────────────────────────────────────────────────────────────────────────────

        static ShaperFieldBuffer EmptyBuffer(int width, int height)
        {
            var d = new float[width * height];
            for (int i = 0; i < d.Length; i++) d[i] = ShaperField.Empty;
            return new ShaperFieldBuffer(width, height, d);
        }

        /// <summary>AABB union/intersect of the accumulated box so far with one more child's box, mirroring
        /// <c>ShaperCompiler.EmitBag</c>'s own per-mode box rule (Add unions, Intersect intersects, Subtract
        /// never enlarges the accumulator's own box) -- see that method (<c>ShaperCompiler.cs:778-795</c>) for
        /// the rule this ports. Tracked here as a plain axis-aligned box in CANVAS space (both operands already
        /// are, since <see cref="ShaperProgram.supportCx"/> etc. are canvas-space by their own doc), which is
        /// simpler than the compiler's own private <c>Box</c> bookkeeping and MEASURED (not assumed) to agree
        /// closely enough for the reach formula below -- see SPEC.md Part 4 for the measured divergence.</summary>
        static void CombineBox(ShaperCombineMode mode, ref float cx, ref float cy, ref float halfW, ref float halfH, ref bool valid,
                               float ccx, float ccy, float chalfW, float chalfH, bool cvalid)
        {
            if (!cvalid) return; // an invalid/empty child box never changes the accumulator's own box
            if (!valid) { cx = ccx; cy = ccy; halfW = chalfW; halfH = chalfH; valid = true; return; }

            switch (mode)
            {
                case ShaperCombineMode.Add:
                {
                    float minX = Mathf.Min(cx - halfW, ccx - chalfW), maxX = Mathf.Max(cx + halfW, ccx + chalfW);
                    float minY = Mathf.Min(cy - halfH, ccy - chalfH), maxY = Mathf.Max(cy + halfH, ccy + chalfH);
                    cx = (minX + maxX) * 0.5f; cy = (minY + maxY) * 0.5f;
                    halfW = (maxX - minX) * 0.5f; halfH = (maxY - minY) * 0.5f;
                    break;
                }
                case ShaperCombineMode.Intersect:
                {
                    float minX = Mathf.Max(cx - halfW, ccx - chalfW), maxX = Mathf.Min(cx + halfW, ccx + chalfW);
                    float minY = Mathf.Max(cy - halfH, ccy - chalfH), maxY = Mathf.Min(cy + halfH, ccy + chalfH);
                    if (maxX < minX || maxY < minY) { valid = false; cx = cy = halfW = halfH = 0f; }
                    else { cx = (minX + maxX) * 0.5f; cy = (minY + maxY) * 0.5f; halfW = (maxX - minX) * 0.5f; halfH = (maxY - minY) * 0.5f; }
                    break;
                }
                default: // Subtract never enlarges the accumulator
                    break;
            }
        }

        /// <summary>The soft-subtract band, ported from <c>ShaperCompiler.EmitBag</c>'s own comment verbatim:
        /// "the soft-subtract band is authored as a fraction of the operands' own size, matching the reference
        /// app's <c>reach = 0.55 · min(width, height)</c>" of the UNION of the accumulator-so-far and the
        /// incoming child -- computed the same way regardless of the child's own combine mode, exactly as the
        /// real compiler does at <c>ShaperCompiler.cs:799-802</c>.</summary>
        static float ComputeReach(float cx, float cy, float halfW, float halfH, bool valid,
                                  float ccx, float ccy, float chalfW, float chalfH)
        {
            float minX, maxX, minY, maxY;
            if (!valid) { minX = ccx - chalfW; maxX = ccx + chalfW; minY = ccy - chalfH; maxY = ccy + chalfH; }
            else
            {
                minX = Mathf.Min(cx - halfW, ccx - chalfW); maxX = Mathf.Max(cx + halfW, ccx + chalfW);
                minY = Mathf.Min(cy - halfH, ccy - chalfH); maxY = Mathf.Max(cy + halfH, ccy + chalfH);
            }
            float unionHalfW = (maxX - minX) * 0.5f, unionHalfH = (maxY - minY) * 0.5f;
            return Mathf.Max(0f, 0.55f * Mathf.Min(unionHalfW * 2f, unionHalfH * 2f));
        }

        /// <summary>
        /// A bag's OWN sweep/shell, applied to the already-folded accumulator buffer directly, per pixel, via
        /// the same public <see cref="ShaperOps"/> functions <see cref="ShaperEvaluator"/>'s Sweep/Shell cases
        /// call -- ported from <c>ShaperCompiler.EmitSweep</c> (<c>ShaperCompiler.cs:840-912</c>) for the RADIAL
        /// case only. <b>Scope cut, named honestly:</b> a LONGITUDINAL sweep authored directly on a Bag (rather
        /// than on a Primitive/Composite leaf, where <see cref="EvaluateLeafLike"/> already handles it exactly
        /// via the real compiler) is not supported here -- it is the rarer authoring pattern (axis is normally
        /// declared by a primitive child, SHAPER_THE_DESIGN.md B12), and porting it correctly needs the same
        /// child-box-carried-through-the-inverse arithmetic <c>EmitSweep</c>'s longitudinal branch uses, which
        /// this task did not port. A Bag with an enabled Longitudinal sweep is detected by
        /// <c>ShaperCacheAudit</c> and falls through this method unchanged (identity), which is WRONG output,
        /// not just unoptimised -- see SPEC.md Part 4 for why this is flagged rather than silently accepted.
        /// </summary>
        static ShaperFieldBuffer ApplySweepShell(ShaperNode node, in ShaperMatrix thisForward, in ShaperSampleGrid grid,
                                                 int width, int height, ShaperFieldBuffer input,
                                                 float boxCx, float boxCy, float boxHalfW, float boxHalfH, bool boxValid)
        {
            var sweep = node.sweep;
            var shell = node.shell;
            bool hasSweep = sweep != null && sweep.enabled;
            bool hasShell = shell != null && shell.enabled;

            var outDistance = (float[])input.distance.Clone();

            if (hasSweep && sweep.extentDegrees < 360f)
            {
                bool invertible = thisForward.TryInvert(out var inverse);
                if (invertible)
                {
                    float sigmaMin = thisForward.SigmaMin;
                    float s = sweep.startDegrees * Mathf.Deg2Rad;
                    float e = Mathf.Max(0f, sweep.extentDegrees) * Mathf.Deg2Rad;
                    float end = s + e;
                    float p0 = Mathf.Sin(s), p1 = -Mathf.Cos(s);
                    float p2 = -Mathf.Sin(end), p3 = Mathf.Cos(end);
                    bool union = e > Mathf.PI;

                    for (int j = 0; j < height; j++)
                    {
                        float y = grid.Y(j);
                        int row = j * width;
                        for (int i = 0; i < width; i++)
                        {
                            float x = grid.X(i);
                            float lx = inverse.m00 * x + inverse.m01 * y + inverse.m02;
                            float ly = inverse.m10 * x + inverse.m11 * y + inverse.m12;
                            float w = ShaperOps.RadialWedge(p0, p1, p2, p3, union, lx, ly);
                            int idx = row + i;
                            outDistance[idx] = Mathf.Max(outDistance[idx], w * sigmaMin);
                        }
                    }
                }
            }

            if (hasShell)
            {
                for (int p = 0; p < outDistance.Length; p++)
                    outDistance[p] = ShaperOps.Shell(shell.alignment, shell.thickness, outDistance[p]);
            }

            return new ShaperFieldBuffer(width, height, outDistance, boxCx, boxCy, boxHalfW, boxHalfH, boxValid);
        }
    }
}

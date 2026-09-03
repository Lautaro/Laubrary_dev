// DotGenEvaluator.cs
// The whole procedural pipeline in one pass: areas, dots, mutation, recursion.
//
// It touches no graphics API and no editor API, and it never calls an ambient random — every draw comes from
// DotGenMath.Hash01 over the document seed and stable indices. That is what makes a DotGen document a
// reproducible thing rather than a lucky one: the same asset and the same seed give the same picture on any
// machine, and a gizmo can show what a mutator did without running the pipeline a second time.

using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.DotGen
{
    public static class DotGenEvaluator
    {
        public static DotGenResult Evaluate(DotGen doc)
        {
            var result = new DotGenResult();
            if (doc == null) return result;

            var tree = new DotGenTree(doc.generators);
            if (tree.Root == null) return result;

            var frame = DotGenMath.FixedFrame;
            var rootArea = DotGenMath.MakeArea(tree.Root, frame, new Vector2(0.5f, 0.5f), alignInside: true);

            int globalIndex = 0;
            Walk(doc, tree, tree.Root, new List<DotArea> { rootArea }, result, ref globalIndex);
            return result;
        }

        static void Walk(DotGen doc, DotGenTree tree, DotGenerator g, List<DotArea> inputAreas, DotGenResult result, ref int globalIndex)
        {
            if (g == null || !g.enabled) return;   // a disabled generator stops its whole subtree

            var gd = new DotGenGeneratorData { gen = g };
            result.order.Add(gd);
            result.byId[g.id] = gd;

            var placement = g.ActivePlacement;
            int seed = doc.seed;

            // Every surviving dot of every instance, in the order children will be offered them.
            var childAnchors = new List<ChildAnchor>();

            int instanceCap = Mathf.Max(1, g.maxInstances);
            int areaCount = Mathf.Min(inputAreas.Count, instanceCap);

            var local = new List<DotLocal>();
            var world = new List<DotPoint>();

            for (int ai = 0; ai < areaCount; ai++)
            {
                var a = inputAreas[ai];
                a.idx = globalIndex++;
                gd.areas.Add(a);
                result.totalAreas++;

                local.Clear();
                if (placement != null && placement.enabled) placement.Evaluate(g, a, a.idx, seed, local);

                world.Clear();
                for (int i = 0; i < local.Count; i++)
                {
                    var q = local[i];
                    Vector2 p = DotGenMath.Xform(q.x, q.y, a);
                    world.Add(new DotPoint
                    {
                        x = p.x, y = p.y, key = q.key,
                        hasCell = q.hasCell,
                        cellW = q.hasCell ? q.cellW * a.w : 0f,
                        cellH = q.hasCell ? q.cellH * a.h : 0f,
                        cellRot = a.rot + q.cellRot,
                        area = a
                    });
                }
                gd.baseDots.AddRange(world);

                var pts = new List<DotPoint>(world);
                ApplyMutators(g, a, pts, a.idx, seed, gd.traces);

                gd.finalDots.AddRange(pts);
                for (int i = 0; i < pts.Count; i++)
                    childAnchors.Add(new ChildAnchor { p = pts[i], parentArea = a });
            }

            if (g.showDots) result.totalVisibleDots += gd.finalDots.Count;

            var children = tree.ChildrenOf(g);
            for (int ci = 0; ci < children.Count; ci++)
            {
                var child = children[ci];
                var childAreas = new List<DotArea>();
                int childCap = Mathf.Max(1, child.maxInstances);
                int every = Mathf.Max(1, child.spawnEvery);
                double chance = (double)child.spawnChance / 100.0;
                int idLen = child.id != null ? child.id.Length : 0;

                for (int i = 0; i < childAnchors.Count && childAreas.Count < childCap; i++)
                {
                    if (i % every != 0) continue;
                    if (DotGenMath.Hash01(doc.seed + 37, i, idLen) > chance) continue;

                    var h = childAnchors[i];
                    bool useCell = child.areaBasis == DotAreaBasis.Cell && h.p.hasCell && h.p.cellW != 0f && h.p.cellH != 0f;
                    var basis = useCell
                        ? new DotArea { cx = h.p.x, cy = h.p.y, w = h.p.cellW, h = h.p.cellH, rot = h.p.cellRot }
                        : h.parentArea;

                    childAreas.Add(DotGenMath.MakeArea(child, basis, new Vector2(h.p.x, h.p.y), alignInside: false));
                }

                gd.childIds.Add(child.id);
                Walk(doc, tree, child, childAreas, result, ref globalIndex);
            }
        }

        static void ApplyMutators(DotGenerator g, in DotArea a, List<DotPoint> pts, int instIdx, int seed, List<DotMutatorTrace> traces)
        {
            if (g.mutators == null) return;

            for (int mi = 0; mi < g.mutators.Count; mi++)
            {
                var m = g.mutators[mi];
                if (m == null) continue;

                var trace = new DotMutatorTrace { moduleId = m.id, module = m, area = a };
                trace.before.AddRange(pts);

                if (m.enabled)
                {
                    var sel = g.ResolveSelector(m.selectorId);
                    m.Apply(g, a, pts, instIdx, seed, sel, trace.removed);
                }

                // The trace is recorded even for a disabled mutator, so its card can still be hovered and show
                // "this is where your dots are" rather than nothing at all.
                trace.after.AddRange(pts);
                traces.Add(trace);
            }
        }

        struct ChildAnchor
        {
            public DotPoint p;
            public DotArea parentArea;
        }
    }
}

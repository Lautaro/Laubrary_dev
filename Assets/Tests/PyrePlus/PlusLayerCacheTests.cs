using System.Collections.Generic;
using NUnit.Framework;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// T-0061 — the per-layer decomposition of a frame. A frame put together from layer buffers rendered on their
    /// own (PlanFrame / RenderLayerFrame / AccumulateChannel / ComposeFrame) is what RenderFrame produces: exactly,
    /// for every layer RenderFrame isolates itself (forms, posts over content, clips, mattes, borders) and for a
    /// fast-path layer over transparent pixels; within rounding (≤ 1 LSB) for a fast-path layer over earlier
    /// content. Plus the key rules (an edit changes one key; visibility changes none; a move changes the moved
    /// indices), the fused-background variant, the LRU store, and worker-thread rendering of layer jobs.
    public class PlusLayerCacheTests
    {
        const int Size = 40, Frames = 7, Seed = 31;

        // Seven layers exercising every composite feature: a WriteMatte writer, a clipped form, a LumaMatte and
        // the swarm form it shapes (with geometry + pixel + post modifiers), a windowed layer, a hidden layer, a
        // flat form with an over-matte border; plus a global geometry modifier (in the keys) and a global post
        // (applied live). Every Draw layer is one RenderFrame isolates, so the composite must be exact.
        static PyrePlusSpec BuildIsolatedSpec()
        {
            var spec = ScriptableObject.CreateInstance<PyrePlusSpec>();
            spec.canvasSize = Size; spec.frameCount = Frames; spec.seed = Seed;
            spec.layers.Clear();

            var writer = new PyrePlusLayer { shapeForm = ShapeForm.Disc, matteEnabled = true, matteRole = MatteRole.WriteMatte, matteChannel = 0, alpha = new ZUIValue(1f) };
            writer.shapeScale = new ZUIValue(1.4f);
            spec.layers.Add(writer);

            var plasma = new PyrePlusLayer { matteEnabled = true, matteRole = MatteRole.Draw, clipByChannel = 0, form = new PlasmaBloomForm(), alpha = new ZUIValue(1f) };
            spec.layers.Add(plasma);

            var luma = new PyrePlusLayer { shapeForm = ShapeForm.Disc, matteEnabled = true, matteRole = MatteRole.LumaMatte, matteScope = MatteScope.NextLayer, alpha = new ZUIValue(1f) };
            spec.layers.Add(luma);

            var arc = new PyrePlusLayer { matteEnabled = false, form = new ArcBurstForm { layout = ArcBurstForm.Layout.Bolt }, alpha = new ZUIValue(1f) };
            arc.swarmEnabled = true; arc.swarmCount = 3;
            arc.modifiers.Add(new WobbleModifier());
            arc.modifiers.Add(new TintModifier());
            arc.modifiers.Add(new BloomModifier());
            spec.layers.Add(arc);

            var fork = new PyrePlusLayer { matteEnabled = false, form = new ForkBlastForm(), alpha = new ZUIValue(1f), startFrame = 2, endFrame = 5 };
            spec.layers.Add(fork);

            var hidden = new PyrePlusLayer { matteEnabled = false, form = new InfernoForm(), alpha = new ZUIValue(1f), enabled = false };
            spec.layers.Add(hidden);

            var rimmed = new PyrePlusLayer { matteEnabled = false, shapeForm = ShapeForm.Star, alpha = new ZUIValue(0.9f), borderEnabled = true, borderOverMatte = true };
            spec.layers.Add(rimmed);

            spec.globalModifiers.Add(new WobbleModifier());
            spec.globalModifiers.Add(new BloomModifier());
            return spec;
        }

        // One render of layer li of frame f on its own, the way the editor cache does it.
        static void RenderLayers(PyrePlusSpec spec, int f, out PyrePlusRenderer.LayerPlan[] plans, out Color32[][] px, out Color32[][] borders)
        {
            plans = PyrePlusRenderer.PlanFrame(spec, f);
            px = new Color32[plans.Length][];
            borders = new Color32[plans.Length][];
            for (int li = 0; li < plans.Length; li++)
            {
                if (!plans[li].active) continue;
                float[] hf = plans[li].isHeightConsumer ? PyrePlusRenderer.AccumulateChannel(spec, plans, px, li, spec.layers[li].heightFromChannel) : null;
                px[li] = PyrePlusRenderer.RenderLayerFrame(spec, li, f, plans[li], hf, out borders[li]);
            }
        }

        static Color32[] ComposeFromLayers(PyrePlusSpec spec, int f)
        {
            RenderLayers(spec, f, out var plans, out var px, out var borders);
            return PyrePlusRenderer.ComposeFrame(spec, f, plans, px, borders);
        }

        static int DiffPixels(Color32[] a, Color32[] b, out int maxChannelDiff)
        {
            Assert.That(b, Is.Not.Null);
            Assert.That(b.Length, Is.EqualTo(a.Length));
            int d = 0; maxChannelDiff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int m = Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b), Mathf.Abs(a[i].a - b[i].a));
                if (m > 0) d++;
                if (m > maxChannelDiff) maxChannelDiff = m;
            }
            return d;
        }

        static int Lit(Color32[] px) { int n = 0; foreach (var c in px) if (c.a > 0) n++; return n; }

        [Test]
        public void ComposeFromLayerBuffers_MatchesRenderFrame_Exactly_ForIsolatedLayers()
        {
            var spec = BuildIsolatedSpec();
            try
            {
                int lit = 0;
                for (int f = 0; f < Frames; f++)
                {
                    var full = PyrePlusRenderer.RenderFrame(spec, f);
                    var composed = ComposeFromLayers(spec, f);
                    Assert.That(DiffPixels(full, composed, out _), Is.EqualTo(0), $"frame {f} differs");
                    lit += Lit(full);
                }
                Assert.That(lit, Is.GreaterThan(0), "the spec renders nothing — the test proves nothing");
                // The plan says what it should about this spec.
                var p3 = PyrePlusRenderer.PlanFrame(spec, 3);
                Assert.That(p3[0].isMatte && p3[1].hasClip && p3[2].isLuma && p3[3].matteActive && !p3[3].fastPath, Is.True, "roles");
                Assert.That(p3[4].active, Is.True, "windowed layer alive at frame 3");
                Assert.That(PyrePlusRenderer.PlanFrame(spec, 0)[4].active, Is.False, "windowed layer inactive at frame 0");
                Assert.That(p3[5].active, Is.False, "hidden layer");
                Assert.That(p3[6].hasBorder && !p3[6].fastPath, Is.True, "bordered layer isolated");
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void FastPathLayers_MatchExactlyOverTransparent_AndWithinQuantisationOverContent()
        {
            // Two plain Disc swarms (fast path) — the first over nothing (exact), the second overlapping the first.
            var spec = ScriptableObject.CreateInstance<PyrePlusSpec>();
            try
            {
                spec.canvasSize = Size; spec.frameCount = Frames; spec.seed = Seed;
                spec.layers.Clear();
                var a = new PyrePlusLayer { matteEnabled = false, shapeForm = ShapeForm.Disc, alpha = new ZUIValue(0.7f) };
                a.swarmEnabled = true; a.swarmCount = 4;
                spec.layers.Add(a);
                for (int f = 0; f < Frames; f++)
                    Assert.That(DiffPixels(PyrePlusRenderer.RenderFrame(spec, f), ComposeFromLayers(spec, f), out _), Is.EqualTo(0), $"single fast-path layer, frame {f}");

                var b = new PyrePlusLayer { matteEnabled = false, shapeForm = ShapeForm.Ring, alpha = new ZUIValue(0.6f) };
                b.shapeScale = new ZUIValue(1.3f);
                spec.layers.Add(b);
                var plans = PyrePlusRenderer.PlanFrame(spec, 0);
                Assert.That(plans[0].fastPath && plans[1].fastPath, Is.True);
                // Over content the layer's straight-alpha colour is quantised to bytes before it composites, so the
                // straight colour of a NEAR-TRANSPARENT pixel can drift a lot (dividing by a tiny alpha amplifies a
                // 1/255 step) while what is actually painted — premultiplied colour and alpha — moves by a step or two.
                int differing = 0; float worstPremul = 0; int worstAlpha = 0;
                for (int f = 0; f < Frames; f++)
                {
                    var full = PyrePlusRenderer.RenderFrame(spec, f);
                    var composed = ComposeFromLayers(spec, f);
                    differing += DiffPixels(full, composed, out _);
                    for (int i = 0; i < full.Length; i++)
                    {
                        float fa = full[i].a / 255f, ca = composed[i].a / 255f;
                        worstPremul = Mathf.Max(worstPremul, Mathf.Abs(full[i].r * fa - composed[i].r * ca), Mathf.Abs(full[i].g * fa - composed[i].g * ca), Mathf.Abs(full[i].b * fa - composed[i].b * ca));
                        worstAlpha = Mathf.Max(worstAlpha, Mathf.Abs(full[i].a - composed[i].a));
                    }
                }
                Assert.That(worstAlpha, Is.LessThanOrEqualTo(1), $"alpha drifted by {worstAlpha} ({differing} px differ)");
                Assert.That(worstPremul, Is.LessThanOrEqualTo(2f), $"premultiplied colour drifted by {worstPremul:F2}/255 ({differing} px differ)");
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void FirstFastPathLayerOverVisibleBackground_IsFusedAndExact()
        {
            // Opaque background + a plain Disc with a Bloom post: RenderFrame posts over bg + disc. The cache must
            // render that layer fused with the background (the plan says so) and the composite must be exact.
            var spec = ScriptableObject.CreateInstance<PyrePlusSpec>();
            try
            {
                spec.canvasSize = Size; spec.frameCount = Frames; spec.seed = Seed;
                spec.background = new Color(0.1f, 0.2f, 0.3f, 1f);
                spec.layers.Clear();
                var disc = new PyrePlusLayer { matteEnabled = false, shapeForm = ShapeForm.Disc, alpha = new ZUIValue(1f) };
                disc.modifiers.Add(new BloomModifier());
                spec.layers.Add(disc);
                spec.layers.Add(new PyrePlusLayer { matteEnabled = false, form = new InfernoForm(), alpha = new ZUIValue(1f) });
                var plans = PyrePlusRenderer.PlanFrame(spec, 2);
                Assert.That(plans[0].fastPath && plans[0].fuseBackground, Is.True, "first layer fused");
                Assert.That(plans[1].fuseBackground, Is.False);
                for (int f = 0; f < Frames; f++)
                    Assert.That(DiffPixels(PyrePlusRenderer.RenderFrame(spec, f), ComposeFromLayers(spec, f), out _), Is.EqualTo(0), $"frame {f}");
                // The variant carries the background: a background edit changes it, nothing else.
                var keys = new ulong[spec.layers.Count];
                for (int li = 0; li < keys.Length; li++) keys[li] = PlusLayerKey.LayerKey(spec, li);
                ulong v0 = PlusLayerKey.FrameVariant(spec, plans, keys, 0);
                Assert.That(PlusLayerKey.FrameVariant(spec, plans, keys, 1), Is.EqualTo(0UL));
                spec.background = new Color(0.1f, 0.2f, 0.35f, 1f);
                Assert.That(PlusLayerKey.FrameVariant(spec, PyrePlusRenderer.PlanFrame(spec, 2), keys, 0), Is.Not.EqualTo(v0));
                Assert.That(PlusLayerKey.LayerKey(spec, 0), Is.EqualTo(keys[0]), "background is not in the layer key");
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void Keys_EditChangesOneLayer_VisibilityChangesNone_MoveChangesTheMovedIndices()
        {
            var spec = BuildIsolatedSpec();
            try
            {
                int L = spec.layers.Count;
                ulong[] K() { var k = new ulong[L]; for (int i = 0; i < L; i++) k[i] = PlusLayerKey.LayerKey(spec, i); return k; }
                var k0 = K();
                Assert.That(K(), Is.EqualTo(k0), "stable across calls");

                // A dial on layer 3 changes layer 3's key only.
                spec.layers[3].alpha = new ZUIValue(0.5f);
                var k1 = K();
                for (int i = 0; i < L; i++) Assert.That(k1[i] != k0[i], Is.EqualTo(i == 3), $"layer {i} after editing layer 3");
                // A form dial too (through PlusForm.ContentHash).
                ((ArcBurstForm)spec.layers[3].form).layout = ArcBurstForm.Layout.Crown;
                var k2 = K();
                for (int i = 0; i < L; i++) Assert.That(k2[i] != k1[i], Is.EqualTo(i == 3), $"layer {i} after a form edit");
                // A modifier dial on layer 3.
                spec.layers[3].modifiers[1].enabled = false;
                Assert.That(K()[3], Is.Not.EqualTo(k2[3]));
                Assert.That(K()[1], Is.EqualTo(k2[1]));

                // Visibility and name change no key.
                var k3 = K();
                spec.layers[1].enabled = false; spec.layers[6].name = "renamed";
                Assert.That(K(), Is.EqualTo(k3), "enabled / name are not in the key");
                spec.layers[1].enabled = true;

                // A global POST changes no key; a global GEOMETRY modifier changes all.
                spec.globalModifiers[1].enabled = false;
                Assert.That(K(), Is.EqualTo(k3), "global post is applied live");
                ((WobbleModifier)spec.globalModifiers[0]).enabled = false;
                var k4 = K();
                for (int i = 0; i < L; i++) Assert.That(k4[i], Is.Not.EqualTo(k3[i]), $"layer {i} after a global geometry edit");

                // Moving the last layer to the front shifts every index: all keys change; moving it back restores them.
                var moved = spec.layers[L - 1];
                spec.layers.RemoveAt(L - 1); spec.layers.Insert(0, moved);
                var k5 = K();
                for (int i = 0; i < L; i++) Assert.That(k5[i], Is.Not.EqualTo(k4[i]));
                spec.layers.RemoveAt(0); spec.layers.Add(moved);
                Assert.That(K(), Is.EqualTo(k4), "moved back = same keys (cache hits)");
                // Swapping two adjacent layers changes exactly those two.
                (spec.layers[4], spec.layers[5]) = (spec.layers[5], spec.layers[4]);
                var k6 = K();
                for (int i = 0; i < L; i++) Assert.That(k6[i] != k4[i], Is.EqualTo(i == 4 || i == 5), $"layer {i} after an adjacent swap");

                // Spec-level render inputs are in every key.
                spec.seed++;
                var k7 = K();
                for (int i = 0; i < L; i++) Assert.That(k7[i], Is.Not.EqualTo(k6[i]));
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void LayerStore_HitsAcrossToggles_EvictsLru_ProtectsCurrentGeneration()
        {
            var store = new PlusLayerCache(capacityBytes: 4 * 100 * 3);   // room for three 100-px slots
            store.BeginGeneration();
            var px = new Color32[100];
            store.Put(1, 2, 0, 0, px, null);
            store.Put(2, 2, 0, 0, px, null);
            Assert.That(store.TryGet(1, 0, 0, out var s) && s.pixels == px, Is.True);
            Assert.That(store.TryGet(1, 0, 7, out _), Is.False, "a different variant is a miss");
            Assert.That(store.TryGet(1, 1, 0, out _), Is.False, "an unrendered frame is a miss");
            // Over capacity in the same generation: nothing evicted (the live spec must fit).
            store.Put(3, 2, 0, 0, px, null);
            store.Put(3, 2, 1, 0, px, null);
            Assert.That(store.EntryCount, Is.EqualTo(3));
            Assert.That(store.UsedBytes, Is.EqualTo(4 * 100 * 4));
            // Next generation touches 3 and 1 only; key 2 is the least recently used and goes.
            store.BeginGeneration();
            store.TryGet(3, 0, 0, out _);
            store.TryGet(1, 0, 0, out _);
            store.Put(4, 2, 0, 0, px, null);
            Assert.That(store.TryGet(2, 0, 0, out _), Is.False, "LRU entry evicted");
            Assert.That(store.TryGet(1, 0, 0, out _) && store.TryGet(3, 1, 0, out _) && store.TryGet(4, 0, 0, out _), Is.True);
            Assert.That(store.Evictions, Is.EqualTo(1));
        }

        [Test]
        public void ComposingFromTheStore_RecompositesOnToggle_WithNoRenders()
        {
            // The editor's loop in miniature: render every layer-frame into the store once, then hide a layer,
            // reorder nothing, and rebuild every frame from the store alone — identical to RenderFrame of the
            // toggled spec, with zero new renders.
            var spec = BuildIsolatedSpec();
            try
            {
                var store = new PlusLayerCache();
                int renders = 0;
                Color32[] Compose(int f)
                {
                    var plans = PyrePlusRenderer.PlanFrame(spec, f);
                    var keys = new ulong[plans.Length];
                    for (int li = 0; li < plans.Length; li++) keys[li] = PlusLayerKey.LayerKey(spec, li);
                    var px = new Color32[plans.Length][]; var borders = new Color32[plans.Length][];
                    for (int li = 0; li < plans.Length; li++)
                    {
                        if (!plans[li].active) continue;
                        ulong variant = PlusLayerKey.FrameVariant(spec, plans, keys, li);
                        if (store.TryGet(keys[li], f, variant, out var slot)) { px[li] = slot.pixels; borders[li] = slot.border; continue; }
                        float[] hf = plans[li].isHeightConsumer ? PyrePlusRenderer.AccumulateChannel(spec, plans, px, li, spec.layers[li].heightFromChannel) : null;
                        px[li] = PyrePlusRenderer.RenderLayerFrame(spec, li, f, plans[li], hf, out borders[li]);
                        store.Put(keys[li], Frames, f, variant, px[li], borders[li]);
                        renders++;
                    }
                    return PyrePlusRenderer.ComposeFrame(spec, f, plans, px, borders);
                }
                for (int f = 0; f < Frames; f++) Assert.That(DiffPixels(PyrePlusRenderer.RenderFrame(spec, f), Compose(f), out _), Is.EqualTo(0));
                int firstFill = renders;
                Assert.That(firstFill, Is.GreaterThan(0));

                // Hide the LumaMatte: the layer above it now composites un-matted — from its cached buffer.
                spec.layers[2].enabled = false;
                for (int f = 0; f < Frames; f++) Assert.That(DiffPixels(PyrePlusRenderer.RenderFrame(spec, f), Compose(f), out _), Is.EqualTo(0), $"luma hidden, frame {f}");
                Assert.That(renders, Is.EqualTo(firstFill), "hiding a layer rendered something");
                spec.layers[2].enabled = true;
                // Hide the matte writer: the clipped layer composites unclipped — cached.
                spec.layers[0].enabled = false;
                for (int f = 0; f < Frames; f++) Assert.That(DiffPixels(PyrePlusRenderer.RenderFrame(spec, f), Compose(f), out _), Is.EqualTo(0), $"writer hidden, frame {f}");
                Assert.That(renders, Is.EqualTo(firstFill));
                spec.layers[0].enabled = true;
                // A global post edit: live, no renders.
                ((BloomModifier)spec.globalModifiers[1]).enabled = false;
                for (int f = 0; f < Frames; f++) Assert.That(DiffPixels(PyrePlusRenderer.RenderFrame(spec, f), Compose(f), out _), Is.EqualTo(0), $"global post off, frame {f}");
                Assert.That(renders, Is.EqualTo(firstFill));
                // Everything back: still no renders.
                ((BloomModifier)spec.globalModifiers[1]).enabled = true;
                for (int f = 0; f < Frames; f++) Compose(f);
                Assert.That(renders, Is.EqualTo(firstFill));
                // Edit one layer's dial: exactly that layer's frames render again.
                spec.layers[3].alpha = new ZUIValue(0.4f);
                for (int f = 0; f < Frames; f++) Assert.That(DiffPixels(PyrePlusRenderer.RenderFrame(spec, f), Compose(f), out _), Is.EqualTo(0), $"after edit, frame {f}");
                Assert.That(renders, Is.EqualTo(firstFill + Frames), "one layer × every frame");
                // Undo it: hits.
                spec.layers[3].alpha = new ZUIValue(1f);
                for (int f = 0; f < Frames; f++) Compose(f);
                Assert.That(renders, Is.EqualTo(firstFill + Frames), "undo found the old buffers");
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void LayerJobsOnWorkerThreads_MatchTheMainThread()
        {
            var spec = BuildIsolatedSpec();
            try
            {
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out var why), Is.True, why);
                // Main-thread reference: every layer-frame and every composed frame.
                var refPx = new Color32[Frames][][]; var refBorders = new Color32[Frames][][]; var refPlans = new PyrePlusRenderer.LayerPlan[Frames][];
                var refFrames = new Color32[Frames][];
                for (int f = 0; f < Frames; f++)
                {
                    RenderLayers(spec, f, out refPlans[f], out refPx[f], out refBorders[f]);
                    refFrames[f] = PyrePlusRenderer.ComposeFrame(spec, f, refPlans[f], refPx[f], refBorders[f]);
                }
                foreach (int workers in new[] { 2, 4 })
                    for (int run = 0; run < 2; run++)
                    {
                        using (var fill = new PlusLayerFill(spec, workers))
                        {
                            int expected = 0;
                            for (int f = 0; f < Frames; f++)
                                for (int li = 0; li < refPlans[f].Length; li++)
                                {
                                    if (!refPlans[f][li].active) continue;
                                    int ff = f, ll = li; var plan = refPlans[f][li]; var pf = refPlans[f]; var writers = refPx[f];
                                    fill.Enqueue((ff, ll), s =>
                                    {
                                        float[] hf = plan.isHeightConsumer ? PyrePlusRenderer.AccumulateChannel(s, pf, writers, ll, s.layers[ll].heightFromChannel) : null;
                                        var px = PyrePlusRenderer.RenderLayerFrame(s, ll, ff, plan, hf, out var border);
                                        return (px, border);
                                    });
                                    expected++;
                                }
                            for (int f = 0; f < Frames; f++)
                            {
                                int ff = f; var pf = refPlans[f]; var px = refPx[f]; var bd = refBorders[f];
                                fill.Enqueue((ff, -1), s => PyrePlusRenderer.ComposeFrame(s, ff, pf, px, bd), urgent: true);
                                expected++;
                            }
                            int got = 0;
                            var deadline = System.DateTime.UtcNow.AddSeconds(60);
                            while (got < expected && System.DateTime.UtcNow < deadline)
                            {
                                if (!fill.TryTake(out var r)) { System.Threading.Thread.Sleep(1); continue; }
                                got++;
                                var (f, li) = ((int, int))r.tag;
                                Assert.That(r.payload, Is.Not.Null, fill.FirstError?.ToString());
                                if (li < 0) Assert.That(DiffPixels(refFrames[f], (Color32[])r.payload, out _), Is.EqualTo(0), $"{workers} workers run {run}: composed frame {f}");
                                else
                                {
                                    var (px, border) = ((Color32[], Color32[]))r.payload;
                                    Assert.That(DiffPixels(refPx[f][li], px, out _), Is.EqualTo(0), $"{workers} workers run {run}: layer {li} frame {f}");
                                    Assert.That(border == null, Is.EqualTo(refBorders[f][li] == null));
                                    if (border != null) Assert.That(DiffPixels(refBorders[f][li], border, out _), Is.EqualTo(0));
                                }
                            }
                            Assert.That(got, Is.EqualTo(expected), "every job came back");
                            Assert.That(fill.Pending, Is.EqualTo(0));
                        }
                    }
            }
            finally { Object.DestroyImmediate(spec); }
        }
    }
}

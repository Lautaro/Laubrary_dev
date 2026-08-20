using System.Collections.Generic;
using NUnit.Framework;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// T-0060 — the parallel frame fill is byte-identical to the serial renderer: a five-layer spec (Disc + Plasma
    /// Bloom + Arc Burst with the swarm on + ForkBlast under geometry/pixel/post modifiers + Inferno) rendered frame by
    /// frame on the main thread against PlusFrameFill on worker threads, every frame compared pixel for pixel, on
    /// several runs and worker counts (thread scheduling varies between runs). Plus the parallel-safety predicate,
    /// cancellation hygiene and the cold-vs-warm (fresh spec object vs reused) identity.
    public class PlusFrameFillTests
    {
        const int Size = 40, Frames = 7, Seed = 31;

        static PyrePlusSpec BuildSpec()
        {
            var spec = ScriptableObject.CreateInstance<PyrePlusSpec>();
            spec.canvasSize = Size; spec.frameCount = Frames; spec.seed = Seed;
            spec.layers.Clear();

            var disc = new PyrePlusLayer { matteEnabled = false, shapeForm = ShapeForm.Disc, alpha = new ZUIValue(0.8f) };
            spec.layers.Add(disc);

            var plasma = new PyrePlusLayer { matteEnabled = false, form = new PlasmaBloomForm(), alpha = new ZUIValue(1f) };
            spec.layers.Add(plasma);

            var arc = new PyrePlusLayer { matteEnabled = false, form = new ArcBurstForm { layout = ArcBurstForm.Layout.Bolt }, alpha = new ZUIValue(1f) };
            arc.swarmEnabled = true; arc.swarmCount = 3;
            spec.layers.Add(arc);

            var fork = new PyrePlusLayer { matteEnabled = false, form = new ForkBlastForm(), alpha = new ZUIValue(1f) };
            fork.modifiers.Add(new WobbleModifier());
            fork.modifiers.Add(new TintModifier());
            fork.modifiers.Add(new BloomModifier());
            spec.layers.Add(fork);

            var inferno = new PyrePlusLayer { matteEnabled = false, form = new InfernoForm(), alpha = new ZUIValue(1f) };
            spec.layers.Add(inferno);
            return spec;
        }

        static Color32[][] RenderSerial(PyrePlusSpec spec)
        {
            var o = new Color32[Frames][];
            for (int i = 0; i < Frames; i++) o[i] = PyrePlusRenderer.RenderFrame(spec, i);
            return o;
        }

        static int DiffPixels(Color32[] a, Color32[] b)
        {
            Assert.That(b, Is.Not.Null, "a frame came back null");
            Assert.That(b.Length, Is.EqualTo(a.Length));
            int d = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) d++;
            return d;
        }

        static void AssertIdentical(Color32[][] serial, Color32[][] parallel, string what)
        {
            for (int f = 0; f < Frames; f++)
                Assert.That(DiffPixels(serial[f], parallel[f]), Is.EqualTo(0), $"{what}: frame {f} differs");
        }

        [Test]
        public void MultiLayerSpec_IsParallelSafe()
        {
            var spec = BuildSpec();
            try { Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out var why), Is.True, why); }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void ParallelFill_MatchesSerial_OnSeveralRunsAndWorkerCounts()
        {
            var spec = BuildSpec();
            try
            {
                var serial = RenderSerial(spec);
                foreach (int workers in new[] { 2, 3, PlusFrameFill.DefaultWorkers })
                    for (int run = 0; run < 3; run++)
                        AssertIdentical(serial, PlusFrameFill.RenderAll(spec, Frames, workers), $"{workers} workers, run {run}");
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void ParallelFill_FromColdSpec_MatchesWarmSerial()
        {
            // The serial pass ran on one object (its pre-passes warm); the parallel pass starts from a brand-new
            // object whose first frame has never rendered — the clones must solve the pre-pass themselves, once.
            var warm = BuildSpec();
            var cold = BuildSpec();
            try
            {
                var serial = RenderSerial(warm);
                AssertIdentical(serial, PlusFrameFill.RenderAll(cold, Frames, 4), "cold spec");
                // And the other way round: serial on the object the parallel fill already warmed.
                AssertIdentical(RenderSerial(cold), serial, "serial after parallel");
            }
            finally { Object.DestroyImmediate(warm); Object.DestroyImmediate(cold); }
        }

        [Test]
        public void ParallelFill_RunsOnWorkerThreadsAndCancels()
        {
            var spec = BuildSpec();
            try
            {
                var order = new List<int>();
                for (int i = 0; i < Frames; i++) order.Add(i);
                var fill = new PlusFrameFill(spec, order, 2);
                Assert.That(fill.Workers, Is.EqualTo(2));
                fill.Cancel();
                fill.Dispose();   // joins; an in-flight frame finishes, nothing throws
                Assert.That(fill.WorkersIdle, Is.True);
                Assert.That(fill.FirstError, Is.Null);
                fill.Dispose();   // idempotent
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void IsParallelSafe_RejectsStatefulAndMainThreadOnlyLayers()
        {
            var spec = ScriptableObject.CreateInstance<PyrePlusSpec>();
            try
            {
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.True, "default Disc spec");
                var l = spec.layers[0];
                l.shapeForm = ShapeForm.Fire;
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out var why), Is.False); StringAssert.Contains("Fire", why);
                l.shapeForm = ShapeForm.Fireball;
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.False);
                l.shapeForm = ShapeForm.Text;
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.False);
                l.shapeForm = ShapeForm.Sprite;
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.False);
                l.shapeForm = ShapeForm.Sprite; l.form = new InfernoForm();   // a form overrides the enum draw
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.True, "form over a Sprite enum slot");
                l.shapeForm = ShapeForm.Text;                                  // …but Text still bakes its atlas
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.False);
                l.shapeForm = ShapeForm.Disc; l.form = null;
                l.shapeFill.texture = ZuiFill.TextureKind.Sprite;
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out why), Is.False); StringAssert.Contains("sprite", why);
                l.shapeFill.texture = ZuiFill.TextureKind.Noise;
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.True, "noise texture is pure math");
                l.enabled = false; l.shapeForm = ShapeForm.Fire;
                Assert.That(PyrePlusRenderer.IsParallelSafe(spec, out _), Is.True, "a disabled layer does not count");
            }
            finally { Object.DestroyImmediate(spec); }
        }

        [Test]
        public void PrepassCache_IsSharedBetweenAFormAndItsRenderClone()
        {
            var cache = new PlusPrepassCache<object>();
            var a = new InfernoForm();
            var b = (InfernoForm)a.Clone();
            b.SharePrepassWith(a);
            Assert.That(b.PrepassIdentity, Is.SameAs(a));
            Assert.That(a.PrepassIdentity, Is.SameAs(a));
            var ctx = new PlusFormCtx(8, 8, 0f, 1, 0, null, 1f, null, null, null, 0f, 0, 4);
            int builds = 0;
            var va = cache.Get(ctx, a, () => { builds++; return new object(); });
            var vb = cache.Get(ctx, b, () => { builds++; return new object(); });
            Assert.That(vb, Is.SameAs(va), "the clone reads the origin's entry");
            Assert.That(builds, Is.EqualTo(1));
            // A plain duplicate (the editor's Clone) keeps its own entry.
            var c = (InfernoForm)a.Clone();
            cache.Get(ctx, c, () => { builds++; return new object(); });
            Assert.That(builds, Is.EqualTo(2));
        }
    }
}

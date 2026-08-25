using System;
using System.Collections.Generic;
using NUnit.Framework;
using Laubrary.Pyre;
using Laubrary.SpriteFx;
using UnityEngine;

namespace Laubrary.Pyre.Tests
{
    /// A minimal form for the cache / clip-stats / supersample tests: a flat disc of `radiusFrac` whose field is
    /// a radial falloff scaled by life.
    [Serializable]
    public sealed class TestDiscForm : PyreForm
    {
        public override string DisplayName => "Test Disc";
        [UnityEngine.Range(0f, 1f)] public float radiusFrac = 0.6f;
        public float gain = 1f;
        public PyreRamp ramp = PyreRampPresets.Ember();
        [NonSerialized] public int renders;

        public void Field(in PyreFormCtx ctx, float life, float[] buf)
        {
            float r = radiusFrac * ctx.W * 0.5f, cx = ctx.W * 0.5f, cy = ctx.H * 0.5f;
            for (int y = 0; y < ctx.H; y++)
                for (int x = 0; x < ctx.W; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / Mathf.Max(1e-3f, r);
                    buf[y * ctx.W + x] = d < 1f ? gain * (1f - d) * (0.25f + life) : 0f;
                }
        }

        public override void Render(in PyreFormCtx ctx, Color32[] target)
        {
            renders++;
            float r = radiusFrac * ctx.W * 0.5f, cx = ctx.W * 0.5f, cy = ctx.H * 0.5f;
            for (int y = 0; y < ctx.H; y++)
                for (int x = 0; x < ctx.W; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy < r * r) target[y * ctx.W + x] = new Color32(255, 128, 0, 255);
                }
        }
    }

    public class PyreCapabilityTests
    {
        static PyreFormCtx Ctx(int w, int h, int frames = 12, int frameIndex = 3, int seed = 7, int salt = 0)
            => new PyreFormCtx(w, h, frames > 1 ? frameIndex / (float)(frames - 1) : 0f, seed, salt, null, 1f, null, null, null, 0f, frameIndex, frames, null);

        // ── supersample ──

        static int AlphaLevels(Color32[] buf) { var s = new HashSet<byte>(); foreach (var c in buf) s.Add(c.a); return s.Count; }

        [Test]
        public void Supersample_FlatDisc_HasMoreAlphaLevelsThanOneX()
        {
            var form = new TestDiscForm();
            var ctx = Ctx(32, 32);
            var one = new Color32[32 * 32]; var three = new Color32[32 * 32];
            PyreSupersample.Render(ctx, 1, form.Render, one);
            PyreSupersample.Render(ctx, 3, form.Render, three);
            Assert.That(AlphaLevels(one), Is.EqualTo(2), "1×: transparent or opaque only");
            Assert.That(AlphaLevels(three), Is.GreaterThan(4), "3×: edge pixels get partial coverage");
            // coverage mass is preserved (≈ the same opaque area)
            long a1 = 0, a3 = 0; foreach (var c in one) a1 += c.a; foreach (var c in three) a3 += c.a;
            Assert.That(a3, Is.EqualTo(a1).Within(a1 * 0.08));
            // premultiplied down-filter: an edge pixel keeps the disc's colour, never a dark fringe
            foreach (var c in three) if (c.a > 0 && c.a < 255) { Assert.That(c.r, Is.EqualTo(255)); Assert.That(c.g, Is.EqualTo(128)); }
        }

        [Test]
        public void Supersample_Downsample_PremultipliedAverage_IsExact()
        {
            // a 2×2 block: one opaque red texel + three transparent (with garbage RGB) → red at alpha 64, not dark red
            var big = new Color32[4];
            big[0] = new Color32(255, 0, 0, 255);
            big[1] = big[2] = big[3] = new Color32(0, 0, 0, 0);
            var tgt = new Color32[1];
            PyreSupersample.Downsample(big, 2, 2, 2, tgt, 1, 1);
            Assert.That(tgt[0], Is.EqualTo(new Color32(255, 0, 0, 64)));
        }

        // ── prepass cache ──

        [Test]
        public void PrepassCache_HitsUntilAnyKeyInputChanges()
        {
            var cache = new PyrePrepassCache<List<int>>();
            var form = new TestDiscForm();
            int builds = 0;
            Func<List<int>> build = () => { builds++; return new List<int> { builds }; };

            var a = cache.Get(Ctx(16, 16), form, build);
            var b = cache.Get(Ctx(16, 16, frameIndex: 9), form, build);      // another frame, same spec ⇒ hit
            Assert.That(b, Is.SameAs(a)); Assert.That(builds, Is.EqualTo(1));

            form.gain = 2f;                                                   // a dial edit ⇒ content hash changes ⇒ miss
            var c = cache.Get(Ctx(16, 16), form, build);
            Assert.That(c, Is.Not.SameAs(a)); Assert.That(builds, Is.EqualTo(2));

            cache.Get(Ctx(16, 16), form, build);
            Assert.That(builds, Is.EqualTo(2), "unchanged again ⇒ hit");
            cache.Get(Ctx(24, 16), form, build); Assert.That(builds, Is.EqualTo(3), "canvas size is part of the key");
            cache.Get(Ctx(24, 16, seed: 8), form, build); Assert.That(builds, Is.EqualTo(4), "seed is part of the key");
            cache.Get(Ctx(24, 16, seed: 8, frames: 30), form, build); Assert.That(builds, Is.EqualTo(5), "frame count is part of the key");
            cache.Get(Ctx(24, 16, seed: 8, frames: 30), form, build, extraHash: 99); Assert.That(builds, Is.EqualTo(6), "extra hash is part of the key");

            form.ramp.stops[2].color = Color.cyan;                            // a nested ramp edit counts too
            cache.Get(Ctx(24, 16, seed: 8, frames: 30), form, build, extraHash: 99); Assert.That(builds, Is.EqualTo(7));

            var other = new TestDiscForm();                                   // a different form instance has its own entry
            cache.Get(Ctx(16, 16), other, build); Assert.That(builds, Is.EqualTo(8));
            Assert.That(cache.Hits, Is.EqualTo(2)); Assert.That(cache.Misses, Is.EqualTo(8));
        }

        [Test]
        public void FormClone_DeepCopiesNestedRamp_AndHashesByContent()
        {
            var a = new TestDiscForm();
            var b = (TestDiscForm)a.Clone();
            Assert.That(b.ramp, Is.Not.SameAs(a.ramp));
            Assert.That(b.ramp.stops, Is.Not.SameAs(a.ramp.stops));
            Assert.That(b.ContentHash(), Is.EqualTo(a.ContentHash()), "a clone hashes equal");
            b.ramp.stops[0].pos = 0.01f;
            Assert.That(b.ContentHash(), Is.Not.EqualTo(a.ContentHash()), "a nested edit changes the hash");
            Assert.That(a.ramp.stops[0].pos, Is.EqualTo(0f), "the original is untouched");
        }

        // ── clip stats ──

        [Test]
        public void ClipStats_SampleFrames_SpanTheClip()
        {
            CollectionAssert.AreEqual(new[] { 0, 29 }, PyreClipStats.SampleFrames(30, 2));
            var f = PyreClipStats.SampleFrames(30, 8);
            Assert.That(f.Length, Is.EqualTo(8)); Assert.That(f[0], Is.EqualTo(0)); Assert.That(f[7], Is.EqualTo(29));
            CollectionAssert.AreEqual(new[] { 0 }, PyreClipStats.SampleFrames(1, 8));
            Assert.That(PyreClipStats.SampleFrames(4, 10).Length, Is.EqualTo(4), "never more samples than frames");
        }

        [Test]
        public void ClipStats_PoolsOverSampledFrames_AndIsCachedPerSpec()
        {
            var form = new TestDiscForm();
            var cache = new PyrePrepassCache<PyreClipStats.Result>();
            var ctx = Ctx(24, 24, frames: 12, frameIndex: 2);
            int fills = 0;
            var c = ctx;
            var r = PyreClipStats.Get(cache, ctx, form, 4, 0f, (life, buf) => { fills++; form.Field(c, life, buf); });
            Assert.That(fills, Is.EqualTo(4));
            Assert.That(r.frames, Is.EqualTo(new[] { 0, 4, 7, 11 }));
            Assert.That(r.pooled.peak, Is.EqualTo(r.perFrame[3].peak).Within(1e-6f), "the last frame (life 1) is brightest");
            Assert.That(r.pooled.q99, Is.GreaterThan(r.perFrame[0].q99), "pooled q99 sits above the dimmest frame's");
            Assert.That(r.pooled.q99, Is.LessThanOrEqualTo(r.pooled.peak));
            var again = PyreClipStats.Get(cache, Ctx(24, 24, frames: 12, frameIndex: 9), form, 4, 0f, (life, buf) => { fills++; form.Field(c, life, buf); });
            Assert.That(again, Is.SameAs(r)); Assert.That(fills, Is.EqualTo(4), "another frame reuses the clip statistics");
        }
    }
}

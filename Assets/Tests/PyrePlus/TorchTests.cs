using System.IO;
using NUnit.Framework;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// Port 04 — Flame / agent2 the GROUNDED flame (gen 2). Reference values recorded from numpy 1.26 (perm tables,
    /// tongue / ember streams, `heat_field` at frame 0) and the contract's own noise tiles
    /// (D:/Claude@GDrive/Flame/GEN2/contract/agent2/<draw>/noise_tile.npy).
    public class TorchTests
    {
        const string Contract = "D:/Claude@GDrive/Flame/GEN2/contract/agent2";

        static PlusFormCtx Ctx(int W, int frames, int frame, int seed = 115) =>
            new PlusFormCtx(W, W, frames > 1 ? frame / (float)(frames - 1) : 0f, seed, 0, null, 1f, null, null, null, 0f, frame, frames);

        [Test]
        public void PermTable_MatchesNumpyPermutation()
        {
            // np.random.default_rng(115).permutation(256)[:8], [255]; default_rng(811)[:8]
            var p = PlusTorch.PermTable(115);
            Assert.That(p.Length, Is.EqualTo(512));
            Assert.That(new[] { p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7] }, Is.EqualTo(new[] { 131, 100, 122, 20, 19, 204, 120, 206 }));
            Assert.That(p[255], Is.EqualTo(146));
            Assert.That(p[256], Is.EqualTo(p[0]), "doubled");
            int sum = 0; for (int i = 0; i < 256; i++) sum += p[i];
            Assert.That(sum, Is.EqualTo(32640), "a permutation of 0..255");
            var q = PlusTorch.PermTable(811);
            Assert.That(new[] { q[0], q[1], q[2], q[3], q[4], q[5], q[6], q[7] }, Is.EqualTo(new[] { 92, 249, 58, 142, 126, 138, 84, 229 }));
        }

        [Test]
        public void GradientNoise_MatchesTheContractNoiseTiles()
        {
            // Every draw's tile: raw single-octave pnoise3 at x = col·8/64, y = row·8/64, z = 0, periods (64, 12, 6), perm_table(seed).
            // Recorded: tile[0,0], tile[20,10] (row 20, col 10), tile[63,63], mean. numpy evaluates in float32.
            (uint seed, double v00, double v2010, double v6363, double mean)[] tiles =
            {
                (115, 0.0, 0.163818359375, 0.12079370021820068, -0.029033906757831573),
                (811, 0.0, -0.159423828125, 0.133090540766716, -0.0038711875677108765),
                (233, 0.0, 0.02587890625, 0.24226373434066772, 0.0129961296916008),
                (139, 0.0, 0.72412109375, -0.14668530225753784, -0.006912834942340851),
                (707, 0.0, 0.586181640625, -0.12321895360946655, 0.0027651339769363403),
            };
            foreach (var t in tiles)
            {
                var perm = PlusTorch.PermTable(t.seed);
                double N(int col, int row) => PlusFieldOps.GradientNoise3Periodic(col * 8.0 / 64.0, row * 8.0 / 64.0, 0.0, 64, 12, 6, perm);
                Assert.That(N(0, 0), Is.EqualTo(t.v00).Within(1e-6), t.seed + " [0,0]");
                Assert.That(N(10, 20), Is.EqualTo(t.v2010).Within(2e-5), t.seed + " [20,10]");
                Assert.That(N(63, 63), Is.EqualTo(t.v6363).Within(2e-5), t.seed + " [63,63]");
                double sum = 0.0;
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) sum += N(x, y);
                Assert.That(sum / 4096.0, Is.EqualTo(t.mean).Within(2e-6), t.seed + " mean");
            }
        }

        [Test]
        public void GradientNoise_IsPeriodicOnEveryAxis()
        {
            var perm = PlusTorch.PermTable(7);
            double a = PlusFieldOps.GradientNoise3Periodic(1.3, 2.7, 0.4, 64, 5, 6, perm);
            Assert.That(PlusFieldOps.GradientNoise3Periodic(1.3 + 64, 2.7, 0.4, 64, 5, 6, perm), Is.EqualTo(a).Within(1e-12));
            Assert.That(PlusFieldOps.GradientNoise3Periodic(1.3, 2.7 + 5, 0.4, 64, 5, 6, perm), Is.EqualTo(a).Within(1e-12));
            Assert.That(PlusFieldOps.GradientNoise3Periodic(1.3, 2.7 - 10, 0.4 + 12, 64, 5, 6, perm), Is.EqualTo(a).Within(1e-12));
        }

        [Test]
        public void TongueAndEmberTables_MatchNumpyStreams()
        {
            // barbs (seed 115): tongue_table → tongue 0 and 25; ember stream (seed + 977) x0 / phase / rise / drift of ember 0, x0 of ember 8.
            // The dials are float32 (1.2f is not 1.2), so a range's low/high carry ~6e-8 — the stream itself is exact (phase / hot at 1e-12).
            var s = TorchSettings.Barbs();
            var T = PlusTorch.TongueTable(s, 115);
            Assert.That(T.Length, Is.EqualTo(26));
            Assert.That(T[0].x0, Is.EqualTo(-10.34705401861505).Within(1e-6));
            Assert.That(T[0].y0, Is.EqualTo(15.20878956767071).Within(1e-6));
            Assert.That(T[0].phase, Is.EqualTo(0.844860801134519).Within(1e-12));
            Assert.That(T[0].rise, Is.EqualTo(16.112014663242334).Within(1e-6));
            Assert.That(T[0].outX, Is.EqualTo(-4.726398804757048).Within(1e-6));
            Assert.That(T[0].w, Is.EqualTo(2.076228261631878).Within(1e-6));
            Assert.That(T[0].l, Is.EqualTo(6.389959771286646).Within(1e-6));
            Assert.That(T[0].life, Is.EqualTo(0.5507066409374646).Within(1e-6));
            Assert.That(T[0].hot, Is.EqualTo(0.9967288660522408).Within(1e-12));
            Assert.That(T[25].x0, Is.EqualTo(-8.104254628008995).Within(1e-6));
            Assert.That(T[25].y0, Is.EqualTo(17.414625331275538).Within(1e-6));
            var E = PlusTorch.EmberTable(s, 115);
            Assert.That(E.Length, Is.EqualTo(9));
            Assert.That(E[0].x0, Is.EqualTo(-5.757382465610204).Within(1e-6));
            Assert.That(E[0].phase, Is.EqualTo(0.815620044943159).Within(1e-12));
            Assert.That(E[0].rise, Is.EqualTo(42.17377706264509).Within(1e-6));
            Assert.That(E[0].drift, Is.EqualTo(1.652733574377691).Within(1e-6));
            Assert.That(E[8].x0, Is.EqualTo(-1.3930229817348243).Within(1e-6));
        }

        /// barbs' heat field at frame 0 on the source's 1× grid (X = col + 0.5 − 32, Y = 108 − row − 0.5): u = 1/3 with
        /// the anchor at (cx, base_y)/3 makes Accumulate's supersample centres land exactly on those points.
        static void BarbsFrame0(out float[] H, out float[] C)
        {
            var s = TorchSettings.Barbs();
            H = new float[64 * 118]; C = new float[64 * 118];
            var fr = new TorchFrame { W2 = 64, H2 = 118, ox = 32.0 / 3, oy = 108.0 / 3, u = 1.0 / 3, amp = 1, seed = 115, src = TorchSource.Barbs };
            PlusTorch.Accumulate(s, fr, 0.0, new TorchScratch(), H, C);
        }

        [Test]
        public void HeatField_MatchesNumpyAtFrame0()
        {
            BarbsFrame0(out var H, out var C);
            // gen.heat_field(barbs, t = 0): a_var[60, 32], c_var[60, 32], a_var.max(), a_var.sum() (float32 in numpy)
            Assert.That(H[60 * 64 + 32], Is.EqualTo(0.9879345893859863).Within(2e-5));
            Assert.That(C[60 * 64 + 32], Is.EqualTo(0.7976619005203247).Within(2e-5));
            float max = 0f; double sum = 0.0;
            foreach (var v in H) { if (v > max) max = v; sum += v; }
            Assert.That(max, Is.EqualTo(1.5650354623794556).Within(2e-5));
            Assert.That(sum, Is.EqualTo(1169.553955078125).Within(0.05));
        }

        [Test]
        public void HeatField_ClosesTheLoop_AtEveryVariant()
        {
            // gen.check_closure: the field at t = 0 and t = 1 is the same field (< 1e-4), for every gen-2 term at once.
            (TorchSettings s, TorchSource src, uint seed)[] all =
            {
                (TorchSettings.Emberbed(), TorchSource.Emberbed, 811), (TorchSettings.Surge(), TorchSource.Surge, 233),
                (TorchSettings.Barbs(), TorchSource.Barbs, 115), (TorchSettings.Curl(), TorchSource.Curl, 139),
                (TorchSettings.Lash(), TorchSource.Lash, 707),
            };
            foreach (var (s, src, seed) in all)
            {
                int n = src.W * src.H;
                var H0 = new float[n]; var C0 = new float[n]; var H1 = new float[n]; var C1 = new float[n];
                var fr = new TorchFrame { W2 = src.W, H2 = src.H, ox = src.cx / 3, oy = src.baseY / 3, u = 1.0 / 3, amp = 1, seed = seed, src = src };
                var sc = new TorchScratch();
                PlusTorch.Accumulate(s, fr, 0.0, sc, H0, C0);
                PlusTorch.Accumulate(s, fr, 1.0, sc, H1, C1);
                double err = 0, lit = 0;
                for (int i = 0; i < n; i++) { err = System.Math.Max(err, System.Math.Abs(H0[i] - H1[i])); err = System.Math.Max(err, System.Math.Abs(C0[i] - C1[i])); if (H0[i] > s.aLo) lit++; }
                Assert.That(err, Is.LessThan(1e-4), seed + " loop closes");
                Assert.That(lit, Is.GreaterThan(200), seed + " is lit");
            }
        }

        [Test]
        public void TorchPresets_MatchTheContractRampJson_WhenAvailable()
        {
            (string draw, string ramp, System.Func<PlusRamp> preset, float top)[] all =
            {
                ("lash", "RAMP_HOT", PlusRampPresets.TorchHot, 0.88f), ("emberbed", "RAMP_EMBER", PlusRampPresets.TorchEmber, 0.82f),
                ("surge", "RAMP_WHITE", PlusRampPresets.TorchWhite, 1.12f), ("barbs", "RAMP_RIM", PlusRampPresets.TorchRim, 1.15f),
                ("curl", "RAMP_GOLD", PlusRampPresets.TorchGold, 1.18f),
            };
            foreach (var e in all)
            {
                var r = e.preset();
                Assert.That(r.stops.Count, Is.EqualTo(14), e.ramp + " 7 bands as 14 stepped stops");
                Assert.That(r.space, Is.EqualTo(PlusRampSpace.Srgb));
                Assert.That(PlusRampPresets.Torch(e.ramp).stops.Count, Is.EqualTo(14), "by Kiln name");
                Assert.That(PlusRampPresets.TorchTop(e.ramp), Is.EqualTo(e.top));
                string path = Path.Combine(Contract, e.draw, "ramp.json");
                if (!File.Exists(path)) continue;
                var stops = (JArray)JObject.Parse(File.ReadAllText(path))["ramps"][e.ramp]["stops"];
                Assert.That(stops.Count, Is.EqualTo(r.stops.Count), e.ramp + " stop count vs contract");
                for (int i = 0; i < stops.Count; i++)
                {
                    var st = (JArray)stops[i];
                    Assert.That(r.stops[i].pos, Is.EqualTo(st[0].Value<float>()).Within(1e-6), $"{e.ramp}[{i}].pos");
                    var c = (JArray)st[1];
                    Assert.That(Mathf.RoundToInt(r.stops[i].color.r * 255f), Is.EqualTo(c[0].Value<int>()), $"{e.ramp}[{i}].r");
                    Assert.That(Mathf.RoundToInt(r.stops[i].color.g * 255f), Is.EqualTo(c[1].Value<int>()), $"{e.ramp}[{i}].g");
                    Assert.That(Mathf.RoundToInt(r.stops[i].color.b * 255f), Is.EqualTo(c[2].Value<int>()), $"{e.ramp}[{i}].b");
                }
            }
            Assert.That(PlusRampPresets.Torch("nope"), Is.Null);
        }

        [Test]
        public void Bands_CollapseRepeatedPositionsToTheLaterStop()
        {
            PlusTorch.Bands(PlusRampPresets.TorchRim(), out var thr, out var cols);
            Assert.That(thr.Length, Is.EqualTo(7));     // 7 band starts (the top threshold IS position 1.0)
            Assert.That(thr[0], Is.EqualTo(0f));
            Assert.That(thr[1], Is.EqualTo(0.16f / 1.15f).Within(1e-6));
            Assert.That(cols[0], Is.EqualTo(new Color32(150, 16, 16, 255)));
            Assert.That(cols[1], Is.EqualTo(new Color32(214, 40, 18, 255)));
            Assert.That(cols[6], Is.EqualTo(new Color32(255, 255, 236, 255)));
            // shade(): colour = band of the highest threshold ≤ C; below the first → band 0
            Assert.That(PlusShade.Banded(0.01f, thr, cols), Is.EqualTo(cols[0]));
            Assert.That(PlusShade.Banded(0.30f / 1.15f + 1e-4f, thr, cols), Is.EqualTo(new Color32(240, 92, 20, 255)));
            Assert.That(PlusShade.Banded(1f, thr, cols), Is.EqualTo(cols[6]));
        }

        [Test]
        public void Render_IsDeterministic_ColdVsWarm_AndAlphaIsContinuous()
        {
            var a = new TorchForm(); var b = new TorchForm();
            var ta = new Color32[64 * 64]; var tb = new Color32[64 * 64];
            a.Render(Ctx(64, 24, 4), ta);
            a.Render(Ctx(64, 24, 9), ta);
            a.Render(Ctx(64, 24, 4), ta);
            b.Render(Ctx(64, 24, 4), tb);
            int diff = 0, lit = 0, partial = 0;
            var levels = new System.Collections.Generic.HashSet<byte>();
            for (int i = 0; i < ta.Length; i++)
            {
                if (!ta[i].Equals(tb[i])) diff++;
                if (ta[i].a > 0) { lit++; levels.Add(ta[i].a); }
                if (ta[i].a > 0 && ta[i].a < 255) partial++;
            }
            Assert.That(diff, Is.EqualTo(0));
            Assert.That(lit, Is.GreaterThan(100), "frame 4 of 24 is lit at 64 px");
            Assert.That(partial, Is.GreaterThan(20), "a continuous edge");
            Assert.That(levels.Count, Is.GreaterThan(30), "many distinct alpha levels (no threshold, no dither)");
        }

        [Test]
        public void EveryVariant_RendersSomething_AndStaysDeterministic()
        {
            foreach (TorchForm.Variant v in System.Enum.GetValues(typeof(TorchForm.Variant)))
            {
                var f = new TorchForm { variant = v };
                var t1 = new Color32[48 * 48]; var t2 = new Color32[48 * 48];
                f.Render(Ctx(48, 24, 6), t1);
                f.Render(Ctx(48, 24, 12), t2);
                f.Render(Ctx(48, 24, 6), t2);
                int lit = 0, diff = 0;
                for (int i = 0; i < t1.Length; i++) { if (t1[i].a > 0) lit++; if (!t1[i].Equals(t2[i])) diff++; }
                Assert.That(lit, Is.GreaterThan(50), v + " draws at frame 6");
                Assert.That(diff, Is.EqualTo(0), v + " is deterministic");
            }
        }

        [Test]
        public void Flame_IsGrounded_AndScalesWithTheCanvas()
        {
            // Nothing below the fuel bed; the lit area grows ~4× from 64 to 128 px (same canvas fractions).
            var f = new TorchForm();
            var t64 = new Color32[64 * 64]; var t128 = new Color32[128 * 128];
            f.Render(Ctx(64, 24, 3), t64);
            f.Render(Ctx(128, 24, 3), t128);
            int lit64 = 0, lit128 = 0, below = 0;
            int bedRow = Mathf.FloorToInt(f.ground * 64) - 2;   // y-up rows under the bed (two px of slack for the one-px alpha ramp)
            for (int i = 0; i < t64.Length; i++) { if (t64[i].a > 0) { lit64++; if (i / 64 < bedRow) below++; } }
            foreach (var c in t128) if (c.a > 0) lit128++;
            Assert.That(below, Is.EqualTo(0), "grounded");
            Assert.That(lit128 / (double)lit64, Is.InRange(3.0, 5.2));
        }

        [Test]
        public void Clone_IsDeep_AndContentHashTracksEveryDial()
        {
            var a = new TorchForm { variant = TorchForm.Variant.Lash };
            var b = (TorchForm)a.Clone();
            Assert.That(b.ContentHash(), Is.EqualTo(a.ContentHash()));
            b.lash.tongueX = new Vector2(1f, 2f);
            Assert.That(b.ContentHash(), Is.Not.EqualTo(a.ContentHash()), "a range dial is hashed");
            Assert.That(a.lash.tongueX, Is.EqualTo(TorchSettings.Lash().tongueX), "the copy owns its box");
            var c = (TorchForm)a.Clone();
            c.lash.ramp.stops[3].color = Color.black;
            Assert.That(c.ContentHash(), Is.Not.EqualTo(a.ContentHash()), "a ramp stop is hashed");
            Assert.That(a.lash.ramp.stops[3].color, Is.Not.EqualTo(Color.black), "the copy owns its ramp");
        }

        [Test]
        public void SetContractParam_LoadsGeometryPairsKindsAndRamp()
        {
            var f = new TorchForm();
            Assert.That(f.SetContractParam("tag", "curl"), Is.True);
            Assert.That(f.variant, Is.EqualTo(TorchForm.Variant.Curl));
            // curl: 96 × 112 → S = 112, left 8, top 0; cx 48, base_y 104, h_flame 68
            f.SetContractParam("w", 96); f.SetContractParam("h", 112); f.SetContractParam("cx", 48.0); f.SetContractParam("base_y", 104.0); f.SetContractParam("h_flame", 68.0);
            Assert.That(f.axisX, Is.EqualTo(56f / 112f).Within(1e-6));
            Assert.That(f.ground, Is.EqualTo(8f / 112f).Within(1e-6));
            Assert.That(f.height, Is.EqualTo(68f / 112f).Within(1e-6));
            Assert.That(f.curl.hFlame, Is.EqualTo(68f));
            Assert.That(f.SetContractParam("tongue_x", new System.Collections.Generic.List<object> { 15.0, 23.0 }), Is.True);
            Assert.That(f.curl.tongueX, Is.EqualTo(new Vector2(15f, 23f)));
            Assert.That(f.SetContractParam("turb_kind", "billow"), Is.True);
            Assert.That(f.curl.turbKind, Is.EqualTo(TorchNoiseKind.Billow));
            Assert.That(f.SetContractParam("abig", 0.56), Is.True);
            Assert.That(f.SetContractParam("toct", 3), Is.True);
            Assert.That(f.SetContractParam("ramp", "RAMP_GOLD"), Is.True);
            Assert.That(f.curl.rampTop, Is.EqualTo(1.18f));
            Assert.That(f.SetContractParam("hand_tuned", "every value chosen by eye"), Is.False);
            Assert.That(f.SetContractParam("no_such_dial", 1.0), Is.False);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Laubrary.PyrePlus;
using Laubrary.PyrePlus.Forms.Kiln;
using UnityEngine;

namespace Laubrary.PyrePlus.Tests
{
    /// PlusBands (T-0064): the hard band table behind the bands control. The control changes presentation, not
    /// bytes — so the table must read the contract's step stops losslessly and evaluate exactly as the old
    /// `PlusShade.Banded` path did.
    public class PlusBandsTests
    {
        static readonly string[] TorchNames = { "hot", "ember", "white", "rim", "gold" };
        static readonly string[] ArcNames = { "ion", "violet", "acid", "plasma", "cyan", "magenta", "chroma", "steel", "crimson", "teal" };

        /// The old reading of a step-stop ramp (what PlusTorch.Bands did before PlusBands existed), kept here as the
        /// reference: stable sort, repeated positions collapse to the later stop.
        static void OldBands(PlusRamp ramp, out float[] thresholds, out Color32[] colors)
        {
            var stops = new List<PlusRampStop>(ramp.stops);
            var idx = new int[stops.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            System.Array.Sort(idx, (a, b) => { int c = stops[a].pos.CompareTo(stops[b].pos); return c != 0 ? c : a.CompareTo(b); });
            var th = new List<float>(); var co = new List<Color32>();
            foreach (int i in idx)
            {
                var st = stops[i];
                if (th.Count > 0 && th[th.Count - 1] == st.pos) co[co.Count - 1] = st.color;
                else { th.Add(st.pos); co.Add(st.color); }
            }
            thresholds = th.ToArray(); colors = co.ToArray();
        }

        [Test]
        public void TorchPresets_SevenBands_EvalEqualsTheOldBandedPath()
        {
            foreach (var name in TorchNames)
            {
                var ramp = PlusRampPresets.Torch(name);
                var bands = PlusRampPresets.TorchBands(name);
                Assert.That(bands.Count, Is.EqualTo(7), name + ": seven bands (the closing stop at 1 is not a band)");
                Assert.That(bands.transparentBelowFirst, Is.False);
                Assert.That(bands.Thresholds[0], Is.EqualTo(0f));
                OldBands(ramp, out var thr, out var cols);
                for (int k = 0; k <= 1000; k++)
                {
                    float t = k / 1000f * 1.2f;   // the torch field is compared against thresholds up to ~1.18 / top
                    Color32 expect = PlusShade.Banded(t, thr, cols);
                    Color32 got = PlusShade.Banded(t, bands.Thresholds, bands.Colors32);
                    Assert.That(got, Is.EqualTo(expect), $"{name} @ t={t:0.000} (tables)");
                    Assert.That((Color32)bands.Eval(t), Is.EqualTo(expect), $"{name} @ t={t:0.000} (Eval)");
                }
            }
        }

        [Test]
        public void ArcPresets_FiveBands_EvalEqualsTheOldBandedPath_AndIsClearUnderTheFloor()
        {
            foreach (var name in ArcNames)
            {
                var bands = ArcBands.Get(name);
                Assert.That(bands.Count, Is.EqualTo(5), name);
                Assert.That(bands.transparentBelowFirst, Is.True, name + ": the under-floor cut shows in the preview");
                Assert.That(bands.Thresholds, Is.EqualTo(new[] { 0.045f, 0.13f, 0.30f, 0.58f, 0.92f }));
                for (int k = 0; k <= 1000; k++)
                {
                    float t = k / 1000f;
                    Color32 expect = PlusShade.Banded(t, bands.Thresholds, bands.Colors32);
                    var e = bands.Eval(t);
                    if (t < 0.045f) Assert.That(e.a, Is.EqualTo(0f), $"{name} clear under the floor @ {t}");
                    else Assert.That((Color32)e, Is.EqualTo(expect), $"{name} @ t={t:0.000}");
                }
            }
        }

        [Test]
        public void StopsRoundTrip_IsLossless()
        {
            foreach (var name in TorchNames)
            {
                var a = PlusRampPresets.TorchBands(name);
                var back = PlusBands.FromStops(a.ToStops());
                Assert.That(back.Count, Is.EqualTo(a.Count));
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That(back.stops[i].pos, Is.EqualTo(a.stops[i].pos), $"{name}[{i}].pos");
                    Assert.That(back.stops[i].color, Is.EqualTo(a.stops[i].color), $"{name}[{i}].color");
                }
                Assert.That(back.ContentHash(), Is.EqualTo(a.ContentHash()));
            }
            // the contract's own 14-stop encoding reads to the same table as its 7-stop round trip
            var fromContract = PlusBands.FromStops(PlusRampPresets.TorchRim());
            var viaStops = PlusBands.FromStops(fromContract.ToStops());
            Assert.That(viaStops.ContentHash(), Is.EqualTo(fromContract.ContentHash()));
        }

        [Test]
        public void FromStops_SortsAndCollapsesRepeatedPositions()
        {
            var r = new PlusRamp();
            r.stops.Add(new PlusRampStop(0.5f, Color.blue));
            r.stops.Add(new PlusRampStop(0f, Color.red));
            r.stops.Add(new PlusRampStop(0.5f, Color.green));   // same position, later → wins
            var b = PlusBands.FromStops(r);
            Assert.That(b.Count, Is.EqualTo(2));
            Assert.That(b.stops[0].pos, Is.EqualTo(0f)); Assert.That(b.stops[0].color, Is.EqualTo(Color.red));
            Assert.That(b.stops[1].pos, Is.EqualTo(0.5f)); Assert.That(b.stops[1].color, Is.EqualTo(Color.green));
        }

        [Test]
        public void SetCount_GrowSplitsTheWidestBand_ShrinkRemovesTheNarrowest()
        {
            var b = PlusBands.FromStops(ArcBands.Violet().ToStops(), transparentBelowFirst: true);   // 0.045 0.13 0.30 0.58 0.92
            var before = new List<PlusRampStop>(b.stops.ConvertAll(s => new PlusRampStop(s.pos, s.color)));
            b.SetCount(7);
            Assert.That(b.Count, Is.EqualTo(7));
            // the widest band was 0.58..0.92 (0.34) → split at 0.75; then 0.30..0.58 (0.28) → split at 0.44
            Assert.That(b.Thresholds, Is.EqualTo(new[] { 0.045f, 0.13f, 0.30f, 0.44f, 0.58f, 0.75f, 0.92f }).Within(1e-6));
            foreach (var s in before) Assert.That(b.stops.Exists(x => x.pos == s.pos && x.color == s.color), "existing entries are kept");
            Assert.That(b.stops[3].color, Is.EqualTo(b.stops[2].color), "a new band copies the colour it split from");
            Assert.That(b.stops[5].color, Is.EqualTo(b.stops[4].color));
            // the picture is unchanged until a new band is recoloured
            var orig = ArcBands.Violet();
            for (int k = 0; k <= 200; k++) Assert.That(b.Eval(k / 200f), Is.EqualTo(orig.Eval(k / 200f)));

            b.SetCount(3);   // the narrowest bands go first (never band 0): 0.044 (0.0..), 0.13 (0.085 wide) …
            Assert.That(b.Count, Is.EqualTo(3));
            Assert.That(b.Thresholds[0], Is.EqualTo(0.045f), "band 0 survives every shrink");
            for (int i = 1; i < b.Count; i++) Assert.That(b.Thresholds[i], Is.GreaterThan(b.Thresholds[i - 1]), "still ascending");

            b.SetCount(1);
            Assert.That(b.Count, Is.EqualTo(1));
            b.SetCount(PlusBands.Max + 5);
            Assert.That(b.Count, Is.EqualTo(PlusBands.Max), "clamped to Max");

            var empty = new PlusBands();
            empty.SetCount(4);
            Assert.That(empty.Thresholds, Is.EqualTo(new[] { 0f, 0.25f, 0.5f, 0.75f }), "an empty table seeds even bands");
        }

        [Test]
        public void SetThreshold_StaysBetweenNeighbours_AndBand0IsLockedWithoutAFloor()
        {
            var t = PlusRampPresets.TorchBands("rim");
            t.SetThreshold(0, 0.3f);
            Assert.That(t.Thresholds[0], Is.EqualTo(0f), "no floor → band 0 starts at 0");
            t.SetThreshold(2, 0.9f);
            Assert.That(t.Thresholds[2], Is.LessThan(t.Thresholds[3]));
            Assert.That(t.Thresholds[2], Is.GreaterThan(t.Thresholds[1]));
            t.SetThreshold(2, 0f);
            Assert.That(t.Thresholds[2], Is.GreaterThan(t.Thresholds[1]));

            var a = ArcBands.Ion();
            a.SetThreshold(0, 0.1f);
            Assert.That(a.Thresholds[0], Is.EqualTo(0.1f), "a floor IS editable");
            a.SetThreshold(0, 0.5f);
            Assert.That(a.Thresholds[0], Is.LessThan(a.Thresholds[1]));
        }

        [Test]
        public void ContentHash_TracksEdits_AndCachedTablesFollow()
        {
            var a = PlusRampPresets.TorchBands("gold");
            int h0 = a.ContentHash();
            var thr0 = a.Thresholds;
            Assert.That(a.ContentHash(), Is.EqualTo(h0), "building the cached tables does not change the hash");
            Assert.That(ReferenceEquals(a.Thresholds, thr0), "tables are cached until an edit");
            a.SetColor(3, Color.black);
            Assert.That(a.ContentHash(), Is.Not.EqualTo(h0), "a colour edit changes the hash");
            Assert.That(a.Colors32[3], Is.EqualTo((Color32)Color.black), "and the tables rebuild");
            var c = a.Clone();
            Assert.That(c.ContentHash(), Is.EqualTo(a.ContentHash()));
            Assert.That(c.stops, Is.Not.SameAs(a.stops));
            c.SetThreshold(2, 0.3f);
            Assert.That(a.Thresholds[2], Is.Not.EqualTo(0.3f), "the clone owns its entries");
        }

        [Test]
        public void Forms_CloneDeepCopiesTheTable_AndTheFormHashFollowsABandEdit()
        {
            var torch = new TorchForm { variant = TorchForm.Variant.Barbs };
            var t2 = (TorchForm)torch.Clone();
            Assert.That(t2.barbs.ramp, Is.Not.SameAs(torch.barbs.ramp));
            Assert.That(t2.ContentHash(), Is.EqualTo(torch.ContentHash()));
            t2.barbs.ramp.SetColor(1, Color.cyan);
            Assert.That(t2.ContentHash(), Is.Not.EqualTo(torch.ContentHash()));
            Assert.That(torch.barbs.ramp.GetColor(1), Is.Not.EqualTo(Color.cyan));

            var arc = new ArcBurstForm();
            var a2 = (ArcBurstForm)arc.Clone();
            a2.palette.SetCount(7);
            Assert.That(arc.palette.Count, Is.EqualTo(5));
            Assert.That(a2.ContentHash(), Is.Not.EqualTo(arc.ContentHash()), "a count change re-keys the layer");
        }

        [Test]
        public void OldPlusRampShapedData_LoadsIntoPlusBands()
        {
            // A layer saved while the field was a PlusRamp: JsonUtility writes the same `stops` list, and the step-stop
            // encoding collapses on deserialize — no migration code in the forms.
            var old = PlusRampPresets.TorchHot();
            string json = JsonUtility.ToJson(old);
            var loaded = JsonUtility.FromJson<PlusBands>(json);
            Assert.That(loaded.Count, Is.EqualTo(7));
            Assert.That(loaded.ContentHash(), Is.EqualTo(PlusRampPresets.TorchBands("hot").ContentHash()));
        }
    }
}

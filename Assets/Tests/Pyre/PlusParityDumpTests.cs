using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Laubrary.Pyre;
using Laubrary.SpriteFx;
using Laubrary.Pyre.Editor.Parity;
using Laubrary.Pyre.Forms.Kiln;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Laubrary.Pyre.Tests
{
    public class PyreParityDumpTests
    {
        const string DetonateContract = @"D:\Claude@GDrive\Flame\GEN7\contract\agent3_fork_explosive\detonate";

        static string TempDir(string name)
        {
            string d = Path.Combine(Path.GetTempPath(), "pyreplus_parity_tests", name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        [Test]
        public void WriteNpy_ProducesAValidV1Header_AndFlipsRows()
        {
            int W = 5, H = 3;
            var plane = new float[W * H];
            for (int i = 0; i < plane.Length; i++) plane[i] = i;   // row 0 = 0..4, row 2 = 10..14
            string path = Path.Combine(TempDir("npy"), "0000_H.npy");
            PyreParityDump.WriteNpy(path, plane, W, H, flipY: true);
            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes[0], Is.EqualTo(0x93));
            Assert.That(Encoding.ASCII.GetString(bytes, 1, 5), Is.EqualTo("NUMPY"));
            Assert.That(bytes[6], Is.EqualTo(1)); Assert.That(bytes[7], Is.EqualTo(0));
            int hlen = bytes[8] | (bytes[9] << 8);
            Assert.That((10 + hlen) % 64, Is.EqualTo(0), "header padded to a 64-byte boundary");
            string header = Encoding.ASCII.GetString(bytes, 10, hlen);
            Assert.That(header, Does.Contain("'descr': '<f4'").And.Contain("'fortran_order': False").And.Contain("'shape': (3, 5)"));
            Assert.That(header.EndsWith("\n"));
            Assert.That(bytes.Length - 10 - hlen, Is.EqualTo(W * H * 4));
            float first = BitConverter.ToSingle(bytes, 10 + hlen);
            Assert.That(first, Is.EqualTo(10f), "the plane's TOP row (y = H-1) is written first");
            float last = BitConverter.ToSingle(bytes, bytes.Length - 4);
            Assert.That(last, Is.EqualTo(4f));
        }

        [Test]
        public void Dump_WritesFramesFieldsProbeAndMeta_ForAForkBlastSpec()
        {
            var spec = ScriptableObject.CreateInstance<Pyre>();
            try
            {
                spec.canvasSize = 24; spec.frameCount = 3; spec.seed = 5;
                spec.layers[0].form = new ForkBlastForm();
                spec.layers[0].alpha = new ZUIValue(1f);   // the default envelope is 0 at frame 0, where the form draws (and publishes) nothing
                string dir = TempDir("dump");
                int n = PyreParityDump.Dump(spec, dir, new PyreParityDump.DumpOptions { rampName = "EMBER" });
                Assert.That(n, Is.EqualTo(3));
                for (int f = 0; f < 3; f++)
                {
                    Assert.That(File.Exists(Path.Combine(dir, "frames", $"{f:0000}.png")));
                    Assert.That(File.Exists(Path.Combine(dir, "fields", $"{f:0000}_H.npy")), "ForkBlastForm publishes H");
                    Assert.That(File.Exists(Path.Combine(dir, "fields", $"{f:0000}_T.npy")), "ForkBlastForm publishes T");
                }
                var probe = JObject.Parse(File.ReadAllText(Path.Combine(dir, "ramp_probe.json")));
                Assert.That(probe.Value<string>("ramp"), Is.EqualTo("EMBER"));
                for (int i = 0; i <= 10; i++) Assert.That(probe[(i / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)], Is.Not.Null);
                var hot = (JArray)probe["1.0"]; var cold = (JArray)probe["0.0"];
                Assert.That(hot[0].Value<int>() + hot[1].Value<int>(), Is.GreaterThan(cold[0].Value<int>() + cold[1].Value<int>()), "t=1 is the hot end (default fill is hot-end-left, probe reverses it)");
                var meta = JObject.Parse(File.ReadAllText(Path.Combine(dir, "meta.json")));
                Assert.That(meta.Value<string>("form"), Is.EqualTo(typeof(ForkBlastForm).FullName));
                Assert.That(PyreFormDebug.FieldSink, Is.Null, "the sink is uninstalled after the dump");
                // a PNG decodes back to the canvas size with some lit pixels
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(Path.Combine(dir, "frames", "0001.png")));
                Assert.That(tex.width, Is.EqualTo(24)); Assert.That(tex.height, Is.EqualTo(24));
                int lit = 0; foreach (var c in tex.GetPixels32()) if (c.a > 0) lit++;
                Assert.That(lit, Is.GreaterThan(0));
                UnityEngine.Object.DestroyImmediate(tex);
                Directory.Delete(dir, true);
            }
            finally { ScriptableObject.DestroyImmediate(spec); }
        }

        [Test]
        public void Dump_LeavesTheNormalRenderPathUntouched()
        {
            // The field sink is only installed for the dump's duration; a render afterwards is byte-identical to
            // one before (the hook is a null check, nothing else).
            var spec = ScriptableObject.CreateInstance<Pyre>();
            try
            {
                spec.canvasSize = 20; spec.frameCount = 4; spec.seed = 3;
                spec.layers[0].form = new ForkBlastForm();
                var before = PyreRenderer.RenderFrame(spec, 2);
                string dir = TempDir("identity");
                PyreParityDump.Dump(spec, dir);
                var after = PyreRenderer.RenderFrame(spec, 2);
                for (int i = 0; i < before.Length; i++) Assert.That(after[i], Is.EqualTo(before[i]));
                Directory.Delete(dir, true);
            }
            finally { ScriptableObject.DestroyImmediate(spec); }
        }

        [Test]
        public void FromContract_BuildsASpecAndMapsSameNamedParams()
        {
            if (!Directory.Exists(DetonateContract)) Assert.Ignore("gen-7 detonate contract not present on this machine");
            var spec = PyreParityDump.FromContract(DetonateContract, typeof(ForkBlastForm), out var map);
            try
            {
                Assert.That(spec.Width, Is.EqualTo(184)); Assert.That(spec.frameCount, Is.EqualTo(30)); Assert.That(spec.seed, Is.EqualTo(101));
                var form = spec.layers[0].form as ForkBlastForm;
                Assert.That(form, Is.Not.Null);
                Assert.That(form.spread, Is.EqualTo(180f)); Assert.That(form.drag, Is.EqualTo(1.55f).Within(1e-5f));
                Assert.That(form.roundAt, Is.EqualTo(0.3f).Within(1e-5f), "snake_case round_at maps to roundAt");
                Assert.That(form.fieldHigh, Is.EqualTo(1.2f), "`hi` has no same-named field: stays at the form default and is listed as unmapped");
                Assert.That(map.unmapped, Does.Contain("hi"));
                Assert.That(map.mapped.Count, Is.GreaterThan(10));
                Assert.That(spec.layers[0].alpha.mode, Is.EqualTo(ZUIValue.Mode.Static));
                Assert.That(map.ToMeta()["unmapped"], Is.Not.Null);
            }
            finally { ScriptableObject.DestroyImmediate(spec); }
        }
    }
}

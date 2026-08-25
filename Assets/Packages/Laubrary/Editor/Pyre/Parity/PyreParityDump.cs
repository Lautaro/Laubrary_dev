// PyreParityDump — the Pyre half of the Kiln parity harness (editor-only, no window, no menu item).
//
// A Kiln agent ships a reference CONTRACT next to its published draw (`D:/CODEZ/Kiln/docs/contract_schema.md`):
// golden frames, pre-shade float planes, the ramp, the fully-resolved params. This class produces the matching
// PORT DUMP from a Pyre and hands both to Kiln's comparer (`parity_compare.py`), which scores them stage by
// stage (field, silhouette, exposure, ramp, pixel, temporal) and draws the contact sheet a human then looks at.
//
//   var spec = PyreParityDump.FromContract(contractDrawDir, typeof(ForkBlastForm), out var mapping);
//   PyreParityDump.Dump(spec, dumpDir, new PyreParityDump.DumpOptions { meta = mapping.ToMeta() });
//   var r = PyreParityDump.Compare(contractDrawDir, dumpDir);      // r.pass, r.reportDir, r.summary
//
// Conventions (checked against the gen-7 ForkBlast `detonate` contract): PNG/npy rows run TOP-DOWN; the renderer's
// buffers are y-up (row 0 = bottom) — Texture2D.SetPixels32 + EncodeToPNG already emit top-down, the npy writer
// flips explicitly. Ramp probe t: 0 = cold edge, 1 = hottest core — a layer Fill authored hot-end-left (Inferno /
// Fork Blast) is sampled at 1−t unless the form implements IPlusRampProbe. Float planes come from forms that
// implement IPlusFieldPublisher, through the PyreFormDebug.FieldSink the dump installs for its duration only.
// Never compares through the GIF exporter: every frame is PyreRenderer.RenderFrame straight to a lossless PNG.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Laubrary.SpriteFx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Laubrary.Pyre.Editor.Parity
{
    public static class PyreParityDump
    {
        public const string ComparerPath = "D:/CODEZ/Kiln/parity_compare.py";

        public sealed class DumpOptions
        {
            public bool fields = true;          // write fields/NNNN_<name>.npy for every plane a form publishes
            public bool rampProbe = true;       // write ramp_probe.json (11 samples, t = 0, 0.1 … 1)
            public int layerIndex = -1;         // which layer's fill/form answers the ramp probe; -1 = first layer with a form, else 0
            public JObject meta;                // extra content for meta.json (FromContract's mapping report goes here)
            public string rampName;             // `"ramp": name` key in ramp_probe.json — which contract ramp to compare against
            public int cropW, cropH;            // > 0: write the CENTRED cropW × cropH window of every frame and plane (a non-square
                                                // contract letterboxed on Pyre's square canvas — Energy Projectile's 192 × 96 orbs)
        }

        /// The centred crop window of a W × H buffer in y-UP rows (renderer convention): null when no crop applies.
        static T[] Crop<T>(T[] src, int W, int H, int cw, int ch)
        {
            if (cw <= 0 || ch <= 0 || (cw == W && ch == H)) return src;
            cw = Mathf.Min(cw, W); ch = Mathf.Min(ch, H);
            int x0 = (W - cw) / 2, y0 = H - (H - ch) / 2 - ch;   // the window's TOP row is (H−ch)/2 in y-down terms
            var dst = new T[cw * ch];
            for (int y = 0; y < ch; y++) Array.Copy(src, (y0 + y) * W + x0, dst, y * cw, cw);
            return dst;
        }

        // ── dump ────────────────────────────────────────────────────────────────────────────────────────────

        /// Render every frame of `spec` into `outDir` in the port-dump layout. Returns the frame count written.
        public static int Dump(Pyre spec, string outDir, DumpOptions opt = null)
        {
            opt ??= new DumpOptions();
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            Directory.CreateDirectory(outDir);
            string framesDir = Path.Combine(outDir, "frames");
            Directory.CreateDirectory(framesDir);
            string fieldsDir = Path.Combine(outDir, "fields");
            if (opt.fields) Directory.CreateDirectory(fieldsDir);

            int W = spec.Width, H = spec.Height, frames = Mathf.Max(1, spec.frameCount);
            int outW = opt.cropW > 0 ? Mathf.Min(opt.cropW, W) : W, outH = opt.cropH > 0 ? Mathf.Min(opt.cropH, H) : H;
            var tex = new Texture2D(outW, outH, TextureFormat.RGBA32, false, false);
            var published = new List<(string name, float[] plane)>();
            var planeNames = new SortedSet<string>();
            Action<string, float[]> sink = (name, plane) => published.Add((name, plane));
            var prev = PyreFormDebug.FieldSink;
            try
            {
                if (opt.fields) PyreFormDebug.FieldSink = sink;
                for (int f = 0; f < frames; f++)
                {
                    published.Clear();
                    var buf = Crop(PyreRenderer.RenderFrame(spec, f), W, H, outW, outH);
                    tex.SetPixels32(buf);   // row 0 = bottom; EncodeToPNG writes the top row first ⇒ the PNG is y-down like the contract's
                    File.WriteAllBytes(Path.Combine(framesDir, $"{f:0000}.png"), ImageConversion.EncodeToPNG(tex));
                    if (opt.fields)
                        foreach (var (name, plane) in published)
                        {
                            if (plane == null || plane.Length != W * H) continue;
                            planeNames.Add(name);
                            WriteNpy(Path.Combine(fieldsDir, $"{f:0000}_{name}.npy"), Crop(plane, W, H, outW, outH), outW, outH, flipY: true);
                        }
                }
            }
            finally
            {
                PyreFormDebug.FieldSink = prev;
                UnityEngine.Object.DestroyImmediate(tex);
            }

            // Ramp probe from the chosen layer (contract convention: t = 0 cold … 1 hot).
            var layer = PickLayer(spec, opt.layerIndex);
            if (opt.rampProbe && layer != null)
            {
                var probe = new JObject();
                if (!string.IsNullOrEmpty(opt.rampName)) probe["ramp"] = opt.rampName;
                for (int i = 0; i <= 10; i++)
                {
                    float t = i / 10f;
                    Color c = ProbeRamp(layer, t);
                    probe[t.ToString("0.0", CultureInfo.InvariantCulture)] = new JArray(
                        Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                        Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f), Math.Round(Mathf.Clamp01(c.a), 5));
                }
                File.WriteAllText(Path.Combine(outDir, "ramp_probe.json"), probe.ToString(), new UTF8Encoding(false));
            }

            var meta = opt.meta != null ? (JObject)opt.meta.DeepClone() : new JObject();
            meta["renderer"] = "PyreRenderer.RenderFrame";
            meta["spec"] = new JObject { ["name"] = spec.name, ["w"] = W, ["h"] = H, ["frames"] = frames, ["seed"] = spec.seed, ["layers"] = spec.layers?.Count ?? 0 };
            if (outW != W || outH != H) meta["crop"] = new JObject { ["w"] = outW, ["h"] = outH, ["note"] = "centred window of the square canvas (y-down top row = (H - h) / 2)" };
            meta["form"] = layer?.form != null ? layer.form.GetType().FullName : (layer != null ? "enum:" + layer.shapeForm : null);
            meta["planes"] = new JArray(planeNames);
            meta["written_at"] = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(outDir, "meta.json"), meta.ToString(), new UTF8Encoding(false));
            return frames;
        }

        static PyreLayer PickLayer(Pyre spec, int index)
        {
            if (spec.layers == null || spec.layers.Count == 0) return null;
            if (index >= 0 && index < spec.layers.Count) return spec.layers[index];
            foreach (var l in spec.layers) if (l != null && l.form != null) return l;
            return spec.layers[0];
        }

        /// The layer's colour at contract ramp position t (0 cold … 1 hot). A form answers itself when it can;
        /// otherwise the Fill is read at 1−t because Pyre's heat-ramp forms read their Fill hot-end-left.
        public static Color ProbeRamp(PyreLayer layer, float t)
        {
            if (layer.form is IPlusRampProbe p) return p.ProbeRamp(t);
            return layer.shapeFill != null ? layer.shapeFill.Evaluate(1f - t, 0.5f, 0.5f) : Color.white;
        }

        /// Standard .npy v1.0, float32 little-endian, C-order, shape (H, W). `flipY` writes the plane's last row first
        /// (renderer buffers are y-up; the contract's planes are y-down like its PNGs).
        public static void WriteNpy(string path, float[] plane, int W, int H, bool flipY)
        {
            string dict = $"{{'descr': '<f4', 'fortran_order': False, 'shape': ({H}, {W}), }}";
            int preamble = 6 + 2 + 2;                                    // magic + version + header length
            int pad = 64 - (preamble + dict.Length + 1) % 64; if (pad == 64) pad = 0;
            string header = dict + new string(' ', pad) + "\n";
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            bw.Write((byte)0x93); bw.Write(Encoding.ASCII.GetBytes("NUMPY"));
            bw.Write((byte)1); bw.Write((byte)0);
            bw.Write((ushort)header.Length);
            bw.Write(Encoding.ASCII.GetBytes(header));
            var row = new byte[W * 4];
            for (int r = 0; r < H; r++)
            {
                int y = flipY ? H - 1 - r : r;
                Buffer.BlockCopy(plane, y * W * 4, row, 0, W * 4);
                if (!BitConverter.IsLittleEndian) Array.Reverse(row);   // never the case on the editor platforms, kept honest
                bw.Write(row);
            }
        }

        // ── from contract ───────────────────────────────────────────────────────────────────────────────────

        /// What FromContract could and could not map; `ToMeta()` is what goes into the dump's meta.json.
        public sealed class Mapping
        {
            public string contractDir, formType;
            public int w, h, frames, seed;
            public readonly List<string> mapped = new List<string>();      // "name -> field = value"
            public readonly List<string> unmapped = new List<string>();    // params with no same-named field
            public readonly List<string> notes = new List<string>();

            public JObject ToMeta() => new JObject
            {
                ["contract_dir"] = contractDir, ["form_type"] = formType,
                ["mapped"] = new JArray(mapped), ["unmapped"] = new JArray(unmapped), ["notes"] = new JArray(notes),
            };
        }

        /// Build a spec from a contract draw folder's params.json: canvas (w, h — square canvas: max of the two, noted
        /// when they differ), frames, seed, ONE layer carrying a fresh `formType` with its alpha envelope held at 1
        /// (the contract has no such envelope), swarm off, then set every form field that shares a param's name
        /// (snake_case ⇄ camelCase, case-insensitive; float / int / bool / ZUIValue-as-static). Everything else is
        /// listed in `mapping.unmapped` — a repeatable baseline, not a claim of completeness.
        public static Pyre FromContract(string contractDrawDir, Type formType, out Mapping mapping)
        {
            if (!typeof(PyreForm).IsAssignableFrom(formType)) throw new ArgumentException($"{formType} is not a PyreForm");
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(contractDrawDir, "params.json")));
            mapping = new Mapping { contractDir = contractDrawDir.Replace('\\', '/'), formType = formType.FullName };
            mapping.w = doc.Value<int?>("w") ?? 64; mapping.h = doc.Value<int?>("h") ?? mapping.w;
            mapping.frames = doc.Value<int?>("frames") ?? 16; mapping.seed = doc.Value<int?>("seed") ?? 0;

            var spec = ScriptableObject.CreateInstance<Pyre>();
            spec.name = "Parity " + new DirectoryInfo(contractDrawDir).Name;
            spec.canvasSize = Mathf.Max(mapping.w, mapping.h);
            if (mapping.w != mapping.h) mapping.notes.Add($"contract canvas {mapping.w}x{mapping.h} is not square; Pyre renders {spec.canvasSize}x{spec.canvasSize}");
            spec.frameCount = mapping.frames;
            spec.seed = mapping.seed;
            spec.background = new Color(0, 0, 0, 0);
            spec.backgroundUseFill = false;

            var form = (PyreForm)Activator.CreateInstance(formType);
            var layer = new PyreLayer { name = form.DisplayName, form = form, swarmEnabled = false, matteEnabled = false };
            layer.alpha = new ZUIValue(1f);
            mapping.notes.Add("layer alpha envelope forced to Static 1 (the contract has no layer-level envelope)");
            spec.layers = new List<PyreLayer> { layer };

            var fields = formType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var byKey = new Dictionary<string, FieldInfo>();
            foreach (var f in fields) byKey[Key(f.Name)] = f;

            var pars = doc["params"] as JObject;
            if (pars != null)
                foreach (var kv in pars)
                {
                    if (kv.Key is "w" or "h" or "frames" or "fps" or "seed") continue;
                    var entry = kv.Value as JObject;
                    JToken val = entry != null && entry.ContainsKey("value") ? entry["value"] : kv.Value;
                    if (!byKey.TryGetValue(Key(kv.Key), out var f)) { mapping.unmapped.Add(kv.Key); continue; }
                    if (TrySet(form, f, val, out string shown)) mapping.mapped.Add($"{kv.Key} -> {f.Name} = {shown}");
                    else mapping.unmapped.Add($"{kv.Key} (field {f.Name}: {f.FieldType.Name} cannot take {val?.Type})");
                }
            return spec;
        }

        static string Key(string name) => name.Replace("_", "").ToLowerInvariant();

        static bool TrySet(object owner, FieldInfo f, JToken val, out string shown)
        {
            shown = null;
            if (val == null || val.Type == JTokenType.Null) return false;
            var t = f.FieldType;
            try
            {
                if (t == typeof(float) && IsNumber(val)) { float v = val.Value<float>(); f.SetValue(owner, v); shown = v.ToString(CultureInfo.InvariantCulture); return true; }
                if (t == typeof(int) && IsNumber(val)) { int v = Mathf.RoundToInt(val.Value<float>()); f.SetValue(owner, v); shown = v.ToString(); return true; }
                if (t == typeof(bool) && (val.Type == JTokenType.Boolean || IsNumber(val))) { bool v = val.Type == JTokenType.Boolean ? val.Value<bool>() : val.Value<float>() != 0f; f.SetValue(owner, v); shown = v.ToString(); return true; }
                if (t == typeof(ZUIValue) && IsNumber(val)) { float v = val.Value<float>(); f.SetValue(owner, new ZUIValue(v)); shown = "static " + v.ToString(CultureInfo.InvariantCulture); return true; }
            }
            catch (Exception e) { shown = e.Message; }
            return false;
        }

        static bool IsNumber(JToken t) => t.Type == JTokenType.Float || t.Type == JTokenType.Integer;

        // ── compare ─────────────────────────────────────────────────────────────────────────────────────────

        public sealed class CompareResult
        {
            public bool pass;
            public int exitCode;
            public string reportDir, reportJson, reportMd, contactSheet, stdout, stderr;
            public string summary;       // the comparer's one-line verdict (PASS/FAIL + per-stage status)
            public JObject report;       // parsed report.json (null when the comparer did not produce one)
        }

        /// Shell out to Kiln's comparer. `reportDir` defaults to <dumpDir>/parity_report. `python` must be on PATH.
        public static CompareResult Compare(string contractDrawDir, string dumpDir, string reportDir = null, string python = "python")
        {
            reportDir ??= Path.Combine(dumpDir, "parity_report");
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{ComparerPath}\" \"{contractDrawDir}\" \"{dumpDir}\" --out \"{reportDir}\"",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            var res = new CompareResult { reportDir = reportDir };
            using (var p = Process.Start(psi))
            {
                res.stdout = p.StandardOutput.ReadToEnd();
                res.stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                res.exitCode = p.ExitCode;
            }
            res.reportJson = Path.Combine(reportDir, "report.json");
            res.reportMd = Path.Combine(reportDir, "report.md");
            res.contactSheet = Path.Combine(reportDir, "contact_sheet.png");
            if (File.Exists(res.reportJson))
            {
                res.report = JObject.Parse(File.ReadAllText(res.reportJson));
                res.pass = res.report.Value<bool?>("pass") ?? false;
            }
            else res.pass = false;
            var firstLine = (res.stdout ?? "").Split('\n');
            res.summary = firstLine.Length > 0 ? firstLine[0].Trim() : "";
            return res;
        }
    }
}

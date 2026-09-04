// TEMP PROBE T-0235 — PM deletes after running.
//
// Renders the Orb at defaults, one column per variant, plus Plasma Bloom, as ONE contact-sheet PNG, and prints
// a per-variant FNV-1a hash of the raw pixels. The point is that T-0235 changed captions only: run this on the
// commit before T-0235 and on the commit after, and every hash must match.
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Laubrary.Pyre;
using Laubrary.Pyre.Forms.Kiln;
using UnityEngine;

namespace Laubrary.PyreShaper.Editor
{
    public static class T0235_OrbDialProbe
    {
        const int Size = 96, Frames = 8, Cols = 4;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            var sheets = new System.Collections.Generic.List<(string label, Color32[] px, int w, int h)>();

            foreach (Enum v in Enum.GetValues(typeof(OrbForm.Variant)))
            {
                var form = new OrbForm { variant = (OrbForm.Variant)v };
                var strip = RenderStrip(form, out int w, out int h);
                sheets.Add(("Orb/" + v, strip, w, h));
                sb.AppendLine($"Orb/{v,-12} hash {Hash(strip):X8}");
            }

            var bloom = new PlasmaBloomForm();
            var bs = RenderStrip(bloom, out int bw, out int bh);
            sheets.Add(("PlasmaBloom", bs, bw, bh));
            sb.AppendLine($"PlasmaBloom{"",-11} hash {Hash(bs):X8}");

            string path = Path.Combine(Path.GetTempPath(), "T0235-orb-contact-sheet.png");
            WriteSheet(sheets, path);
            sb.AppendLine("sheet: " + path);

            sb.AppendLine();
            sb.AppendLine(CaptionReport(typeof(OrbForm)));
            sb.AppendLine(CaptionReport(typeof(OrbForm.EmberdriftSettings)));
            sb.AppendLine(CaptionReport(typeof(PlasmaPopulation)));
            return sb.ToString();
        }

        /// One horizontal strip: `Frames` frames of the form's own life, left to right.
        static Color32[] RenderStrip(PyreForm form, out int w, out int h)
        {
            w = Size * Frames; h = Size;
            var strip = new Color32[w * h];
            var frame = new Color32[Size * Size];
            for (int f = 0; f < Frames; f++)
            {
                Array.Clear(frame, 0, frame.Length);
                float life = Frames > 1 ? f / (float)(Frames - 1) : 0f;
                var ctx = new PyreFormCtx(Size, Size, life, 2101, 0, null, 1f, null, null, null, 0f, f, Frames);
                form.Prepare(new PyreFormPrepareCtx(life, 2101, 0, (val, id) => val?.staticValue ?? 0f));
                form.Render(ctx, frame);
                for (int y = 0; y < Size; y++)
                    Array.Copy(frame, y * Size, strip, y * w + f * Size, Size);
            }
            return strip;
        }

        static uint Hash(Color32[] px)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (var c in px)
                {
                    h = (h ^ c.r) * 16777619u; h = (h ^ c.g) * 16777619u;
                    h = (h ^ c.b) * 16777619u; h = (h ^ c.a) * 16777619u;
                }
                return h;
            }
        }

        static void WriteSheet(System.Collections.Generic.List<(string label, Color32[] px, int w, int h)> rows, string path)
        {
            int w = 0, h = 0;
            foreach (var r in rows) { w = Mathf.Max(w, r.w); h += r.h; }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var all = new Color32[w * h];
            int yOff = 0;
            for (int i = rows.Count - 1; i >= 0; i--)   // bottom-up: row 0 ends on top
            {
                var r = rows[i];
                for (int y = 0; y < r.h; y++) Array.Copy(r.px, y * r.w, all, (yOff + y) * w, r.w);
                yOff += r.h;
            }
            tex.SetPixels32(all);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// Old caption → new caption → group, straight off the attributes, so the map document can be checked
        /// against what the card will actually draw rather than against what the map claims.
        static string CaptionReport(Type t)
        {
            var sb = new StringBuilder("── " + t.Name + " ──\n");
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsNotSerialized) continue;
                var lab = (ZUILabelAttribute)Attribute.GetCustomAttribute(f, typeof(ZUILabelAttribute));
                var grp = (ZUIGroupAttribute)Attribute.GetCustomAttribute(f, typeof(ZUIGroupAttribute));
                sb.AppendLine($"  {f.Name,-22} {UnityEditor.ObjectNames.NicifyVariableName(f.Name),-24} → "
                    + $"{lab?.Label ?? "(unchanged)",-26} [{grp?.Group ?? "-"}{(grp != null && grp.Advanced ? ", advanced" : "")}]");
            }
            return sb.ToString();
        }
    }
}

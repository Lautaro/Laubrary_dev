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
            sb.AppendLine(RampAdjustCheck());

            sb.AppendLine();
            sb.AppendLine(CaptionReport(typeof(OrbForm)));
            sb.AppendLine(CaptionReport(typeof(OrbForm.EmberdriftSettings)));
            sb.AppendLine(CaptionReport(typeof(PlasmaPopulation)));
            return sb.ToString();
        }

        /// Open a tool on an Orb-bearing asset and photograph its window. Read-only: the asset is selected and
        /// shown, never written. Call twice — the first call opens and focuses, the second photographs a window
        /// that has actually painted.
        public static string Shot(string menuItem, string assetPath, string windowTitle, string file, float scrollY = 0f)
        {
            var asset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null) return $"{assetPath}: NOT FOUND";
            UnityEditor.Selection.activeObject = asset;
            UnityEditor.EditorApplication.ExecuteMenuItem(menuItem);

            UnityEditor.EditorWindow win = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
                if (w != null && w.titleContent != null && w.titleContent.text == windowTitle) { win = w; break; }
            if (win == null) return $"{windowTitle}: window not found";
            // Both tools are ZuiAssetWindow<T>s, which open on their browser until told which asset to edit.
            // Selecting it in the Project view is not that instruction — SetAsset is.
            for (Type wt = win.GetType(); wt != null; wt = wt.BaseType)
            {
                var m = wt.GetMethod("SetAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                                 | BindingFlags.DeclaredOnly);
                if (m == null || m.GetParameters().Length != 1) continue;
                if (!m.GetParameters()[0].ParameterType.IsInstanceOfType(asset)) continue;
                m.Invoke(win, new object[] { asset });
                break;
            }
            if (scrollY > 0f && win.rootVisualElement != null)
                foreach (var sv in UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.ScrollView>(win.rootVisualElement).ToList())
                    sv.scrollOffset = new Vector2(sv.scrollOffset.x, scrollY);

            win.Focus();
            win.Repaint();

            var r = win.position;
            int w2 = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, 4096);
            int h2 = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, 4096);
            var px = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(new Vector2(r.x, r.y), w2, h2);
            if (px == null || px.Length != w2 * h2) return $"{windowTitle}: screen read failed";

            var tex = new Texture2D(w2, h2, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            string path = Path.Combine(Path.GetTempPath(), file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return $"{windowTitle}: {path} ({w2}x{h2}) selected={asset.name}";
        }

        /// OrbForm caches a baked LUT keyed on its ramp's identity. Turning an Adjust knob must repaint, and
        /// turning it back must land on exactly the bytes it started from — a cache that only notices the stops
        /// fails the first half, and one that re-bakes unconditionally would still have to pass the second.
        static string RampAdjustCheck()
        {
            var form = new OrbForm();
            uint before = Hash(RenderStrip(form, out _, out _));

            var adj = form.emberdrift.ramp.adjust;
            float saved = adj.hueShift;
            adj.hueShift = 0.5f;   // = +90°; the knob is -1..1 where ±1 is ±180°, so a whole number is exactly identity
            uint turned = Hash(RenderStrip(form, out _, out _));

            adj.hueShift = saved;
            uint restored = Hash(RenderStrip(form, out _, out _));

            return "── ramp Adjust / LUT cache ──\n"
                 + $"  default {before:X8} · hueShift +90° {turned:X8} · back {restored:X8}\n"
                 + $"  knob repaints:  {(turned != before ? "YES" : "NO — cache did not invalidate")}\n"
                 + $"  restores exact: {(restored == before ? "YES" : "NO")}";
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
                // Built by reflection, not by `new`: two of the constructor's parameters are SpriteFx modifier
                // arrays, and naming them — even to pass null — would drag a SpriteFx reference into this
                // assembly for a throwaway probe. There is exactly one constructor, so the binding is
                // unambiguous with nulls.
                var ctx = (PyreFormCtx)Activator.CreateInstance(typeof(PyreFormCtx), new object[]
                {
                    Size, Size, life, 2101, 0, null, 1f, null, null, null, 0f, f, Frames, null,
                });
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

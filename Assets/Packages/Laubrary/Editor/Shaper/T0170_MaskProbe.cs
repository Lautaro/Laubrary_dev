// TEMP PROBE T-0170 — PM deletes after running
//
// Exercises the consumer-side cross-layer mask (ShaperLayer.mask): a disc cut by an offset ellipse renders as
// a crescent, the invert case renders the complementary lens, a mask naming a layer that does not exist renders
// the disc untouched instead of throwing, and a mask source with contributesToPicture off never paints itself.
// Renders each case twice and reports whether the two renders are bit-identical (BC-1.3). PNGs are written to
// the task workspace. No [MenuItem] — invoked once via the Unity CLI's `command eval_file`, per
// PROGRAMME_RULES.md's code-only posture.
using System;
using System.IO;
using System.Text;
using Laubrary.Shaper;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class T0170_MaskProbe
    {
        const string OutDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0170";

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0170 cross-layer mask probe");

            ShaperDocument doc = null;
            try
            {
                doc = Build(out ShaperLayer disc, out ShaperLayer cutter);
                sb.AppendLine("Document " + doc.canvasWidth + "x" + doc.canvasHeight
                              + " -- layer 0 \"" + cutter.name + "\" (mask source, id " + cutter.id + "), "
                              + "layer 1 \"" + disc.name + "\" (masked).");

                // ── baseline: the mask off entirely ─────────────────────────────────────────────────────
                int savedId = disc.mask.sourceLayerId;
                disc.mask.sourceLayerId = 0;
                var baseline = Render(doc, sb, "baseline (no mask)");
                disc.mask.sourceLayerId = savedId;

                // ── the crescent: Subtract the offset ellipse's coverage ────────────────────────────────
                disc.mask.mode = ShaperMaskMode.Subtract;
                disc.mask.invert = false;
                var crescent = Render(doc, sb, "crescent (Subtract)");

                // ── invert: the same mask read backwards, which keeps exactly the overlap ───────────────
                disc.mask.invert = true;
                var lens = Render(doc, sb, "invert (Subtract, inverted)");
                disc.mask.invert = false;

                // ── the mask source drawn as well, to show contributesToPicture actually withholds it ───
                cutter.contributesToPicture = true;
                var withCutter = Render(doc, sb, "mask source visible");
                cutter.contributesToPicture = false;

                // ── missing source: an id no layer carries ──────────────────────────────────────────────
                disc.mask.sourceLayerId = 987654;
                Color32[] missing = null;
                try { missing = Render(doc, sb, "missing source"); }
                catch (Exception e) { sb.AppendLine("FAIL: missing-source render THREW: " + e); }
                disc.mask.sourceLayerId = savedId;

                // ── the claims ──────────────────────────────────────────────────────────────────────────
                float aBase = AlphaSum(baseline), aCres = AlphaSum(crescent), aLens = AlphaSum(lens);
                int nBase = Covered(baseline), nCres = Covered(crescent), nLens = Covered(lens);

                sb.AppendLine("Covered px  -- baseline " + nBase + ", crescent " + nCres + ", lens " + nLens
                              + ", with source visible " + Covered(withCutter) + ".");
                sb.AppendLine("Alpha sum   -- baseline " + aBase.ToString("F2")
                              + ", crescent " + aCres.ToString("F2") + ", lens " + aLens.ToString("F2")
                              + ", crescent+lens " + (aCres + aLens).ToString("F2") + ".");

                Claim(sb, "the mask removes coverage", nCres > 0 && nCres < nBase,
                      "crescent " + nCres + " vs baseline " + nBase);
                // Subtract and its inverse partition the layer exactly: a·(1−m) + a·m = a, per sample. A
                // near-exact match is therefore a real check on the arithmetic, not a coincidence of areas.
                Claim(sb, "Subtract and its invert partition the layer",
                      Mathf.Abs((aCres + aLens) - aBase) <= aBase * 0.01f + 1f,
                      "|(" + (aCres + aLens).ToString("F2") + ") - " + aBase.ToString("F2") + "|");
                Claim(sb, "contributesToPicture withholds the mask source",
                      Covered(withCutter) > nBase, "visible " + Covered(withCutter) + " vs hidden " + nBase);
                Claim(sb, "a missing source renders the layer unmasked, without throwing",
                      missing != null && Identical(missing, baseline),
                      missing == null ? "threw" : "differs from baseline in "
                                                  + Differences(missing, baseline) + " px");

                Write(sb, "mask-baseline.png", baseline, doc);
                Write(sb, "mask-crescent.png", crescent, doc);
                Write(sb, "mask-invert.png", lens, doc);
                Write(sb, "mask-source-visible.png", withCutter, doc);
                if (missing != null) Write(sb, "mask-missing-source.png", missing, doc);
            }
            catch (Exception e)
            {
                sb.AppendLine("FAIL: probe threw:");
                sb.AppendLine(e.ToString());
            }
            finally
            {
                if (doc != null) UnityEngine.Object.DestroyImmediate(doc);
            }
            return sb.ToString();
        }

        /// Layer 0 is the cutter and layer 1 is the disc, so the masked layer is ABOVE its source — the case
        /// a producer-side matte could express. The reverse order works identically and is why the reference
        /// lives on the consumer; nothing in the renderer requires the source to come first.
        static ShaperDocument Build(out ShaperLayer disc, out ShaperLayer cutter)
        {
            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            doc.canvasWidth = 96;
            doc.canvasHeight = 96;
            doc.frameCount = 1;

            cutter = new ShaperLayer
            {
                name = "Cutter",
                root = ShaperNode.Primitive(new ShaperPrimitiveDef
                {
                    kind = ShaperPrimitiveKind.Ellipse,
                    ellipseRxDial = new ZUIValue(26f),
                    ellipseRyDial = new ZUIValue(26f),
                }, "Cutter"),
                contributesToPicture = false,
            };
            cutter.root.transform.translate = new Vector2(20f, 8f);

            disc = new ShaperLayer
            {
                name = "Disc",
                root = ShaperNode.Primitive(new ShaperPrimitiveDef
                {
                    kind = ShaperPrimitiveKind.Ellipse,
                    ellipseRxDial = new ZUIValue(28f),
                    ellipseRyDial = new ZUIValue(28f),
                }, "Disc"),
            };

            doc.layers.Add(cutter);
            doc.layers.Add(disc);

            // IdOf allocates the source's stable id, exactly as the window's picker does.
            disc.mask.sourceLayerId = doc.IdOf(cutter);
            disc.mask.quantity = ShaperMaskQuantity.Coverage;
            return doc;
        }

        /// Render frame 0 twice and report determinism; returns the first render.
        static Color32[] Render(ShaperDocument doc, StringBuilder sb, string label)
        {
            var a = ShaperDocumentRenderer.RenderFrame(doc, 0);
            var b = ShaperDocumentRenderer.RenderFrame(doc, 0);
            int diff = Differences(a, b);
            sb.AppendLine("Determinism [" + label + "]: " + (diff == 0 ? "PASS" : "FAIL -- " + diff + " px differ")
                          + " (" + (a == null ? 0 : a.Length) + " px).");
            return a;
        }

        static int Covered(Color32[] px)
        {
            int n = 0;
            if (px != null) for (int i = 0; i < px.Length; i++) if (px[i].a > 0) n++;
            return n;
        }

        static float AlphaSum(Color32[] px)
        {
            float s = 0f;
            if (px != null) for (int i = 0; i < px.Length; i++) s += px[i].a / 255f;
            return s;
        }

        static int Differences(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return int.MaxValue;
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) n++;
            return n;
        }

        static bool Identical(Color32[] a, Color32[] b) => Differences(a, b) == 0;

        static void Claim(StringBuilder sb, string what, bool ok, string detail)
            => sb.AppendLine((ok ? "PASS: " : "FAIL: ") + what + "  [" + detail + "]");

        static void Write(StringBuilder sb, string file, Color32[] px, ShaperDocument doc)
        {
            if (px == null) return;
            try
            {
                Directory.CreateDirectory(OutDir);
                var tex = new Texture2D(doc.canvasWidth, doc.canvasHeight, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
                tex.SetPixels32(px);
                tex.Apply(false);
                byte[] png = ImageConversion.EncodeToPNG(tex);
                File.WriteAllBytes(Path.Combine(OutDir, file), png);
                UnityEngine.Object.DestroyImmediate(tex);
                sb.AppendLine("Wrote " + Path.Combine(OutDir, file) + " (" + png.Length + " bytes).");
            }
            catch (Exception e)
            {
                sb.AppendLine("FAIL: PNG write threw for " + file + ": " + e);
            }
        }
    }
}

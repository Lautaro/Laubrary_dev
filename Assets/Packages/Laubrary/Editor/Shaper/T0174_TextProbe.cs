// TEMP PROBE T-0174 — PM deletes after running
//
// Builds two one-layer ShaperDocuments whose root node is a ShaperPrimitiveKind.Text primitive spelling "BOOM":
//   1. an Outward border + a Dome height stage + a Point light, on a flat fill — the "everything downstream
//      applies to text for free" claim, stated as a picture;
//   2. the same shape under a Linear Gradient fill, which is Pyre's PerCharGradient (a gradient swept in SPACE
//      across the line, Pyre.cs:404-410) with no text-specific code at all.
// Renders #1 twice and checks the two renders are bit-identical (BC-1.3), reports the per-glyph index channel's
// occupancy, and writes both PNGs under the task's workspace folder. No [MenuItem] — invoked once via the Unity
// CLI's `command eval_file`, per PROGRAMME_RULES.md's code-only posture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Laubrary.Shaper;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class T0174_TextProbe
    {
        const string OutDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0174";

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0174 Text-shape probe");

            // ── the font ────────────────────────────────────────────────────────────────────────────────
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            string fontSource = "TMP_Settings.defaultFontAsset";
            if (font == null)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var candidate = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (candidate != null && candidate.characterLookupTable != null && candidate.characterLookupTable.Count > 0)
                    {
                        font = candidate; fontSource = "AssetDatabase: " + path; break;
                    }
                }
            }
            if (font == null)
            {
                sb.AppendLine("FAIL: no TMP_FontAsset available (no TMP default, none found by AssetDatabase).");
                return sb.ToString();
            }
            sb.AppendLine("Font: " + font.name + "  (" + fontSource + "), atlas " +
                          (font.atlasTexture != null ? font.atlasTexture.width + "x" + font.atlasTexture.height +
                                                       " " + font.atlasTexture.format : "none") +
                          ", padding " + font.atlasPadding + ", glyphs " +
                          (font.characterLookupTable != null ? font.characterLookupTable.Count : 0));

            // ── the raster itself, before any document wraps it ─────────────────────────────────────────
            var lit = MakeTextDef(font);
            ShaperCompiledTextField raster = ShaperTextPrepassCache.Get(
                lit, lit.textSize, lit.textLetterSpacing, lit.textLineSpacing, lit.textWeight, lit.textAlign);
            if (raster == null)
            {
                sb.AppendLine("FAIL: ShaperTextPrepassCache.Get returned null for \"BOOM\" — no glyph laid out.");
                return sb.ToString();
            }

            int insideTexels = raster.distance.Count(d => d < 0f);
            float dMin = raster.distance.Min(), dMax = raster.distance.Max();
            var glyphsSeen = new HashSet<int>();
            foreach (int g in raster.glyphIndex) if (g >= 0) glyphsSeen.Add(g);

            sb.AppendLine("Raster: " + raster.width + "x" + raster.height + " texels, half-extent " +
                          raster.halfExtentX.ToString("F1") + "x" + raster.halfExtentY.ToString("F1") +
                          " canvas px, glyphs " + raster.glyphCount + ", lines " + raster.lineCount);
            sb.AppendLine("Signed distance: " + insideTexels + " texels inside, range " +
                          dMin.ToString("F2") + " .. " + dMax.ToString("F2") + " canvas px.");
            sb.AppendLine("Per-glyph index channel: " + glyphsSeen.Count + " of " + raster.glyphCount +
                          " glyph ids present in the raster" +
                          (glyphsSeen.Count == raster.glyphCount && raster.glyphCount == 4
                              ? " (PASS -- every letter of BOOM is individually addressable)."
                              : " (check -- expected 4 for \"BOOM\")."));

            // The op the compiler emits, so the fork around ShaperPrimitives.Bake is verified rather than assumed.
            var probeNode = ShaperNode.Primitive(MakeTextDef(font), "TextProbe");
            ShaperProgram program = ShaperCompiler.Compile(probeNode, 0f, 0u);
            bool emittedTextOp = program.ops.Any(o => o.kind == ShaperOpKind.TextSample);
            sb.AppendLine("Compiled program: " + program.ops.Length + " ops, textFields " + program.textFields.Length +
                          ", TextSample op emitted: " + (emittedTextOp ? "yes" : "NO -- the compiler fork did not fire"));

            // ── document 1: border + Dome height + Point light ──────────────────────────────────────────
            var doc = BuildDocument(font, gradientFill: false);
            Color32[] px1 = null, px2 = null;
            string err = null;
            try
            {
                px1 = ShaperDocumentRenderer.RenderFrame(doc, 0);
                px2 = ShaperDocumentRenderer.RenderFrame(doc, 0);
            }
            catch (Exception e) { err = e.ToString(); }

            if (err != null)
            {
                sb.AppendLine("FAIL: render threw:");
                sb.AppendLine(err);
                UnityEngine.Object.DestroyImmediate(doc);
                return sb.ToString();
            }
            if (px1 == null || px1.Length != doc.canvasWidth * doc.canvasHeight)
            {
                sb.AppendLine("FAIL: RenderFrame returned null or a wrong-length pixel array.");
                UnityEngine.Object.DestroyImmediate(doc);
                return sb.ToString();
            }

            int mismatches = 0;
            for (int i = 0; i < px1.Length; i++) if (!px1[i].Equals(px2[i])) mismatches++;
            int opaque = px1.Count(c => c.a > 0);

            sb.AppendLine("Canvas " + doc.canvasWidth + "x" + doc.canvasHeight + " -- covered (a>0): " + opaque + " px.");
            sb.AppendLine(mismatches == 0
                ? "Determinism: PASS -- two renders of phase 0 bit-identical (0/" + px1.Length + " mismatches)."
                : "Determinism: FAIL -- " + mismatches + "/" + px1.Length + " pixels differ between two renders.");
            if (opaque == 0) sb.AppendLine("WARNING: zero covered pixels -- the text shape produced no coverage at all.");

            // Lighting actually varying across the Dome is what tells a flat silhouette from a lit one.
            var lums = px1.Where(c => c.a > 200).Select(c => (c.r + c.g + c.b) / 3).ToArray();
            sb.AppendLine(lums.Length > 0
                ? "Height+light: solid pixels span luminance " + lums.Min() + ".." + lums.Max() +
                  (lums.Max() - lums.Min() > 20 ? " (PASS -- the Dome is shaded, not flat)." : " (check -- nearly flat).")
                : "Height+light: no solid pixels to measure.");

            sb.AppendLine(WritePng(px1, doc.canvasWidth, doc.canvasHeight, "boom-border-dome-point.png"));
            UnityEngine.Object.DestroyImmediate(doc);

            // ── document 2: the per-character gradient variant ──────────────────────────────────────────
            var docG = BuildDocument(font, gradientFill: true);
            Color32[] pxg = null;
            try { pxg = ShaperDocumentRenderer.RenderFrame(docG, 0); }
            catch (Exception e) { err = e.ToString(); }
            if (err != null || pxg == null)
            {
                sb.AppendLine("FAIL: gradient-variant render threw or returned null:");
                sb.AppendLine(err ?? "(null pixels)");
            }
            else
            {
                // Per-character variation, measured: split the canvas into four columns (one per letter) and
                // compare each column's mean hue-ish signature. A gradient swept across the line makes them differ.
                var meansR = new float[4];
                var meansB = new float[4];
                var counts = new int[4];
                for (int y = 0; y < docG.canvasHeight; y++)
                {
                    for (int x = 0; x < docG.canvasWidth; x++)
                    {
                        Color32 c = pxg[y * docG.canvasWidth + x];
                        if (c.a < 200) continue;
                        int band = Mathf.Clamp(x * 4 / docG.canvasWidth, 0, 3);
                        meansR[band] += c.r; meansB[band] += c.b; counts[band]++;
                    }
                }
                var parts = new List<string>();
                float spreadR = 0f;
                float minR = float.MaxValue, maxR = float.MinValue;
                for (int b = 0; b < 4; b++)
                {
                    if (counts[b] == 0) { parts.Add("band" + b + ": none"); continue; }
                    float r = meansR[b] / counts[b], bl = meansB[b] / counts[b];
                    parts.Add("band" + b + ": R" + r.ToString("F0") + "/B" + bl.ToString("F0"));
                    minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                }
                if (maxR > minR) spreadR = maxR - minR;
                sb.AppendLine("Per-char gradient: " + string.Join(", ", parts));
                sb.AppendLine(spreadR > 20f
                    ? "Per-char gradient: PASS -- mean red varies " + spreadR.ToString("F0") + " across the four letters."
                    : "Per-char gradient: check -- only " + spreadR.ToString("F0") + " of variation across the letters.");
                sb.AppendLine(WritePng(pxg, docG.canvasWidth, docG.canvasHeight, "boom-perchar-gradient.png"));
            }
            UnityEngine.Object.DestroyImmediate(docG);

            return sb.ToString();
        }

        static ShaperPrimitiveDef MakeTextDef(TMP_FontAsset font) => new ShaperPrimitiveDef
        {
            kind = ShaperPrimitiveKind.Text,
            textFont = font,
            textString = "BOOM",
            textAlign = ShaperTextAlign.Centre,
            textSizeDial = new ZUIValue(44f),
            textLetterSpacingDial = new ZUIValue(2f),
            textLineSpacingDial = new ZUIValue(0f),
            textWeightDial = new ZUIValue(0.5f),
        };

        static ShaperDocument BuildDocument(TMP_FontAsset font, bool gradientFill)
        {
            var node = ShaperNode.Primitive(MakeTextDef(font), "BOOM");
            node.border = new ShaperBorderDef
            {
                enabled = true,
                alignment = ShaperShellAlignment.Outward,
                width = new ZUIValue(3f),
            };
            node.fill = gradientFill ? GradientFill() : SolidFill();

            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            doc.canvasWidth = 192;
            doc.canvasHeight = 80;
            doc.frameCount = 1;
            doc.frameRate = 12;

            doc.lightRig.lights.Clear();
            doc.lightRig.lights.Add(new ShaperLight
            {
                name = "Key",
                enabled = true,
                kind = ShaperLightKind.Point,
                colour = new Color(1f, 0.95f, 0.85f),
                intensity = new ZUIValue(1.2f),
                posX = new ZUIValue(-40f),
                posY = new ZUIValue(28f),
                posZ = new ZUIValue(45f),
                range = new ZUIValue(220f),
            });

            doc.layers.Add(new ShaperLayer
            {
                name = "Text",
                root = node,
                height = new ShaperHeightDef
                {
                    technique = ShaperExtrusionTechnique.Dome,
                    depth = new ZUIValue(12f),
                    curve = new ZUIValue(1f),
                },
                // Profile normals so the Dome actually catches the point light — a Constant normal would light
                // the whole silhouette identically and the height stage would be invisible in the picture.
                response = new ShaperLightResponse { receiveLighting = true, normalKind = ShaperNormalKind.Profile },
            });
            return doc;
        }

        static ShaperFillDef SolidFill() => new ShaperFillDef
        {
            kind = ShaperFillKind.Solid,
            solidColor = new Color(0.85f, 0.35f, 0.15f),
        };

        static ShaperFillDef GradientFill()
        {
            var g = new ZuiGradient();
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.15f, 0.05f), 0f),
                    new GradientColorKey(new Color(1f, 0.85f, 0.10f), 0.5f),
                    new GradientColorKey(new Color(0.15f, 0.7f, 1f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            g.gradient = grad;

            return new ShaperFillDef
            {
                kind = ShaperFillKind.Gradient,
                gradientMode = ShaperGradientMode.Linear,
                gradient = g,
                gradientTint = Color.white,
                gradientAngleDegrees = new ZUIValue(0f),   // sweep across the line, left to right
                gradientSize = new ZUIValue(1f),
            };
        }

        static string WritePng(Color32[] px, int w, int h, string fileName)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                tex.SetPixels32(px);
                tex.Apply(false);
                byte[] png = ImageConversion.EncodeToPNG(tex);
                string outPath = Path.Combine(OutDir, fileName);
                File.WriteAllBytes(outPath, png);
                UnityEngine.Object.DestroyImmediate(tex);
                return "Wrote " + outPath + " (" + png.Length + " bytes).";
            }
            catch (Exception e)
            {
                return "FAIL: PNG write threw: " + e;
            }
        }
    }
}

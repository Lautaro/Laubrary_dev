// TEMP PROBE T-0175 — PM deletes after running
//
// Builds a one-layer ShaperDocument whose root node is a ShaperPrimitiveKind.Sprite primitive (alpha threshold
// + softness), a border and a Round height stage, renders phase 0 twice, checks the two renders are bit-identical
// (BC-1.3's determinism contract), and writes the first render to a PNG under the task's workspace folder. No
// [MenuItem] — invoked once via the Unity CLI's `command eval_file`, per PROGRAMME_RULES.md's code-only posture.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Laubrary.Shaper;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class T0175_SpriteShapeProbe
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0175 Sprite-shape probe");

            // ── find a small sprite in the project ──────────────────────────────────────────────────────
            // Prefers anything under 256px on both sides — the EDT bake grid clamps to 128 texels regardless
            // of source size, so a large source (a full BackSplash background, easily 1000+ px) only costs an
            // expensive Graphics.Blit downsample for no extra bake fidelity; picking small keeps the probe fast.
            const float MaxPreferredDim = 256f;
            string[] guids = AssetDatabase.FindAssets("t:Sprite");
            Sprite sprite = null, fallbackSprite = null;
            string spritePath = null, fallbackPath = null;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                if (candidate == null) continue;
                if (fallbackSprite == null) { fallbackSprite = candidate; fallbackPath = path; }
                if (candidate.rect.width <= MaxPreferredDim && candidate.rect.height <= MaxPreferredDim)
                {
                    sprite = candidate; spritePath = path; break;
                }
            }
            if (sprite == null) { sprite = fallbackSprite; spritePath = fallbackPath; }
            if (sprite == null)
            {
                sb.AppendLine("FAIL: no Sprite asset found anywhere in the project (t:Sprite search empty).");
                return sb.ToString();
            }
            sb.AppendLine("Sprite: " + sprite.name + "  (" + spritePath + "), rect " +
                          sprite.rect.width.ToString("F0") + "x" + sprite.rect.height.ToString("F0"));

            // ── build the document ──────────────────────────────────────────────────────────────────────
            var def = new ShaperPrimitiveDef
            {
                kind = ShaperPrimitiveKind.Sprite,
                spriteAsset = sprite,
                spriteFitMode = ShaperSpriteFitMode.Uniform,
                spriteHalfWDial = new ZUIValue(40f),
                spriteHalfHDial = new ZUIValue(40f),
                spriteThresholdDial = new ZUIValue(0.5f),
                spriteSoftnessDial = new ZUIValue(1.5f),
            };

            var node = ShaperNode.Primitive(def, "SpriteShape");
            node.border = new ShaperBorderDef
            {
                enabled = true,
                alignment = ShaperShellAlignment.Outward,
                width = new ZUIValue(4f),
            };

            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            doc.canvasWidth = 96;
            doc.canvasHeight = 96;
            doc.frameCount = 1;
            doc.frameRate = 12;
            doc.layers.Add(new ShaperLayer
            {
                name = "Sprite",
                root = node,
                height = new ShaperHeightDef
                {
                    technique = ShaperExtrusionTechnique.Round,
                    depth = new ZUIValue(14f),
                    curve = new ZUIValue(1f),
                },
            });

            // ── render phase 0 twice — determinism (BC-1.3) ─────────────────────────────────────────────
            Color32[] px1 = null, px2 = null;
            string renderError = null;
            try
            {
                px1 = ShaperDocumentRenderer.RenderFrame(doc, 0);
                px2 = ShaperDocumentRenderer.RenderFrame(doc, 0);
            }
            catch (Exception e)
            {
                renderError = e.ToString();
            }

            if (renderError != null)
            {
                sb.AppendLine("FAIL: render threw:");
                sb.AppendLine(renderError);
                UnityEngine.Object.DestroyImmediate(doc);
                return sb.ToString();
            }

            if (px1 == null || px1.Length != doc.canvasWidth * doc.canvasHeight)
            {
                sb.AppendLine("FAIL: RenderFrame returned null or wrong-length pixel array.");
                UnityEngine.Object.DestroyImmediate(doc);
                return sb.ToString();
            }

            int mismatches = 0;
            for (int i = 0; i < px1.Length; i++)
                if (!px1[i].Equals(px2[i])) mismatches++;

            int opaque = px1.Count(c => c.a > 0);
            int fullyOpaque = px1.Count(c => c.a >= 250);

            sb.AppendLine("Canvas " + doc.canvasWidth + "x" + doc.canvasHeight +
                          " -- covered (a>0): " + opaque + " px, near-solid (a>=250): " + fullyOpaque + " px.");
            sb.AppendLine(mismatches == 0
                ? "Determinism: PASS -- two renders of phase 0 bit-identical (0/" + px1.Length + " mismatches)."
                : "Determinism: FAIL -- " + mismatches + "/" + px1.Length + " pixels differ between two renders.");

            if (opaque == 0)
                sb.AppendLine("WARNING: zero covered pixels -- the sprite shape produced no coverage at all.");

            // ── write the PNG ────────────────────────────────────────────────────────────────────────────
            string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0175";
            try
            {
                Directory.CreateDirectory(outDir);
                var tex = new Texture2D(doc.canvasWidth, doc.canvasHeight, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
                tex.SetPixels32(px1);
                tex.Apply(false);
                byte[] png = ImageConversion.EncodeToPNG(tex);
                string outPath = Path.Combine(outDir, "sprite-shape-phase0.png");
                File.WriteAllBytes(outPath, png);
                UnityEngine.Object.DestroyImmediate(tex);
                sb.AppendLine("Wrote " + outPath + " (" + png.Length + " bytes).");
            }
            catch (Exception e)
            {
                sb.AppendLine("FAIL: PNG write threw:");
                sb.AppendLine(e.ToString());
            }

            UnityEngine.Object.DestroyImmediate(doc);
            return sb.ToString();
        }
    }
}

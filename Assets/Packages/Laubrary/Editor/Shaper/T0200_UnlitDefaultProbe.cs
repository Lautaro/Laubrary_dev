// TEMP PROBE T-0200 — PM deletes after running
//
// W6.7 — a document with NO enabled lights must render its layers unlit pass-through (LR-4.3), not through the
// default ambient (0.18) that used to darken every fresh document before an author ever touched the Lights
// card. Four contact-sheet cells, in the same "one star per cell, everything else at its documented default"
// shape T-0196's probe used: (1)/(2) a fresh, light-less document — white Solid fill and the default Gradient
// fill — must show its full, undarkened paint; (3) the SAME document with one enabled Point light added must
// visibly shade; (4) the light present but DISABLED must match (1) again, proving the gate reads "enabled", not
// "authored". A fifth image is written separately — the real Assets/Demos/ShaperDemo/ShaperDemoDoc.asset,
// loaded READ-ONLY (never modified, never saved — CLAUDE.md's standing rule) and rendered at its own phase01 and
// own canvas size, to eyeball that an authored document with real lights (including its Gem/ArcBurst forms)
// still looks the way it always did. Run via the Unity CLI (`unity command eval_file`) once T-0200 has editor
// rights — see the task's handover for the call.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class T0200_UnlitDefaultProbe
    {
        const int Cell = 96, Cols = 2, Pad = 6;
        const string OutPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0200\contact_sheet.png";
        const string DemoOutPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0200\demo_doc_frame0.png";
        const string DemoDocPath = "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset";

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0200 unlit-default contact sheet — a light-less document must render unlit; "
                         + "an enabled light must still shade; a disabled one must not.");

            var cells = new List<KeyValuePair<string, Func<Color32[]>>>
            {
                new("NoLights_WhiteFill", () => RenderStar(NoLights(), WhiteFill())),
                new("NoLights_Gradient",  () => RenderStar(NoLights(), GradientFill())),
                new("PointLight_WhiteFill", () => RenderStar(OneEnabledPointLight(), WhiteFill())),
                new("LightDisabled_WhiteFill", () => RenderStar(OneDisabledPointLight(), WhiteFill())),
            };

            int rows = Mathf.CeilToInt(cells.Count / (float)Cols);
            int texW = Cols * (Cell + Pad) + Pad, texH = rows * (Cell + Pad) + Pad;
            var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(18, 18, 22, 255);
            sheet.SetPixels32(bg);

            for (int c = 0; c < cells.Count; c++)
            {
                try
                {
                    Color32[] px = cells[c].Value();
                    int cx = Pad + (c % Cols) * (Cell + Pad);
                    int cy = texH - Pad - Cell - (c / Cols) * (Cell + Pad);
                    if (px != null && px.Length == Cell * Cell)
                    {
                        sheet.SetPixels32(cx, cy, Cell, Cell, px);
                        Color32 centre = px[(Cell / 2) * Cell + Cell / 2];
                        sb.AppendLine("  " + cells[c].Key + ": rendered. Centre pixel RGBA = "
                                     + centre.r + "," + centre.g + "," + centre.b + "," + centre.a);
                    }
                    else
                    {
                        sb.AppendLine("  " + cells[c].Key + ": FAILED (px.Length=" +
                                      (px == null ? -1 : px.Length) + ", expected " + (Cell * Cell) + ").");
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine("  " + cells[c].Key + ": THREW " + e.GetType().Name + ": " + e.Message);
                }
            }

            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllBytes(OutPath, sheet.EncodeToPNG());
            sb.AppendLine("Wrote " + OutPath);

            // The demo document has its own canvas size (not necessarily Cell x Cell), so it gets its own PNG
            // at native resolution rather than being force-fit into the grid above. Read-only: LoadAssetAtPath
            // only, no field is ever written and nothing is ever saved back to it.
            try
            {
                var doc = AssetDatabase.LoadAssetAtPath<ShaperDocument>(DemoDocPath);
                if (doc == null) throw new Exception("could not load " + DemoDocPath);
                Color32[] px = ShaperDocumentRenderer.RenderPhase(doc, doc.phase01);
                int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);
                if (px != null && px.Length == w * h)
                {
                    var demoTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    demoTex.SetPixels32(px);
                    demoTex.Apply();
                    File.WriteAllBytes(DemoOutPath, demoTex.EncodeToPNG());
                    sb.AppendLine("  DemoDoc_Frame0 (read-only, " + w + "x" + h + "): rendered. Wrote " + DemoOutPath);
                }
                else
                {
                    sb.AppendLine("  DemoDoc_Frame0: FAILED (px.Length=" + (px == null ? -1 : px.Length)
                                 + ", expected " + (w * h) + ").");
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("  DemoDoc_Frame0: THREW " + e.GetType().Name + ": " + e.Message);
            }

            sb.Append("A human must still look at both PNGs — cell 1/2 of the contact sheet must read "
                     + "undarkened (no grey wash), cell 3 must visibly shade, cell 4 must match cell 1, and "
                     + "demo_doc_frame0.png (the real demo document, including its Gem/ArcBurst forms) must "
                     + "look exactly as it did before this task.");
            return sb.ToString();
        }

        static ShaperLightRig NoLights() => new ShaperLightRig();

        static ShaperLightRig OneEnabledPointLight()
        {
            var rig = new ShaperLightRig();
            rig.lights.Add(new ShaperLight { name = "L", enabled = true, kind = ShaperLightKind.Point });
            return rig;
        }

        static ShaperLightRig OneDisabledPointLight()
        {
            var rig = new ShaperLightRig();
            rig.lights.Add(new ShaperLight { name = "L", enabled = false, kind = ShaperLightKind.Point });
            return rig;
        }

        static ShaperFillDef WhiteFill() => new ShaperFillDef { kind = ShaperFillKind.Solid, solidColor = Color.white };
        static ShaperFillDef GradientFill() => new ShaperFillDef { kind = ShaperFillKind.Gradient };

        static Color32[] RenderStar(ShaperLightRig rig, ShaperFillDef fill)
        {
            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            try
            {
                doc.canvasWidth = Cell;
                doc.canvasHeight = Cell;
                doc.pixelSize = 1f;
                doc.frameCount = 1;
                doc.lightRig = rig;

                var star = ShaperNode.Primitive(new ShaperPrimitiveDef
                {
                    kind = ShaperPrimitiveKind.Star,
                    starArms = 5,
                    starRadius = 34f,
                }, "Star");
                star.fill = fill;

                doc.layers.Add(new ShaperLayer { name = "L", root = star });
                return ShaperDocumentRenderer.RenderPhase(doc, 0f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(doc);
            }
        }
    }
}

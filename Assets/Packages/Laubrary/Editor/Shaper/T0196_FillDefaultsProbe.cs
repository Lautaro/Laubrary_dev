// TEMP PROBE T-0196 — PM deletes after running
//
// W6.3 — a fresh fill's DEFAULTS, not an authored one. Every fill kind is constructed the same way an author
// gets one (new ShaperFillDef { kind = ... }, nothing else touched) and painted onto the same star primitive
// the owner's screenshot used, so the contact sheet is a direct "does this kind show its own character at
// first sight" check, cell by cell. Run via the Unity CLI (`unity command eval_file`) once T-0196 has editor
// rights — see the task's handover for the call.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class T0196_FillDefaultsProbe
    {
        const int Cell = 96, Cols = 5, Pad = 6;
        const string OutPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0196\contact_sheet.png";

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0196 fill defaults contact sheet — one star per fill kind, every dial at its default.");

            var kinds = (ShaperFillKind[])Enum.GetValues(typeof(ShaperFillKind));
            var cells = new List<KeyValuePair<string, ShaperFillDef>>();
            foreach (var k in kinds)
                cells.Add(new KeyValuePair<string, ShaperFillDef>(k.ToString(), new ShaperFillDef { kind = k }));

            int rows = Mathf.CeilToInt(cells.Count / (float)Cols);
            int texW = Cols * (Cell + Pad) + Pad, texH = rows * (Cell + Pad) + Pad;
            var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(18, 18, 22, 255);
            sheet.SetPixels32(bg);

            for (int c = 0; c < cells.Count; c++)
            {
                var doc = ScriptableObject.CreateInstance<ShaperDocument>();
                try
                {
                    doc.canvasWidth = Cell;
                    doc.canvasHeight = Cell;
                    doc.pixelSize = 1f;
                    doc.frameCount = 1;

                    var star = ShaperNode.Primitive(new ShaperPrimitiveDef
                    {
                        kind = ShaperPrimitiveKind.Star,
                        starArms = 5,
                        starRadius = 34f,
                    }, "Star");
                    star.fill = cells[c].Value;

                    doc.layers.Add(new ShaperLayer { name = "L", root = star });

                    Color32[] px = ShaperDocumentRenderer.RenderPhase(doc, 0f);

                    int cx = Pad + (c % Cols) * (Cell + Pad);
                    int cy = texH - Pad - Cell - (c / Cols) * (Cell + Pad);
                    if (px != null && px.Length == Cell * Cell)
                    {
                        sheet.SetPixels32(cx, cy, Cell, Cell, px);
                        sb.AppendLine("  " + cells[c].Key + ": rendered.");
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
                finally
                {
                    UnityEngine.Object.DestroyImmediate(doc);
                }
            }

            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllBytes(OutPath, sheet.EncodeToPNG());
            sb.AppendLine("Wrote " + OutPath);
            sb.Append("A human must still look at the PNG — a cell rendering without throwing is not the same "
                     + "as a cell visibly showing that kind's character.");
            return sb.ToString();
        }
    }
}

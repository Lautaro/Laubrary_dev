// TEMP PROBE T-0196 — PM deletes after running
//
// W6.3 — a fresh fill's DEFAULTS, not an authored one. Every fill kind is constructed the same way an author
// gets one (new ShaperFillDef { kind = ... }, nothing else touched) and painted onto the same star primitive
// the owner's screenshot used, so the contact sheet is a direct "does this kind show its own character at
// first sight" check, cell by cell.
//
// Also carries the PM's follow-up (owner's screenshot at frame 14/16, T-0190's seeded fade on the layer
// root's fill veil): renders a FRESH document exactly as ShaperWindow.NewLayer/SeededBorderFill build it
// (not a hand-rolled equivalent), with a border added, at phase 0 / mid / last, so a human can see the fill
// and the border fade together rather than a fixed opaque outline behind a vanishing fill; and a data-level
// check that the border's seeded veil curve actually matches the layer root's.
//
// Run via the Unity CLI (`unity command eval_file`) once T-0196 has editor rights — see the task's handover
// for the call.
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
        const string OutDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0196";

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine(RunContactSheet());
            sb.AppendLine();
            sb.AppendLine(RunFadeTogetherCheck());
            return sb.ToString();
        }

        // ── every fill kind at bare defaults, one star per cell ──────────────────────────────────────────

        static string RunContactSheet()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0196 fill defaults contact sheet — one star per fill kind, every dial at its default.");

            var kinds = (ShaperFillKind[])Enum.GetValues(typeof(ShaperFillKind));
            var cells = new List<KeyValuePair<string, ShaperFillDef>>();
            foreach (var k in kinds)
                cells.Add(new KeyValuePair<string, ShaperFillDef>(k.ToString(), new ShaperFillDef { kind = k }));

            // One extra cell: a fresh Gradient on a star at phase 0.5 (mid-animation, well clear of the
            // frame-14/16 fade tail the owner's capture caught), specifically to re-confirm the ramp reaches
            // edge to edge on a star's own bound — the question the original capture looked like it was
            // asking before the PM's addendum traced it to the seeded fade instead.
            cells.Add(new KeyValuePair<string, ShaperFillDef>("Gradient @ mid-phase", new ShaperFillDef
            {
                kind = ShaperFillKind.Gradient,
                gradientMode = ShaperGradientMode.Radial,
            }));

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

                    // The mid-phase cell renders at 0.5; every other cell is a static 1-frame document so its
                    // phase does not matter.
                    float phase = cells[c].Key.StartsWith("Gradient @ mid-phase") ? 0.5f : 0f;
                    Color32[] px = ShaperDocumentRenderer.RenderPhase(doc, phase);

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
            string path = Path.Combine(OutDir, "contact_sheet.png");
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            sb.AppendLine("Wrote " + path);
            sb.Append("A human must still look at the PNG — a cell rendering without throwing is not the same "
                     + "as a cell visibly showing that kind's character.");
            return sb.ToString();
        }

        // ── fill + border fade together (PM addendum) ────────────────────────────────────────────────────

        static string RunFadeTogetherCheck()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0196 fade-together check — a fresh layer's border must fade with its fill (W6.3 addendum).");

            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            try
            {
                doc.canvasWidth = Cell;
                doc.canvasHeight = Cell;
                doc.pixelSize = 1f;
                doc.frameCount = 16;

                // The REAL production path — NewLayer is exactly what "New document -> Add layer" runs, and
                // SeededBorderFill is exactly what clicking "Add border" runs on that layer's root.
                var layer = ShaperWindow.NewLayer("Probe", doc);
                layer.root.border = new ShaperBorderDef { fill = ShaperWindow.SeededBorderFill(layer.root) };
                doc.layers.Add(layer);

                // Data-level check: the border's veil curve must be a Curve (not Static) and land on the same
                // normalised life fractions as the fill's, or the two were not actually seeded to match.
                var fillVeil = layer.root.fill.veil;
                var borderVeil = layer.root.border.fill.veil;
                bool bothCurves = fillVeil.mode == ZUIValue.Mode.Curve && borderVeil.mode == ZUIValue.Mode.Curve;
                bool pointsMatch = bothCurves && fillVeil.points.Count == borderVeil.points.Count;
                if (pointsMatch)
                {
                    for (int i = 0; i < fillVeil.points.Count; i++)
                    {
                        if (Mathf.Abs(fillVeil.points[i].time - borderVeil.points[i].time) > 1e-5f ||
                            Mathf.Abs(fillVeil.points[i].value - borderVeil.points[i].value) > 1e-5f)
                        {
                            pointsMatch = false;
                            break;
                        }
                    }
                }
                sb.AppendLine("  Data-level: fill.veil.mode=" + fillVeil.mode + ", border.fill.veil.mode=" + borderVeil.mode +
                              ", points match=" + pointsMatch + " (expected Curve/Curve/True).");

                // Render phase 0 / mid (0.425, the middle of the seeded hold plateau 0.15..0.7) / 1 into one
                // strip, so a human can see whether the border silhouette and the fill both visibly dim
                // together at phase 1, rather than the border staying opaque while the fill fades out.
                float[] phases = { 0f, 0.425f, 1f };
                int texW = phases.Length * (Cell + Pad) + Pad, texH = Cell + 2 * Pad;
                var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
                var bg = new Color32[texW * texH];
                for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(18, 18, 22, 255);
                sheet.SetPixels32(bg);

                for (int i = 0; i < phases.Length; i++)
                {
                    Color32[] px = ShaperDocumentRenderer.RenderPhase(doc, phases[i]);
                    if (px == null || px.Length != Cell * Cell)
                    {
                        sb.AppendLine("  phase " + phases[i] + ": FAILED to render.");
                        continue;
                    }
                    // Mean alpha over the whole cell, as a coarse directional signal only — the PNG itself is
                    // the real evidence, this line is not a substitute for looking at it.
                    long alphaSum = 0;
                    for (int p = 0; p < px.Length; p++) alphaSum += px[p].a;
                    float meanAlpha = alphaSum / (float)(px.Length * 255);
                    sb.AppendLine("  phase " + phases[i] + ": mean alpha " + meanAlpha.ToString("F3"));

                    int cx = Pad + i * (Cell + Pad), cy = Pad;
                    sheet.SetPixels32(cx, cy, Cell, Cell, px);
                }

                sheet.Apply();
                string path = Path.Combine(OutDir, "fade_together.png");
                Directory.CreateDirectory(OutDir);
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                sb.AppendLine("Wrote " + path + " (left to right: phase 0, 0.425, 1).");
            }
            catch (Exception e)
            {
                sb.AppendLine("  THREW " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(doc);
            }

            sb.Append("A human must still look at the PNG — the mean-alpha line is a coarse smoke signal, "
                     + "not proof the border's OWN silhouette (not just the total picture) dims with the fill.");
            return sb.ToString();
        }
    }
}

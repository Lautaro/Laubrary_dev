// TEMP PROBE T-0172 — PM deletes after running
//
// Renders a contact sheet exercising the four things T-0172 added to the fill stage — OverPhase, Procedural
// (Noise/Grid/Dots), the animated sprite-sheet mode on Texture, and a couple of Pyre ramp presets applied to
// RampByQuantity — by driving ShaperFillCompiler/ShaperFillOps directly, the same two entry points every
// existing fill kind goes through. No MenuItem, no EditorWindow: a plain static class with one RunAll(),
// invoked through the Unity CLI, matching every Shaper*Audit probe's own convention.
//
// PM by-eye pass #1 found Noise/Grid/Dots illegible at the scales this probe was hand-authoring for them
// (24/16/14) — those numbers were never the field's own default, and it turned out the DEFAULT (1) was just
// as broken (ShaperFillDef.proceduralScale's doc comment has the arithmetic). Fixed at the source
// (ShaperFillDef.cs's default, now 0.5) rather than in this probe: the cells below no longer override
// proceduralScale at all, so what renders here is genuinely what an author sees on a freshly added
// Procedural fill.

using System.IO;
using System.Text;
using Laubrary.PyreShaper;
using Laubrary.Shaper;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class T0172_FillContactSheetProbe
    {
        const int Cell = 96;
        const int Cols = 4;
        const int Pad = 6;
        const float Half = 48f;

        public static string RunAll()
        {
            var sb = new StringBuilder("T-0172 fill contact sheet\n");

            var anchor = new ShaperFillAnchor
            {
                inverse = ShaperMatrix.Identity,
                localCx = 0f, localCy = 0f, localHalfW = Half, localHalfH = Half, localValid = true,
                canvasHalfW = Half, canvasHalfH = Half,
            };

            var cells = new System.Collections.Generic.List<(string label, ShaperFillDef def, float phase, uint seed)>();

            // ── OverPhase — one flat colour, sampled at three phases along a red→blue ramp ──────────────────
            var overPhaseGrad = new ZuiGradient();
            overPhaseGrad.gradient.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            var overPhaseDef = new ShaperFillDef { kind = ShaperFillKind.OverPhase, overPhaseGradient = overPhaseGrad };
            cells.Add(("OverPhase @0.0", overPhaseDef, 0f, 0u));
            cells.Add(("OverPhase @0.5", overPhaseDef, 0.5f, 0u));
            cells.Add(("OverPhase @1.0", overPhaseDef, 1f, 0u));

            // ── Procedural: Noise (Value/Ridged/Steps), Grid, Dots — every dial below left at its FIELD
            //    DEFAULT (no proceduralScale/gridLineWidth/dotSize override) so this cell is what an author
            //    sees on a freshly added Procedural fill, not a hand-tuned demo value. ─────────────────────
            var noiseGrad = new ZuiGradient();
            noiseGrad.gradient.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            foreach (var nk in new[] { ShaperNoiseKind.Value, ShaperNoiseKind.Ridged, ShaperNoiseKind.Steps })
            {
                var d = new ShaperFillDef
                {
                    kind = ShaperFillKind.Procedural,
                    proceduralKind = ShaperProceduralKind.Noise,
                    noiseKind = nk,
                    proceduralGradient = noiseGrad,
                };
                cells.Add(("Noise " + nk, d, 0f, 1u));
            }

            var gridDef = new ShaperFillDef
            {
                kind = ShaperFillKind.Procedural,
                proceduralKind = ShaperProceduralKind.Grid,
                proceduralTint = new Color(1f, 0.8f, 0.2f),
            };
            cells.Add(("Grid", gridDef, 0f, 0u));

            var dotsDef = new ShaperFillDef
            {
                kind = ShaperFillKind.Procedural,
                proceduralKind = ShaperProceduralKind.Dots,
                proceduralTint = new Color(0.2f, 0.9f, 1f),
            };
            cells.Add(("Dots", dotsDef, 0f, 0u));

            // ── Animated Texture — a 2-frame sheet, red then blue, stepped by phase (never lerped) ─────────
            var sheet2 = new Texture2D(4, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var texels = new Color32[8];
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 4; x++)
                    texels[y * 4 + x] = x < 2 ? new Color32(220, 40, 40, 255) : new Color32(40, 90, 220, 255);
            sheet2.SetPixels32(texels);
            sheet2.Apply(false, false);
            var animDef = new ShaperFillDef
            {
                kind = ShaperFillKind.Texture,
                texture = sheet2,
                textureAnimated = true,
                textureFrameColumns = new ZUIValue(2f),
                textureFrameRows = new ZUIValue(1f),
                textureFrameCount = new ZUIValue(2f),
            };
            cells.Add(("Anim tex frame 0", animDef, 0.1f, 0u));
            cells.Add(("Anim tex frame 1", animDef, 0.75f, 0u));

            // ── Ramp presets from Pyre, applied to RampByQuantity ───────────────────────────────────────────
            PyreShaperRampPresets.Preset? FindPreset(string name)
            {
                foreach (var p in PyreShaperRampPresets.All) if (p.name == name) return p;
                return null;
            }
            var emberPreset = FindPreset("Ember");
            var plasmaPreset = FindPreset("Plasma — Ion");

            if (emberPreset.HasValue)
            {
                var g = PyreShaperRampPresets.ToZuiGradient(emberPreset.Value.factory());
                cells.Add(("Ramp preset: Ember", new ShaperFillDef
                {
                    kind = ShaperFillKind.RampByQuantity,
                    rampGradient = g,
                    rampInputLow = new ZUIValue(0f),
                    rampInputHigh = new ZUIValue(1f),
                }, 0f, 0u));
            }
            if (plasmaPreset.HasValue)
            {
                var g = PyreShaperRampPresets.ToZuiGradient(plasmaPreset.Value.factory());
                cells.Add(("Ramp preset: Plasma Ion", new ShaperFillDef
                {
                    kind = ShaperFillKind.RampByQuantity,
                    rampGradient = g,
                    rampInputLow = new ZUIValue(0f),
                    rampInputHigh = new ZUIValue(1f),
                }, 0f, 0u));
            }

            int rows = Mathf.CeilToInt(cells.Count / (float)Cols);
            int texW = Cols * (Cell + Pad) + Pad, texH = rows * (Cell + Pad) + Pad;
            var outSheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(18, 18, 22, 255);
            outSheet.SetPixels32(bg);

            var determinismMismatches = 0;
            for (int c = 0; c < cells.Count; c++)
            {
                var (label, def, phase, seed) = cells[c];
                var pixels = RenderCell(def, anchor, phase, seed);

                // Determinism check (BC-1.3): the same def/phase/seed compiled and sampled a second time must
                // produce bit-identical output — never UnityEngine.Random/System.Random in a generator path.
                var pixels2 = RenderCell(def, anchor, phase, seed);
                for (int i = 0; i < pixels.Length; i++)
                    if (!pixels[i].Equals(pixels2[i])) { determinismMismatches++; break; }

                DrawLabel(pixels, Cell, label);
                int cx = Pad + (c % Cols) * (Cell + Pad);
                int cy = texH - Pad - Cell - (c / Cols) * (Cell + Pad);
                outSheet.SetPixels32(cx, cy, Cell, Cell, pixels);
            }
            outSheet.Apply();

            string dir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0172";
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "fill-contact-sheet.png");
            File.WriteAllBytes(path, outSheet.EncodeToPNG());

            sb.AppendLine("  " + cells.Count + " cells, " + Cols + " x " + rows + ", " + texW + "x" + texH +
                          " px, written to " + path);
            for (int c = 0; c < cells.Count; c++)
                sb.AppendLine("    row " + (c / Cols) + " col " + (c % Cols) + "  " + cells[c].label);
            sb.AppendLine("  determinism mismatches: " + determinismMismatches + " / " + cells.Count + " cells");
            return sb.ToString();
        }

        static Color32[] RenderCell(ShaperFillDef def, in ShaperFillAnchor anchor, float phase, uint seed)
        {
            var prog = ShaperFillCompiler.Compile(def, anchor, phase, seed);
            var pixels = new Color32[Cell * Cell];
            for (int y = 0; y < Cell; y++)
            {
                float ny = ((y + 0.5f) / Cell) * (Half * 2f) - Half;
                for (int x = 0; x < Cell; x++)
                {
                    float nx = ((x + 0.5f) / Cell) * (Half * 2f) - Half;
                    // A synthetic box SDF (negative inside) — the only edge-distance reader this probe
                    // exercises is RampByQuantity's Coverage pick, which does not read it anyway; kept for
                    // completeness with ShaperFillOps.Sample's signature.
                    float edge = -Mathf.Min(Half - Mathf.Abs(nx), Half - Mathf.Abs(ny));
                    // Sweeps 0..1 left→right — a stand-in "coverage" so the ramp-preset cells show the whole
                    // ramp rather than one clamped endpoint (RampByQuantity has no positional sheet of its
                    // own to visualize with; this is a probe convenience, not a claim about real coverage).
                    float q = Mathf.Clamp01((nx + Half) / (Half * 2f));

                    ShaperFillOps.Sample(in prog.op, prog.bulk, nx, ny, edge, q,
                        out float r, out float g, out float b, out float veil, out float _);

                    byte br = ShaperSrgb.EncodeToByte(r);
                    byte bg2 = ShaperSrgb.EncodeToByte(g);
                    byte bb = ShaperSrgb.EncodeToByte(b);
                    byte ba = (byte)Mathf.Clamp(Mathf.RoundToInt(veil * 255f), 0, 255);

                    // Composite over a neutral checker so a low-veil Grid/Dots cell still reads on a viewer
                    // that treats PNG alpha as black, matching ShaperBorderAudit's own reasoning.
                    bool checker = ((x / 8) + (y / 8)) % 2 == 0;
                    byte cbg = (byte)(checker ? 40 : 56);
                    float a01 = ba / 255f;
                    byte outR = (byte)Mathf.RoundToInt(Mathf.Lerp(cbg, br, a01));
                    byte outG = (byte)Mathf.RoundToInt(Mathf.Lerp(cbg, bg2, a01));
                    byte outB = (byte)Mathf.RoundToInt(Mathf.Lerp(cbg, bb, a01));
                    pixels[y * Cell + x] = new Color32(outR, outG, outB, 255);
                }
            }
            return pixels;
        }

        static void DrawLabel(Color32[] pixels, int cell, string text)
        {
            // A minimal 3x5 block-letter stamp is not worth building here; a solid strip at the cell's top
            // edge is enough for a human to line the label list up against the picture by row/col, which is
            // how ShaperBorderAudit's own contact sheets are read.
            for (int x = 0; x < cell; x++)
                for (int y = cell - 3; y < cell; y++)
                    pixels[y * cell + x] = new Color32(0, 0, 0, 220);
        }
    }
}

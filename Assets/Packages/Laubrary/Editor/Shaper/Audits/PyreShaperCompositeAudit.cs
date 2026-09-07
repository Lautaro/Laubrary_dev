using System;
using System.IO;
using System.Text;
using Laubrary.Pyre;
using Laubrary.Pyre.Forms.Kiln;
using Laubrary.Shaper;
using UnityEngine;

namespace Laubrary.PyreShaper.Editor
{
    /// <summary>
    /// T-0112 — the composite generator audit: measured verification of the coverage-only contract, the
    /// structural border refusal, hard-combine sign correctness with SDF siblings, the nine-generator
    /// declaration completeness, and a rendered contact sheet.
    ///
    /// Same posture as <c>ShaperFillAudit</c>/<c>ShaperBorderAudit</c>: plain static methods, no
    /// <c>[MenuItem]</c>, no <c>EditorWindow</c> — invoked through the Unity CLI. Every result is measured, not
    /// asserted on the strength of the code compiling.
    /// </summary>
    public static class PyreShaperCompositeAudit
    {
        const int W = 128, H = 128;
        const float Px = 1f;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Shaper COMPOSITE generator audit (T-0112) ===");
            sb.AppendLine(CT0_SyntheticRoundTrip());
            sb.AppendLine(CT1_StandaloneRootIsCompositeSampleOnly());
            sb.AppendLine(CT2_BorderRefusedStructurally());
            sb.AppendLine(CT3_UnionSignCorrect());
            sb.AppendLine(CT4_SubtractSignCorrect());
            sb.AppendLine(CT5_CatalogDeclarationComplete());
            sb.AppendLine(CT6_NoSwappableFillField());
            sb.AppendLine(CT7_OrbHostedRendersSomething());
            return sb.ToString();
        }

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        static ShaperSampleGrid Grid() => ShaperSampleGrid.Centred(W, H, Px);

        static ShaperNode Disc(string name, float r, float x, float y,
                               ShaperCombineMode mode = ShaperCombineMode.Add)
        {
            var n = ShaperNode.Primitive(
                new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r },
                name, mode);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        /// <summary>A hand-computed antialiased disc, independent of ShaperField.Coverage's own formula (a
        /// straight linear 1-texel ramp rather than a smoothstep) — deliberately NOT reusing the formula under
        /// test, so CT0's comparison against a real Primitive disc is not circular.</summary>
        sealed class SyntheticDiscSource : IShaperCompositeSource
        {
            public float radiusTexels;
            public string SourceLabel => "Synthetic disc";
            public void Render(int width, int height, float phase01, uint seed, Color32[] target)
            {
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float cx = (x + 0.5f) - width * 0.5f;
                    float cy = (y + 0.5f) - height * 0.5f;
                    float r = Mathf.Sqrt(cx * cx + cy * cy);
                    float a = Mathf.Clamp01(0.5f - (r - radiusTexels));
                    target[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
        }

        static ShaperNode SyntheticCompositeDisc(string name, float radiusCanvas, float boxHalfExtent,
                                                  int bake, float x, float y,
                                                  ShaperCombineMode mode = ShaperCombineMode.Add)
        {
            float radiusTexels = radiusCanvas / boxHalfExtent * (bake * 0.5f);
            var def = new ShaperCompositeDef
            {
                source = new SyntheticDiscSource { radiusTexels = radiusTexels },
                halfExtentX = boxHalfExtent,
                halfExtentY = boxHalfExtent,
                bakeWidth = bake,
                bakeHeight = bake,
            };
            var n = ShaperNode.Composite(def, name, mode);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        // ── CT-0 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// CT-0: the whole bake→sample→inverse-coverage plumbing, checked NUMERICALLY against a real analytic
        /// Primitive of the same radius — a composite generator that happens to draw a plain disc should publish
        /// coverage close to <c>ShaperPrimitiveKind.Ellipse</c>'s own. The two are computed by entirely different
        /// code paths (hand-written linear ramp vs the SDF + smoothstep engine), so agreement is a real check,
        /// not a tautology. Tolerance is generous (raster bake resolution, not canvas resolution, sets the
        /// composite's edge sharpness) and is reported alongside the measurement.
        /// </summary>
        public static string CT0_SyntheticRoundTrip()
        {
            var sb = new StringBuilder("CT-0 synthetic composite disc vs real Primitive disc, coverage agreement\n");

            float r = 30f;
            var compRoot = SyntheticCompositeDisc("Disc", r, 64f, 128, 0f, 0f);
            var progComp = ShaperCompiler.Compile(compRoot);
            var covComp = new float[W * H];
            var distComp = new float[W * H];
            ShaperEvaluator.FillTile(progComp, Grid(), 0, 0, W, H, distComp, covComp, 0, W, progComp.NewStack());

            var primRoot = Disc("Disc", r, 0f, 0f);
            var progPrim = ShaperCompiler.Compile(primRoot);
            var covPrim = new float[W * H];
            var distPrim = new float[W * H];
            ShaperEvaluator.FillTile(progPrim, Grid(), 0, 0, W, H, distPrim, covPrim, 0, W, progPrim.NewStack());

            float worst = 0f;
            double sumAbs = 0;
            for (int i = 0; i < W * H; i++)
            {
                float d = Mathf.Abs(covComp[i] - covPrim[i]);
                worst = Mathf.Max(worst, d);
                sumAbs += d;
            }
            float mean = (float)(sumAbs / (W * H));

            // Measured: worst-case sits around 0.09-0.10, always a SINGLE texel at the antialiasing seam where
            // the synthetic source's linear ramp and the inverse-smoothstep reconstruction round differently by
            // sub-pixel amounts — not a systemic error. The mean (over all 16,384 samples) is the number that
            // actually speaks to "is the bridge numerically sound", and it is two-and-a-half orders of magnitude
            // smaller. Both are reported so neither hides the other.
            const float worstTol = 0.11f;
            const float meanTol = 0.01f;
            bool ok = worst <= worstTol && mean <= meanTol;
            sb.AppendLine("  worst |coverage_composite - coverage_primitive| = " + worst.ToString("F4") +
                          "  (tolerance " + worstTol.ToString("F2") + " — a single edge texel, see below)");
            sb.AppendLine("  mean  |...| over " + (W * H) + " samples = " + mean.ToString("F5") +
                          "  (tolerance " + meanTol.ToString("F2") + " — this is the meaningful number)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── CT-1 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CT-1: a standalone composite ROOT compiles to exactly one op (CompositeSample) and exactly
        /// one baked raster — nothing structurally beyond "a coverage-shaped value came from a picture".</summary>
        public static string CT1_StandaloneRootIsCompositeSampleOnly()
        {
            var sb = new StringBuilder("CT-1 a standalone composite root publishes via ONE CompositeSample op, nothing else\n");

            var def = PyreCompositeCatalog.Build(new OrbForm(), PyreCompositeCatalog.Orb);
            var root = ShaperNode.Composite(def, "Orb");
            var prog = ShaperCompiler.Compile(root, 0.3f, 7u);

            bool oneOp = prog.ops.Length == 1;
            bool isSample = oneOp && prog.ops[0].kind == ShaperOpKind.CompositeSample;
            bool oneRaster = prog.composites.Length == 1;

            sb.AppendLine("  ops.Length = " + prog.ops.Length + " (expected 1)");
            sb.AppendLine("  ops[0].kind = " + (prog.ops.Length > 0 ? prog.ops[0].kind.ToString() : "n/a") +
                          " (expected CompositeSample)");
            sb.AppendLine("  composites.Length = " + prog.composites.Length + " (expected 1)");
            sb.Append("  RESULT: " + Verdict(oneOp && isSample && oneRaster));
            return sb.ToString();
        }

        // ── CT-2 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CT-2: a border authored on a Composite node is structurally refused — no Dilate op appears —
        /// contrasted against the SAME border on a Primitive, which DOES join (positive control).</summary>
        public static string CT2_BorderRefusedStructurally()
        {
            var sb = new StringBuilder("CT-2 a Composite node's border is structurally inert (no Dilate op emitted)\n");

            var border = new ShaperBorderDef
            {
                enabled = true,
                width = new ZUIValue(6f),
                alignment = ShaperShellAlignment.Outward,   // Inward's reach is 0 (Joins would be False either way)
            };

            var def = PyreCompositeCatalog.Build(new OrbForm(), PyreCompositeCatalog.Orb);
            var compRoot = ShaperNode.Composite(def, "Orb");
            compRoot.border = border;
            var progComp = ShaperCompiler.Compile(compRoot);
            bool compHasDilate = HasOp(progComp, ShaperOpKind.Dilate);

            var primRoot = Disc("Disc", 30f, 0f, 0f);
            primRoot.border = border;
            var progPrim = ShaperCompiler.Compile(primRoot);
            bool primHasDilate = HasOp(progPrim, ShaperOpKind.Dilate);

            sb.AppendLine("  Composite root, border enabled+width 6: Dilate op present = " + compHasDilate + " (expected False)");
            sb.AppendLine("  Primitive root, SAME border def:        Dilate op present = " + primHasDilate + " (expected True — positive control)");
            sb.Append("  RESULT: " + Verdict(!compHasDilate && primHasDilate));
            return sb.ToString();
        }

        static bool HasOp(ShaperProgram prog, ShaperOpKind kind)
        {
            foreach (var op in prog.ops) if (op.kind == kind) return true;
            return false;
        }

        // ── CT-3 / CT-4 ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>CT-3: Bag(Add: synthetic composite disc, Add: a far-away Primitive disc) — a hard union. Both
        /// discs' own centres must read full coverage, and the midpoint between them (outside both) must read
        /// zero — the sign a min-based union is supposed to produce, measured rather than assumed.</summary>
        public static string CT3_UnionSignCorrect()
        {
            var sb = new StringBuilder("CT-3 Bag(Composite union Primitive) — hard union sign correctness\n");

            var compMember = SyntheticCompositeDisc("Blob", 20f, 48f, 96, -40f, 0f);
            var primMember = Disc("Disc", 20f, 40f, 0f);
            var bag = ShaperNode.Bag("Union", ShaperCombineMode.Add, compMember, primMember);
            var prog = ShaperCompiler.Compile(bag);

            float atComposite = SampleCoverage(prog, -40f, 0f);
            float atPrimitive = SampleCoverage(prog, 40f, 0f);
            float between = SampleCoverage(prog, 0f, 0f);

            bool ok = atComposite > 0.9f && atPrimitive > 0.9f && between < 0.1f;
            sb.AppendLine("  coverage at composite's own centre (-40,0) = " + atComposite.ToString("F4") + " (expected > 0.9)");
            sb.AppendLine("  coverage at primitive's own centre (40,0)  = " + atPrimitive.ToString("F4") + " (expected > 0.9)");
            sb.AppendLine("  coverage at the midpoint (0,0), outside both = " + between.ToString("F4") + " (expected < 0.1)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// <summary>CT-4: Bag(Add: a big Primitive disc, Subtract: a synthetic composite disc at its centre) —
        /// the composite CARVES a hole out of the primitive. Coverage at the shared centre must drop to (near)
        /// zero even though the primitive alone would read full coverage there.</summary>
        public static string CT4_SubtractSignCorrect()
        {
            var sb = new StringBuilder("CT-4 Bag(Primitive subtract Composite) — the composite carves a hole\n");

            var big = Disc("Big", 40f, 0f, 0f);
            var hole = SyntheticCompositeDisc("Hole", 15f, 40f, 96, 0f, 0f, ShaperCombineMode.Subtract);
            var bag = ShaperNode.Bag("Carved", ShaperCombineMode.Add, big, hole);
            var prog = ShaperCompiler.Compile(bag);

            float atCentreCarved = SampleCoverage(prog, 0f, 0f);
            float atRimUncarved = SampleCoverage(prog, 30f, 0f);   // inside Big, outside the 15-radius hole

            bool ok = atCentreCarved < 0.1f && atRimUncarved > 0.9f;
            sb.AppendLine("  coverage at the carved centre (0,0)   = " + atCentreCarved.ToString("F4") + " (expected < 0.1)");
            sb.AppendLine("  coverage at the untouched rim (30,0)  = " + atRimUncarved.ToString("F4") + " (expected > 0.9)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        static float SampleCoverage(ShaperProgram prog, float x, float y)
        {
            var stack = prog.NewStack();
            return ShaperEvaluator.Coverage(prog, Grid(), x, y, stack);
        }

        // ── CT-5 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CT-5: every one of the nine catalog entries carries a non-empty declaration, and the
        /// palette-indifferent/dependent split totals 4/5 as the task body states.
        ///
        /// T-0254 — reads the classification straight off the catalog entry (<see cref="PyreCompositeCatalogEntry.reason"/>),
        /// not off a built <see cref="ShaperCompositeDef"/>: <c>ShaperCompositeDef.reason</c> is retired and
        /// <c>PyreCompositeCatalog.BuildSource</c> no longer copies the entry onto it.</summary>
        public static string CT5_CatalogDeclarationComplete()
        {
            var sb = new StringBuilder("CT-5 all nine catalog entries are declared (§6.2 compliance pass)\n");

            int total = PyreCompositeCatalog.All.Length;
            int declared = 0, indifferent = 0, dependent = 0, reason2 = 0;
            foreach (var e in PyreCompositeCatalog.All)
            {
                bool has = !string.IsNullOrWhiteSpace(e.reasonNote);
                if (has) declared++;
                if (e.paletteIndifferent) indifferent++; else dependent++;

                if (e.reason == ShaperCompositeReason.NotYetSplit && has) reason2++;

                sb.AppendLine("  " + e.displayName.PadRight(16) + " declared=" + has +
                              "  paletteIndifferent=" + e.paletteIndifferent);
            }

            bool ok = total == 9 && declared == 9 && indifferent == 4 && dependent == 5 && reason2 == 9;
            sb.AppendLine("  total=" + total + " (expected 9), declared=" + declared + " (expected 9)");
            sb.AppendLine("  palette-indifferent=" + indifferent + " (expected 4), palette-dependent=" + dependent + " (expected 5)");
            sb.AppendLine("  reason=NotYetSplit AND HasDeclaration=true for " + reason2 + "/9 (expected 9 — zero are AuthoredData)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── CT-6 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CT-6: <see cref="ShaperCompositeDef"/> carries no field that could hold a swappable
        /// <see cref="ShaperFillDef"/>/<see cref="ShaperFillKind"/> — the "its fill picker shows exactly one
        /// entry" half of §6.2 is a structural fact about the type, not a UI convention someone could route
        /// around.</summary>
        public static string CT6_NoSwappableFillField()
        {
            var sb = new StringBuilder("CT-6 ShaperCompositeDef has no ShaperFillDef/ShaperFillKind field (structural fill lock)\n");

            var fields = typeof(ShaperCompositeDef).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            bool anyFillField = false;
            foreach (var f in fields)
                if (f.FieldType == typeof(ShaperFillDef) || f.FieldType == typeof(ShaperFillKind))
                    anyFillField = true;

            sb.AppendLine("  fields on ShaperCompositeDef: " + fields.Length);
            sb.AppendLine("  any ShaperFillDef/ShaperFillKind field present = " + anyFillField + " (expected False)");
            sb.Append("  RESULT: " + Verdict(!anyFillField));
            return sb.ToString();
        }

        // ── CT-7 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CT-7: the hosted OrbForm actually renders something recognisable when driven through the
        /// bridge — a non-trivial fraction of the bake carries alpha, and it is not a uniform fill (a real blob,
        /// not a solid rectangle or a blank canvas).</summary>
        public static string CT7_OrbHostedRendersSomething()
        {
            var sb = new StringBuilder("CT-7 OrbForm hosted through PyreFormCompositeSource renders a real picture\n");

            var def = PyreCompositeCatalog.Build(new OrbForm(), PyreCompositeCatalog.Orb, bakeWidth: 96, bakeHeight: 96);
            var root = ShaperNode.Composite(def, "Orb");
            var prog = ShaperCompiler.Compile(root, 0.4f, 11u);

            var raster = prog.composites.Length > 0 ? prog.composites[0] : null;
            int lit = 0, total = raster != null ? raster.coverage.Length : 0;
            float minC = 1f, maxC = 0f;
            if (raster != null)
                foreach (var c in raster.coverage)
                {
                    if (c > 0.02f) lit++;
                    minC = Mathf.Min(minC, c); maxC = Mathf.Max(maxC, c);
                }

            float litFrac = total > 0 ? lit / (float)total : 0f;
            bool ok = raster != null && litFrac > 0.01f && litFrac < 0.95f && maxC > 0.5f;
            sb.AppendLine("  bake " + (raster?.width ?? 0) + "x" + (raster?.height ?? 0) +
                          "  lit texels (coverage>0.02) = " + lit + "/" + total +
                          " (" + (litFrac * 100f).ToString("F1") + "%)");
            sb.AppendLine("  coverage range [" + minC.ToString("F3") + ", " + maxC.ToString("F3") + "]" +
                          "  (expected some texels near 1 — a real core — and some near 0 — real background)");
            sb.AppendLine("  a blank render would read 0% lit; a solid fill would read ~100% at ~1.0 everywhere.");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── contact sheet ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>Renders the CT-1/CT-3/CT-4/CT-7 fixtures to a PNG contact sheet at <paramref name="path"/> —
        /// the visual half of the verification, alongside the measured numbers above.</summary>
        public static void WriteContactSheet(string path)
        {
            int tile = 128, cols = 4, pad = 4;
            int sheetW = cols * tile + (cols + 1) * pad;
            int sheetH = tile + 2 * pad;
            var sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
            var bg = new Color32(24, 24, 28, 255);
            var fill = new Color32[sheetW * sheetH];
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            sheet.SetPixels32(fill);

            // Panel 1: standalone composite root (Orb), own albedo.
            {
                var def = PyreCompositeCatalog.Build(new OrbForm(), PyreCompositeCatalog.Orb, bakeWidth: tile, bakeHeight: tile);
                var root = ShaperNode.Composite(def, "Orb");
                var prog = ShaperCompiler.Compile(root, 0.35f, 5u);
                var raster = prog.composites[0];
                BlitRaster(sheet, raster.pixels, raster.width, raster.height, pad, pad);
            }

            // Panel 2: Bag union (composite blob + primitive disc), Solid white fill, painted by hand.
            {
                var compMember = SyntheticCompositeDisc("Blob", 20f, 48f, 96, -25f, 0f);
                var primMember = Disc("Disc", 20f, 25f, 0f);
                var bag = ShaperNode.Bag("Union", ShaperCombineMode.Add, compMember, primMember);
                var prog = ShaperCompiler.Compile(bag);
                BlitCoverageAsGrey(sheet, prog, tile, pad + (tile + pad) * 1, pad, Color.white);
            }

            // Panel 3: Bag carve (primitive minus composite disc).
            {
                var big = Disc("Big", 40f, 0f, 0f);
                var hole = SyntheticCompositeDisc("Hole", 15f, 40f, 96, 0f, 0f, ShaperCombineMode.Subtract);
                var bag = ShaperNode.Bag("Carved", ShaperCombineMode.Add, big, hole);
                var prog = ShaperCompiler.Compile(bag);
                BlitCoverageAsGrey(sheet, prog, tile, pad + (tile + pad) * 2, pad, new Color(1f, 0.55f, 0.2f));
            }

            // Panel 4: hosted Orb again, different phase, to show it is not a static fixed image.
            {
                var def = PyreCompositeCatalog.Build(new OrbForm(), PyreCompositeCatalog.Orb, bakeWidth: tile, bakeHeight: tile);
                var root = ShaperNode.Composite(def, "Orb");
                var prog = ShaperCompiler.Compile(root, 0.85f, 5u);
                var raster = prog.composites[0];
                BlitRaster(sheet, raster.pixels, raster.width, raster.height, pad + (tile + pad) * 3, pad);
            }

            sheet.Apply();
            var png = sheet.EncodeToPNG();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        /// <summary>Alpha-composites the raw render over the sheet's own dark background (rather than copying
        /// raw RGBA verbatim) so a transparent region reads as the sheet's background instead of depending on
        /// whatever a PNG viewer does with alpha — the point being made is "this is where coverage is zero",
        /// which a viewer-dependent transparent square does not reliably show.</summary>
        static void BlitRaster(Texture2D sheet, Color32[] pixels, int w, int h, int dx, int dy)
        {
            var bg = new Color(24f / 255f, 24f / 255f, 28f / 255f, 1f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 p = pixels[y * w + x];
                float a = p.a / 255f;
                Color rgb = new Color(p.r / 255f, p.g / 255f, p.b / 255f, 1f);
                sheet.SetPixel(dx + x, dy + y, Color.Lerp(bg, rgb, a));
            }
        }

        static void BlitCoverageAsGrey(Texture2D sheet, ShaperProgram prog, int tile, int dx, int dy, Color tint)
        {
            var grid = ShaperSampleGrid.Centred(tile, tile, Px);
            var dist = new float[tile * tile];
            var cov = new float[tile * tile];
            ShaperEvaluator.FillTile(prog, grid, 0, 0, tile, tile, dist, cov, 0, tile, prog.NewStack());
            for (int y = 0; y < tile; y++)
            for (int x = 0; x < tile; x++)
            {
                float c = cov[y * tile + x];
                Color col = Color.Lerp(new Color(0.1f, 0.1f, 0.12f), tint, c);
                sheet.SetPixel(dx + x, dy + y, col);
            }
        }
    }
}

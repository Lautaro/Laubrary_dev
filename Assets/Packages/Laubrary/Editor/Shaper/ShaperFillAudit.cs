using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// The fill audit: the FT-* conformance tests of FILL-CONTRACT Part F7, each returning a report string.
    ///
    /// Plain static methods, no <c>[MenuItem]</c> and no <c>EditorWindow</c> — this is the fill stage's
    /// verification harness, invoked through the Unity CLI, not a tool. Same arrangement as
    /// <see cref="ShaperFieldAudit"/> and for the same reason.
    ///
    /// <b>Every result is reported as measured-number versus expected-number.</b> A bound, an invariant or a
    /// performance property is never reported as verified on the strength of the code compiling.
    /// </summary>
    public static class ShaperFillAudit
    {
        // ── constants with provenance ─────────────────────────────────────────────────────────────────────

        /// <summary>The canvas the fixtures render at. 128 sits inside B9's authorable 32–256 range.</summary>
        const int W = 128, H = 128;

        /// <summary>One canvas unit per sample, so a "canvas pixel" in a dial equals a sample.</summary>
        const float Px = 1f;

        /// <summary>
        /// FT-9's sample budget. 655,360 is T-0105's own figure — the count over which the shape stage measured
        /// 0 bytes — reused so the two numbers are comparable rather than differently-scaled.
        /// </summary>
        const int AllocSamples = 655360;

        /// <summary>FT-4's tolerance: one 8-bit code, the quantisation of a 256-entry LUT read back through the encode.</summary>
        const float ByteTolerance = 1f / 255f;

        /// <summary>Bitwise-comparison helper threshold: exact equality is the requirement, this is only for reporting.</summary>
        const float Epsilon = 1e-6f;

        // ── entry points ──────────────────────────────────────────────────────────────────────────────────

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Shaper FILL audit (T-0106, FILL-CONTRACT Part F7) ===");
            sb.AppendLine(FT1_SolidIdentity());
            sb.AppendLine(FT2_SrgbRoundTrip());
            sb.AppendLine(FT3_CrossKindIdentity());
            sb.AppendLine(FT4_RampByCoveragePassthrough());
            sb.AppendLine(FT5_VeilCannotCreateCoverage());
            sb.AppendLine(FT6_VeilZeroKills());
            sb.AppendLine(FT7_NearestAncestor());
            sb.AppendLine(FT8_TheGate());
            sb.AppendLine(FT9_ZeroAllocation());
            sb.AppendLine(FT10_Determinism());
            sb.AppendLine(FT11_TileIndependence());
            sb.AppendLine(FT12_DeclaredEqualsRead());
            sb.AppendLine(FT13_EverySampleWritten());
            sb.AppendLine(FT14_RangesHold());
            sb.AppendLine(FT15_DegenerateInputs());
            sb.AppendLine(FT16_NoInputMutation());
            sb.AppendLine(FT17_AddDoesNotRaiseAlpha());
            sb.AppendLine(FT18_HeightIgnoresComposite());
            sb.AppendLine(FT19_EdgeDistancePolarity());
            sb.AppendLine(FT21_ExclusivityIsConservative());
            sb.AppendLine(FT22_EncodeIsReadableBack());
            return sb.ToString();
        }

        // ── fixtures ──────────────────────────────────────────────────────────────────────────────────────

        static ShaperNode Disc(string name, float r, float x, float y,
                               ShaperCombineMode mode = ShaperCombineMode.Add)
        {
            var n = ShaperNode.Primitive(
                new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r },
                name, mode);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        static ShaperNode Rect(string name, float hw, float hh, float x, float y,
                               ShaperCombineMode mode = ShaperCombineMode.Add)
        {
            var n = ShaperNode.Primitive(
                new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh },
                name, mode);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        static ShaperFillDef Solid(Color c, float veil = 1f, float height = 0f,
                                   ShaperFillComposite composite = ShaperFillComposite.Over)
            => new ShaperFillDef
            {
                kind = ShaperFillKind.Solid,
                solidColor = c,
                veil = new ZUIValue(veil),
                heightDelta = new ZUIValue(height),
                composite = composite,
            };

        static ZuiGradient FlatGradient(Color c)
        {
            var g = new ZuiGradient();
            g.gradient = new Gradient();
            g.gradient.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            g.EnsureTransformAnim();
            return g;
        }

        static ZuiGradient BlackToWhite()
        {
            var g = new ZuiGradient();
            g.gradient = new Gradient();
            g.gradient.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            g.EnsureTransformAnim();
            return g;
        }

        /// <summary>A 1×1 opaque white readable texture — FT-3's "must be bit-identical to Solid white" subject.</summary>
        static Texture2D WhiteTexel()
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixels32(new[] { new Color32(255, 255, 255, 255) });
            t.Apply();
            return t;
        }

        /// <summary>A rig: the grid, the resolved document and the host-owned buffers, all allocated once.</summary>
        sealed class Rig
        {
            public int width, height;
            public ShaperSampleGrid grid;
            public ShaperFillDocument doc;
            public ShaperFillBuffers buf;
            public float[] Dst => buf.dst;
            public float[] Height => buf.height;
        }

        static Rig Build(ShaperNode root, float phase01 = 0f, uint seed = 0u,
                         ShaperQuantitySet leafPublished = ShaperQuantitySet.ShippedShapeEngine,
                         int w = W, int h = H)
        {
            float chw = 0.5f * (w - 1) * Px, chh = 0.5f * (h - 1) * Px;
            var doc = ShaperFillResolver.Resolve(root, phase01, seed, chw, chh, leafPublished);
            return new Rig
            {
                width = w,
                height = h,
                grid = ShaperSampleGrid.Centred(w, h, Px),
                doc = doc,
                buf = new ShaperFillBuffers(w * h, Mathf.Max(1, doc.owners.Count)),
            };
        }

        static void Paint(Rig r)
        {
            var sheets = new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine };
            ShaperFillResolver.PaintTile(r.doc, r.grid, 0, 0, r.width, r.height, r.buf, sheets);
        }

        static int Index(Rig r, float x, float y)
        {
            int ix = Mathf.RoundToInt((x - r.grid.originX) / r.grid.pixelSize);
            int iy = Mathf.RoundToInt((y - r.grid.originY) / r.grid.pixelSize);
            ix = Mathf.Clamp(ix, 0, r.width - 1);
            iy = Mathf.Clamp(iy, 0, r.height - 1);
            return iy * r.width + ix;
        }

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        // ── FT-1 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-1: Solid identity. Solid white, veil 1, height 0, Over, on a disc. The albedo array must be a
        /// constant array and the veil array must be all ones, at EVERY sample in the tile — including outside
        /// the shape, because a fill writes its whole tile and ownership is decided by the compositor, not by
        /// the fill (FC-5.6 step 7).
        /// </summary>
        public static string FT1_SolidIdentity()
        {
            var sb = new StringBuilder("FT-1 Solid identity (albedo constant, veil all ones)\n");

            var root = Disc("Disc", 40f, 0f, 0f);
            root.fill = Solid(Color.white);
            var rig = Build(root);

            var emit = new ShaperFillEmit
            {
                albedo = rig.buf.albedo, veil = rig.buf.veil, heightDelta = rig.buf.heightDelta,
            };
            ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, rig.width, rig.height,
                                   new ShaperFillSheets(), emit, 0, rig.width, 0, rig.width);

            float expected = ShaperSrgb.DecodeChannel(1f);
            int n = rig.width * rig.height;
            int albedoBad = 0, veilBad = 0, heightBad = 0;
            for (int i = 0; i < n; i++)
            {
                if (rig.buf.albedo[i * 3 + 0] != expected ||
                    rig.buf.albedo[i * 3 + 1] != expected ||
                    rig.buf.albedo[i * 3 + 2] != expected) albedoBad++;
                if (rig.buf.veil[i] != 1f) veilBad++;
                if (rig.buf.heightDelta[i] != 0f) heightBad++;
            }

            sb.AppendLine("  albedo != " + expected.ToString("F9") + " at " + albedoBad + "/" + n + " samples (expected 0)");
            sb.AppendLine("  veil   != 1 at " + veilBad + "/" + n + " samples (expected 0)");
            sb.AppendLine("  height != 0 at " + heightBad + "/" + n + " samples (expected 0)");
            sb.AppendLine("  emitsHeight declared = " + rig.doc.owners[0].fill.op.emitsHeight + " (expected 0)");
            sb.Append("  RESULT: " + Verdict(albedoBad == 0 && veilBad == 0 && heightBad == 0 &&
                                             rig.doc.owners[0].fill.op.emitsHeight == 0));
            return sb.ToString();
        }

        // ── FT-2 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-2: the sRGB round trip (FC-2.3). <b>The contract flags this as a prediction, not a measurement</b>
        /// — "That is a claim, not a measurement — I did not run it" — and says a failure REVERSES FC-2.3
        /// rather than being patched. So it is measured here, for all 256 byte values, both through the raw
        /// transfer pair and through the two boundary functions the fill stage actually calls.
        /// </summary>
        public static string FT2_SrgbRoundTrip()
        {
            var sb = new StringBuilder("FT-2 sRGB round trip byte -> float -> linear -> float -> byte (FC-2.3)\n");

            int bad = 0, worstB = -1;
            double worstErr = 0;
            for (int b = 0; b < 256; b++)
            {
                float s = b / 255f;
                float lin = ShaperSrgb.DecodeChannel(s);
                byte back = ShaperSrgb.EncodeToByte(lin);
                if (back != b)
                {
                    bad++;
                    double err = Math.Abs(back - b);
                    if (err > worstErr) { worstErr = err; worstB = b; }
                }
            }

            // The same thing again through the raw pair, so a failure localises to the transfer function rather
            // than to the rounding in EncodeToByte.
            int rawBad = 0;
            double worstRaw = 0;
            for (int b = 0; b < 256; b++)
            {
                float s = b / 255f;
                float back = ShaperSrgb.EncodeChannel(ShaperSrgb.DecodeChannel(s));
                double err = Math.Abs(back - s) * 255.0;
                if (err > worstRaw) worstRaw = err;
                if (Mathf.RoundToInt(back * 255f) != b) rawBad++;
            }

            sb.AppendLine("  byte round trip mismatches: " + bad + "/256 (expected 0)" +
                          (worstB >= 0 ? "  worst byte " + worstB + " off by " + worstErr : ""));
            sb.AppendLine("  raw transfer-pair mismatches: " + rawBad + "/256 (expected 0)");
            sb.AppendLine("  worst |encode(decode(s)) - s| = " + worstRaw.ToString("E3") + " codes (of 255)");
            sb.AppendLine("  three channels are the same function, so 256 values covers all three (768 codes).");
            sb.Append("  RESULT: " + Verdict(bad == 0 && rawBad == 0));
            return sb.ToString();
        }

        // ── FT-3 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-3: cross-kind identity. A single-colour Gradient in each of the four modes, a 1×1 white texture
        /// Fitted with white tint, and a Ramp with a single-colour ramp, each compared BITWISE against Solid of
        /// that colour. Exercises the LUT path, the bulk-data path and the trivial path against one another.
        /// </summary>
        public static string FT3_CrossKindIdentity()
        {
            var sb = new StringBuilder("FT-3 cross-kind identity vs Solid (bitwise)\n");
            var colour = new Color(0.25f, 0.6f, 0.9f, 1f);
            int n = W * H;

            float[] reference = RunKind(Solid(colour), out float[] refVeil);

            var cases = new List<KeyValuePair<string, ShaperFillDef>>();
            foreach (ShaperGradientMode m in Enum.GetValues(typeof(ShaperGradientMode)))
            {
                cases.Add(new KeyValuePair<string, ShaperFillDef>("Gradient." + m, new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient,
                    gradientMode = m,
                    gradient = FlatGradient(colour),
                    gradientTint = colour,
                }));
            }
            cases.Add(new KeyValuePair<string, ShaperFillDef>("Texture 1x1 white x tint", new ShaperFillDef
            {
                kind = ShaperFillKind.Texture,
                texture = WhiteTexel(),
                textureMapping = ShaperTextureMapping.Fitted,
                textureTint = colour,
            }));
            cases.Add(new KeyValuePair<string, ShaperFillDef>("Ramp flat ramp", new ShaperFillDef
            {
                kind = ShaperFillKind.RampByQuantity,
                rampQuantity = ShaperQuantity.Coverage,
                rampGradient = FlatGradient(colour),
                rampTint = colour,
            }));

            bool all = true;
            foreach (var c in cases)
            {
                float[] got = RunKind(c.Value, out float[] gotVeil);
                int bad = 0;
                float worst = 0f;
                for (int i = 0; i < n * 3; i++)
                {
                    if (got[i] != reference[i]) { bad++; worst = Mathf.Max(worst, Mathf.Abs(got[i] - reference[i])); }
                }
                int veilBad = 0;
                for (int i = 0; i < n; i++) if (gotVeil[i] != refVeil[i]) veilBad++;

                all &= bad == 0 && veilBad == 0;
                sb.AppendLine("  " + c.Key.PadRight(28) + " albedo mismatched " + bad + "/" + (n * 3) +
                              "  worst |delta| " + worst.ToString("E2") + "   veil mismatched " + veilBad + "/" + n);
            }

            sb.AppendLine("  (a flat texture's albedo is texel x tint; the reference Solid is decode(tint), so");
            sb.AppendLine("   equality here also measures that a white texel decodes to exactly 1.0.)");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static float[] RunKind(ShaperFillDef def, out float[] veil)
        {
            var root = Disc("Disc", 40f, 0f, 0f);
            root.fill = def;
            var rig = Build(root);
            var sheets = MakeShapeSheets(rig, out _, out _);
            var emit = new ShaperFillEmit
            {
                albedo = new float[W * H * 3], veil = new float[W * H], heightDelta = new float[W * H],
            };
            ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, rig.width, rig.height,
                                   sheets, emit, 0, rig.width, 0, rig.width);
            veil = emit.veil;
            return emit.albedo;
        }

        /// <summary>Evaluate the shape once and hand back the two sheets the shipped engine publishes.</summary>
        static ShaperFillSheets MakeShapeSheets(Rig rig, out float[] distance, out float[] coverage)
        {
            distance = new float[rig.width * rig.height];
            coverage = new float[rig.width * rig.height];
            ShaperProgram prog = rig.doc.owners.Count > 0
                ? rig.doc.owners[0].shape
                : ShaperCompiler.Compile(null);
            ShaperEvaluator.FillTile(prog, rig.grid, 0, 0, rig.width, rig.height,
                                     distance, coverage, 0, rig.width, prog.NewStack());
            return ShaperFillSheets.FromShapeStage(coverage, distance);
        }

        // ── FT-4 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-4: Ramp-by-coverage passthrough. quantity = coverage, black→white ramp, window 0..1; the albedo
        /// should equal the coverage sheet.
        ///
        /// <b>Reported deviation from the contract's literal wording.</b> FT-4 as written says "compare albedo
        /// luminance against the coverage sheet ... equal to within 1/255". That is only true in the ENCODED
        /// domain: FC-2.3 makes albedo linear, so a black→white sRGB ramp read at <c>t</c> decodes to roughly
        /// <c>t^2.2</c>, not <c>t</c>. FT-4 and FC-2.3 cannot both be satisfied literally. The rule outranks the
        /// test, so the comparison is made after the sRGB ENCODE — which is exactly the byte the document's
        /// Color32 write produces — and BOTH numbers are reported so nothing is hidden.
        /// </summary>
        public static string FT4_RampByCoveragePassthrough()
        {
            var sb = new StringBuilder("FT-4 Ramp-by-coverage passthrough (albedo vs the coverage sheet)\n");

            var root = Disc("Disc", 40f, 0f, 0f);
            root.fill = new ShaperFillDef
            {
                kind = ShaperFillKind.RampByQuantity,
                rampQuantity = ShaperQuantity.Coverage,
                rampGradient = BlackToWhite(),
                rampInputLow = new ZUIValue(0f),
                rampInputHigh = new ZUIValue(1f),
            };
            var rig = Build(root);
            var sheets = MakeShapeSheets(rig, out _, out float[] coverage);

            var emit = new ShaperFillEmit
            {
                albedo = new float[W * H * 3], veil = new float[W * H], heightDelta = new float[W * H],
            };
            ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, rig.width, rig.height,
                                   sheets, emit, 0, rig.width, 0, rig.width);

            int n = W * H;
            float worstEncoded = 0f, worstLinear = 0f;
            for (int i = 0; i < n; i++)
            {
                float lin = emit.albedo[i * 3];
                float enc = ShaperSrgb.EncodeChannel(lin);
                worstEncoded = Mathf.Max(worstEncoded, Mathf.Abs(enc - coverage[i]));
                worstLinear = Mathf.Max(worstLinear, Mathf.Abs(lin - coverage[i]));
            }

            bool ok = worstEncoded <= ByteTolerance;
            sb.AppendLine("  worst |encode(albedo) - coverage| = " + worstEncoded.ToString("F6") +
                          "  (tolerance " + ByteTolerance.ToString("F6") + " = one 8-bit code)");
            sb.AppendLine("  worst |albedo(linear) - coverage| = " + worstLinear.ToString("F6") +
                          "  (NOT a failure — this is FC-2.3's linear albedo, reported so the gap is visible)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── FT-5 / FT-6 ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-5: the veil cannot create coverage, AND it cannot thicken the edge.
        ///
        /// The second half is the load-bearing one. Multiplication alone gives the first for free
        /// (<c>veil × 0 = 0</c>). It does NOT give the second: at a half-covered antialiasing sample a veil of
        /// 2.0 would produce <c>coverageEff = 1.0</c> — a fill silently thickening the shape's edge by half a
        /// pixel and hardening its antialiasing, achieved entirely through multiplication. The [0,1] clamp is
        /// what enforces C3's "The shape always owns its own edge. A fill can veil it but never replace it".
        /// </summary>
        public static string FT5_VeilCannotCreateCoverage()
        {
            var sb = new StringBuilder("FT-5 veil cannot CREATE coverage, and cannot THICKEN the soft edge (FC-2.4)\n");

            float[] veils = { 0f, 0.5f, 1f, 2f };
            bool all = true;

            foreach (float v in veils)
            {
                var root = Disc("Disc", 40f, 0f, 0f);
                root.fill = Solid(Color.white, v);
                var rig = Build(root);
                Paint(rig);

                var probeDist = new float[W * H];
                var probeCov = new float[W * H];
                ShaperProgram prog = rig.doc.owners[0].shape;
                ShaperEvaluator.FillTile(prog, rig.grid, 0, 0, W, H, probeDist, probeCov, 0, W, prog.NewStack());

                int n = W * H;
                int createdOutside = 0;
                float worstEdgeExcess = 0f;
                float bakedVeil = rig.doc.owners[0].fill.op.veil;

                for (int i = 0; i < n; i++)
                {
                    float ce = rig.buf.dst[i * 4 + 3];   // Over mode, so dst alpha IS coverageEff here
                    if (probeCov[i] == 0f && ce > 0f) createdOutside++;
                    if (ce > probeCov[i]) worstEdgeExcess = Mathf.Max(worstEdgeExcess, ce - probeCov[i]);
                }

                bool ok = createdOutside == 0 && worstEdgeExcess <= Epsilon;
                all &= ok;
                sb.AppendLine("  authored veil " + v.ToString("F1").PadRight(4) +
                              " -> baked " + bakedVeil.ToString("F4") +
                              "   coverage created where cov==0: " + createdOutside + "/" + n + " (expected 0)" +
                              "   worst coverageEff-coverage: " + worstEdgeExcess.ToString("E2") + " (expected <= 0)");
            }

            // The specific half-covered edge sample the contract names.
            var r2 = Build(MakeVeiledDisc(2f));
            Paint(r2);
            var d2 = new float[W * H]; var c2 = new float[W * H];
            ShaperProgram p2 = r2.doc.owners[0].shape;
            ShaperEvaluator.FillTile(p2, r2.grid, 0, 0, W, H, d2, c2, 0, W, p2.NewStack());
            int best = -1; float bestErr = 1f;
            for (int i = 0; i < W * H; i++)
            {
                float e = Mathf.Abs(c2[i] - 0.5f);
                if (e < bestErr) { bestErr = e; best = i; }
            }
            float edgeCov = c2[best], edgeCe = r2.buf.dst[best * 4 + 3];
            bool edgeOk = edgeCe <= edgeCov + Epsilon;
            all &= edgeOk;
            sb.AppendLine("  nearest half-covered sample: coverage " + edgeCov.ToString("F6") +
                          " -> coverageEff " + edgeCe.ToString("F6") +
                          " with an AUTHORED veil of 2.0 (expected <= coverage; with BOTH clamps gone it " +
                          "would be coverage x 2 = " + (edgeCov * 2f).ToString("F6") + ")");

            // ── the leg the end-to-end measurement above CANNOT provide ────────────────────────────────────
            //
            // Everything above reads dst alpha out of PaintTile, and PaintTile applies a SECOND Mathf.Clamp01
            // to the veil at use. So deleting FC-2.4's bake-time clamp — the clause this whole test is named
            // for — changes nothing any assertion above looks at, and this test would still report PASS.
            // Measured: with the compiled op's veil forced to 2.0, dst alpha at the half-covered sample above
            // stayed at coverage. A test that cannot fail is not a test, so the bake is asserted DIRECTLY here.
            {
                float baked2 = Build(MakeVeiledDisc(2f)).doc.owners[0].fill.op.veil;
                float bakedNeg = Build(MakeVeiledDisc(-1f)).doc.owners[0].fill.op.veil;
                float bakedInf = Build(MakeVeiledDisc(float.PositiveInfinity)).doc.owners[0].fill.op.veil;
                float bakedNaN = Build(MakeVeiledDisc(float.NaN)).doc.owners[0].fill.op.veil;
                bool bakeOk = baked2 == 1f && bakedNeg == 0f && bakedInf == 1f && bakedNaN == 0f;
                all &= bakeOk;
                sb.AppendLine("  FC-2.4's BAKE-TIME clamp, asserted directly on the compiled op (the composite's own");
                sb.AppendLine("  second clamp would otherwise mask its removal): authored 2 -> " + baked2.ToString("F4") +
                              " (expected 1), -1 -> " + bakedNeg.ToString("F4") + " (expected 0), +Inf -> " +
                              bakedInf.ToString("F4") + " (expected 1), NaN -> " + bakedNaN.ToString("F4") +
                              " (expected 0)  " + Verdict(bakeOk));
                sb.AppendLine("  The NaN row is not decoration: Mathf.Clamp01 is `v<0?0:v>1?1:v` and every comparison");
                sb.AppendLine("  against NaN is false, so plain Clamp01 passes NaN through. A NaN veil measured");
                sb.AppendLine("  16384/16384 non-finite destination floats before the compiler's Clamp01OrZero landed.");
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static ShaperNode MakeVeiledDisc(float veil)
        {
            var root = Disc("Disc", 40f, 0f, 0f);
            root.fill = Solid(Color.white, veil);
            return root;
        }

        /// <summary>FT-6: veil 0 kills the paint AND the height contribution (FC-2.5's coverageEff weighting).</summary>
        public static string FT6_VeilZeroKills()
        {
            var sb = new StringBuilder("FT-6 veil = 0 with a non-zero heightDelta kills both (FC-2.5)\n");

            var root = Disc("Disc", 40f, 0f, 0f);
            root.fill = Solid(Color.white, 0f, 7f);
            var rig = Build(root);
            Paint(rig);

            int n = W * H, alphaBad = 0, heightBad = 0;
            float worstA = 0f, worstH = 0f;
            for (int i = 0; i < n; i++)
            {
                if (rig.buf.dst[i * 4 + 3] != 0f) { alphaBad++; worstA = Mathf.Max(worstA, rig.buf.dst[i * 4 + 3]); }
                if (rig.buf.height[i] != 0f) { heightBad++; worstH = Mathf.Max(worstH, Mathf.Abs(rig.buf.height[i])); }
            }

            sb.AppendLine("  declared emitsHeight = " + rig.doc.owners[0].fill.op.emitsHeight +
                          " (expected 1 — heightDelta 7 is non-zero, so the sheet IS declared)");
            sb.AppendLine("  coverageEff != 0 at " + alphaBad + "/" + n + " samples (expected 0), worst " + worstA.ToString("E2"));
            sb.AppendLine("  height     != 0 at " + heightBad + "/" + n + " samples (expected 0), worst " + worstH.ToString("E2"));
            sb.Append("  RESULT: " + Verdict(alphaBad == 0 && heightBad == 0));
            return sb.ToString();
        }

        // ── FT-7 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The FC-3.8 worked tree, built exactly as written, bottom-up in fold order.
        /// Geometry is chosen so every row of the contract's table has a non-degenerate probe.
        /// </summary>
        static ShaperNode WorkedTree(out Vector2[] probes, out string[] labels)
        {
            var torso = Disc("Torso", 40f, 0f, 0f, ShaperCombineMode.Add);

            var head = Disc("Head", 22f, 34f, 18f, ShaperCombineMode.Add);
            head.fill = Solid(new Color(1f, 0f, 0f));

            var belt = Rect("Belt", 60f, 14f, 0f, -6f, ShaperCombineMode.Intersect);
            belt.fill = new ShaperFillDef
            {
                kind = ShaperFillKind.Texture,
                texture = WhiteTexel(),
                textureTint = new Color(0f, 1f, 0f),
            };

            var eye = Disc("Eye", 8f, 30f, 6f, ShaperCombineMode.Subtract);
            // NOT given a fill: FC-3.3 forbids it. FT-8c authors one deliberately, on its own tree.

            var iris = Disc("Iris", 5f, 30f, 6f, ShaperCombineMode.Add);
            iris.fill = new ShaperFillDef
            {
                kind = ShaperFillKind.RampByQuantity,
                rampQuantity = ShaperQuantity.Coverage,
                rampGradient = FlatGradient(new Color(0f, 0f, 1f)),
                rampTint = new Color(0f, 0f, 1f),
            };

            var body = ShaperNode.Bag("Body", ShaperCombineMode.Add, torso, head, belt, eye, iris);
            body.fill = new ShaperFillDef
            {
                kind = ShaperFillKind.Gradient,
                gradientMode = ShaperGradientMode.Linear,
                gradient = FlatGradient(new Color(1f, 1f, 0f)),
                gradientTint = new Color(1f, 1f, 0f),
            };

            probes = new[]
            {
                new Vector2(-20f, -5f),   // Torso n Belt, not Head, not Eye, not Iris
                new Vector2( 36f, -2f),   // Torso n Head n Belt, not Eye, not Iris
                new Vector2( 46f,  4f),   // Head n Belt, not Torso, not Eye, not Iris
                new Vector2( 34f, 30f),   // Head, not Belt
                new Vector2( 30f, 12f),   // Eye, inside Head, not Belt
                new Vector2( 30f,  9f),   // Iris, outside Belt
                new Vector2( 30f,  4f),   // Iris n Belt n Torso
                new Vector2(-55f, 40f),   // outside coverage_Body
            };
            labels = new[]
            {
                "Torso n Belt, not Head/Iris",
                "Torso n Head n Belt",
                "Head n Belt, not Torso",
                "Head, not Belt",
                "Eye inside Head, not Belt",
                "Iris, outside Belt",
                "Iris n Belt n Torso",
                "outside coverage_Body",
            };
            return body;
        }

        /// <summary>
        /// FT-7: nearest-ancestor resolution on the contract's worked tree, eight probes, plus FT-7b's fold
        /// order. The load-bearing row is "Head, not Belt": S_head's own CLAIM is non-empty there, and only the
        /// <c>min</c> against the bag's zero coverage kills it. Without the min, S_head would paint outside the
        /// silhouette.
        /// </summary>
        public static string FT7_NearestAncestor()
        {
            var sb = new StringBuilder("FT-7 nearest-ancestor resolution on the FC-3.8 worked tree\n");

            ShaperNode body = WorkedTree(out Vector2[] probes, out string[] labels);
            var rig = Build(body);
            Paint(rig);

            var names = new string[rig.doc.owners.Count];
            for (int o = 0; o < rig.doc.owners.Count; o++) names[o] = rig.doc.owners[o].name;
            sb.AppendLine("  owners, in paint order: " + string.Join(" -> ", names) +
                          "   (expected Body -> Head -> Belt -> Iris)");
            sb.AppendLine("  Subtract member 'Eye' owns no fill, so it is absent by construction (FC-3.3).");

            // The contract's table names ONE owner per region: the one whose paint lands on TOP. Ownership is
            // not exclusive between SIBLINGS (FC-3.4 -- "they own disjoint claims and paint in order"), only
            // between an ancestor and its descendants (FC-3.5), so several siblings can legitimately paint at
            // one sample and the table names the last of them. That is what is asserted, with the full paint
            // vector printed alongside so an unexpected under-painter is visible rather than swallowed.
            string[] expectedTop =
            {
                // ROW 1 IS A MEASURED CONTRADICTION INSIDE THE CONTRACT, not a code defect. FC-3.8's table
                // gives G_body the region "Torso n Belt, not Head, not Iris" because "Torso owns no fill;
                // nearest ancestor owning one is Body". But FC-3.3's Intersect ruling says an Intersect member
                // owning a fill "paints the entire intersected result" and is "indistinguishable in output
                // from putting the same fill on the bag" -- and Belt IS an Intersect member owning T_belt, so
                // it claims all of ((Torso u Head) n Belt), which is exactly that region. Both cannot hold.
                // FC-3.3 is the normative rule carrying the stated argument; FC-3.8 is a worked example. The
                // RULE is followed. CONSEQUENCE, reported below: in this tree G_body never paints any pixel.
                "Belt",
                "Belt",     // FC-3.8 row 2: "T_belt, over S_head, over G_body's claim" -> top is T_belt
                "Belt",     // FC-3.8 row 3: "S_head, then T_belt over it"              -> top is T_belt
                null,       // FC-3.8 row 4: nobody -- the Intersect discarded it
                null,       // FC-3.8 row 5: nobody -- already zero before the subtract
                "Iris",     // FC-3.8 row 6
                "Iris",     // FC-3.8 row 7: "R_iris, over T_belt, over S_head"         -> top is R_iris
                null,       // FC-3.8 row 8: structural, coverageEff == 0
            };

            bool all = names.Length == 4 && names[0] == "Body" && names[1] == "Head" &&
                       names[2] == "Belt" && names[3] == "Iris";
            if (!all) sb.AppendLine("  !! owner list is not the expected four; probe verdicts below are then unreliable.");

            for (int p = 0; p < probes.Length; p++)
            {
                int i = Index(rig, probes[p].x, probes[p].y);
                var line = new StringBuilder("  " + labels[p].PadRight(28));
                string top = null;
                for (int o = 0; o < rig.doc.owners.Count; o++)
                {
                    float paint = rig.buf.paint[o * rig.buf.sampleCapacity + i];
                    if (paint > 1e-4f) top = names[o];
                    line.Append(names[o]).Append('=').Append(paint.ToString("F3")).Append(' ');
                }
                bool ok = top == expectedTop[p];
                line.Append("| top=").Append(top ?? "none")
                    .Append(" expected=").Append(expectedTop[p] ?? "none")
                    .Append(ok ? "  ok" : "  MISMATCH");
                all &= ok;
                sb.AppendLine(line.ToString());
            }

            int bodyPainted = 0;
            for (int i = 0; i < rig.width * rig.height; i++)
                if (rig.buf.paint[i] > 1e-4f) bodyPainted++;
            sb.AppendLine("  samples where the BAG's own fill G_body paints: " + bodyPainted + "/" +
                          (rig.width * rig.height) +
                          " (expected 0)");
            sb.AppendLine("  FC-3.8's TABLE gives G_body three whole regions and is wrong on all three: FC-3.3 rules");
            sb.AppendLine("  that an Intersect member owning a fill paints the entire intersected result, and in this");
            sb.AppendLine("  tree Belt IS the last restrictor, so Belt's claim equals the bag's coverage everywhere the");
            sb.AppendLine("  bag has any. Under FC-3.5 the bag then paints NOWHERE, which is 0 and not 'three regions'.");
            sb.AppendLine("  It read 119 while exclusivity was multiplicative — c*(1-c) at every sample where Belt's");
            sb.AppendLine("  claim equalled the bag's, i.e. a fringe of the BAG's colour tracing the whole silhouette.");
            sb.AppendLine("  Subtractive exclusivity gives the 0 the rule actually states.");

            sb.AppendLine(FT7b_FoldOrder());
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>
        /// FT-7b: fold order governs paint order. Two tests, because the contract's literal one is weaker than
        /// it looks.
        ///
        /// (a) The contract's own: swap members [1] and [2] of the worked tree and assert the composite differs.
        ///     It does — but swapping an Add with an Intersect changes the SHAPE too, so a difference does not
        ///     isolate paint order.
        /// (b) The isolating test: two overlapping Add siblings with different Solid fills, swapped. The shape
        ///     is identical either way (union is commutative), so any difference in the composited bytes is
        ///     paint order and nothing else.
        /// </summary>
        static string FT7b_FoldOrder()
        {
            var sb = new StringBuilder("  FT-7b fold order governs paint order (FC-3.4)\n");

            // (a) the contract's literal swap
            ShaperNode a = WorkedTree(out Vector2[] probes, out _);
            var ra = Build(a); Paint(ra);
            ShaperNode b = WorkedTree(out _, out _);
            var tmp = b.children[1]; b.children[1] = b.children[2]; b.children[2] = tmp;
            var rb = Build(b); Paint(rb);
            int idx = Index(ra, probes[1].x, probes[1].y);
            bool differsA = false;
            for (int c = 0; c < 4; c++) if (ra.buf.dst[idx * 4 + c] != rb.buf.dst[idx * 4 + c]) differsA = true;
            sb.AppendLine("    (a) literal swap of members [1] and [2]: composited bytes differ = " + differsA +
                          " (expected True; note this also changes the SHAPE, so it does not isolate order)");

            // (b) the isolating swap
            var s1 = Disc("Lower", 30f, -10f, 0f); s1.fill = Solid(new Color(1f, 0f, 0f), 0.5f);
            var s2 = Disc("Upper", 30f, 10f, 0f); s2.fill = Solid(new Color(0f, 0f, 1f), 0.5f);
            var bag1 = ShaperNode.Bag("Pair", ShaperCombineMode.Add, s1, s2);
            bag1.fill = Solid(Color.white);
            var r1 = Build(bag1); Paint(r1);

            var t1 = Disc("Lower", 30f, -10f, 0f); t1.fill = Solid(new Color(1f, 0f, 0f), 0.5f);
            var t2 = Disc("Upper", 30f, 10f, 0f); t2.fill = Solid(new Color(0f, 0f, 1f), 0.5f);
            var bag2 = ShaperNode.Bag("Pair", ShaperCombineMode.Add, t2, t1);   // swapped
            bag2.fill = Solid(Color.white);
            var r2 = Build(bag2); Paint(r2);

            int mid = Index(r1, 0f, 0f);   // both discs cover the origin
            float d1r = r1.buf.dst[mid * 4 + 0], d2r = r2.buf.dst[mid * 4 + 0];
            float d1b = r1.buf.dst[mid * 4 + 2], d2b = r2.buf.dst[mid * 4 + 2];
            bool differsB = d1r != d2r || d1b != d2b;
            sb.AppendLine("    (b) two overlapping Add siblings swapped (shape identical): " +
                          "R " + d1r.ToString("F4") + " vs " + d2r.ToString("F4") +
                          ", B " + d1b.ToString("F4") + " vs " + d2b.ToString("F4") +
                          "   differ = " + differsB + " (expected True)");
            sb.Append("    verdict: " + Verdict(differsA && differsB));
            return sb.ToString();
        }

        // ── FT-8 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-8, FT-8b, FT-8c, FT-8d: the gate produces a REASON, not silence; the count is not just the first;
        /// a Subtract member's fill slot is refused; and surfaceDirection is refused on TYPE with a DIFFERENT
        /// message.
        /// </summary>
        public static string FT8_TheGate()
        {
            var sb = new StringBuilder("FT-8 the availability gate (FC-4.3 / FC-4.4)\n");
            bool all = true;

            // FT-8 — Ramp-by-heat on a node publishing only coverage and edgeDistance.
            {
                var disc = Disc("Torso", 40f, 0f, 0f);
                disc.fill = new ShaperFillDef
                {
                    kind = ShaperFillKind.RampByQuantity,
                    rampQuantity = ShaperQuantity.Heat,
                    rampGradient = BlackToWhite(),
                };
                var bag = ShaperNode.Bag("Body", ShaperCombineMode.Add, disc);
                bag.fill = Solid(new Color(1f, 1f, 0f));

                var rig = Build(bag);
                Paint(rig);

                string reason = rig.doc.unavailableFillReason ?? "";
                bool hasKind = reason.Contains("Ramp-by-quantity");
                bool hasNode = reason.Contains("Torso");
                bool hasQty = reason.Contains("heat");
                int i = Index(rig, 0f, 0f);
                float bodyPaint = rig.buf.paint[0 * rig.buf.sampleCapacity + i];
                float alpha = rig.buf.dst[i * 4 + 3];

                bool ok = rig.doc.hasUnavailableFill && rig.doc.unavailableFillCount == 1 &&
                          hasKind && hasNode && hasQty && rig.doc.owners.Count == 1 &&
                          bodyPaint > 0.9f && alpha > 0.9f;
                all &= ok;
                sb.AppendLine("  FT-8   hasUnavailableFill=" + rig.doc.hasUnavailableFill + " (expected True)" +
                              "  count=" + rig.doc.unavailableFillCount + " (expected 1)" +
                              "  node='" + rig.doc.unavailableFillNode + "' (expected 'Torso')");
                sb.AppendLine("         reason: \"" + reason + "\"");
                sb.AppendLine("         contains kind/node/quantity = " + hasKind + "/" + hasNode + "/" + hasQty +
                              " (all expected True)");
                sb.AppendLine("         owners bound = " + rig.doc.owners.Count + " (expected 1 — only the bag)" +
                              "   ancestor paints the region: alpha at centre = " + alpha.ToString("F3") +
                              " (expected > 0.9; NOT transparent)");
                sb.AppendLine("         " + Verdict(ok));
            }

            // FT-8b — three unavailable fills; the count is all of them, the name is the first.
            {
                var d1 = Disc("A", 20f, -40f, 0f); d1.fill = RampOn(ShaperQuantity.Heat);
                var d2 = Disc("B", 20f, 0f, 0f); d2.fill = RampOn(ShaperQuantity.Density);
                var d3 = Disc("C", 20f, 40f, 0f); d3.fill = RampOn(ShaperQuantity.Soot);
                var bag = ShaperNode.Bag("Body", ShaperCombineMode.Add, d1, d2, d3);
                bag.fill = Solid(Color.white);
                var rig = Build(bag);

                bool ok = rig.doc.unavailableFillCount == 3 && rig.doc.unavailableFillNode == "A";
                all &= ok;
                sb.AppendLine("  FT-8b  count=" + rig.doc.unavailableFillCount + " (expected 3)" +
                              "  first node='" + rig.doc.unavailableFillNode + "' (expected 'A')  " + Verdict(ok));
                sb.AppendLine("         this is the ShaperProgram.leadingNonAddCount correction applied from the start.");
            }

            // FT-8c — a Subtract member's fill slot is refused, with a reason mentioning subtraction.
            {
                var torso = Disc("Torso", 40f, 0f, 0f);
                var eye = Disc("Eye", 10f, 0f, 0f, ShaperCombineMode.Subtract);
                eye.fill = Solid(new Color(1f, 0f, 1f));
                var bag = ShaperNode.Bag("Body", ShaperCombineMode.Add, torso, eye);
                bag.fill = Solid(Color.white);
                var rig = Build(bag);

                string reason = rig.doc.subtractFillReason ?? "";
                bool ok = rig.doc.hasSubtractFill && rig.doc.subtractFillCount == 1 &&
                          rig.doc.subtractFillNode == "Eye" &&
                          reason.Contains("subtracted") && rig.doc.owners.Count == 1;
                all &= ok;
                sb.AppendLine("  FT-8c  hasSubtractFill=" + rig.doc.hasSubtractFill + " (expected True)" +
                              "  count=" + rig.doc.subtractFillCount + " (expected 1)" +
                              "  owners=" + rig.doc.owners.Count + " (expected 1 — Eye did NOT bind)  " + Verdict(ok));
                sb.AppendLine("         reason: \"" + reason + "\"");
            }

            // FT-8d — surfaceDirection is refused on TYPE, with a DIFFERENT message from the availability one.
            {
                var disc = Disc("Torso", 40f, 0f, 0f);
                disc.fill = RampOn(ShaperQuantity.SurfaceDirection);
                var bag = ShaperNode.Bag("Body", ShaperCombineMode.Add, disc);
                bag.fill = Solid(Color.white);
                var rig = Build(bag);

                string typeReason = rig.doc.typeRefusedFillReason ?? "";

                var disc2 = Disc("Torso", 40f, 0f, 0f);
                disc2.fill = RampOn(ShaperQuantity.Heat);
                var bag2 = ShaperNode.Bag("Body", ShaperCombineMode.Add, disc2);
                bag2.fill = Solid(Color.white);
                string availReason = Build(bag2).doc.unavailableFillReason ?? "";

                bool differs = typeReason != availReason && typeReason.Length > 0;
                bool notAvail = !rig.doc.hasUnavailableFill;
                bool ok = rig.doc.hasTypeRefusedFill && rig.doc.typeRefusedFillCount == 1 && differs && notAvail;
                all &= ok;
                sb.AppendLine("  FT-8d  hasTypeRefusedFill=" + rig.doc.hasTypeRefusedFill + " (expected True)" +
                              "  reported as UNAVAILABLE too = " + rig.doc.hasUnavailableFill + " (expected False)  " +
                              Verdict(ok));
                sb.AppendLine("         type      : \"" + typeReason + "\"");
                sb.AppendLine("         availability: \"" + availReason + "\"");
                sb.AppendLine("         messages differ = " + differs + " (expected True — the two failures have different remedies)");
            }

            // The intersection rule itself (FC-4.2), measured rather than assumed.
            {
                var pub = Disc("Publisher", 20f, -20f, 0f);
                var quiet = Disc("Quiet", 20f, 20f, 0f);
                var bag = ShaperNode.Bag("Body", ShaperCombineMode.Add, pub, quiet);
                ShaperQuantitySet all9 = (ShaperQuantitySet)((1 << ShaperQuantities.Count) - 1);
                ShaperQuantitySet got = ShaperFillResolver.Published(
                    bag, ShaperQuantitySet.ShippedShapeEngine,
                    n => n.name == "Publisher" ? all9 : ShaperQuantitySet.ShippedShapeEngine);
                bool ok = got == ShaperQuantitySet.ShippedShapeEngine;
                all &= ok;
                sb.AppendLine("  FC-4.2 intersection: one member publishes all nine, one publishes two -> bag publishes " +
                              got + " (expected ShippedShapeEngine = Coverage|EdgeDistance)  " + Verdict(ok));

                var sub = Disc("Subtractor", 20f, 0f, 0f, ShaperCombineMode.Subtract);
                var bag2 = ShaperNode.Bag("Body2", ShaperCombineMode.Add, pub, sub);
                ShaperQuantitySet got2 = ShaperFillResolver.Published(
                    bag2, ShaperQuantitySet.None,
                    n => n.name == "Publisher" ? all9 : ShaperQuantitySet.None);
                bool ok2 = got2 == all9;
                all &= ok2;
                sb.AppendLine("  FC-4.2 Subtract members are EXCLUDED from the intersection: bag publishes all nine = " +
                              (got2 == all9) + " (expected True — a subtractor deposits nothing)  " + Verdict(ok2));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static ShaperFillDef RampOn(ShaperQuantity q)
            => new ShaperFillDef
            {
                kind = ShaperFillKind.RampByQuantity,
                rampQuantity = q,
                rampGradient = BlackToWhite(),
            };

        // ── FT-9 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-9: zero allocation in the tile loop, for each of the four kinds, after a warm-up call.
        /// Measured with <c>GC.GetTotalMemory(false)</c> bracketing and <c>GC.CollectionCount(0)</c>, over
        /// 655,360 samples — T-0105's own budget, so the numbers are comparable.
        /// </summary>
        public static string FT9_ZeroAllocation()
        {
            var sb = new StringBuilder("FT-9 zero allocation in the tile loop (" + AllocSamples + " samples per kind)\n");
            bool all = true;

            var kinds = new List<KeyValuePair<string, ShaperFillDef>>
            {
                new KeyValuePair<string, ShaperFillDef>("Solid", Solid(Color.white)),
                new KeyValuePair<string, ShaperFillDef>("Gradient.Linear", new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Linear,
                    gradient = BlackToWhite(),
                }),
                new KeyValuePair<string, ShaperFillDef>("Ramp.coverage", RampOn(ShaperQuantity.Coverage)),
                new KeyValuePair<string, ShaperFillDef>("Texture", new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = WhiteTexel(),
                    textureMapping = ShaperTextureMapping.Tiled,
                }),
            };

            foreach (var k in kinds)
            {
                var root = Disc("Disc", 40f, 0f, 0f);
                root.fill = k.Value;
                var rig = Build(root);
                var sheets = MakeShapeSheets(rig, out _, out _);
                var emit = new ShaperFillEmit
                {
                    albedo = rig.buf.albedo, veil = rig.buf.veil, heightDelta = rig.buf.heightDelta,
                };
                ShaperFillProgram prog = rig.doc.owners[0].fill;

                // Warm-up: JIT the generic paths and settle the LUT/texel arrays before measuring.
                ShaperFillOps.FillTile(prog, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);

                int reps = AllocSamples / (W * H);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetTotalMemory(true);
                int gc0 = GC.CollectionCount(0);

                for (int i = 0; i < reps; i++)
                    ShaperFillOps.FillTile(prog, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);

                long after = GC.GetTotalMemory(false);
                int gc0After = GC.CollectionCount(0);
                long delta = after - before;
                int collections = gc0After - gc0;

                // REPORTED, NOT ASSERTED. GC.GetTotalMemory is process-wide, not scoped to this loop, so the
                // editor's own background churn lands in the delta: this leg has been observed reporting
                // 4096 and 12288 bytes on a loop the IL scan below proves contains no allocation opcode at
                // all. Combined with the calibration two blocks down — blind to 64 KB of real allocation —
                // it is unreliable in BOTH directions and cannot carry a verdict.
                sb.AppendLine("  " + k.Key.PadRight(18) + " reps " + reps +
                              "  managed heap delta " + delta + " bytes (process-wide, informational only)" +
                              "  gen-0 collections " + collections);
            }

            // ── the heap instrument above is CALIBRATED here, because an uncalibrated zero proves nothing ──
            //
            // Measured on this editor: GC.GetTotalMemory(false) reports a delta of 0 for real allocations of
            // 1 KB, 8 KB and 64 KB, and only moves at 512 KB; GC.CollectionCount(0) reports 0 gen-0
            // collections for 1 MB of garbage in 1000 allocations. So over this test's 40 reps the heap leg
            // could not have seen a loop allocating up to ~1.6 KB per call, and the "stronger signal" is
            // weaker still. The zeros above are therefore consistent with the claim but do not establish it.
            {
                long floor = CalibrateHeapFloor(out int gen0For1Mb);
                sb.AppendLine("  instrument calibration: smallest allocation GC.GetTotalMemory(false) could see = " +
                              (floor < 0 ? ">512 KB" : floor + " bytes") +
                              ";  gen-0 collections for 1 MB of garbage = " + gen0For1Mb + " (a 0 here means that leg is blind)");
            }

            // ── the instrument that CAN establish it: the IL itself ────────────────────────────────────────
            //
            // An allocation is an opcode, not a heuristic: newobj (0x73), newarr (0x8D) and box (0x8C) are the
            // only ways managed memory is taken, and none of them can hide from a scan of the method body. A
            // byte-frequency scan is an UPPER bound — an operand byte can coincide with an opcode value — so a
            // zero is conclusive and a non-zero would need disassembling. Zero is what the fill loop reports.
            {
                var t = typeof(ShaperFillOps);
                int total = 0; var names = new StringBuilder();
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public |
                                               System.Reflection.BindingFlags.NonPublic |
                                               System.Reflection.BindingFlags.Static))
                {
                    if (m.DeclaringType != t) continue;
                    var body = m.GetMethodBody(); if (body == null) continue;
                    var il = body.GetILAsByteArray(); if (il == null) continue;
                    int hits = 0;
                    for (int i = 0; i < il.Length; i++)
                        if (il[i] == 0x73 || il[i] == 0x8D || il[i] == 0x8C) hits++;
                    total += hits;
                    names.Append(m.Name + "(" + il.Length + "B:" + hits + ") ");
                }
                bool ilOk = total == 0;
                all &= ilOk;
                sb.AppendLine("  IL scan of every ShaperFillOps method for newobj/newarr/box: " + total +
                              " occurrences (expected 0)  " + Verdict(ilOk));
                sb.AppendLine("    " + names.ToString().TrimEnd());
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>
        /// The smallest real allocation <c>GC.GetTotalMemory(false)</c> actually reports, in bytes, or -1 if
        /// even 512 KB is invisible. This is FT-9's true detection floor and it is measured rather than assumed.
        /// </summary>
        static long CalibrateHeapFloor(out int gen0For1Mb)
        {
            long[] sizes = { 1024, 8 * 1024, 64 * 1024, 512 * 1024 };
            long floor = -1;
            foreach (long s in sizes)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long b0 = GC.GetTotalMemory(true);
                var keep = new object[Mathf.Max(1, (int)(s / 1024))];
                for (int i = 0; i < keep.Length; i++) keep[i] = new byte[1024];
                long b1 = GC.GetTotalMemory(false);
                if (b1 - b0 > 0 && keep.Length > 0) { floor = s; break; }
            }
            GC.Collect();
            int g0 = GC.CollectionCount(0);
            object sink = null;
            for (int i = 0; i < 1000; i++) sink = new byte[1024];
            gen0For1Mb = GC.CollectionCount(0) - g0;
            if (sink == null) gen0For1Mb = -1;
            return floor;
        }

        // ── FT-10 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-10: determinism. The same document compiled and filled twice in one session must give bitwise
        /// identical output, including for a <c>MinMax</c> dial — the case that catches a stray
        /// <c>System.Random</c>. The across-a-domain-reload leg is NOT run here (see the report's honesty
        /// buckets); this measures the within-session half.
        /// </summary>
        public static string FT10_Determinism()
        {
            var sb = new StringBuilder("FT-10 determinism (same inputs -> identical bytes, within one session)\n");
            bool all = true;

            var minMax = new ZUIValue { mode = ZUIValue.Mode.MinMax, min = 0.1f, max = 0.9f };
            var defs = new List<KeyValuePair<string, Func<ShaperFillDef>>>
            {
                new KeyValuePair<string, Func<ShaperFillDef>>("Solid", () => Solid(Color.white)),
                new KeyValuePair<string, Func<ShaperFillDef>>("Gradient", () => new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Radial,
                    gradient = BlackToWhite(),
                }),
                new KeyValuePair<string, Func<ShaperFillDef>>("Ramp", () => RampOn(ShaperQuantity.Coverage)),
                new KeyValuePair<string, Func<ShaperFillDef>>("Texture", () => new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = WhiteTexel(),
                }),
                new KeyValuePair<string, Func<ShaperFillDef>>("Solid + MinMax veil", () =>
                {
                    var d = Solid(Color.white);
                    d.veil = new ZUIValue { mode = ZUIValue.Mode.MinMax, min = minMax.min, max = minMax.max };
                    return d;
                }),
            };

            foreach (var kv in defs)
            {
                float[] a = RunDeterministic(kv.Value(), 12345u);
                float[] b = RunDeterministic(kv.Value(), 12345u);
                int bad = 0;
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) bad++;
                bool ok = bad == 0;
                all &= ok;
                sb.AppendLine("  " + kv.Key.PadRight(20) + " mismatched " + bad + "/" + a.Length +
                              " floats (expected 0)  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static float[] RunDeterministic(ShaperFillDef def, uint seed)
        {
            var root = Disc("Disc", 40f, 0f, 0f);
            root.fill = def;
            var rig = Build(root, 0.37f, seed);
            Paint(rig);
            var copy = new float[W * H * 4];
            Array.Copy(rig.buf.dst, copy, copy.Length);
            return copy;
        }

        // ── FT-11 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-11: tile independence (BC-1.6), for each kind in BOTH Stamped and Fixed space. Fixed is the case
        /// that fails if the absolute canvas position is computed from a tile-local origin — it passes every
        /// visual check on a whole-grid render and seams on the first decomposed one.
        /// </summary>
        public static string FT11_TileIndependence()
        {
            var sb = new StringBuilder("FT-11 tile independence: whole grid vs a 7x5 prime decomposition (BC-1.6)\n");
            bool all = true;

            foreach (ShaperFillSpace space in Enum.GetValues(typeof(ShaperFillSpace)))
            {
                var kinds = new List<KeyValuePair<string, ShaperFillDef>>
                {
                    new KeyValuePair<string, ShaperFillDef>("Solid", Solid(Color.white)),
                    new KeyValuePair<string, ShaperFillDef>("Gradient.Linear", new ShaperFillDef
                    {
                        kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Linear,
                        gradient = BlackToWhite(), space = space,
                    }),
                    new KeyValuePair<string, ShaperFillDef>("Ramp.coverage", RampOn(ShaperQuantity.Coverage)),
                    new KeyValuePair<string, ShaperFillDef>("Texture.Tiled", new ShaperFillDef
                    {
                        kind = ShaperFillKind.Texture, texture = Checker8(),
                        textureMapping = ShaperTextureMapping.Tiled,
                        textureTilesX = new ZUIValue(3f), textureTilesY = new ZUIValue(3f),
                        space = space,
                    }),
                };

                foreach (var k in kinds)
                {
                    var root = Disc("Disc", 40f, 0f, 0f);
                    root.fill = k.Value;
                    var rig = Build(root);
                    var sheets = MakeShapeSheets(rig, out _, out _);
                    ShaperFillProgram prog = rig.doc.owners[0].fill;

                    var whole = new ShaperFillEmit
                    {
                        albedo = new float[W * H * 3], veil = new float[W * H], heightDelta = new float[W * H],
                    };
                    ShaperFillOps.FillTile(prog, rig.grid, 0, 0, W, H, sheets, whole, 0, W, 0, W);

                    var tiled = new ShaperFillEmit
                    {
                        albedo = new float[W * H * 3], veil = new float[W * H], heightDelta = new float[W * H],
                    };
                    for (int y = 0; y < H; y += 5)
                        for (int x = 0; x < W; x += 7)
                        {
                            int tw = Mathf.Min(7, W - x), th = Mathf.Min(5, H - y);
                            ShaperFillOps.FillTile(prog, rig.grid, x, y, tw, th, sheets, tiled,
                                                   y * W + x, W, y * W + x, W);
                        }

                    int bad = 0;
                    for (int i = 0; i < W * H * 3; i++) if (whole.albedo[i] != tiled.albedo[i]) bad++;
                    for (int i = 0; i < W * H; i++) if (whole.veil[i] != tiled.veil[i]) bad++;

                    bool ok = bad == 0;
                    all &= ok;
                    sb.AppendLine("  " + space.ToString().PadRight(8) + k.Key.PadRight(18) +
                                  " mismatched " + bad + " floats (expected 0)  " + Verdict(ok));
                }
            }

            // ── the same test on the INTEGRATED entry point, which the legs above never touch ──────────────
            //
            // Everything above drives ShaperFillOps.FillTile directly. The path a host actually calls is
            // ShaperFillResolver.PaintTile, which additionally evaluates every owner's own shape program per
            // tile, builds the claim/descendant-claim/paint arrays per tile, and composites — every one of
            // which is a chance to seam that the direct call cannot expose. BC-1.6 binds the whole pipeline,
            // not one function in it.
            {
                var root = Disc("Wide", 34f, 12f, 0f);
                root.transform.rotation = 25f;
                var inner = Disc("Inner", 14f, 12f, 0f);
                inner.fill = Solid(new Color(1f, 0.8f, 0.2f), 0.6f);
                var bag = ShaperNode.Bag("Tiled", ShaperCombineMode.Add, root, inner);
                bag.fill = new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = Checker8(),
                    textureMapping = ShaperTextureMapping.Tiled,
                    textureTilesX = new ZUIValue(3f), textureTilesY = new ZUIValue(3f),
                    space = ShaperFillSpace.Fixed,
                };
                var rig = Build(bag);
                var sheets = new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine };
                ShaperFillResolver.PaintTile(rig.doc, rig.grid, 0, 0, W, H, rig.buf, sheets);
                var whole = new float[W * H * 4];
                Array.Copy(rig.buf.dst, whole, whole.Length);

                var tiled2 = new float[W * H * 4];
                var tbuf = new ShaperFillBuffers(7 * 5, rig.doc.owners.Count);
                for (int y = 0; y < H; y += 5)
                    for (int x = 0; x < W; x += 7)
                    {
                        int tw = Mathf.Min(7, W - x), th = Mathf.Min(5, H - y);
                        ShaperFillResolver.PaintTile(rig.doc, rig.grid, x, y, tw, th, tbuf, sheets);
                        for (int j = 0; j < th; j++)
                            for (int i = 0; i < tw; i++)
                            {
                                int s = (j * tw + i) * 4, d = ((y + j) * W + (x + i)) * 4;
                                tiled2[d] = tbuf.dst[s]; tiled2[d + 1] = tbuf.dst[s + 1];
                                tiled2[d + 2] = tbuf.dst[s + 2]; tiled2[d + 3] = tbuf.dst[s + 3];
                            }
                    }
                int bad2 = 0;
                for (int i = 0; i < whole.Length; i++) if (whole[i] != tiled2[i]) bad2++;
                bool ok2 = bad2 == 0;
                all &= ok2;
                sb.AppendLine("  PaintTile (integrated: shape eval + claims + composite), 2 owners, Fixed Tiled texture,");
                sb.AppendLine("    whole grid vs the same 7x5 decomposition: " + bad2 + "/" + whole.Length +
                              " floats differ (expected 0)  " + Verdict(ok2));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static Texture2D Checker8()
        {
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color32[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    px[y * 8 + x] = ((x + y) & 1) == 0
                        ? new Color32(255, 60, 20, 255)
                        : new Color32(20, 60, 255, 128);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ── FT-12 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-12: declared set == read set (BC-3.8's shape), measured BEHAVIOURALLY.
        ///
        /// For each kind under a spread of settings, every scalar sheet is swapped for a distinctly-valued
        /// array one at a time; if the output changes, the fill READ that sheet. The measured read set is then
        /// compared against <c>RequiredSet ∪ OptionalSet</c>. A fill that reads an undeclared sheet fails, and
        /// so does one that declares a sheet it never reads.
        ///
        /// <b>Method caveat, stated rather than glossed:</b> a false negative is possible in principle — a fill
        /// could read a sheet and produce identical output for two different arrays by coincidence. The probe
        /// arrays are deliberately far apart (all zeros versus a steep ramp of large values) to make that
        /// vanishingly unlikely, but it is a behavioural measurement, not an instrumented one.
        /// </summary>
        public static string FT12_DeclaredEqualsRead()
        {
            var sb = new StringBuilder("FT-12 declared read set == measured read set (BC-3.8)\n");
            bool all = true;

            var cases = new List<KeyValuePair<string, ShaperFillDef>>
            {
                new KeyValuePair<string, ShaperFillDef>("Solid", Solid(Color.white)),
                new KeyValuePair<string, ShaperFillDef>("Gradient.Linear", new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Linear, gradient = BlackToWhite(),
                }),
                new KeyValuePair<string, ShaperFillDef>("Gradient.Radial", new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Radial, gradient = BlackToWhite(),
                }),
                new KeyValuePair<string, ShaperFillDef>("Gradient.Angular", new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Angular, gradient = BlackToWhite(),
                }),
                new KeyValuePair<string, ShaperFillDef>("Gradient.ByEdgeDistance", new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.ByEdgeDistance, gradient = BlackToWhite(),
                }),
                new KeyValuePair<string, ShaperFillDef>("Ramp.coverage", RampOn(ShaperQuantity.Coverage)),
                new KeyValuePair<string, ShaperFillDef>("Ramp.edgeDistance", RampOn(ShaperQuantity.EdgeDistance)),
                new KeyValuePair<string, ShaperFillDef>("Texture.Fitted", new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = Checker8(),
                }),
            };

            int n = W * H;
            var zeros = new float[n];
            var ramp = new float[n];
            for (int i = 0; i < n; i++) ramp[i] = (i % 97) * 0.37f - 12f;

            foreach (var c in cases)
            {
                ShaperQuantitySet declared = c.Value.RequiredSet() | c.Value.OptionalSet();
                ShaperQuantitySet measured = ShaperQuantitySet.None;

                var root = Disc("Disc", 40f, 0f, 0f);
                root.fill = c.Value;
                var rig = Build(root, 0f, 0u, (ShaperQuantitySet)((1 << ShaperQuantities.Count) - 1));
                ShaperFillProgram prog = rig.doc.owners[0].fill;

                float[] baseline = FillWith(prog, rig, zeros, ShaperQuantity.Coverage, allZero: true);

                for (int qi = 0; qi < ShaperQuantities.Count; qi++)
                {
                    var q = (ShaperQuantity)qi;
                    if (q == ShaperQuantity.SurfaceDirection) continue;   // no scalar sheet exists
                    float[] got = FillWith(prog, rig, ramp, q, allZero: false);
                    bool differs = false;
                    for (int i = 0; i < got.Length; i++) if (got[i] != baseline[i]) { differs = true; break; }
                    if (differs) measured |= ShaperQuantities.Of(q);
                }

                // ── the behavioural measurement above has one blind spot, closed here ──────────────────────
                //
                // Perturbing a sheet only detects a read whose VALUE reaches the output. A fill that indexes
                // an array and discards the result is invisible to it — and that was a real defect, not a
                // hypothetical: FillTile used to take `sheets.edgeDistance` unconditionally, so a Solid fill
                // declaring [None] still indexed the edge sheet at every sample. The instrumented form is to
                // hand the fill a DELIBERATELY SHORT array for every sheet it did not declare: any index into
                // it throws, so a read cannot hide behind a discarded value.
                int shortHits = 0; var shortNames = new StringBuilder();
                for (int qi = 0; qi < ShaperQuantities.Count; qi++)
                {
                    var q = (ShaperQuantity)qi;
                    if (q == ShaperQuantity.SurfaceDirection) continue;
                    if (ShaperQuantities.Contains(declared, q)) continue;   // declared: it is entitled to read it
                    var trap = new ShaperFillSheets
                    {
                        coverage = zeros, height = zeros, edgeDistance = zeros, heat = zeros,
                        density = zeros, soot = zeros, depth = zeros, age = zeros,
                        published = (ShaperQuantitySet)((1 << ShaperQuantities.Count) - 1),
                    };
                    var one = new float[1];
                    switch (q)
                    {
                        case ShaperQuantity.Coverage: trap.coverage = one; break;
                        case ShaperQuantity.Height: trap.height = one; break;
                        case ShaperQuantity.EdgeDistance: trap.edgeDistance = one; break;
                        case ShaperQuantity.Heat: trap.heat = one; break;
                        case ShaperQuantity.Density: trap.density = one; break;
                        case ShaperQuantity.Soot: trap.soot = one; break;
                        case ShaperQuantity.Depth: trap.depth = one; break;
                        case ShaperQuantity.Age: trap.age = one; break;
                    }
                    var em = new ShaperFillEmit
                    {
                        albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n],
                    };
                    try { ShaperFillOps.FillTile(prog, rig.grid, 0, 0, W, H, trap, em, 0, W, 0, W); }
                    catch (IndexOutOfRangeException) { shortHits++; shortNames.Append(ShaperQuantities.Name(q) + " "); }
                }

                bool ok = measured == declared && shortHits == 0;
                all &= ok;
                sb.AppendLine("  " + c.Key.PadRight(26) + " declared [" + declared + "]  measured [" + measured +
                              "]  undeclared sheets INDEXED " + shortHits +
                              (shortHits > 0 ? " (" + shortNames.ToString().Trim() + ")" : "") + " (expected 0)  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static float[] FillWith(ShaperFillProgram prog, Rig rig, float[] probe, ShaperQuantity q, bool allZero)
        {
            int n = W * H;
            var zeros = new float[n];
            var sheets = new ShaperFillSheets
            {
                coverage = zeros, height = zeros, edgeDistance = zeros, heat = zeros,
                density = zeros, soot = zeros, depth = zeros, age = zeros,
                published = (ShaperQuantitySet)((1 << ShaperQuantities.Count) - 1),
            };
            if (!allZero)
            {
                switch (q)
                {
                    case ShaperQuantity.Coverage: sheets.coverage = probe; break;
                    case ShaperQuantity.Height: sheets.height = probe; break;
                    case ShaperQuantity.EdgeDistance: sheets.edgeDistance = probe; break;
                    case ShaperQuantity.Heat: sheets.heat = probe; break;
                    case ShaperQuantity.Density: sheets.density = probe; break;
                    case ShaperQuantity.Soot: sheets.soot = probe; break;
                    case ShaperQuantity.Depth: sheets.depth = probe; break;
                    case ShaperQuantity.Age: sheets.age = probe; break;
                }
            }
            var emit = new ShaperFillEmit
            {
                albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n],
            };
            ShaperFillOps.FillTile(prog, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);
            return emit.albedo;
        }

        // ── FT-13 / FT-14 ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>FT-13: every output sample is written — a NaN sentinel must not survive inside the tile.</summary>
        public static string FT13_EverySampleWritten()
        {
            var sb = new StringBuilder("FT-13 every output sample is written (NaN sentinel must not survive)\n");
            bool all = true;
            int n = W * H;

            foreach (var kv in EveryKind())
            {
                var root = Disc("Disc", 40f, 0f, 0f);
                root.fill = kv.Value;
                var rig = Build(root);
                var sheets = MakeShapeSheets(rig, out _, out _);

                var emit = new ShaperFillEmit
                {
                    albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n],
                };
                for (int i = 0; i < n * 3; i++) emit.albedo[i] = float.NaN;
                for (int i = 0; i < n; i++) { emit.veil[i] = float.NaN; emit.heightDelta[i] = float.NaN; }

                ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);

                int survived = 0;
                for (int i = 0; i < n * 3; i++) if (float.IsNaN(emit.albedo[i])) survived++;
                for (int i = 0; i < n; i++)
                {
                    if (float.IsNaN(emit.veil[i])) survived++;
                    if (float.IsNaN(emit.heightDelta[i])) survived++;
                }
                bool ok = survived == 0;
                all &= ok;
                sb.AppendLine("  " + kv.Key.PadRight(26) + " sentinels surviving " + survived + "/" + (n * 5) +
                              " (expected 0)  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>FT-14: ranges hold over a dense parameter sweep — veil in [0,1], albedo finite and >= 0, height finite.</summary>
        public static string FT14_RangesHold()
        {
            var sb = new StringBuilder("FT-14 ranges hold over a parameter sweep\n");
            int n = W * H;
            int badVeil = 0, badAlbedo = 0, badHeight = 0, cases = 0;
            float worstVeilLow = 1f, worstVeilHigh = 0f, worstAlbedo = 0f;

            float[] veils = { -1f, 0f, 0.33f, 1f, 2f, 1000f };
            float[] sizes = { 0f, 0.001f, 1f, 50f };
            float[] heights = { -5f, 0f, 3f };

            foreach (float v in veils)
                foreach (float s in sizes)
                    foreach (float hd in heights)
                        foreach (ShaperGradientMode gm in Enum.GetValues(typeof(ShaperGradientMode)))
                        {
                            var def = new ShaperFillDef
                            {
                                kind = ShaperFillKind.Gradient,
                                gradientMode = gm,
                                gradient = BlackToWhite(),
                                gradientSize = new ZUIValue(s),
                                veil = new ZUIValue(v),
                                heightDelta = new ZUIValue(hd),
                            };
                            var root = Disc("Disc", 40f, 0f, 0f);
                            root.fill = def;
                            var rig = Build(root);
                            var sheets = MakeShapeSheets(rig, out _, out _);
                            var emit = new ShaperFillEmit
                            {
                                albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n],
                            };
                            ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);
                            cases++;

                            for (int i = 0; i < n; i++)
                            {
                                float vv = emit.veil[i];
                                if (float.IsNaN(vv) || float.IsInfinity(vv) || vv < 0f || vv > 1f) badVeil++;
                                worstVeilLow = Mathf.Min(worstVeilLow, vv);
                                worstVeilHigh = Mathf.Max(worstVeilHigh, vv);
                                float hh = emit.heightDelta[i];
                                if (float.IsNaN(hh) || float.IsInfinity(hh)) badHeight++;
                                for (int c = 0; c < 3; c++)
                                {
                                    float a = emit.albedo[i * 3 + c];
                                    if (float.IsNaN(a) || float.IsInfinity(a) || a < 0f) badAlbedo++;
                                    worstAlbedo = Mathf.Max(worstAlbedo, a);
                                }
                            }
                        }

            bool ok = badVeil == 0 && badAlbedo == 0 && badHeight == 0;
            sb.AppendLine("  cases swept: " + cases + " (veil x size x height x gradient mode), " + n + " samples each");
            sb.AppendLine("  veil out of [0,1] or non-finite: " + badVeil + " (expected 0)   observed veil range [" +
                          worstVeilLow.ToString("F4") + ", " + worstVeilHigh.ToString("F4") + "]");
            sb.AppendLine("  albedo negative or non-finite:   " + badAlbedo + " (expected 0)   max albedo " +
                          worstAlbedo.ToString("F4"));
            sb.AppendLine("  height non-finite:               " + badHeight + " (expected 0)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        static List<KeyValuePair<string, ShaperFillDef>> EveryKind()
        {
            var list = new List<KeyValuePair<string, ShaperFillDef>>
            {
                new KeyValuePair<string, ShaperFillDef>("Solid", Solid(Color.white)),
                new KeyValuePair<string, ShaperFillDef>("Ramp.coverage", RampOn(ShaperQuantity.Coverage)),
                new KeyValuePair<string, ShaperFillDef>("Texture.Fitted", new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = Checker8(),
                }),
                new KeyValuePair<string, ShaperFillDef>("Texture.Tiled", new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = Checker8(),
                    textureMapping = ShaperTextureMapping.Tiled,
                }),
            };
            foreach (ShaperGradientMode m in Enum.GetValues(typeof(ShaperGradientMode)))
                list.Add(new KeyValuePair<string, ShaperFillDef>("Gradient." + m, new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = m, gradient = BlackToWhite(),
                }));
            return list;
        }

        // ── FT-15 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-15: degenerate inputs do not NaN, and produce a diagnostic where FC-6.5 requires one.
        /// Zero-extent anchor (FC-1.5b), degenerate ramp window (FC-6.3a), size 0, null gradient, null texture,
        /// an unreadable texture, and a fill on an empty bag.
        /// </summary>
        public static string FT15_DegenerateInputs()
        {
            var sb = new StringBuilder("FT-15 degenerate inputs: finite output, and a diagnostic where required\n");
            bool all = true;
            int n = W * H;

            // (1) an EMPTY bag — a fill whose anchor box is Box.Invalid (FC-1.5b).
            {
                var bag = ShaperNode.Bag("Empty", ShaperCombineMode.Add);
                bag.fill = new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Radial,
                    gradient = BlackToWhite(),
                };
                var rig = Build(bag);
                var op = rig.doc.owners[0].fill.op;
                bool bound = rig.doc.owners.Count == 1;
                var sheets = MakeShapeSheets(rig, out _, out _);
                var emit = new ShaperFillEmit { albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n] };
                ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);
                int bad = CountNonFinite(emit, n);
                bool ok = bound && bad == 0 && op.invHx == 0f && op.invHy == 0f;
                all &= ok;
                sb.AppendLine("  empty bag: bound=" + bound + " (expected True — a degenerate anchor MUST still bind)" +
                              "  invHx/invHy = " + op.invHx + "/" + op.invHy + " (expected 0/0 — never divides)" +
                              "  non-finite " + bad + " (expected 0)  " + Verdict(ok));
            }

            // (2) degenerate ramp window (FC-6.3a).
            {
                var def = RampOn(ShaperQuantity.Coverage);
                def.rampInputLow = new ZUIValue(0.5f);
                def.rampInputHigh = new ZUIValue(0.5f);
                var root = Disc("Disc", 40f, 0f, 0f); root.fill = def;
                var rig = Build(root);
                var op = rig.doc.owners[0].fill.op;
                var sheets = MakeShapeSheets(rig, out _, out float[] cov);
                var emit = new ShaperFillEmit { albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n] };
                ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);
                int bad = CountNonFinite(emit, n);
                // A hard step: every sample's albedo must be exactly one of the two ramp ends.
                float lo = ShaperSrgb.DecodeChannel(0f), hi = ShaperSrgb.DecodeChannel(1f);
                int notBinary = 0;
                for (int i = 0; i < n; i++)
                {
                    float a = emit.albedo[i * 3];
                    if (Mathf.Abs(a - lo) > 1e-5f && Mathf.Abs(a - hi) > 1e-5f) notBinary++;
                }
                bool ok = op.hardStep == 1 && bad == 0 && notBinary == 0;
                all &= ok;
                sb.AppendLine("  |high-low| < 1e-6: hardStep=" + op.hardStep + " (expected 1)  non-finite " + bad +
                              " (expected 0)  samples not at a ramp end " + notBinary + " (expected 0)  " + Verdict(ok));
            }

            // (3) size 0 on every gradient mode.
            {
                int bad = 0;
                foreach (ShaperGradientMode m in Enum.GetValues(typeof(ShaperGradientMode)))
                {
                    var def = new ShaperFillDef
                    {
                        kind = ShaperFillKind.Gradient, gradientMode = m,
                        gradient = BlackToWhite(), gradientSize = new ZUIValue(0f),
                    };
                    var root = Disc("Disc", 40f, 0f, 0f); root.fill = def;
                    var rig = Build(root);
                    var sheets = MakeShapeSheets(rig, out _, out _);
                    var emit = new ShaperFillEmit { albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n] };
                    ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);
                    bad += CountNonFinite(emit, n);
                }
                bool ok = bad == 0;
                all &= ok;
                sb.AppendLine("  size = 0, all four gradient modes: non-finite " + bad + " (expected 0)  " + Verdict(ok));
            }

            // (4)(5)(6) the three fallbacks, each of which MUST record a diagnostic (FC-6.5).
            {
                var checks = new List<KeyValuePair<string, ShaperFillDef>>
                {
                    new KeyValuePair<string, ShaperFillDef>("null gradient", new ShaperFillDef
                    {
                        kind = ShaperFillKind.Gradient, gradient = null,
                        gradientTint = new Color(1f, 0.5f, 0f),
                    }),
                    new KeyValuePair<string, ShaperFillDef>("null ramp gradient", new ShaperFillDef
                    {
                        kind = ShaperFillKind.RampByQuantity, rampGradient = null,
                        rampTint = new Color(0f, 0.5f, 1f),
                    }),
                    new KeyValuePair<string, ShaperFillDef>("null texture", new ShaperFillDef
                    {
                        kind = ShaperFillKind.Texture, texture = null,
                        textureTint = new Color(0.2f, 0.9f, 0.4f),
                    }),
                };

                foreach (var c in checks)
                {
                    var root = Disc("Belt", 40f, 0f, 0f); root.fill = c.Value;
                    var rig = Build(root);
                    string diag = rig.doc.owners[0].fill.diagnostic;
                    var sheets = MakeShapeSheets(rig, out _, out _);
                    var emit = new ShaperFillEmit { albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n] };
                    ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);
                    int bad = CountNonFinite(emit, n);
                    // Painting SOMETHING visible: the tint, not black and not transparent.
                    bool visible = emit.albedo[0] > 0f || emit.albedo[1] > 0f || emit.albedo[2] > 0f;
                    bool ok = diag != null && bad == 0 && visible && rig.doc.hasFallbackFill;
                    all &= ok;
                    sb.AppendLine("  " + c.Key.PadRight(20) + " diagnostic=" + (diag != null ? "\"" + diag + "\"" : "NONE") +
                                  "  paints something " + visible + "  non-finite " + bad + "  " + Verdict(ok));
                }
            }

            // (7) an UNREADABLE texture — the case FC-6.4d says will actually happen.
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                t.SetPixels32(new Color32[16]);
                t.Apply(false, true);   // makeNoLongerReadable: exactly the import setting that bites
                var root = Disc("Belt", 40f, 0f, 0f);
                root.fill = new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = t, textureTint = new Color(1f, 0f, 0.5f),
                };
                var rig = Build(root);
                string diag = rig.doc.owners[0].fill.diagnostic ?? "";
                bool names = diag.Contains("Read/Write");
                bool ok = diag.Length > 0 && names;
                all &= ok;
                sb.AppendLine("  unreadable texture  diagnostic=\"" + diag + "\"");
                sb.AppendLine("                      names the import setting = " + names +
                              " (expected True — the asset looks fine in the inspector)  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static int CountNonFinite(ShaperFillEmit emit, int n)
        {
            int bad = 0;
            for (int i = 0; i < n * 3; i++)
                if (float.IsNaN(emit.albedo[i]) || float.IsInfinity(emit.albedo[i])) bad++;
            for (int i = 0; i < n; i++)
            {
                if (float.IsNaN(emit.veil[i]) || float.IsInfinity(emit.veil[i])) bad++;
                if (float.IsNaN(emit.heightDelta[i]) || float.IsInfinity(emit.heightDelta[i])) bad++;
            }
            return bad;
        }

        // ── FT-16 / FT-17 / FT-18 / FT-19 ─────────────────────────────────────────────────────────────────

        /// <summary>FT-16: a fill never mutates an input sheet (BC-3.7c/e). Hash both sheets before and after.</summary>
        public static string FT16_NoInputMutation()
        {
            var sb = new StringBuilder("FT-16 a fill never writes to an input sheet (BC-3.7c)\n");
            bool all = true;
            int n = W * H;

            foreach (var kv in EveryKind())
            {
                var root = Disc("Disc", 40f, 0f, 0f);
                root.fill = kv.Value;
                var rig = Build(root);
                var sheets = MakeShapeSheets(rig, out float[] dist, out float[] cov);
                ulong hd0 = Hash(dist), hc0 = Hash(cov);

                var emit = new ShaperFillEmit { albedo = new float[n * 3], veil = new float[n], heightDelta = new float[n] };
                ShaperFillOps.FillTile(rig.doc.owners[0].fill, rig.grid, 0, 0, W, H, sheets, emit, 0, W, 0, W);

                ulong hd1 = Hash(dist), hc1 = Hash(cov);
                bool ok = hd0 == hd1 && hc0 == hc1;
                all &= ok;
                sb.AppendLine("  " + kv.Key.PadRight(26) + " distance hash " + (hd0 == hd1 ? "unchanged" : "CHANGED") +
                              ", coverage hash " + (hc0 == hc1 ? "unchanged" : "CHANGED") + "  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static ulong Hash(float[] a)
        {
            // FNV-1a over the raw bits. Deterministic and never UnityEngine.Random.
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < a.Length; i++)
            {
                uint bits = (uint)BitConverter.SingleToInt32Bits(a[i]);
                for (int b = 0; b < 4; b++)
                {
                    h ^= (byte)(bits >> (b * 8));
                    h *= 1099511628211UL;
                }
            }
            return h;
        }

        /// <summary>
        /// FT-17: Add does not raise alpha (FC-2.6b). An additive fill over a transparent destination must leave
        /// the destination's alpha at zero while raising its RGB — that is what makes it read as light rather
        /// than as paint.
        /// </summary>
        public static string FT17_AddDoesNotRaiseAlpha()
        {
            var sb = new StringBuilder("FT-17 Add does not raise alpha (FC-2.6b)\n");

            var root = Disc("Disc", 40f, 0f, 0f);
            root.fill = Solid(new Color(1f, 0.8f, 0.4f), 1f, 0f, ShaperFillComposite.Add);
            var rig = Build(root);
            Paint(rig);

            int n = W * H, alphaRaised = 0, rgbRaised = 0;
            float maxAlpha = 0f, maxRgb = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = rig.buf.dst[i * 4 + 3];
                float r = rig.buf.dst[i * 4 + 0];
                if (a != 0f) { alphaRaised++; maxAlpha = Mathf.Max(maxAlpha, a); }
                if (r > 0f) { rgbRaised++; maxRgb = Mathf.Max(maxRgb, r); }
            }

            bool ok = alphaRaised == 0 && rgbRaised > 0;
            sb.AppendLine("  samples with alpha != 0: " + alphaRaised + "/" + n + " (expected 0)  max alpha " + maxAlpha);
            sb.AppendLine("  samples with RGB   > 0: " + rgbRaised + "/" + n + " (expected > 0)  max R " + maxRgb.ToString("F4"));
            sb.AppendLine("  the veil still applies in Add mode — it is a STRENGTH there, not an opacity:");
            {
                var r2 = Disc("Disc", 40f, 0f, 0f);
                r2.fill = Solid(new Color(1f, 0.8f, 0.4f), 0f, 0f, ShaperFillComposite.Add);
                var rg2 = Build(r2); Paint(rg2);
                float m = 0f;
                for (int i = 0; i < n; i++) m = Mathf.Max(m, rg2.buf.dst[i * 4 + 0]);
                bool ok2 = m == 0f;
                ok &= ok2;
                sb.AppendLine("    veil 0 in Add mode: max R = " + m.ToString("E2") + " (expected 0)  " + Verdict(ok2));
            }
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// <summary>FT-18: Over versus Add do not differ in height (FC-2.6c). The switch governs colour only.</summary>
        public static string FT18_HeightIgnoresComposite()
        {
            var sb = new StringBuilder("FT-18 Over vs Add produce identical height (FC-2.6c)\n");

            var a = Disc("Disc", 40f, 0f, 0f);
            a.fill = Solid(new Color(1f, 0.5f, 0.2f), 0.7f, 4.25f, ShaperFillComposite.Over);
            var ra = Build(a); Paint(ra);

            var b = Disc("Disc", 40f, 0f, 0f);
            b.fill = Solid(new Color(1f, 0.5f, 0.2f), 0.7f, 4.25f, ShaperFillComposite.Add);
            var rb = Build(b); Paint(rb);

            int n = W * H, bad = 0;
            float worst = 0f, maxHeight = 0f;
            for (int i = 0; i < n; i++)
            {
                if (ra.buf.height[i] != rb.buf.height[i]) { bad++; worst = Mathf.Max(worst, Mathf.Abs(ra.buf.height[i] - rb.buf.height[i])); }
                maxHeight = Mathf.Max(maxHeight, ra.buf.height[i]);
            }
            // Also assert the height is actually non-trivial, so "identical" is not "both zero".
            bool nonTrivial = maxHeight > 0f;
            bool colourDiffers = false;
            for (int i = 0; i < n; i++) if (ra.buf.dst[i * 4 + 3] != rb.buf.dst[i * 4 + 3]) { colourDiffers = true; break; }

            bool ok = bad == 0 && nonTrivial && colourDiffers;
            sb.AppendLine("  height mismatched " + bad + "/" + n + " (expected 0)  worst |delta| " + worst.ToString("E2"));
            sb.AppendLine("  max height reached " + maxHeight.ToString("F4") +
                          " (expected > 0 — otherwise 'identical' would be vacuous; 4.25 x veil 0.7 x coverage 1 = 2.975)");
            sb.AppendLine("  the COLOUR result does differ between the two modes = " + colourDiffers + " (expected True)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// <summary>
        /// FT-19: the edge-distance polarity is the shipped one (FC-1.3). A reversed ramp means Pyre's
        /// opposite-polarity band expression was inherited literally.
        /// </summary>
        public static string FT19_EdgeDistancePolarity()
        {
            var sb = new StringBuilder("FT-19 ByEdgeDistance polarity: t=0 at the boundary, t=1 at depthPixels in\n");

            const float R = 40f, Depth = 8f;
            var root = Disc("Disc", R, 0f, 0f);
            root.fill = new ShaperFillDef
            {
                kind = ShaperFillKind.Gradient,
                gradientMode = ShaperGradientMode.ByEdgeDistance,
                gradient = BlackToWhite(),
                gradientDepthPixels = new ZUIValue(Depth),
            };
            var rig = Build(root);
            var op = rig.doc.owners[0].fill.op;

            ShaperProgram prog = rig.doc.owners[0].shape;
            float[] stack = prog.NewStack();

            float dBoundary = ShaperEvaluator.Distance(prog, R, 0f, stack);
            float dInside = ShaperEvaluator.Distance(prog, R - Depth, 0f, stack);
            float dDeep = ShaperEvaluator.Distance(prog, 0f, 0f, stack);
            float dOutside = ShaperEvaluator.Distance(prog, R + 10f, 0f, stack);

            float tBoundary = ShaperFillOps.ByEdgeDistanceT(dBoundary, op.invDepthPixels);
            float tInside = ShaperFillOps.ByEdgeDistanceT(dInside, op.invDepthPixels);
            float tDeep = ShaperFillOps.ByEdgeDistanceT(dDeep, op.invDepthPixels);
            float tOutside = ShaperFillOps.ByEdgeDistanceT(dOutside, op.invDepthPixels);

            bool ok = Mathf.Abs(tBoundary) < 1e-4f &&
                      Mathf.Abs(tInside - 1f) < 1e-4f &&
                      Mathf.Abs(tDeep - 1f) < 1e-4f &&
                      Mathf.Abs(tOutside) < 1e-4f;

            sb.AppendLine("  at the boundary   d = " + dBoundary.ToString("F4") + "  t = " + tBoundary.ToString("F6") + " (expected 0)");
            sb.AppendLine("  " + Depth + " px inside     d = " + dInside.ToString("F4") + "  t = " + tInside.ToString("F6") + " (expected 1)");
            sb.AppendLine("  at the centre     d = " + dDeep.ToString("F4") + "  t = " + tDeep.ToString("F6") + " (expected 1, saturated)");
            sb.AppendLine("  10 px outside     d = " + dOutside.ToString("F4") + "  t = " + tOutside.ToString("F6") + " (expected 0)");
            sb.AppendLine("  the shipped field is NEGATIVE INSIDE, so t = clamp01(-d/depth). A reversed reading here");
            sb.AppendLine("  would mean Pyre's unsigned, growing-inward BorderInsideDistance was ported literally.");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── FT-21 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-21: exclusivity is CONSERVATIVE, at fractional coverage and three owners deep.
        ///
        /// FT-7 checks the ownership TABLE — who owns which region — at eight probes whose claims are all 0 or
        /// 1. That is the binary skeleton of FC-3.5 and it cannot see the continuous half, which is where the
        /// arithmetic actually lives: `claim` is an area fraction, so at every antialiased boundary the
        /// exclusivity expression is evaluated strictly between 0 and 1 and its choice of form is visible.
        ///
        /// Two assertions, and they are deliberately different in kind:
        ///
        /// <b>(a) Conservation.</b> Along one ancestor chain the paint regions are disjoint by construction
        /// (claim[o] = min(ownCoverage[o], claim[ancestor]), so a descendant's claim can never exceed its
        /// ancestor's). Their sum must therefore equal the root's coverage exactly, at every sample, including
        /// fractional ones. This is what caught the multiplicative form: `claim*(1-descendantClaim)` leaves
        /// `c*(1-c)` wherever a descendant claims all of its ancestor's fractional claim — a 0.15..0.25 fringe
        /// of the ANCESTOR's colour tracing the whole silhouette, which is a visible artefact and not a
        /// rounding error.
        ///
        /// <b>(b) The composited alpha versus the shape's own coverage — KNOWN OPEN, reported as a number.</b>
        /// Exclusive owners hold DISJOINT sub-areas of a pixel, and `Over` (FC-2.6a) is the compositing rule
        /// for INDEPENDENT ones. Compositing two disjoint halves with Over under-reports: an ancestor at 0.5
        /// and a descendant at 0.5 give 0.5 + 0.5*0.5 = 0.75 where the shape's own coverage is 1.0. So a
        /// one-pixel, up-to-25%-transparent ring appears wherever a child fill's soft edge sits inside its
        /// parent's silhouette. This is a contradiction between FC-3.5 (exclusive, therefore disjoint) and
        /// FC-3.4/FC-2.6a (Over, therefore independent), not an implementation slip — resolving it needs a
        /// ruling on the spec, so this leg REPORTS the deficit rather than failing on it.
        /// </summary>
        public static string FT21_ExclusivityIsConservative()
        {
            var sb = new StringBuilder("FT-21 exclusivity is conservative at fractional coverage, three owners deep\n");
            bool all = true;
            int n = W * H;

            var c3 = Disc("C", 14f, 0f, 0f); c3.fill = Solid(new Color(0.2f, 0.4f, 1f));
            var b3 = ShaperNode.Bag("B", ShaperCombineMode.Add, Disc("Bd", 28f, 0f, 0f), c3);
            b3.fill = Solid(new Color(0.2f, 0.9f, 0.3f));
            var a3 = ShaperNode.Bag("A", ShaperCombineMode.Add, Disc("Ad", 42f, 0f, 0f), b3);
            a3.fill = Solid(new Color(0.9f, 0.2f, 0.2f));

            var rig = Build(a3);
            Paint(rig);
            int k = rig.doc.owners.Count, cap = rig.buf.sampleCapacity;

            double worstExcess = 0; int nFrac = 0, nBad = 0;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int o = 0; o < k; o++) sum += rig.buf.paint[o * cap + i];
                float rootCov = rig.buf.ownCoverage[i];
                if (rootCov > 1e-4f && rootCov < 0.9999f) nFrac++;
                double d = Mathf.Abs(sum - rootCov);
                if (d > 1e-4) { nBad++; if (d > worstExcess) worstExcess = d; }
            }
            bool consOk = nBad == 0;
            all &= consOk;
            sb.AppendLine("  owners: " + string.Join(" -> ", rig.doc.owners.ConvertAll(o => o.name).ToArray()) +
                          " (concentric r42 / r28 / r14, so every pair is ancestor-descendant and the regions are disjoint)");
            sb.AppendLine("  fractional-coverage samples in the fixture: " + nFrac + " (a 0 here would make this test vacuous)");
            sb.AppendLine("  (a) sum(paint over the chain) != rootCoverage at " + nBad + "/" + n +
                          " samples (expected 0), worst " + worstExcess.ToString("F5") + "  " + Verdict(consOk));

            int nDef = 0; double totalDef = 0, worstDef = 0;
            for (int i = 0; i < n; i++)
            {
                float d = rig.buf.ownCoverage[i] - rig.buf.dst[i * 4 + 3];
                if (d > 1e-3f) { nDef++; totalDef += d; if (d > worstDef) worstDef = d; }
            }
            bool defOk = nDef == 0;
            all &= defOk;
            sb.AppendLine("  (b) composited alpha BELOW the shape's own coverage at " + nDef + "/" + n +
                          " samples (expected 0), total " + totalDef.ToString("F3") +
                          ", worst " + worstDef.ToString("F4") + "  " + Verdict(defOk));
            sb.AppendLine("      SAME INSTRUMENT, SAME FIXTURE as the escalation that measured 244/16384, total 29.288,");
            sb.AppendLine("      worst 0.2484 on the build before the FC-3.5a amendment — so the before and after numbers");
            sb.AppendLine("      are directly comparable and this line is the whole evidence for the fix. Exclusive owners");
            sb.AppendLine("      hold DISJOINT sub-areas of a pixel; Over is the rule for INDEPENDENT ones and under-reports");
            sb.AppendLine("      (0.5 and 0.5 give 0.75 where coverage is 1.0). The chain now SUMS and only the fold into a");
            sb.AppendLine("      parent's siblings uses Over, which is what FC-2.6a was always describing.");

            // (c) the sibling half must NOT have been converted to a sum — that would silently reverse FC-3.4.
            // Two overlapping Over siblings at veil 0.5 each: Over gives 0.75, a sum would give 1.0.
            {
                var lo = Disc("Lower", 30f, -10f, 0f); lo.fill = Solid(new Color(1f, 0f, 0f), 0.5f);
                var up = Disc("Upper", 30f, 10f, 0f); up.fill = Solid(new Color(0f, 0f, 1f), 0.5f);
                var pair = ShaperNode.Bag("Pair", ShaperCombineMode.Add, lo, up);
                pair.fill = Solid(Color.white);
                var rp = Build(pair); Paint(rp);
                float aMid = rp.buf.dst[Index(rp, 0f, 0f) * 4 + 3];
                bool sibOk = Mathf.Abs(aMid - 0.75f) < 1e-4f;
                all &= sibOk;
                sb.AppendLine("  (c) two OVERLAPPING siblings, veil 0.5 each, alpha where both cover = " +
                              aMid.ToString("F5") + " (expected 0.75000 — Over; a sum would read 1.00000)  " +
                              Verdict(sibOk));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── FT-22 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-22: the <c>Color32</c> write is READABLE BACK (FC-2.3). The leg that would have caught the
        /// encoder defect T-0106's verification measured: the shipped default premultiplied in LINEAR and then
        /// sRGB-encoded the premultiplied value, which is neither straight alpha nor conventional
        /// premultiplied-sRGB, so no consumer could reconstruct the authored colour at any alpha but 0 and 1.
        ///
        /// Three legs. <b>(a)</b> the straight round trip must be EXACT for all 256 codes at several alphas —
        /// the assertion the old encoder failed at every alpha strictly between the endpoints. <b>(b)</b> the
        /// alpha byte itself round-trips. <b>(c)</b> the additive-over-transparent case is still representable:
        /// it survives in the float destination exactly, and it survives into bytes through a backdrop
        /// composite, which is the honest 8-bit answer now that the second encoder is gone.
        /// </summary>
        public static string FT22_EncodeIsReadableBack()
        {
            var sb = new StringBuilder("FT-22 the Color32 write is readable back (FC-2.3)\n");
            bool all = true;

            // (a) straight round trip, all 256 codes, at five alphas including the two endpoints.
            float[] alphas = { 1f, 0.75f, 0.5f, 0.25f, 0.1f };
            var dst = new float[256 * 4];
            var px = new Color32[256];
            foreach (float a in alphas)
            {
                for (int v = 0; v < 256; v++)
                {
                    // Author a grey at code v, decode to linear (FC-2.3's compile-time boundary), then
                    // premultiply by alpha — exactly the state PaintTile leaves in dst for a solid fill.
                    float lin = ShaperSrgb.DecodeChannel(v / 255f);
                    dst[v * 4 + 0] = lin * a; dst[v * 4 + 1] = lin * a; dst[v * 4 + 2] = lin * a;
                    dst[v * 4 + 3] = a;
                }
                ShaperFillResolver.Encode(dst, px, 256);

                int worst = 0, bad = 0;
                for (int v = 0; v < 256; v++)
                {
                    int d = Mathf.Abs(px[v].r - v);
                    if (d != 0) { bad++; if (d > worst) worst = d; }
                }
                bool ok = bad == 0;
                all &= ok;
                sb.AppendLine("  (a) alpha " + a.ToString("F2") + ": codes not round-tripping " + bad +
                              "/256 (expected 0), worst |delta| " + worst + " codes  " + Verdict(ok));
            }

            // (b) the alpha byte.
            {
                int bad = 0, worst = 0;
                for (int v = 0; v < 256; v++)
                {
                    dst[v * 4 + 0] = 0f; dst[v * 4 + 1] = 0f; dst[v * 4 + 2] = 0f;
                    dst[v * 4 + 3] = v / 255f;
                }
                ShaperFillResolver.Encode(dst, px, 256);
                for (int v = 0; v < 256; v++)
                {
                    int d = Mathf.Abs(px[v].a - v);
                    if (d != 0) { bad++; if (d > worst) worst = d; }
                }
                bool ok = bad == 0;
                all &= ok;
                sb.AppendLine("  (b) alpha byte round trip: " + bad + "/256 wrong (expected 0), worst " +
                              worst + " codes  " + Verdict(ok));
            }

            // (c) additive over transparent, end to end through PaintTile.
            {
                var root = Disc("Disc", 30f, 0f, 0f);
                root.fill = Solid(new Color(1f, 0.8f, 0.4f), 1f, 0f, ShaperFillComposite.Add);
                var rig = Build(root); Paint(rig);
                int mid = Index(rig, 0f, 0f);
                float gr = rig.buf.dst[mid * 4 + 0], ga = rig.buf.dst[mid * 4 + 3];

                // In the float destination: the glow is there and the alpha is not. That is the representation
                // that carries an additive result, and it is unchanged by deleting the premultiplied encoder.
                bool floatOk = gr > 0f && ga == 0f && !float.IsNaN(gr) && !float.IsInfinity(gr);

                // Into bytes: composite over an opaque backdrop first. The backdrop is BRIGHTENED, which is how
                // an additive glow reads at all in an 8-bit straight-alpha image.
                const float Back = 0.16f;
                float over = Back + gr;
                byte withGlow = ShaperSrgb.EncodeToByte(over);
                byte without = ShaperSrgb.EncodeToByte(Back);
                bool backdropOk = withGlow > without;

                // And the straight encoder's own degenerate branch keeps the COLOUR bytes rather than dividing.
                var one = new float[4] { gr, rig.buf.dst[mid * 4 + 1], rig.buf.dst[mid * 4 + 2], 0f };
                var onePx = new Color32[1];
                ShaperFillResolver.Encode(one, onePx, 1);
                bool degenerateOk = onePx[0].r == ShaperSrgb.EncodeToByte(gr) && onePx[0].a == 0;

                bool ok = floatOk && backdropOk && degenerateOk;
                all &= ok;
                sb.AppendLine("  (c) additive over transparent is still representable:");
                sb.AppendLine("        float dst  R " + gr.ToString("F4") + " > 0, alpha " + ga.ToString("F4") +
                              " == 0, finite = " + floatOk + " (expected True)");
                sb.AppendLine("        over an opaque backdrop, byte " + without + " -> " + withGlow +
                              " (brightened = " + backdropOk + ", expected True)");
                sb.AppendLine("        Encode's alpha<=1e-6 branch keeps the colour byte instead of dividing = " +
                              degenerateOk + " (expected True); alpha is honestly 0 and a straight-alpha");
                sb.AppendLine("        blender will discard it — which is why the float buffer and the backdrop");
                sb.AppendLine("        composite are the two real answers, not a second encoder.");
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── FT-20 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FT-20: the contact sheet. Renders every kind, Gradient's four modes, both spaces, both fits, both
        /// mappings, both composites and the FC-3.8 worked tree into one PNG.
        ///
        /// <b>A passing table is not a picture.</b> This method PRODUCES the artefact; the test is not complete
        /// until a human has looked at it, and the report must say so rather than counting the file's existence
        /// as a pass.
        /// </summary>
        public static string FT20_ContactSheet(string path)
        {
            // Cols is 7 because there are 21 cells and 21 = 7 x 3 exactly. A grid with an empty tail position
            // was one of the things wrong with the first sheet; picking the column count to divide the cell
            // count is the fix, not padding the tail with a blank.
            const int Cell = 96, Cols = 7, Pad = 6;

            var cells = new List<KeyValuePair<string, ShaperNode>>();
            Action<string, ShaperNode> Cellf = (n, node) =>
                cells.Add(new KeyValuePair<string, ShaperNode>(n, node));
            Func<ShaperFillDef, ShaperNode> OnDisc = f => { var d = Disc("Disc", 34f, 0f, 0f); d.fill = f; return d; };

            // 01-03 — Solid, three ways, so "a flat colour" is visibly a flat colour and the veil is visibly
            //          a TRANSPARENCY rather than a darkening. Cell 02 is deliberately WHITE: the previous
            //          sheet rendered white-on-transparent and was invisible, which is the defect this
            //          backdrop exists to make impossible.
            Cellf("01 Solid, tinted", OnDisc(Solid(new Color(0.95f, 0.45f, 0.12f))));
            Cellf("02 Solid, WHITE", OnDisc(Solid(Color.white)));
            Cellf("03 Solid, veil 0.45", OnDisc(Solid(new Color(0.95f, 0.45f, 0.12f), 0.45f)));

            // 04-07 — Gradient's four modes, on a two-stop ramp chosen to read against the backdrop.
            foreach (ShaperGradientMode gm in Enum.GetValues(typeof(ShaperGradientMode)))
            {
                var f = new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = gm, gradient = SheetRamp(),
                    gradientDepthPixels = new ZUIValue(10f),
                };
                Cellf("0" + (4 + (int)gm) + " Gradient." + gm, OnDisc(f));
            }

            // 08-09 — Ramp-by-quantity, on both quantities the shipped shape engine actually publishes.
            Cellf("08 Ramp by coverage", OnDisc(new ShaperFillDef
            {
                kind = ShaperFillKind.RampByQuantity, rampQuantity = ShaperQuantity.Coverage,
                rampGradient = SheetRamp(),
            }));
            Cellf("09 Ramp by edgeDist", OnDisc(new ShaperFillDef
            {
                kind = ShaperFillKind.RampByQuantity, rampQuantity = ShaperQuantity.EdgeDistance,
                rampGradient = SheetRamp(),
                rampInputLow = new ZUIValue(-26f), rampInputHigh = new ZUIValue(0f),
            }));

            // 10-11 — Texture, both mappings. The checker's odd texels are half-alpha, so Fitted and Tiled
            //          also demonstrate FC-6.4c: a texel's alpha goes to the VEIL, and the backdrop shows through.
            foreach (ShaperTextureMapping map in Enum.GetValues(typeof(ShaperTextureMapping)))
                Cellf((10 + (int)map) + " Texture." + map, OnDisc(new ShaperFillDef
                {
                    kind = ShaperFillKind.Texture, texture = Checker8(), textureMapping = map,
                    textureTilesX = new ZUIValue(3f), textureTilesY = new ZUIValue(3f),
                }));

            // 12-15 — space x fit, on a WIDE, ROTATED node, which is the only shape on which either dial is
            //          visible at all. Stamped follows the rotation; Fixed does not. Uniform keeps the radial
            //          a circle; Stretch fits it to the box.
            int n1 = 12;
            foreach (ShaperFillSpace sp in Enum.GetValues(typeof(ShaperFillSpace)))
                foreach (ShaperFillFit fit in Enum.GetValues(typeof(ShaperFillFit)))
                {
                    var d = Disc("Wide", 34f, 0f, 0f);
                    d.transform.scale = new Vector2(1.5f, 0.55f);
                    d.transform.rotation = 25f;
                    d.fill = new ShaperFillDef
                    {
                        kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Radial,
                        gradient = SheetRamp(), space = sp, fit = fit,
                    };
                    Cellf(n1++ + " " + sp + "/" + fit, d);
                }

            // 16-17 — Over versus Add, same two discs, same colours, one dial different. Add must read as
            //          LIGHT (it brightens the backdrop where it spills past the lower disc) and Over as paint.
            foreach (ShaperFillComposite comp in Enum.GetValues(typeof(ShaperFillComposite)))
            {
                var back = Disc("Back", 32f, -12f, 0f); back.fill = Solid(new Color(0.12f, 0.20f, 0.62f));
                var front = Disc("Front", 24f, 14f, 0f); front.fill = Solid(new Color(1f, 0.62f, 0.16f), 0.85f, 0f, comp);
                var bag = ShaperNode.Bag("Comp", ShaperCombineMode.Add, back, front);
                bag.fill = Solid(new Color(0.06f, 0.06f, 0.09f));
                Cellf((16 + (int)comp) + " Composite " + comp, bag);
            }

            // 18 — FC-3.8's worked tree, the ownership case the whole of Part F3 exists for.
            Cellf("18 FC-3.8 tree", WorkedTree(out _, out _));

            // 19 — FC-3.5 exclusivity, isolated: a bag fill and a nested child fill. The child's region must
            //      be SOLID child-colour with no bag colour beneath it and no fringe of bag colour at its rim.
            {
                var inner = Disc("Inner", 16f, 0f, 0f); inner.fill = Solid(new Color(0.98f, 0.85f, 0.15f));
                var outer = Disc("Outer", 34f, 0f, 0f);
                var bag = ShaperNode.Bag("Excl", ShaperCombineMode.Add, outer, inner);
                bag.fill = Solid(new Color(0.72f, 0.10f, 0.30f));
                Cellf("19 Nested ownership", bag);
            }

            // 20 — the availability gate as a PICTURE (FC-4.4): the child asks for `heat`, which nothing
            //      publishes, so it refuses to bind and its region is painted by the nearest binding ancestor.
            //      The cell must show one uniform bag colour and NO hole — a hole is the "silently inert"
            //      failure BC-3.1 names.
            {
                var inner = Disc("Inner", 16f, 0f, 0f); inner.fill = RampOn(ShaperQuantity.Heat);
                var outer = Disc("Outer", 34f, 0f, 0f);
                var bag = ShaperNode.Bag("Gate", ShaperCombineMode.Add, outer, inner);
                bag.fill = new ShaperFillDef
                {
                    kind = ShaperFillKind.Gradient, gradientMode = ShaperGradientMode.Radial,
                    gradient = SheetRamp(),
                };
                Cellf("20 Gate falls back", bag);
            }

            // 21 — the internal fill boundary, and the seam that used to run along it, made VISIBLE rather
            //      than tabulated. A bag fill and a nested child fill partition ONE silhouette, so the child's
            //      soft rim is exactly where the previous build composited two DISJOINT sub-areas with `Over`
            //      and lost up to 25% of the alpha — a one-pixel transparent ring. The bar is a long, thin,
            //      rotated ellipse precisely because that maximises the length of internal boundary in one
            //      cell. Every sample whose composited alpha still falls below the shape's own coverage is
            //      stamped MAGENTA, so a surviving seam would be a screaming line through the middle of the
            //      cell rather than a number in a table. A clean cell IS the assertion.
            int seamCell = cells.Count;
            {
                var bar = ShaperNode.Primitive(
                    new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 32f, ellipseRy = 9f },
                    "Bar", ShaperCombineMode.Add);
                bar.transform.rotation = 28f;
                bar.fill = Solid(new Color(0.10f, 0.55f, 0.95f));
                var outer = Disc("Outer", 36f, 0f, 0f);          // contains the bar, so the SILHOUETTE is the disc
                var bag = ShaperNode.Bag("Seam", ShaperCombineMode.Add, outer, bar);
                bag.fill = Solid(new Color(0.98f, 0.72f, 0.10f));
                Cellf("21 Internal boundary", bag);
            }

            int rows = Mathf.CeilToInt(cells.Count / (float)Cols);
            int texW = Cols * (Cell + Pad) + Pad, texH = rows * (Cell + Pad) + Pad;
            var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(18, 18, 22, 255);
            sheet.SetPixels32(bg);

            var pixels = new Color32[Cell * Cell];
            int seamMarks = -1;
            for (int c = 0; c < cells.Count; c++)
            {
                var rig = Build(cells[c].Value, 0f, 0u, ShaperQuantitySet.ShippedShapeEngine, Cell, Cell);
                Paint(rig);
                CompositeOverBackdrop(rig.buf.dst, pixels, Cell);
                if (c == seamCell) seamMarks = StampSeam(rig, pixels, Cell);
                DrawLabel(pixels, Cell, cells[c].Key);

                int cx = Pad + (c % Cols) * (Cell + Pad);
                int cy = texH - Pad - Cell - (c / Cols) * (Cell + Pad);
                sheet.SetPixels32(cx, cy, Cell, Cell, pixels);
            }
            sheet.Apply();

            byte[] png = sheet.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, png);

            var sb = new StringBuilder("FT-20 contact sheet\n");
            sb.AppendLine("  " + cells.Count + " cells, " + Cols + " x " + rows + ", " + texW + "x" + texH +
                          " px, written to " + path);
            sb.AppendLine("  Each cell is composited over an OPAQUE 8px mid-grey checkerboard and the PNG is fully");
            sb.AppendLine("  opaque, so nothing can render invisibly against a viewer's own background — the failure");
            sb.AppendLine("  the previous sheet had in cells [0,0] and [0,1]. Where the checker shows THROUGH a shape,");
            sb.AppendLine("  that is the veil or a texel alpha doing its job, not a hole. Each cell carries its own");
            sb.AppendLine("  two-digit index bottom-left, drawn in a 3x5 bitmap font, so the legend below is unambiguous.");
            for (int c = 0; c < cells.Count; c++)
                sb.AppendLine("    row " + (c / Cols) + " col " + (c % Cols) + "  " + cells[c].Key);
            sb.AppendLine("  Four cells look wrong at a glance and are not, so the reading is stated rather than left to be re-derived:");
            sb.AppendLine("    08 is FLAT inside on purpose. Coverage is 1 everywhere inside a silhouette, so a ramp BY coverage");
            sb.AppendLine("       is one colour there; the ramp itself is the one-pixel rim. Cell 09 is the same fill on a quantity");
            sb.AppendLine("       that actually varies across the interior, and that is the pair's point.");
            sb.AppendLine("    14 and 15 are IDENTICAL, correctly. Fixed space normalises by the CANVAS half-extents, this canvas is");
            sb.AppendLine("       square, and Uniform vs Stretch is a choice of one divisor vs two — which on a square box is the same");
            sb.AppendLine("       number twice. The dial is doing nothing here because there is nothing for it to do; cells 12 vs 13");
            sb.AppendLine("       are the pair where it bites, because a node's own box is not square.");
            sb.AppendLine("    18 is nearly all ONE colour, which is FC-3.3's ruling made visible rather than a bug. Belt is the last");
            sb.AppendLine("       restrictor in the fold, so the intersected result IS Belt, so Belt's fill owns all of it and Head's");
            sb.AppendLine("       claim is annihilated. The bag's own fill paints 0 samples. FC-3.8's table says otherwise and is wrong.");
            sb.AppendLine("    21 is the FC-3.5a seam probe and it is the one cell whose CORRECT reading is 'nothing to see'.");
            sb.AppendLine("       Every sample whose composited alpha falls below the shape's own coverage is stamped MAGENTA;");
            sb.AppendLine("       on the build before the fix a magenta line traced the whole bar. Marks in this render: " +
                          seamMarks + " (expected 0).");
            sb.Append("  RESULT: RENDERED — NOT VERIFIED BY THE TABLE. A passing table is not a picture; this is " +
                      "not a pass until a human has looked at it.");
            return sb.ToString();
        }

        /// <summary>
        /// Stamp MAGENTA over every sample where the composited alpha has fallen below the shape's own
        /// published coverage — the FC-3.5a seam, drawn instead of counted.
        ///
        /// <b>Why a marker rather than a second picture of the alpha channel.</b> An alpha-channel view is a
        /// cell a reader has to learn to read, and a correct one is a flat white rectangle that looks like a
        /// rendering failure. Stamping the defect ON the ordinary render keeps the cell a picture, keeps it
        /// legible next to its twenty neighbours, and makes the pass condition "no magenta anywhere" — which
        /// needs no legend at all. The threshold is the same 1e-3 FT-21 leg (b) uses, so the picture and the
        /// table cannot disagree.
        /// </summary>
        static int StampSeam(Rig rig, Color32[] px, int cell)
        {
            int marks = 0;
            for (int i = 0; i < cell * cell; i++)
            {
                // owner 0 is the root, and its slab starts at offset 0 — the same read FT-21 makes.
                if (rig.buf.ownCoverage[i] - rig.buf.dst[i * 4 + 3] <= 1e-3f) continue;
                px[i] = new Color32(255, 0, 220, 255);
                marks++;
            }
            return marks;
        }

        /// <summary>
        /// A ramp with enough colour and enough contrast to read against the contact sheet's mid-grey
        /// backdrop. <c>BlackToWhite</c> is the right ramp for FT-4's arithmetic and the wrong one for a
        /// picture, because half of it is the backdrop's own tone.
        /// </summary>
        static ZuiGradient SheetRamp()
        {
            var g = new ZuiGradient();
            g.gradient = new Gradient();
            g.gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.10f, 0.05f, 0.35f), 0f),
                    new GradientColorKey(new Color(0.85f, 0.10f, 0.25f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.90f, 0.30f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            g.EnsureTransformAnim();
            return g;
        }

        /// <summary>
        /// Composite one cell's linear, premultiplied destination over an opaque checkerboard and encode once
        /// (FC-2.3's single encode boundary).
        ///
        /// <b>Why a backdrop rather than <c>ShaperFillResolver.Encode</c> straight to the PNG.</b> A PNG is
        /// straight alpha by definition, so a partly-transparent cell would be composited by whatever the
        /// viewer happens to use as a background — which is what made the old sheet unreadable: a white Solid
        /// over a transparent background is white-on-white in every viewer that renders alpha as white. And an
        /// ADDITIVE fill cannot be shown at all on a transparent sheet, because its alpha is 0 by design
        /// (FC-2.6b) and no straight-alpha 8-bit encoding carries it. Compositing over an opaque mid-tone here
        /// fixes all three, and it makes the veil legible as a veil, because the checker is visible through it.
        /// This is the second of the two answers named on <c>ShaperFillResolver.Encode</c> for carrying an
        /// additive result into bytes; the first is to read the float destination directly.
        ///
        /// The add-over-nothing case survives this too: <c>Add</c> leaves alpha at 0 and RGB above 0, so the
        /// backdrop is BRIGHTENED where an additive fill spills past everything opaque — which is exactly how
        /// an additive glow should read, and cannot be shown at all on a transparent sheet.
        /// </summary>
        static void CompositeOverBackdrop(float[] dst, Color32[] outPixels, int cell)
        {
            const int Square = 8;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    int i = y * cell + x;
                    float back = (((x / Square) + (y / Square)) & 1) == 0 ? 0.16f : 0.31f;   // linear mid-tones
                    float a = Mathf.Clamp01(dst[i * 4 + 3]);
                    float inv = 1f - a;
                    outPixels[i] = new Color32(
                        ShaperSrgb.EncodeToByte(dst[i * 4 + 0] + back * inv),
                        ShaperSrgb.EncodeToByte(dst[i * 4 + 1] + back * inv),
                        ShaperSrgb.EncodeToByte(dst[i * 4 + 2] + back * inv),
                        255);
                }
            // a one-pixel frame, so adjacent cells cannot be mistaken for one image
            for (int x = 0; x < cell; x++)
            {
                outPixels[x] = new Color32(90, 90, 100, 255);
                outPixels[(cell - 1) * cell + x] = new Color32(90, 90, 100, 255);
            }
            for (int y = 0; y < cell; y++)
            {
                outPixels[y * cell] = new Color32(90, 90, 100, 255);
                outPixels[y * cell + cell - 1] = new Color32(90, 90, 100, 255);
            }
        }

        /// <summary>
        /// A 3x5 bitmap font, digits only, one 15-bit int per glyph (three bits per row, top row highest).
        /// Enough to stamp each cell's index so the report's legend maps onto the picture without counting.
        /// </summary>
        static readonly int[] Digits3x5 =
        {
            31599, 11415, 29671, 29647, 23497, 31183, 31215, 29257, 31727, 31695,
        };

        /// <summary>Stamp a label's leading digits into the cell's bottom-left corner, white on a dark plate.</summary>
        static void DrawLabel(Color32[] px, int cell, string label)
        {
            int digits = 0;
            while (digits < label.Length && label[digits] >= '0' && label[digits] <= '9') digits++;
            if (digits == 0) return;

            const int Scale = 2, GlyphW = 3, GlyphH = 5, Gap = 1;
            int w = digits * (GlyphW + Gap) * Scale, h = GlyphH * Scale;
            int ox = 4, oy = 4;                                     // from the cell's bottom-left

            for (int y = -2; y < h + 2; y++)
                for (int x = -2; x < w + 2; x++)
                {
                    int cx = ox + x, cy = oy + y;
                    if (cx < 1 || cy < 1 || cx >= cell - 1 || cy >= cell - 1) continue;
                    px[cy * cell + cx] = new Color32(12, 12, 16, 255);
                }

            for (int d = 0; d < digits; d++)
            {
                int glyph = Digits3x5[label[d] - '0'];
                for (int gy = 0; gy < GlyphH; gy++)
                    for (int gx = 0; gx < GlyphW; gx++)
                    {
                        // The glyph's row 0 is its TOP and sits in the highest bits; the pixel buffer's row 0
                        // is the cell's BOTTOM. So gy counts up from the glyph's LAST row, which is exactly
                        // the low end of the packed int — hence gy*GlyphW rather than (GlyphH-1-gy)*GlyphW.
                        bool on = ((glyph >> (gy * GlyphW + (GlyphW - 1 - gx))) & 1) != 0;
                        if (!on) continue;
                        for (int sy = 0; sy < Scale; sy++)
                            for (int sx = 0; sx < Scale; sx++)
                            {
                                int cx = ox + (d * (GlyphW + Gap) + gx) * Scale + sx;
                                int cy = oy + gy * Scale + sy;
                                if (cx < 1 || cy < 1 || cx >= cell - 1 || cy >= cell - 1) continue;
                                px[cy * cell + cx] = new Color32(240, 240, 245, 255);
                            }
                    }
            }
        }
    }
}

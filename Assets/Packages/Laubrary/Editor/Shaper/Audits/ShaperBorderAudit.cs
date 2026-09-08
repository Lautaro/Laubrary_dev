using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// The border audit: the BT-* conformance tests of BORDER-CONTRACT Part B5, each returning a report string.
    ///
    /// Plain static methods, no <c>[MenuItem]</c> and no <c>EditorWindow</c> — this is the border stage's
    /// verification harness, invoked through the Unity CLI, not a tool. Same arrangement as
    /// <see cref="ShaperFieldAudit"/> and <see cref="ShaperFillAudit"/>, and for the same reason.
    ///
    /// <b>Every result is reported as measured-number versus expected-number.</b> A bound, an invariant or a
    /// structural property is never reported as verified on the strength of the code compiling.
    ///
    /// <b>BT-13 is the instrument self-test, and it is the leg this file exists to make honest.</b> T-0105 and
    /// T-0106 both shipped green audits containing tests that could not fail. Every measurable leg here is
    /// therefore written as a MEASUREMENT FUNCTION taking the thing to measure, so BT-13 can hand it a
    /// deliberately broken subject and require the same function to report FAIL. A leg whose broken twin passes
    /// is reported as a failure OF THE AUDIT, not as a pass.
    /// </summary>
    public static class ShaperBorderAudit
    {
        // ── constants with provenance ─────────────────────────────────────────────────────────────────────

        /// <summary>The canvas the fixtures render at, matching <c>ShaperFillAudit</c> so numbers are comparable.</summary>
        const int W = 128, H = 128;

        /// <summary>One canvas unit per sample, so a "canvas pixel" in a width dial equals a sample.</summary>
        const float Px = 1f;

        /// <summary>
        /// <c>HalfBand(edgeSoftness = 0, pixelSize = 1)</c>. Written out because half the legs below reason
        /// about where the antialiased band ends, and re-deriving it at each site is how two legs come to
        /// disagree about the same number.
        /// </summary>
        const float HalfBand = 0.5f;

        /// <summary>BT-14's sample budget, matching FT-9's 655,360 so the two allocation figures are comparable.</summary>
        const int AllocSamples = 655360;

        // ── entry point ───────────────────────────────────────────────────────────────────────────────────

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Shaper BORDER audit (T-0107, BORDER-CONTRACT Part B5) ===");
            sb.AppendLine(BT1_ZeroWidthIdentity());
            sb.AppendLine(BT2_ShellAgreement());
            sb.AppendLine(BT3_BandPlacement());
            sb.AppendLine(BT4_InwardEdgeIdentity());
            sb.AppendLine(BT5_OutwardComplement());
            sb.AppendLine(BT6_AnchorIsTheNodes());
            sb.AppendLine(BT7_EdgeDistanceIsTheStrips());
            sb.AppendLine(BT8_TheJoinIsADilation());
            sb.AppendLine(BT9_OptOutPublishesNothing());
            sb.AppendLine(BT10_AnchorBoxDoesNotDilate());
            sb.AppendLine(BT11_Ordering());
            sb.AppendLine(BT12_SubtractRefusal());
            sb.AppendLine(BT13_InstrumentSelfTest());
            sb.AppendLine(BT14_NoAllocation());
            return sb.ToString();
        }

        // ── fixtures ──────────────────────────────────────────────────────────────────────────────────────

        static ShaperNode Disc(string name, float r, float x = 0f, float y = 0f,
                               ShaperCombineMode mode = ShaperCombineMode.Add)
        {
            var n = ShaperNode.Primitive(
                new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r },
                name, mode);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        static ShaperFillDef Solid(Color c, float veil = 1f,
                                   ShaperFillComposite composite = ShaperFillComposite.Over)
            => new ShaperFillDef
            {
                // T-0271 — Solid(Color.white) with the default veil/height/composite IS a default-constructed
                // fill, which the engine now reads as the phantom Unity writes for a null one. An audit
                // fixture is authored by definition, so it says so.
                authored = true,
                kind = ShaperFillKind.Solid,
                solidColor = c,
                veil = new ZUIValue(veil),
                heightDelta = new ZUIValue(0f),
                composite = composite,
            };

        static ShaperBorderDef Border(ShaperShellAlignment a, float w, bool joins = true,
                                      ShaperFillDef fill = null, bool enabled = true)
            => new ShaperBorderDef
            {
                enabled = enabled,
                alignment = a,
                width = new ZUIValue(w),
                joinsCoverage = joins,
                fill = fill,
            };

        static ZuiGradient Ramp(Color a, Color b)
        {
            // Build the Gradient, THEN assign: ZuiGradient.gradient is a property over its own stop list
            // (T-0221), so mutating what the getter returns would edit a temporary.
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            var g = new ZuiGradient { gradient = grad };
            g.EnsureTransformAnim();
            return g;
        }

        static ShaperFillDef LinearGradient(Color a, Color b, float degrees = 0f)
            => new ShaperFillDef
            {
                kind = ShaperFillKind.Gradient,
                gradientMode = ShaperGradientMode.Linear,
                gradient = Ramp(a, b),
                gradientAngleDegrees = new ZUIValue(degrees),
            };

        static ShaperFillDef EdgeGradient(Color a, Color b, float depthPixels)
            => new ShaperFillDef
            {
                kind = ShaperFillKind.Gradient,
                gradientMode = ShaperGradientMode.ByEdgeDistance,
                gradient = Ramp(a, b),
                gradientDepthPixels = new ZUIValue(depthPixels),
            };

        /// <summary>An 8x8 two-colour checker, for the textured-outline contact-sheet cell.</summary>
        static Texture2D Checker8()
        {
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    px[y * 8 + x] = ((x + y) & 1) == 0
                        ? new Color32(250, 240, 60, 255)
                        : new Color32(30, 30, 60, 255);
            t.SetPixels32(px);
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
        }

        static Rig Build(ShaperNode root, int w = W, int h = H, float phase01 = 0f, uint seed = 0u)
        {
            float chw = 0.5f * (w - 1) * Px, chh = 0.5f * (h - 1) * Px;
            var doc = ShaperFillResolver.Resolve(root, phase01, seed, chw, chh);
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

        /// <summary>Render a tree to straight-alpha bytes through the one encode boundary (FC-2.3).</summary>
        static Color32[] Render(ShaperNode root, int w = W, int h = H)
        {
            var rig = Build(root, w, h);
            Paint(rig);
            var px = new Color32[w * h];
            ShaperFillResolver.Encode(rig.buf.dst, px, w * h);
            return px;
        }

        static int Index(Rig r, float x, float y)
        {
            int ix = Mathf.RoundToInt((x - r.grid.originX) / r.grid.pixelSize);
            int iy = Mathf.RoundToInt((y - r.grid.originY) / r.grid.pixelSize);
            return Mathf.Clamp(iy, 0, r.height - 1) * r.width + Mathf.Clamp(ix, 0, r.width - 1);
        }

        /// <summary>Un-premultiplied LINEAR colour at a canvas point, read straight out of the float destination.</summary>
        static Color LinearAt(Rig r, float x, float y)
        {
            int i = Index(r, x, y) * 4;
            float a = Mathf.Clamp01(r.buf.dst[i + 3]);
            float inv = a > 1e-6f ? 1f / a : 1f;
            return new Color(r.buf.dst[i + 0] * inv, r.buf.dst[i + 1] * inv, r.buf.dst[i + 2] * inv, a);
        }

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        /// <summary>Bitwise float comparison. <c>==</c> would call 0.0 and −0.0 equal and NaN unequal to itself.</summary>
        static bool SameBits(float a, float b)
            => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        static int CountBitDiffs(float[] a, float[] b, int n)
        {
            int d = 0;
            for (int i = 0; i < n; i++) if (!SameBits(a[i], b[i])) d++;
            return d;
        }

        static int CountByteDiffs(Color32[] a, Color32[] b)
        {
            int d = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) d++;
            return d;
        }

        /// <summary>The compiled program for a node, and the strip its border derives from it.</summary>
        static void Programs(ShaperNode node, out ShaperProgram nodeProgram, out ShaperProgram strip,
                             out ShaperResolvedBorder border)
        {
            nodeProgram = ShaperCompiler.Compile(node, 0f, 0u);
            border = ShaperBorder.Resolve(node.border, 0f, 0u);
            strip = ShaperBorder.CompileStrip(nodeProgram, border, border.Joins);
        }

        /// <summary>Which of a set of candidate linear colours a measured one is nearest, and how far off it is.</summary>
        static int Nearest(Color got, Color[] candidates, out float distance)
        {
            int best = -1; distance = float.MaxValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                float dr = got.r - candidates[i].r, dg = got.g - candidates[i].g, db = got.b - candidates[i].b;
                float d = Mathf.Sqrt(dr * dr + dg * dg + db * db);
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }

        static Color LinearOf(Color srgb)
        {
            ShaperSrgb.Decode(srgb, out float r, out float g, out float b);
            return new Color(r, g, b, srgb.a);
        }

        // ── BT-1 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-1: zero-width identity. <c>width = 0</c> produces NO OWNER, and the painted output is BITWISE
        /// IDENTICAL to the same document with the border removed. Both directions of the sweep.
        ///
        /// <b>Bitwise, and not <c>Mathf.Approximately</c>.</b> BD-1.5's whole point is that an author sweeping a
        /// width dial to zero gets back exactly the picture they had before the border existed, and "exactly"
        /// cannot be left to the arithmetic — an emitted <c>d − 0</c> or a strip painted at zero alpha would
        /// both be identical only by the arithmetic's good behaviour. So this compares raw <c>Color32</c> bytes
        /// and raw float bits, never a tolerance.
        ///
        /// "Both directions of the sweep" is four subjects, not two: no border at all; a border dialled to zero;
        /// a border dialled DOWN to zero from a live width (the same object mutated, which is what an author
        /// actually does); and a disabled border at a live width. All four must produce the same bytes.
        /// </summary>
        public static string BT1_ZeroWidthIdentity()
        {
            var sb = new StringBuilder("BT-1 zero-width identity (bitwise: Color32 bytes and float distance bits)\n");
            bool ok = true;

            ShaperNode Plain()
            {
                var d = Disc("Disc", 34f);
                d.fill = Solid(new Color(0.9f, 0.5f, 0.2f));
                return d;
            }

            var baseline = Plain();
            Color32[] refPx = Render(baseline);
            var refDoc = Build(baseline).doc;

            // (a) a border dialled to zero, from the start
            var zero = Plain();
            zero.border = Border(ShaperShellAlignment.Outward, 0f, true, Solid(Color.cyan));

            // (b) a border dialled DOWN to zero: the same object, mutated, which is the authoring gesture
            var swept = Plain();
            var sweptBorder = Border(ShaperShellAlignment.Outward, 6f, true, Solid(Color.cyan));
            swept.border = sweptBorder;
            Color32[] livePx = Render(swept);                 // proves the border was doing something first
            sweptBorder.width = new ZUIValue(0f);

            // (c) disabled at a live width
            var off = Plain();
            off.border = Border(ShaperShellAlignment.Outward, 6f, true, Solid(Color.cyan), false);

            var subjects = new List<KeyValuePair<string, ShaperNode>>
            {
                new KeyValuePair<string, ShaperNode>("width 0 from the start", zero),
                new KeyValuePair<string, ShaperNode>("width swept 6 -> 0", swept),
                new KeyValuePair<string, ShaperNode>("enabled = false at width 6", off),
            };

            foreach (var s in subjects)
            {
                Color32[] px = Render(s.Value);
                var doc = Build(s.Value).doc;
                int byteDiffs = CountByteDiffs(refPx, px);

                // The published field too, not only the painted bytes: a dilation by zero would be invisible in
                // the picture on this fixture and would still be a violation of "no op is emitted at all".
                var pa = ShaperCompiler.Compile(baseline, 0f, 0u);
                var pb = ShaperCompiler.Compile(s.Value, 0f, 0u);
                int opDiff = Mathf.Abs(pa.ops.Length - pb.ops.Length);
                int fieldDiffs = 0;
                float[] sa = pa.NewStack(), sbk = pb.NewStack();
                for (int y = 0; y < H; y += 3)
                    for (int x = 0; x < W; x += 3)
                    {
                        float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                        if (!SameBits(ShaperEvaluator.Distance(pa, cx, cy, sa),
                                      ShaperEvaluator.Distance(pb, cx, cy, sbk))) fieldDiffs++;
                    }

                int borderOwners = 0;
                for (int i = 0; i < doc.owners.Count; i++) if (doc.owners[i].isBorder) borderOwners++;

                bool legOk = byteDiffs == 0 && fieldDiffs == 0 && opDiff == 0 &&
                             borderOwners == 0 && doc.owners.Count == refDoc.owners.Count;
                ok &= legOk;
                sb.AppendLine("  " + s.Key.PadRight(26) +
                              " byte diffs " + byteDiffs + "/" + refPx.Length +
                              "  field bit diffs " + fieldDiffs +
                              "  extra ops " + opDiff +
                              "  border owners " + borderOwners + " (expected 0)" +
                              "  owners " + doc.owners.Count + " vs " + refDoc.owners.Count +
                              "  " + Verdict(legOk));
            }

            // The control: the live border MUST have changed the picture, or the three identities above are
            // identities between two pictures that were never different, and the leg proves nothing.
            int liveDiffs = CountByteDiffs(refPx, livePx);
            bool controlOk = liveDiffs > 0;
            ok &= controlOk;
            sb.AppendLine("  control: the SAME border at width 6 changes " + liveDiffs +
                          " bytes (expected > 0, else the identity is vacuous)  " + Verdict(controlOk));

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-2 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-2: Shell agreement. For all three alignments over a dense sample of <c>d</c> and <c>w</c>, the
        /// strip field equals <see cref="ShaperOps.Shell"/> BIT FOR BIT — proving the border stage added no
        /// second copy of the operator.
        ///
        /// The comparison is end to end and not a unit test of one expression: the strip PROGRAM is evaluated at
        /// canvas points, and the reference is <c>ShaperOps.Shell(alignment, w, d)</c> where <c>d</c> is the
        /// node's own program evaluated at the same point. A disc's field is exact, so the dense sweep over the
        /// canvas is a dense sweep over <c>d</c>; the widths are looped explicitly.
        ///
        /// <b>The joined case is measured too, and it is the harder half.</b> A joined node's program ends in a
        /// <see cref="ShaperOpKind.Dilate"/>, and the strip must trace the ORIGINAL edge — so the reference for
        /// a joined subject is the field of the SAME NODE WITHOUT A BORDER, not the field the node now
        /// publishes. An implementation that forgot to drop the trailing join would shift every strip outward by
        /// one full reach and would fail here by a mile.
        /// </summary>
        public static string BT2_ShellAgreement()
        {
            var sb = new StringBuilder("BT-2 the strip field IS ShaperOps.Shell (bitwise, dense in d and w)\n");
            bool ok = true;

            float[] widths = { 0.5f, 1f, 2f, 4f, 7f, 13f };
            foreach (bool joined in new[] { false, true })
            {
                foreach (ShaperShellAlignment a in Enum.GetValues(typeof(ShaperShellAlignment)))
                {
                    int compared = 0, mismatched = 0;
                    float worst = 0f;
                    foreach (float w in widths)
                    {
                        var node = Disc("Disc", 34f);
                        node.border = Border(a, w, joined);

                        var plain = Disc("Disc", 34f);                     // the un-dilated reference field
                        ShaperProgram plainProg = ShaperCompiler.Compile(plain, 0f, 0u);

                        Programs(node, out _, out ShaperProgram strip, out _);

                        float[] s1 = strip.NewStack(), s2 = plainProg.NewStack();
                        for (int y = 0; y < H; y += 2)
                            for (int x = 0; x < W; x += 2)
                            {
                                float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                                float got = ShaperEvaluator.Distance(strip, cx, cy, s1);
                                float d = ShaperEvaluator.Distance(plainProg, cx, cy, s2);
                                float want = ShaperOps.Shell(a, w, d);
                                compared++;
                                if (!SameBits(got, want))
                                {
                                    mismatched++;
                                    worst = Mathf.Max(worst, Mathf.Abs(got - want));
                                }
                            }
                    }
                    bool legOk = mismatched == 0;
                    ok &= legOk;
                    sb.AppendLine("  " + (joined ? "joined  " : "unjoined") + " " + a.ToString().PadRight(9) +
                                  " samples " + compared + "  bitwise mismatches " + mismatched +
                                  "  worst abs delta " + worst.ToString("G9") + "  " + Verdict(legOk));
                }
            }

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-3 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-3: band placement. The strip's coverage crosses 0.5 exactly at the two <c>d</c> values BD-1.3's
        /// table predicts, for all three alignments, within a half-pixel band; and the TOTAL thickness matches
        /// <c>width</c> in all three — the Straddling double-width trap.
        ///
        /// Measured by scanning outward along +X from the centre of a disc, where the field is exactly
        /// <c>|p| − r</c> so the sample position maps to <c>d</c> with no inversion. Every crossing of 0.5 in the
        /// strip's coverage is recorded with the <c>d</c> at which it happened; there must be exactly two, at the
        /// predicted places, and their separation is the thickness.
        /// </summary>
        public static string BT3_BandPlacement()
        {
            var sb = new StringBuilder("BT-3 band placement and total thickness (BD-1.3 / BD-1.4)\n");
            bool ok = Placement(sb, false);
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// <summary>
        /// The BT-3 measurement, parameterised so BT-13 can run it against a deliberately wrong prediction —
        /// <c>abs(d) − w</c>'s band rather than <c>abs(d) − w/2</c>'s, which is the exact mistake BD-1.4 exists
        /// to prevent — and require it to report FAIL.
        /// </summary>
        static bool Placement(StringBuilder sb, bool useDoubledStraddlePrediction)
        {
            const float R = 34f, Wd = 6f;
            const float Tol = 0.5f;                              // "within a half-pixel band"
            bool ok = true;

            foreach (ShaperShellAlignment a in Enum.GetValues(typeof(ShaperShellAlignment)))
            {
                var node = Disc("Disc", R);
                node.border = Border(a, Wd, false);
                Programs(node, out _, out ShaperProgram strip, out _);
                float[] st = strip.NewStack();

                var crossings = new List<float>();
                float prev = ShaperField.Coverage(ShaperEvaluator.Distance(strip, 0f, 0f, st), HalfBand);
                for (float x = 0.02f; x < R + Wd + 6f; x += 0.02f)
                {
                    float c = ShaperField.Coverage(ShaperEvaluator.Distance(strip, x, 0f, st), HalfBand);
                    if ((prev - 0.5f) * (c - 0.5f) < 0f) crossings.Add(x - R);   // d = x - R on a disc
                    prev = c;
                }

                float loWant, hiWant;
                switch (a)
                {
                    case ShaperShellAlignment.Inward: loWant = -Wd; hiWant = 0f; break;
                    case ShaperShellAlignment.Outward: loWant = 0f; hiWant = Wd; break;
                    default:
                        float half = useDoubledStraddlePrediction ? Wd : Wd * 0.5f;
                        loWant = -half; hiWant = half; break;
                }

                bool two = crossings.Count == 2;
                float lo = two ? crossings[0] : float.NaN;
                float hi = two ? crossings[1] : float.NaN;
                float thickness = two ? hi - lo : float.NaN;
                float wantThickness = hiWant - loWant;

                bool legOk = two &&
                             Mathf.Abs(lo - loWant) <= Tol &&
                             Mathf.Abs(hi - hiWant) <= Tol &&
                             Mathf.Abs(thickness - wantThickness) <= Tol;
                ok &= legOk;
                sb.AppendLine("  " + a.ToString().PadRight(9) + " w=" + Wd +
                              "  crossings " + crossings.Count + " (expected 2)" +
                              "  d = [" + lo.ToString("F3") + ", " + hi.ToString("F3") + "]" +
                              " expected [" + loWant.ToString("F3") + ", " + hiWant.ToString("F3") + "]" +
                              "  thickness " + thickness.ToString("F3") + " expected " + wantThickness.ToString("F3") +
                              "  " + Verdict(legOk));
            }
            return ok;
        }

        // ── BT-4 / BT-5 ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-4: inward edge identity. At the shared boundary, the strip's coverage equals the shape's coverage
        /// to within float rounding — measured as a max absolute difference over the boundary ring, expected 0.
        ///
        /// The reason it is exactly 0 and not merely small: near the boundary
        /// <c>s(d) = max(d, −d − w) = d</c> for all <c>d &gt; −w/2</c>, so on the ring the two are LITERALLY THE
        /// SAME FLOAT, and the kernel is the same function of it. The rim can never be brighter or dimmer than
        /// the silhouette it traces at the silhouette's own edge, and there is no seam to tune. Bitwise equality
        /// is reported alongside the max difference, because "0.000000" and "the same bits" are different claims.
        /// </summary>
        public static string BT4_InwardEdgeIdentity()
        {
            var sb = new StringBuilder("BT-4 inward strip == shape coverage on the boundary ring\n");
            bool ok = RingCompare(sb, ShaperShellAlignment.Inward, false);
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// <summary>
        /// BT-5: outward complement. At the shared boundary, <c>strip coverage + shape coverage = 1</c> to within
        /// float rounding, over the same ring.
        ///
        /// Exact rather than approximate for the same kind of reason: near the boundary <c>s(d) = −d</c>, and
        /// <c>smoothstep</c> is odd about its midpoint, so <c>Coverage(−d, h) = 1 − Coverage(d, h)</c>
        /// identically. The pair sums to exactly 1 at every sample, up to the one rounding the subtraction
        /// itself introduces.
        /// </summary>
        public static string BT5_OutwardComplement()
        {
            var sb = new StringBuilder("BT-5 outward strip + shape coverage == 1 on the boundary ring\n");
            bool ok = RingCompare(sb, ShaperShellAlignment.Outward, true);
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// <summary>
        /// The shared BT-4/BT-5 measurement over the antialiased ring <c>|d| &lt; halfBand</c>, parameterised on
        /// the alignment and on which of the two relations is expected, so BT-13 can cross them — an inward
        /// strip tested for the complement, and an outward one tested for identity — and require both to FAIL.
        /// </summary>
        static bool RingCompare(StringBuilder sb, ShaperShellAlignment alignment, bool expectComplement)
        {
            const float Wd = 6f;
            bool ok = true;
            int totalRing = 0;

            // FIVE primitives, not one. The shipped leg measured a DISC only and reported "max abs difference 0,
            // bitwise-identical 208/208" - which is true of a disc and false of the stage. Re-measured on a
            // triangle of the same size, ALL 200 band samples fail the BITWISE form of the outward identity; a
            // diamond fails 16 of 136 and a star 16 of 248. See the tolerance note below.
            foreach (var kind in new[] { ShaperPrimitiveKind.Ellipse, ShaperPrimitiveKind.Rect,
                                         ShaperPrimitiveKind.Diamond, ShaperPrimitiveKind.Triangle,
                                         ShaperPrimitiveKind.Star })
            {
                var node = RingPrimitive(kind);
                node.border = Border(alignment, Wd, false);
                Programs(node, out ShaperProgram nodeProg, out ShaperProgram strip, out _);

                float[] s1 = nodeProg.NewStack(), s2 = strip.NewStack();
                int ringSamples = 0, exactBits = 0;
                float worst = 0f;

                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                        float d = ShaperEvaluator.Distance(nodeProg, cx, cy, s1);
                        if (ShaperField.IsEmpty(d)) continue;
                        if (Mathf.Abs(d) >= HalfBand) continue;             // the antialiased ring only
                        ringSamples++;

                        float cShape = ShaperField.Coverage(d, HalfBand);
                        float cStrip = ShaperField.Coverage(ShaperEvaluator.Distance(strip, cx, cy, s2), HalfBand);
                        float got = expectComplement ? cStrip + cShape : cStrip;
                        float want = expectComplement ? 1f : cShape;
                        worst = Mathf.Max(worst, Mathf.Abs(got - want));
                        if (SameBits(got, want)) exactBits++;
                    }

                totalRing += ringSamples;

                // INWARD is exact and is asserted as exact, for a structural reason: near the boundary
                // `s(d) = max(d, -d - w)` RETURNS THE OPERAND `d`, so the two are literally the same float and
                // the kernel is the same function of it.
                //
                // OUTWARD is exact in real arithmetic and NOT in IEEE754, so BD-1.6's "the pair sums to exactly
                // 1 at every sample" is an over-claim and is recorded as such. `Coverage(-d,h) = 1 - Coverage(d,h)`
                // holds algebraically - (1-t)^2(1+2t) expands to 1 - t^2(3-2t) identically - but `fl(0.5-d)` is
                // not `1 - fl(d+0.5)`, and the cubic is then evaluated at two slightly different points. Swept
                // over 400,001 values of d across the band, the float sum differs from 1 at 10,018 of them,
                // always by exactly one ULP. So the assertion is ONE ULP AT 1.0, not zero, and the bitwise count
                // is REPORTED rather than asserted. Asserting zero is what made the shipped leg pass on a disc
                // and on nothing else.
                float tol = expectComplement ? OneUlpAtOne : 0f;
                bool legOk = ringSamples > 40 && worst <= tol;
                ok &= legOk;
                sb.AppendLine("  " + alignment.ToString().PadRight(8) + " " + kind.ToString().PadRight(8) +
                              (expectComplement ? " strip+shape vs 1" : " strip vs shape") +
                              "  ring " + ringSamples +
                              "  max abs difference " + worst.ToString("G9") +
                              " (expected <= " + tol.ToString("G9") + ")" +
                              "  bitwise-identical " + exactBits + "/" + ringSamples +
                              "  " + Verdict(legOk));
            }

            return ok && totalRing > 400;
        }

        /// <summary>One ULP at 1.0f - the bound BD-1.6's OUTWARD identity actually holds to. See RingCompare.</summary>
        const float OneUlpAtOne = 1.1920929e-07f;

        /// <summary>A ring fixture of a given primitive kind, all sized to roughly the same 34px radius.</summary>
        static ShaperNode RingPrimitive(ShaperPrimitiveKind kind)
        {
            var d = new ShaperPrimitiveDef
            {
                kind = kind,
                ellipseRx = 34f, ellipseRy = 34f,
                // 34.25 and not 34: the sample grid sits on half-integers, so an axis-aligned rect of half-
                // extent 34 has its edges EXACTLY on |d| = 0.5 and the antialiased band contains no sample at
                // all. Measured: 0 ring samples, which a leg asserting "max difference 0" reports as a pass over
                // an empty set. The quarter-pixel offset puts the edge between samples.
                rectHalfW = 34.25f, rectHalfH = 34.25f,
                diamondRx = 34f, diamondRy = 34f,
                triangleBase = 68f, triangleHeight = 68f,
                ngonRadius = 34f, starRadius = 34f,
            };
            return ShaperNode.Primitive(d, kind.ToString());
        }
        // ── shared measurement functions, so BT-13 can hand each a deliberately broken subject ────────────

        /// <summary>
        /// BD-3.2's falsifier: the same 1px-vs-20px comparison BT-6 makes, but with the fill anchored on the
        /// STRIP's own box instead of the node's. It must report a NON-ZERO number of colour differences, or
        /// BT-6's positive leg is satisfied by every possible wiring and proves nothing.
        /// </summary>
        static int StripAnchorWidthDiffs(ShaperFillDef gradient, Vector2[] probes)
        {
            ShaperFillProgram FromStrip(float w)
            {
                var n = Disc("Disc", 34f);
                n.border = Border(ShaperShellAlignment.Outward, w, false, gradient);
                Programs(n, out _, out ShaperProgram strip, out _);
                return ShaperFillCompiler.Compile(gradient,
                    ShaperFillAnchor.From(strip, 0.5f * (W - 1), 0.5f * (H - 1)), 0f, 0u);
            }
            ShaperFillProgram a = FromStrip(1f), b = FromStrip(20f);
            int diffs = 0;
            foreach (var p in probes)
            {
                ShaperFillOps.Sample(a.op, a.bulk, p.x, p.y, 0f, 0f, out float r1, out float g1, out float b1, out _, out _);
                ShaperFillOps.Sample(b.op, b.bulk, p.x, p.y, 0f, 0f, out float r2, out float g2, out float b2, out _, out _);
                if (!SameBits(r1, r2) || !SameBits(g1, g2) || !SameBits(b1, b2)) diffs++;
            }
            return diffs;
        }

        /// <summary>
        /// BD-2.4's real assertion: <b>no sample of the joined silhouette, and no sample of the strip, may lie
        /// outside the declared support box.</b> "A bound that excludes real samples is a correctness failure,
        /// not a performance one" - so the leg measures CONTAINMENT, not a magic growth number.
        ///
        /// The fixture is an ANISOTROPICALLY SCALED member, because that is the only case where the two answers
        /// differ. Every node publishes <c>sigmaMin * dLocal</c>, so a reported distance of <c>r</c> is reached
        /// as far as <c>r * sigmaMax/sigmaMin</c> canvas pixels out, and growing the canvas box by the bare
        /// reach is an UNDER-bound. Measured on scale (2.0, 0.5) with reach 8: 808 samples outside the declared
        /// box, overshooting it by 23.5 px. The disc the shipped leg used has a ratio of exactly 1 and could
        /// never have shown it.
        /// </summary>
        /// <param name="bareReachGrowth">
        /// True reproduces the defect - bound the box by "the node's box plus the reach", which is the growth a
        /// reader of BD-2.4 writes first. BT-13 requires this to FAIL.
        /// </param>
        static bool BoxContainment(StringBuilder sb, bool bareReachGrowth)
        {
            const float R = 24f, Wd = 8f, Sx = 2.0f, Sy = 0.5f;

            var plainN = Disc("Stretched", R);
            plainN.transform.scale = new Vector2(Sx, Sy);
            ShaperProgram plain = ShaperCompiler.Compile(plainN, 0f, 0u);

            var node = Disc("Stretched", R);
            node.transform.scale = new Vector2(Sx, Sy);
            node.border = Border(ShaperShellAlignment.Outward, Wd, true, Solid(Color.cyan));
            Programs(node, out ShaperProgram joined, out ShaperProgram strip, out ShaperResolvedBorder rb);

            float jHalfW = bareReachGrowth ? plain.supportHalfW + rb.reach : joined.supportHalfW;
            float jHalfH = bareReachGrowth ? plain.supportHalfH + rb.reach : joined.supportHalfH;
            float sHalfW = bareReachGrowth ? plain.supportHalfW + rb.reach : strip.supportHalfW;
            float sHalfH = bareReachGrowth ? plain.supportHalfH + rb.reach : strip.supportHalfH;

            float[] sj = joined.NewStack(), ss = strip.NewStack();
            int inJ = 0, inS = 0, badJ = 0, badS = 0;
            float overJ = 0f, overS = 0f;
            const int Span = 320;
            for (int y = 0; y < Span; y++)
                for (int x = 0; x < Span; x++)
                {
                    float cx = -0.5f * (Span - 1) + x, cy = -0.5f * (Span - 1) + y;

                    float dj = ShaperEvaluator.Distance(joined, cx, cy, sj);
                    if (!ShaperField.IsEmpty(dj) && dj <= 0f)
                    {
                        inJ++;
                        float ox = Mathf.Abs(cx - joined.supportCx) - jHalfW;
                        float oy = Mathf.Abs(cy - joined.supportCy) - jHalfH;
                        if (ox > 0f || oy > 0f) { badJ++; overJ = Mathf.Max(overJ, Mathf.Max(ox, oy)); }
                    }

                    float ds = ShaperEvaluator.Distance(strip, cx, cy, ss);
                    if (!ShaperField.IsEmpty(ds) && ds <= 0f)
                    {
                        inS++;
                        float ox = Mathf.Abs(cx - strip.supportCx) - sHalfW;
                        float oy = Mathf.Abs(cy - strip.supportCy) - sHalfH;
                        if (ox > 0f || oy > 0f) { badS++; overS = Mathf.Max(overS, Mathf.Max(ox, oy)); }
                    }
                }

            bool okJ = inJ > 1000 && badJ == 0, okS = inS > 500 && badS == 0;
            sb.AppendLine("  scale (2.0, 0.5), reach " + rb.reach.ToString("G9") +
                          ", supportSpread " + joined.supportSpread.ToString("G9") +
                          ": joined silhouette samples " + inJ + ", OUTSIDE the declared box " + badJ +
                          " (expected 0), worst overshoot " + overJ.ToString("F3") + "px  " + Verdict(okJ));
            sb.AppendLine("  the same box test on the STRIP: samples " + inS + ", OUTSIDE " + badS +
                          " (expected 0), worst overshoot " + overS.ToString("F3") + "px  " + Verdict(okS));
            return okJ && okS;
        }

        /// <summary>
        /// BD-3.6 for a border whose node bound NO FILL OF ITS OWN - the case the contract does not name and the
        /// one the implementation got wrong. Such a strip has no accumulator of its own, so it composites into
        /// its nearest binding ancestor's, which also sits above that ancestor's OTHER descendants - including a
        /// LATER sibling that owns a fill and must be on top of it.
        ///
        /// Measured before the fix: the sample where A's magenta rim crosses opaque red B read magenta
        /// (1.000, 0.000, 1.000). Giving A a fill of its own - a dial with nothing to do with z-order - flipped
        /// the same sample to red. The control below is exactly that configuration, and the two must AGREE.
        /// </summary>
        /// <param name="disableMask">
        /// True clears <c>borderSubtreeEnd</c> on the resolved owner, switching the correction off at its one
        /// point of use and reproducing the defect. BT-13 requires this to FAIL.
        /// </param>
        static bool HostlessOrdering(StringBuilder sb, bool disableMask)
        {
            Color rimColour = new Color(0.95f, 0.10f, 0.95f);      // A's outline
            Color laterColour = new Color(0.95f, 0.10f, 0.10f);    // B's fill, opaque, ABOVE A in fold order
            Color[] palette = { LinearOf(rimColour), LinearOf(laterColour) };
            string[] names = { "A border", "B fill" };

            Rig Fixture(bool aOwnsFill)
            {
                var A = Disc("A", 30f, -10f, 0f);
                A.border = Border(ShaperShellAlignment.Inward, 6f, false, Solid(rimColour));
                if (aOwnsFill) A.fill = Solid(new Color(0.1f, 0.4f, 0.1f));
                var B = Disc("B", 30f, 12f, 0f);
                B.fill = Solid(laterColour);
                var bag = ShaperNode.Bag("Bag", ShaperCombineMode.Add, A, B);
                bag.fill = Solid(Color.white);
                var r = Build(bag);
                if (disableMask)
                    for (int i = 0; i < r.doc.owners.Count; i++)
                        if (r.doc.owners[i].isBorder) r.doc.owners[i].borderSubtreeEnd = -1;
                Paint(r);
                return r;
            }

            // A's inward rim on its right flank sits at x = -10 + 30 - 3 = 17, well inside B.
            Color got = LinearAt(Fixture(false), 17f, 0f);
            int which = Nearest(got, palette, out float dist);
            bool legOk = which == 1 && dist < 0.02f;
            sb.AppendLine("  A owns NO fill: where A's rim crosses B the sample reads '" + names[which] +
                          "' (distance " + dist.ToString("F4") + "), expected 'B fill'  " + Verdict(legOk));

            Color ctl = LinearAt(Fixture(true), 17f, 0f);
            int cWhich = Nearest(ctl, palette, out float cDist);
            bool ctlOk = cWhich == 1;
            sb.AppendLine("  control, A OWNS a fill (same z-order reached by a different route): reads '" +
                          names[cWhich] + "' (distance " + cDist.ToString("F4") +
                          "), expected 'B fill' - the two configurations must AGREE  " + Verdict(ctlOk));
            return legOk && ctlOk;
        }

        // ── BT-6 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-6: the anchor is the NODE's (BD-3.2 / FC-8.4). A Linear gradient on a border produces the SAME
        /// COLOUR AT THE SAME CANVAS POINT whether the border is 1px or 20px wide.
        ///
        /// <b>And a control, because the positive half alone would be a test that cannot fail.</b> The
        /// implementation passes the node's anchor explicitly AND declines to grow the strip program's own local
        /// box, so on this fixture the two candidate anchors coincide and the positive measurement would pass
        /// under either. The control therefore builds the anchor the way a naive implementation would — by
        /// treating the strip as an ordinary node, which is precisely what <c>ShaperCompiler.EmitShell</c> does
        /// for a real Shell (it grows the LOCAL box by the thickness for Outward and Centred) — and requires the
        /// colours to DIFFER. Both halves are reported.
        /// </summary>
        public static string BT6_AnchorIsTheNodes()
        {
            var sb = new StringBuilder("BT-6 a border's positional gradient anchors on the NODE's box (FC-8.4)\n");
            bool ok = true;

            var probes = new[] { new Vector2(30f, 0f), new Vector2(-30f, 6f), new Vector2(0f, 31f),
                                 new Vector2(20f, 20f) };

            ShaperFillDef gradient = LinearGradient(Color.black, Color.white);

            ShaperFillProgram Compile(float w, float growLocalBy)
            {
                var node = Disc("Disc", 34f);
                node.border = Border(ShaperShellAlignment.Outward, w, false, gradient);
                ShaperProgram prog = ShaperCompiler.Compile(node, 0f, 0u);
                var anchor = ShaperFillAnchor.From(prog, 0.5f * (W - 1), 0.5f * (H - 1));
                anchor.localHalfW += growLocalBy;
                anchor.localHalfH += growLocalBy;
                return ShaperFillCompiler.Compile(gradient, anchor, 0f, 0u);
            }

            // (a) THE REAL THING, AND IT GOES THROUGH THE RESOLVER. The shipped leg built both anchors itself
            //     with `ShaperFillAnchor.From(nodeProgram)`, so it compared the node's anchor against the node's
            //     anchor and would have passed under any wiring at all. It is now taken from the BOUND BORDER
            //     OWNER of a resolved document, which is the number the picture is actually painted with.
            ShaperFillProgram BoundBorderFill(float w)
            {
                var n = Disc("Disc", 34f);
                n.fill = Solid(Color.grey);
                n.border = Border(ShaperShellAlignment.Outward, w, false, gradient);
                var r = Build(n);
                for (int i = r.doc.owners.Count - 1; i >= 0; i--)
                    if (r.doc.owners[i].isBorder) return r.doc.owners[i].fill;
                return null;
            }

            // (a0) …and the precondition that makes (a) capable of failing: the strip's OWN anchor box must
            //      differ from the node's, or "the anchor is the node's" is satisfied by every wiring and (a)
            //      proves nothing. ShaperBorder.CompileStrip used to copy the node's local box unchanged, which
            //      is exactly that vacuous state; it now grows it by `reach / sigmaMin`, so the two differ
            //      whenever the reach is non-zero and a mis-wiring is visible.
            {
                var n20 = Disc("Disc", 34f);
                n20.border = Border(ShaperShellAlignment.Outward, 20f, false, gradient);
                Programs(n20, out ShaperProgram np, out ShaperProgram sp, out ShaperResolvedBorder rb0);
                bool distinguishable = !SameBits(np.localSupportHalfW, sp.localSupportHalfW) &&
                                       !SameBits(np.localSupportHalfH, sp.localSupportHalfH);
                ok &= distinguishable;
                sb.AppendLine("  precondition: the STRIP's anchor half-extent " + sp.localSupportHalfW.ToString("G9") +
                              " differs from the NODE's " + np.localSupportHalfW.ToString("G9") +
                              " at reach " + rb0.reach.ToString("G9") +
                              " (else this leg cannot fail)  " + Verdict(distinguishable));
            }

            ShaperFillProgram thin = BoundBorderFill(1f), fat = BoundBorderFill(20f);
            int diffs = 0; float worst = 0f;
            foreach (var p in probes)
            {
                ShaperFillOps.Sample(thin.op, thin.bulk, p.x, p.y, 0f, 0f,
                                     out float r1, out float g1, out float b1, out _, out _);
                ShaperFillOps.Sample(fat.op, fat.bulk, p.x, p.y, 0f, 0f,
                                     out float r2, out float g2, out float b2, out _, out _);
                if (!SameBits(r1, r2) || !SameBits(g1, g2) || !SameBits(b1, b2)) diffs++;
                worst = Mathf.Max(worst, Mathf.Abs(r1 - r2));
            }
            bool aOk = diffs == 0;
            ok &= aOk;
            sb.AppendLine("  1px vs 20px outward, RESOLVER-bound anchor: bitwise colour diffs " + diffs + "/" +
                          probes.Length + " (expected 0)  worst channel delta " + worst.ToString("G9") +
                          "  " + Verdict(aOk));

            // (a1) the same measurement against the STRIP's anchor - the mis-wiring BD-3.2 forbids. It must
            //      DIFFER between 1px and 20px, which is what proves (a) is a live test and not a tautology.
            {
                int stripDiffs = StripAnchorWidthDiffs(gradient, probes);
                bool a1Ok = stripDiffs > 0;
                ok &= a1Ok;
                sb.AppendLine("  control, the STRIP's own anchor instead: colour diffs " + stripDiffs + "/" +
                              probes.Length + " (expected > 0, else BD-3.2 is unfalsifiable)  " + Verdict(a1Ok));
            }

            // (b) the control: the anchor a naive "the strip is just a Shell node" implementation would build.
            ShaperFillProgram naive = Compile(20f, 20f);
            int naiveDiffs = 0; float naiveWorst = 0f;
            foreach (var p in probes)
            {
                ShaperFillOps.Sample(thin.op, thin.bulk, p.x, p.y, 0f, 0f, out float r1, out _, out _, out _, out _);
                ShaperFillOps.Sample(naive.op, naive.bulk, p.x, p.y, 0f, 0f, out float r2, out _, out _, out _, out _);
                if (!SameBits(r1, r2)) naiveDiffs++;
                naiveWorst = Mathf.Max(naiveWorst, Mathf.Abs(r1 - r2));
            }
            bool bOk = naiveDiffs > 0;
            ok &= bOk;
            sb.AppendLine("  control, anchor grown by the reach (the naive strip anchor): colour diffs " +
                          naiveDiffs + "/" + probes.Length + " (expected > 0, else this leg cannot fail)" +
                          "  worst channel delta " + naiveWorst.ToString("G9") + "  " + Verdict(bOk));

            // (c) and the resolver really does hand the node's anchor to a border owner, not the strip's.
            {
                var node = Disc("Disc", 34f);
                node.fill = Solid(Color.grey);
                node.border = Border(ShaperShellAlignment.Outward, 20f, false, gradient);
                var rig = Build(node);
                ShaperProgram nodeProg = ShaperCompiler.Compile(Disc("Disc", 34f), 0f, 0u);
                var want = ShaperFillAnchor.From(nodeProg, 0.5f * (W - 1), 0.5f * (H - 1));
                ShaperFillProgram bound = null;
                for (int i = 0; i < rig.doc.owners.Count; i++)
                    if (rig.doc.owners[i].isBorder) bound = rig.doc.owners[i].fill;
                bool cOk = bound != null &&
                           SameBits(bound.op.invHx, ShaperFillCompiler.Compile(gradient, want, 0f, 0u).op.invHx) &&
                           SameBits(bound.op.anchorCx, ShaperFillCompiler.Compile(gradient, want, 0f, 0u).op.anchorCx);
                ok &= cOk;
                sb.AppendLine("  bound border fill's anchor == the NODE's anchor, bitwise (invHx, anchorCx)  " +
                              Verdict(cOk));
            }

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-7 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-7: the <c>edgeDistance</c> handed to a border's fill is the STRIP's, not the node's (BD-3.3). A
        /// <c>ByEdgeDistance</c> ramp on a border reaches <c>t = 1</c> at the strip's centre line and
        /// <c>t = 0</c> at both faces — the RIDGE of BD-3.3, not a monotone ramp.
        ///
        /// Two independent measurements, because either alone would be weak. First, the sheet the resolver
        /// actually hands the border's fill (<c>ownDistance</c> at the border owner's own slab) is compared
        /// bitwise against the strip program's field and against the node's — it must match the first and differ
        /// from the second. Second, the ramp's <c>t</c> is scanned across the band and must rise to 1 and come
        /// back to 0, which a monotone ramp on the node's field cannot do.
        /// </summary>
        public static string BT7_EdgeDistanceIsTheStrips()
        {
            var sb = new StringBuilder("BT-7 a border's edgeDistance is the STRIP's field, and its ramp is a ridge\n");
            bool ok = true;

            const float R = 34f, Wd = 12f;
            var node = Disc("Disc", R);
            node.fill = Solid(new Color(0.15f, 0.15f, 0.2f));
            node.border = Border(ShaperShellAlignment.Centred, Wd, false,
                                 EdgeGradient(Color.black, Color.white, Wd * 0.5f));

            var rig = Build(node);
            Paint(rig);

            int b = -1;
            for (int i = 0; i < rig.doc.owners.Count; i++) if (rig.doc.owners[i].isBorder) b = i;

            var plain = Disc("Disc", R);
            ShaperProgram nodeProg = ShaperCompiler.Compile(plain, 0f, 0u);
            Programs(node, out _, out ShaperProgram strip, out _);
            float[] s1 = nodeProg.NewStack(), s2 = strip.NewStack();

            int matchStrip = 0, matchNode = 0, total = 0;
            for (int y = 0; y < H; y += 2)
                for (int x = 0; x < W; x += 2)
                {
                    float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                    float sheet = rig.buf.ownDistance[b * rig.buf.sampleCapacity + y * W + x];
                    if (SameBits(sheet, ShaperEvaluator.Distance(strip, cx, cy, s2))) matchStrip++;
                    if (SameBits(sheet, ShaperEvaluator.Distance(nodeProg, cx, cy, s1))) matchNode++;
                    total++;
                }
            bool aOk = matchStrip == total && matchNode < total;
            ok &= aOk;
            sb.AppendLine("  ownDistance[border] == strip field: " + matchStrip + "/" + total +
                          " (expected all)   == node field: " + matchNode + "/" + total +
                          " (expected fewer, else the two are indistinguishable)  " + Verdict(aOk));

            // the ridge: t across the band, from the inner face through the centre line to the outer face
            float invDepth = 1f / (Wd * 0.5f);
            float tInner = ShaperFillOps.ByEdgeDistanceT(
                ShaperEvaluator.Distance(strip, R - Wd * 0.5f, 0f, s2), invDepth);
            float tCentre = ShaperFillOps.ByEdgeDistanceT(
                ShaperEvaluator.Distance(strip, R, 0f, s2), invDepth);
            float tOuter = ShaperFillOps.ByEdgeDistanceT(
                ShaperEvaluator.Distance(strip, R + Wd * 0.5f, 0f, s2), invDepth);

            // the monotone alternative, for contrast: the same ramp read off the NODE's field
            float nInner = ShaperFillOps.ByEdgeDistanceT(
                ShaperEvaluator.Distance(nodeProg, R - Wd * 0.5f, 0f, s1), invDepth);
            float nCentre = ShaperFillOps.ByEdgeDistanceT(
                ShaperEvaluator.Distance(nodeProg, R, 0f, s1), invDepth);
            float nOuter = ShaperFillOps.ByEdgeDistanceT(
                ShaperEvaluator.Distance(nodeProg, R + Wd * 0.5f, 0f, s1), invDepth);

            bool ridge = tInner <= 0.05f && tCentre >= 0.95f && tOuter <= 0.05f;
            bool notRidge = !(nInner <= 0.05f && nCentre >= 0.95f && nOuter <= 0.05f);
            ok &= ridge && notRidge;
            sb.AppendLine("  strip ramp t at [inner face, centre line, outer face] = [" +
                          tInner.ToString("F4") + ", " + tCentre.ToString("F4") + ", " + tOuter.ToString("F4") +
                          "]  expected [0, 1, 0]  " + Verdict(ridge));
            sb.AppendLine("  control, the same ramp on the NODE's field = [" +
                          nInner.ToString("F4") + ", " + nCentre.ToString("F4") + ", " + nOuter.ToString("F4") +
                          "]  expected NOT a ridge  " + Verdict(notRidge));

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-8 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-8: the join is a DILATION. With <c>joinsCoverage</c>, the node's published coverage has NO SAMPLE
        /// BELOW 0.999 anywhere strictly inside the dilated silhouette — the direct measurement of the BD-2.2
        /// seam.
        ///
        /// <b>The naive <c>min</c> implementation is run alongside and MUST FAIL the same measurement.</b> That
        /// is not decoration: without it the leg would be a green tick on a test that has never been shown
        /// capable of going red. <c>min(d, max(−d, d − w))</c> has a spurious zero crossing exactly on the
        /// original silhouette, because the strip's field is zero at its inner edge and the node's field is zero
        /// at the same place, so the coverage kernel produces a ring at ≈ 0.5 following the old outline. If both
        /// implementations pass, this leg reports FAIL and says so, because the instrument is then broken rather
        /// than the subject being right.
        /// </summary>
        public static string BT8_TheJoinIsADilation()
        {
            var sb = new StringBuilder("BT-8 joining is a dilation, not a union (the BD-2.2 seam, measured)\n");
            const float R = 34f, Wd = 6f;

            var node = Disc("Disc", R);
            node.border = Border(ShaperShellAlignment.Outward, Wd, true);
            ShaperProgram joined = ShaperCompiler.Compile(node, 0f, 0u);
            ShaperProgram plain = ShaperCompiler.Compile(Disc("Disc", R), 0f, 0u);
            float[] sj = joined.NewStack(), sp = plain.NewStack();

            int inside = 0, realBad = 0, naiveBad = 0;
            float realMin = 1f, naiveMin = 1f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                    float d = ShaperEvaluator.Distance(plain, cx, cy, sp);

                    // "strictly inside the dilated silhouette": a full pixel past the dilated boundary, so the
                    // shape's own antialiased edge is excluded and only interior samples are judged.
                    if (d - Wd >= -1f) continue;
                    inside++;

                    float cReal = ShaperField.Coverage(ShaperEvaluator.Distance(joined, cx, cy, sj), HalfBand);
                    // THE NAIVE ALTERNATIVE, spelled out rather than described: fold the strip in the way any
                    // other member folds in.
                    float cNaive = ShaperField.Coverage(
                        Mathf.Min(d, ShaperOps.Shell(ShaperShellAlignment.Outward, Wd, d)), HalfBand);

                    realMin = Mathf.Min(realMin, cReal);
                    naiveMin = Mathf.Min(naiveMin, cNaive);
                    if (cReal < 0.999f) realBad++;
                    if (cNaive < 0.999f) naiveBad++;
                }

            bool realOk = inside > 1000 && realBad == 0;
            bool naiveFails = naiveBad > 0;
            bool ok = realOk && naiveFails;

            sb.AppendLine("  interior samples judged " + inside);
            sb.AppendLine("  d - reach  (the ruling):  samples below 0.999 = " + realBad +
                          " (expected 0)  min coverage " + realMin.ToString("F6") + "  " + Verdict(realOk));
            sb.AppendLine("  min(d, strip) (the naive union): samples below 0.999 = " + naiveBad +
                          " (expected > 0)  min coverage " + naiveMin.ToString("F6") + "  " +
                          Verdict(naiveFails));
            if (!naiveFails)
                sb.AppendLine("  THE INSTRUMENT IS BROKEN: the naive implementation passed the same measurement, " +
                              "so this leg is incapable of failing and cannot be counted as a pass.");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-9 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-9: opting out publishes nothing. With <c>joinsCoverage = false</c>, the node's published distance is
        /// BITWISE IDENTICAL to the borderless document at every sample, while the painted output is NOT.
        ///
        /// Both halves are the assertion. "Identical field" alone would be satisfied by a border that failed to
        /// draw; "different picture" alone would be satisfied by a border that also dilated. The pair is BD-2.3's
        /// "drawn, not counted" stated as two numbers.
        /// </summary>
        public static string BT9_OptOutPublishesNothing()
        {
            var sb = new StringBuilder("BT-9 joinsCoverage = false: identical field, different picture\n");

            ShaperNode Base()
            {
                var d = Disc("Disc", 34f);
                d.fill = Solid(new Color(0.2f, 0.3f, 0.8f));
                return d;
            }

            var plain = Base();
            var opted = Base();
            opted.border = Border(ShaperShellAlignment.Outward, 6f, false, Solid(Color.yellow));
            var joined = Base();
            joined.border = Border(ShaperShellAlignment.Outward, 6f, true, Solid(Color.yellow));

            ShaperProgram pa = ShaperCompiler.Compile(plain, 0f, 0u);
            ShaperProgram pb = ShaperCompiler.Compile(opted, 0f, 0u);
            ShaperProgram pc = ShaperCompiler.Compile(joined, 0f, 0u);
            float[] sa = pa.NewStack(), sbk = pb.NewStack(), sc = pc.NewStack();

            int n = 0, optedDiffs = 0, joinedDiffs = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                    float d = ShaperEvaluator.Distance(pa, cx, cy, sa);
                    if (!SameBits(d, ShaperEvaluator.Distance(pb, cx, cy, sbk))) optedDiffs++;
                    if (!SameBits(d, ShaperEvaluator.Distance(pc, cx, cy, sc))) joinedDiffs++;
                    n++;
                }

            int pictureDiffs = CountByteDiffs(Render(plain), Render(opted));

            bool fieldOk = optedDiffs == 0;
            bool controlOk = joinedDiffs > 0;
            bool pictureOk = pictureDiffs > 0;
            bool ok = fieldOk && controlOk && pictureOk;

            sb.AppendLine("  published field, opted out vs no border: bit diffs " + optedDiffs + "/" + n +
                          " (expected 0)  " + Verdict(fieldOk));
            sb.AppendLine("  control, the SAME border joined:         bit diffs " + joinedDiffs + "/" + n +
                          " (expected > 0, else the field test cannot fail)  " + Verdict(controlOk));
            sb.AppendLine("  painted bytes, opted out vs no border:   diffs " + pictureDiffs +
                          " (expected > 0 — it is drawn, just not counted)  " + Verdict(pictureOk));
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-10 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-10: the anchor box does not dilate (BD-2.4). Enabling a joined outward border leaves a Linear
        /// gradient on the NODE'S OWN fill BITWISE UNCHANGED, while the culling box provably grew.
        ///
        /// Three numbers, and all three are needed. The gradient's compiled anchor fields must be bit-identical;
        /// the gradient's evaluated colour at a set of canvas points must be bit-identical; and
        /// <c>supportHalfW</c> must have grown by the reach — a bound that excluded real samples would be a
        /// correctness failure, so "unchanged everywhere" is the wrong pass condition and is not the one used.
        /// </summary>
        public static string BT10_AnchorBoxDoesNotDilate()
        {
            var sb = new StringBuilder("BT-10 the join grows the CULLING box and not the ANCHOR box\n");
            bool ok = true;
            const float R = 34f, Wd = 8f;

            ShaperFillDef grad = LinearGradient(new Color(0.1f, 0.1f, 0.4f), new Color(1f, 0.9f, 0.3f));

            var plain = Disc("Disc", R); plain.fill = grad;
            var bordered = Disc("Disc", R); bordered.fill = grad;
            bordered.border = Border(ShaperShellAlignment.Outward, Wd, true, Solid(Color.red));

            ShaperProgram pa = ShaperCompiler.Compile(plain, 0f, 0u);
            ShaperProgram pb = ShaperCompiler.Compile(bordered, 0f, 0u);

            bool localOk = SameBits(pa.localSupportHalfW, pb.localSupportHalfW) &&
                           SameBits(pa.localSupportHalfH, pb.localSupportHalfH) &&
                           SameBits(pa.localSupportCx, pb.localSupportCx) &&
                           SameBits(pa.localSupportCy, pb.localSupportCy);
            ok &= localOk;
            sb.AppendLine("  localSupport half-extents: " + pa.localSupportHalfW.ToString("G9") + " -> " +
                          pb.localSupportHalfW.ToString("G9") + " (expected bitwise identical)  " +
                          Verdict(localOk));

            float grew = pb.supportHalfW - pa.supportHalfW;
            bool cullOk = Mathf.Abs(grew - Wd) < 1e-3f;
            ok &= cullOk;
            sb.AppendLine("  supportHalfW (the culling box) on an ISOTROPIC node: " + pa.supportHalfW.ToString("F4") +
                          " -> " + pb.supportHalfW.ToString("F4") + ", grew " + grew.ToString("F4") +
                          " (expected the reach, " + Wd + ")  " + Verdict(cullOk));

            // ...and the assertion that actually matters, because "grew by the reach" is a magic number and a
            // BOUND is a containment claim. On a disc the two agree; on an anisotropically scaled member the
            // number is right and the bound is WRONG, which is how this defect survived a green audit.
            bool containOk = BoxContainment(sb, false);
            ok &= containOk;

            // The gradient itself, evaluated: an anchor change would move the colour even if a field comparison
            // happened not to notice.
            var anchorA = ShaperFillAnchor.From(pa, 0.5f * (W - 1), 0.5f * (H - 1));
            var anchorB = ShaperFillAnchor.From(pb, 0.5f * (W - 1), 0.5f * (H - 1));
            var fa = ShaperFillCompiler.Compile(grad, anchorA, 0f, 0u);
            var fb = ShaperFillCompiler.Compile(grad, anchorB, 0f, 0u);

            var brokenAnchor = anchorA;
            brokenAnchor.localHalfW += Wd; brokenAnchor.localHalfH += Wd;   // what a dilated ANCHOR box would do
            var fBroken = ShaperFillCompiler.Compile(grad, brokenAnchor, 0f, 0u);

            int diffs = 0, brokenDiffs = 0, probes = 0;
            for (int y = 0; y < H; y += 4)
                for (int x = 0; x < W; x += 4)
                {
                    float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                    ShaperFillOps.Sample(fa.op, fa.bulk, cx, cy, 0f, 0f, out float r1, out float g1, out float b1, out _, out _);
                    ShaperFillOps.Sample(fb.op, fb.bulk, cx, cy, 0f, 0f, out float r2, out float g2, out float b2, out _, out _);
                    ShaperFillOps.Sample(fBroken.op, fBroken.bulk, cx, cy, 0f, 0f, out float r3, out _, out _, out _, out _);
                    if (!SameBits(r1, r2) || !SameBits(g1, g2) || !SameBits(b1, b2)) diffs++;
                    if (!SameBits(r1, r3)) brokenDiffs++;
                    probes++;
                }

            bool gradOk = diffs == 0;
            bool controlOk = brokenDiffs > 0;
            ok &= gradOk && controlOk;
            sb.AppendLine("  the node's own gradient, sampled: bitwise diffs " + diffs + "/" + probes +
                          " (expected 0)  " + Verdict(gradOk));
            sb.AppendLine("  control, an anchor box grown by the reach: diffs " + brokenDiffs + "/" + probes +
                          " (expected > 0, else this leg cannot fail)  " + Verdict(controlOk));

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-11 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-11: ordering (BD-3.5, BD-3.6). On a bag with a bordered member, the bag's border wins in the
        /// overlap; on a bag whose member owns a fill, the bag's border is NOT eaten by it — the case model (a)
        /// of BD-3.5 fails.
        ///
        /// Both cases are built as ONE bag whose single member has the same silhouette as the bag, so the two
        /// borders trace the SAME ring and the overlap is total rather than incidental. The probe reads the
        /// linear colour on that ring and asks which of three authored colours it is nearest — the bag's border,
        /// the member's border, or the member's fill. A model that made the border an owner in the exclusivity
        /// partition would answer "the member's" for the first and "the member's fill" for the second, so both
        /// probes discriminate.
        /// </summary>
        public static string BT11_Ordering()
        {
            var sb = new StringBuilder("BT-11 a bag's border is above its members' borders and above their fills\n");
            bool ok = true;

            Color bagBorder = new Color(0.95f, 0.15f, 0.15f);
            Color memberBorder = new Color(0.15f, 0.95f, 0.25f);
            Color memberFill = new Color(0.15f, 0.35f, 0.95f);
            Color[] palette = { LinearOf(bagBorder), LinearOf(memberBorder), LinearOf(memberFill) };
            string[] names = { "bag border", "member border", "member fill" };

            const float R = 34f;

            // (a) both borders trace the same ring; the bag's must win.
            {
                var member = Disc("Member", R);
                member.fill = Solid(memberFill);
                member.border = Border(ShaperShellAlignment.Inward, 5f, false, Solid(memberBorder));
                var bag = ShaperNode.Bag("Bag", ShaperCombineMode.Add, member);
                bag.fill = Solid(new Color(0.5f, 0.5f, 0.5f));
                bag.border = Border(ShaperShellAlignment.Inward, 5f, false, Solid(bagBorder));

                var rig = Build(bag); Paint(rig);
                Color got = LinearAt(rig, R - 2.5f, 0f);
                int which = Nearest(got, palette, out float dist);
                bool legOk = which == 0 && dist < 0.02f;
                ok &= legOk;
                sb.AppendLine("  overlapping rings, sample at d = -2.5: nearest authored colour is '" +
                              names[which] + "' (distance " + dist.ToString("F4") + "), expected 'bag border'  " +
                              Verdict(legOk));
            }

            // (b) the member owns a fill covering the whole silhouette; the bag's border must survive.
            {
                var member = Disc("Member", R);
                member.fill = Solid(memberFill);
                var bag = ShaperNode.Bag("Bag", ShaperCombineMode.Add, member);
                bag.fill = Solid(new Color(0.5f, 0.5f, 0.5f));
                bag.border = Border(ShaperShellAlignment.Inward, 5f, false, Solid(bagBorder));

                var rig = Build(bag); Paint(rig);
                Color rim = LinearAt(rig, R - 2.5f, 0f);
                Color core = LinearAt(rig, 0f, 0f);
                int wRim = Nearest(rim, palette, out float dRim);
                int wCore = Nearest(core, palette, out float dCore);
                bool legOk = wRim == 0 && dRim < 0.02f && wCore == 2 && dCore < 0.02f;
                ok &= legOk;
                sb.AppendLine("  member owns the fill: rim reads '" + names[wRim] + "' (" + dRim.ToString("F4") +
                              "), core reads '" + names[wCore] + "' (" + dCore.ToString("F4") +
                              "), expected 'bag border' and 'member fill'  " + Verdict(legOk));
            }

            // (c) the probe's sensitivity, so a pass is not a pass by accident: with the bag's border removed,
            //     the SAME probe must report a DIFFERENT colour.
            {
                var member = Disc("Member", R);
                member.fill = Solid(memberFill);
                member.border = Border(ShaperShellAlignment.Inward, 5f, false, Solid(memberBorder));
                var bag = ShaperNode.Bag("Bag", ShaperCombineMode.Add, member);
                bag.fill = Solid(new Color(0.5f, 0.5f, 0.5f));

                var rig = Build(bag); Paint(rig);
                Color got = LinearAt(rig, R - 2.5f, 0f);
                int which = Nearest(got, palette, out float dist);
                bool legOk = which == 1 && dist < 0.02f;
                ok &= legOk;
                sb.AppendLine("  control, bag border removed: the same probe reads '" + names[which] +
                              "' (" + dist.ToString("F4") + "), expected 'member border'  " + Verdict(legOk));
            }

            // (d) BD-3.6 for a border whose node bound NO fill of its own. The contract does not name this
            //     case; the implementation had it landing ABOVE a later sibling that owned a fill, so an
            //     unrelated dial (does the outlined member also carry its own colour?) decided the z-order.
            bool hostlessOk = HostlessOrdering(sb, false);
            ok &= hostlessOk;

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-12 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-12: Subtract refusal (BD-3.7). A border on a Subtract member creates NO OWNER and records a
        /// diagnostic naming the node and the reason; a border on the BAG traces the resulting hole.
        ///
        /// The second half is the important one, and it is why the refusal costs nothing: the way to outline a
        /// hole is a border on the bag, and it already works with no extra feature, because the bag's finished
        /// field is zero on the hole's boundary just as it is on the outer boundary. An Intersect member is
        /// checked too — it MAY own a border, so a blanket refusal on "any non-Add mode" would be caught here.
        /// </summary>
        public static string BT12_SubtractRefusal()
        {
            var sb = new StringBuilder("BT-12 a Subtract member may not own a border; the bag outlines the hole\n");
            bool ok = true;

            var outer = Disc("Outer", 34f);
            var hole = Disc("Hole", 14f, 0f, 0f, ShaperCombineMode.Subtract);
            hole.border = Border(ShaperShellAlignment.Inward, 4f, true, Solid(Color.magenta));
            var bag = ShaperNode.Bag("Ring", ShaperCombineMode.Add, outer, hole);
            bag.fill = Solid(new Color(0.3f, 0.3f, 0.35f));
            bag.border = Border(ShaperShellAlignment.Inward, 3f, false, Solid(new Color(1f, 0.8f, 0.1f)));

            var rig = Build(bag);
            Paint(rig);

            int borderOwners = 0, holeBorderOwners = 0;
            for (int i = 0; i < rig.doc.owners.Count; i++)
            {
                if (!rig.doc.owners[i].isBorder) continue;
                borderOwners++;
                if (ReferenceEquals(rig.doc.owners[i].node, hole)) holeBorderOwners++;
            }

            bool refusedOk = holeBorderOwners == 0 && borderOwners == 1 &&
                             rig.doc.hasSubtractBorder && rig.doc.subtractBorderCount == 1 &&
                             rig.doc.subtractBorderNode == "Hole" &&
                             !string.IsNullOrEmpty(rig.doc.subtractBorderReason);
            ok &= refusedOk;
            sb.AppendLine("  border owners " + borderOwners + " (expected 1, the bag's)  on the Subtract member " +
                          holeBorderOwners + " (expected 0)  diagnostic '" + rig.doc.subtractBorderNode +
                          "' x" + rig.doc.subtractBorderCount + "  " + Verdict(refusedOk));
            sb.AppendLine("    reason: " + (rig.doc.subtractBorderReason ?? "(none)"));

            // The refusal must also suppress the DILATION, or the diagnostic would name a border that
            // nonetheless changed the silhouette.
            {
                var outer2 = Disc("Outer", 34f);
                var hole2 = Disc("Hole", 14f, 0f, 0f, ShaperCombineMode.Subtract);
                var bag2 = ShaperNode.Bag("Ring", ShaperCombineMode.Add, outer2, hole2);
                ShaperProgram pa = ShaperCompiler.Compile(bag2, 0f, 0u);
                ShaperProgram pb = ShaperCompiler.Compile(
                    ShaperNode.Bag("Ring", ShaperCombineMode.Add, Disc("Outer", 34f), hole), 0f, 0u);
                float[] sa = pa.NewStack(), sbk = pb.NewStack();
                int diffs = 0;
                for (int y = 0; y < H; y += 2)
                    for (int x = 0; x < W; x += 2)
                    {
                        float cx = -0.5f * (W - 1) + x, cy = -0.5f * (H - 1) + y;
                        if (!SameBits(ShaperEvaluator.Distance(pa, cx, cy, sa),
                                      ShaperEvaluator.Distance(pb, cx, cy, sbk))) diffs++;
                    }
                bool dilOk = diffs == 0;
                ok &= dilOk;
                sb.AppendLine("  the refused border also does not dilate: field bit diffs " + diffs +
                              " (expected 0)  " + Verdict(dilOk));
            }

            // The bag's border traces BOTH boundaries: the outer edge and the hole's.
            {
                int b = -1;
                for (int i = 0; i < rig.doc.owners.Count; i++) if (rig.doc.owners[i].isBorder) b = i;
                int slab = b * rig.buf.sampleCapacity;
                float onHole = rig.buf.ownCoverage[slab + Index(rig, 15.5f, 0f)];
                float onOuter = rig.buf.ownCoverage[slab + Index(rig, 32.5f, 0f)];
                float inMiddle = rig.buf.ownCoverage[slab + Index(rig, 24f, 0f)];
                bool holeOk = onHole > 0.9f && onOuter > 0.9f && inMiddle < 0.1f;
                ok &= holeOk;
                sb.AppendLine("  the bag's border strip coverage at [hole rim, mid-material, outer rim] = [" +
                              onHole.ToString("F3") + ", " + inMiddle.ToString("F3") + ", " +
                              onOuter.ToString("F3") + "]  expected [~1, ~0, ~1]  " + Verdict(holeOk));
            }

            // An INTERSECT member MAY own a border — a blanket "non-Add is refused" would fail here.
            {
                var a = Disc("A", 34f);
                var bIn = Disc("B", 26f, 10f, 0f, ShaperCombineMode.Intersect);
                bIn.border = Border(ShaperShellAlignment.Inward, 3f, false, Solid(Color.white));
                var bag3 = ShaperNode.Bag("Lens", ShaperCombineMode.Add, a, bIn);
                bag3.fill = Solid(Color.grey);
                var doc = Build(bag3).doc;
                int owners = 0;
                for (int i = 0; i < doc.owners.Count; i++) if (doc.owners[i].isBorder) owners++;
                bool intersectOk = owners == 1 && !doc.hasSubtractBorder;
                ok &= intersectOk;
                sb.AppendLine("  an Intersect member MAY own a border: border owners " + owners +
                              " (expected 1), subtract diagnostic " + doc.hasSubtractBorder +
                              " (expected False)  " + Verdict(intersectOk));
            }

            // BD-3.4, which had no measurement of its own: a member's rim MUST NOT survive in a region a LATER
            // Subtract member carved out of the bag. The clip that stops it is `min(coverage_strip,
            // coverage_ancestor)` against the nearest binding ancestor OF THE NODE - never against the node
            // itself, which for an Outward strip would clip the whole border away. Without it a rim would be
            // left floating in a hole, which is FC-3.3's problem in a new costume.
            {
                var clipA = Disc("A", 34f);
                clipA.fill = Solid(new Color(0.1f, 0.3f, 0.1f));
                clipA.border = Border(ShaperShellAlignment.Inward, 6f, false, Solid(Color.magenta));
                var clipHole = Disc("Hole", 24f, 34f, 0f, ShaperCombineMode.Subtract);
                var clipBag = ShaperNode.Bag("Carved", ShaperCombineMode.Add, clipA, clipHole);
                clipBag.fill = Solid(Color.white);
                var clipRig = Build(clipBag); Paint(clipRig);

                // A's inward rim runs near x = +31 on the right, which the Subtract disc covers, and near
                // x = -31 on the left, which it does not.
                Color inHole = LinearAt(clipRig, 31f, 0f);
                Color intact = LinearAt(clipRig, -31f, 0f);
                bool clipOk = inHole.a < 0.01f && intact.a > 0.99f &&
                              Mathf.Abs(intact.r - LinearOf(Color.magenta).r) < 0.02f;
                ok &= clipOk;
                sb.AppendLine("  BD-3.4 ancestor clip: the member's rim inside a LATER Subtract member's hole has" +
                              " alpha " + inHole.a.ToString("F4") + " (expected 0); on the intact side alpha " +
                              intact.a.ToString("F4") + " and it is the rim colour (expected 1)  " + Verdict(clipOk));
            }

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-13 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-13: the instrument self-test. Each measurement above is re-run against a deliberately broken
        /// implementation or input, and MUST report FAIL. <b>A test that cannot fail is reported as a failure of
        /// the audit</b>, not as a pass.
        ///
        /// This leg exists because T-0105 and T-0106 each shipped a green audit containing a test which could not
        /// go red, and because two of the legs above are structurally at risk of it: BT-6's two candidate anchors
        /// coincide on any fixture whose strip has the same local box as its node, and BT-1's identity is vacuous
        /// if the border it is switching off was never drawing anything.
        ///
        /// Some legs carry their own control inline (BT-1's live border, BT-6's grown anchor, BT-8's naive union,
        /// BT-9's joined twin, BT-10's grown anchor, BT-11's removed bag border, BT-12's Intersect member) —
        /// those are re-asserted here as a list rather than re-run, so this leg's report is the single place a
        /// reader can see whether every measurement is falsifiable. The mutations below are the ones NOT already
        /// covered inline.
        /// </summary>
        public static string BT13_InstrumentSelfTest()
        {
            var sb = new StringBuilder("BT-13 instrument self-test: every measurement must be able to report a failure\n");
            bool ok = true;

            // M1 — BT-1's identity, mutated: a real but tiny border must NOT be bitwise identical to none.
            {
                var plain = Disc("Disc", 34f); plain.fill = Solid(Color.white);
                var tiny = Disc("Disc", 34f); tiny.fill = Solid(Color.white);
                tiny.border = Border(ShaperShellAlignment.Inward, 0.25f, false, Solid(Color.black));
                int diffs = CountByteDiffs(Render(plain), Render(tiny));
                bool detected = diffs > 0;
                ok &= detected;
                sb.AppendLine("  M1  BT-1 vs a 0.25px border: " + diffs +
                              " byte diffs (must be > 0)  detected " + detected + "  " + Verdict(detected));
            }

            // M2 — BT-2's bitwise agreement, mutated: the Straddling double-width trap, abs(d) - w. A stage that
            //      re-derived the expressions instead of calling ShaperOps.Shell would land exactly here.
            {
                var node = Disc("Disc", 34f);
                node.border = Border(ShaperShellAlignment.Centred, 6f, false);
                Programs(node, out _, out ShaperProgram strip, out _);
                ShaperProgram plain = ShaperCompiler.Compile(Disc("Disc", 34f), 0f, 0u);
                float[] s1 = strip.NewStack(), s2 = plain.NewStack();
                int mismatches = 0, compared = 0;
                for (int x = 0; x < W; x += 2)
                {
                    float cx = -0.5f * (W - 1) + x;
                    float got = ShaperEvaluator.Distance(strip, cx, 0f, s1);
                    float d = ShaperEvaluator.Distance(plain, cx, 0f, s2);
                    float wrong = Mathf.Abs(d) - 6f;                     // the trap
                    compared++;
                    if (!SameBits(got, wrong)) mismatches++;
                }
                bool detected = mismatches > 0;
                ok &= detected;
                sb.AppendLine("  M2  BT-2 vs abs(d) - w (the doubled Straddling): " + mismatches + "/" + compared +
                              " mismatches (must be > 0)  detected " + detected + "  " + Verdict(detected));
            }

            // M3 — BT-3's placement, mutated: predict the Straddling band at ±w instead of ±w/2. The same
            //      measurement function must report FAIL against the wrong prediction.
            {
                var scratch = new StringBuilder();
                bool passed = Placement(scratch, true);
                bool detected = !passed;
                ok &= detected;
                sb.AppendLine("  M3  BT-3 with the doubled Straddling prediction: measurement passed = " + passed +
                              " (must be False)  detected " + detected + "  " + Verdict(detected));
            }

            // M4 — BT-4/BT-5 crossed: an inward strip judged by the complement rule, and an outward one judged
            //      by the identity rule. Both must FAIL, or the ring measurement is insensitive to which is which.
            {
                var s4 = new StringBuilder();
                bool inwardAsComplement = RingCompare(s4, ShaperShellAlignment.Inward, true);
                bool outwardAsIdentity = RingCompare(s4, ShaperShellAlignment.Outward, false);
                bool detected = !inwardAsComplement && !outwardAsIdentity;
                ok &= detected;
                sb.AppendLine("  M4  BT-4/BT-5 crossed: inward-as-complement passed = " + inwardAsComplement +
                              ", outward-as-identity passed = " + outwardAsIdentity +
                              " (both must be False)  detected " + detected + "  " + Verdict(detected));
            }

            // M5 — BT-7's ridge test, mutated: the node's own field must NOT produce a ridge. (Asserted inline in
            //      BT-7 as well; repeated here so the list is complete.)
            {
                const float R = 34f, Wd = 12f;
                ShaperProgram plain = ShaperCompiler.Compile(Disc("Disc", R), 0f, 0u);
                float[] s = plain.NewStack();
                float invDepth = 1f / (Wd * 0.5f);
                float a = ShaperFillOps.ByEdgeDistanceT(ShaperEvaluator.Distance(plain, R - Wd * 0.5f, 0f, s), invDepth);
                float b = ShaperFillOps.ByEdgeDistanceT(ShaperEvaluator.Distance(plain, R, 0f, s), invDepth);
                float c = ShaperFillOps.ByEdgeDistanceT(ShaperEvaluator.Distance(plain, R + Wd * 0.5f, 0f, s), invDepth);
                bool isRidge = a <= 0.05f && b >= 0.95f && c <= 0.05f;
                bool detected = !isRidge;
                ok &= detected;
                sb.AppendLine("  M5  BT-7's ridge test on the NODE's field: ridge detected = " + isRidge +
                              " (must be False)  detected " + detected + "  " + Verdict(detected));
            }

            // M6 — BT-12's refusal, mutated: the SAME border on an Add member must produce an owner and no
            //      diagnostic, or the refusal is firing on something other than the Subtract mode.
            {
                var outer = Disc("Outer", 34f);
                var inner = Disc("Inner", 14f, 0f, 0f, ShaperCombineMode.Add);
                inner.border = Border(ShaperShellAlignment.Inward, 4f, true, Solid(Color.magenta));
                var bag = ShaperNode.Bag("Bag", ShaperCombineMode.Add, outer, inner);
                bag.fill = Solid(Color.grey);
                var doc = Build(bag).doc;
                int owners = 0;
                for (int i = 0; i < doc.owners.Count; i++) if (doc.owners[i].isBorder) owners++;
                bool detected = owners == 1 && !doc.hasSubtractBorder;
                ok &= detected;
                sb.AppendLine("  M6  BT-12's refusal with mode = Add: border owners " + owners +
                              " (must be 1), diagnostic " + doc.hasSubtractBorder +
                              " (must be False)  detected " + detected + "  " + Verdict(detected));
            }

            // M7 — BT-14's allocation instrument, run against code that PROVABLY allocates. This is the check
            //      that already earned its keep: the first version of BT-14 measured with
            //      GC.GetAllocatedBytesForCurrentThread, this leg reported 0 bytes seen for a known
            //      65,536-byte allocation, and the method turned out to be an unimplemented stub returning 0
            //      on this Mono. A green BT-14 on that instrument would have been exactly the un-failable test
            //      BT-13 exists to catch. The replacement is an exact IL decoder, and its ability to report a
            //      non-zero is measured here rather than assumed.
            {
                int fromStrip = CountAllocations(typeof(ShaperBorder).GetMethod("CompileStrip"), out string d1);
                int fromSummary = CountAllocations(typeof(ShaperFillDocument).GetMethod("Summary"), out string d2);
                bool detected = fromStrip > 0 && fromSummary > 0;
                ok &= detected;
                sb.AppendLine("  M7  BT-14's IL decoder on methods that DO allocate: ShaperBorder.CompileStrip = " +
                              fromStrip + " [" + d1 + "], ShaperFillDocument.Summary = " + fromSummary +
                              " [" + d2 + "]  both must be > 0  detected " + detected + "  " + Verdict(detected));
            }

            // M8 — the heap instrument BT-14 reports ALONGSIDE the decoder, calibrated on this editor so that
            //      its zero is read with the right amount of weight. Measured 2026-08-31:
            //      GC.GetTotalMemory(false) sees one 65,536-byte array, sees 1 MB of 1 KB arrays, and is
            //      COMPLETELY BLIND to 1 MB of 24-byte arrays — small transient garbage is recycled in the
            //      nursery without the used heap moving at all. So a zero from it bounds only the large-object
            //      case, and that is exactly how BT-14 labels it.
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long b0 = GC.GetTotalMemory(false);
                object big = new byte[64 * 1024];
                long sawBig = GC.GetTotalMemory(false) - b0;

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long b1 = GC.GetTotalMemory(false);
                object sink = null;
                for (int i = 0; i < 43690; i++) sink = new byte[24];      // ~1 MB of small transient garbage
                long sawSmall = GC.GetTotalMemory(false) - b1;

                bool detected = sawBig > 0 && big != null && sink != null;
                ok &= detected;
                sb.AppendLine("  M8  GC.GetTotalMemory(false) calibration: one 65536-byte array -> " + sawBig +
                              " bytes seen (must be > 0);  1 MB of 24-byte arrays -> " + sawSmall +
                              " bytes seen (a 0 here is the KNOWN blind spot, reported not asserted)  " +
                              Verdict(detected));
            }

            // M9 - BT-10's containment measurement against the growth rule it replaced: the canvas box grown
            //      by the BARE reach. On an anisotropically scaled member that is an under-bound, and the leg
            //      must say so. Before this leg existed the same defect measured "grew 8.0000, expected 8" and
            //      reported PASS.
            {
                var sink = new StringBuilder();
                bool passed = BoxContainment(sink, true);
                bool detected = !passed;
                ok &= detected;
                sb.AppendLine("  M9  BT-10's box bound with the BARE reach (the shipped growth): measurement " +
                              "passed = " + passed + " (must be False)  detected " + detected + "  " +
                              Verdict(detected));
            }

            // M10 - BT-6's own falsifier, asserted here as well as inline: anchoring a border's fill on the
            //       STRIP's box instead of the node's must move the colour. If it does not, BD-3.2 is satisfied
            //       by every wiring and BT-6 is decoration - which is exactly what it was while
            //       ShaperBorder.CompileStrip copied the node's local box into the strip unchanged.
            {
                var probes = new[] { new Vector2(30f, 0f), new Vector2(-30f, 6f), new Vector2(0f, 31f),
                                     new Vector2(20f, 20f) };
                int diffs = StripAnchorWidthDiffs(LinearGradient(Color.black, Color.white), probes);
                bool detected = diffs > 0;
                ok &= detected;
                sb.AppendLine("  M10 BT-6's anchor taken from the STRIP: colour diffs " + diffs + "/" +
                              probes.Length + " (must be > 0)  detected " + detected + "  " + Verdict(detected));
            }

            // M11 - BT-11's hostless-ordering leg with the correction switched off at its point of use
            //       (borderSubtreeEnd cleared on the resolved owner). It must report FAIL, or the leg would go
            //       on passing after the fix was reverted.
            {
                var sink = new StringBuilder();
                bool passed = HostlessOrdering(sink, true);
                bool detected = !passed;
                ok &= detected;
                sb.AppendLine("  M11 BT-11's hostless border with the later-sibling mask disabled: measurement " +
                              "passed = " + passed + " (must be False)  detected " + detected + "  " +
                              Verdict(detected));
            }

            sb.AppendLine("  inline controls already asserted by their own legs: BT-1 live border, BT-6 strip " +
                          "anchor + grown anchor, BT-8 naive union, BT-9 joined twin, BT-10 grown anchor + bare-" +
                          "reach bound, BT-11 removed bag border + A-owns-a-fill, BT-12 Intersect member.");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-14 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// BT-14: no allocation. The border paint path allocates ZERO BYTES and causes zero gen-0 collections
        /// over a large sample count, matching the fill stage's guarantee.
        ///
        /// <b>The claim is carried by the IL DECODE, not by a heap number.</b> This doc block used to say the
        /// leg measured <c>GC.GetAllocatedBytesForCurrentThread</c>; it does not and never did - that API
        /// returns 0 on this Mono, which is why the body fell back to <c>GC.GetTotalMemory</c>. The stale
        /// sentence is corrected rather than deleted because it named the right instrument for the wrong
        /// runtime, and the next reader would otherwise reach for it again. <c>GetTotalMemory</c> is reported
        /// and NEVER asserted: BT-13's M8 shows it blind to 1 MB of small transient garbage, and it has been
        /// observed reporting 0 on one run and 12,288 on the next with the code unchanged. What establishes the
        /// claim is a transitive decode of the call graph, whose sensitivity is proven by M7.
        ///
        /// The subject is <see cref="ShaperFillResolver.PaintTile"/> on a document that CONTAINS BORDER OWNERS,
        /// which is the whole per-tile path this task added, and it is run enough times that a single per-tile
        /// allocation of a few bytes would be unmissable.
        /// </summary>
        public static string BT14_NoAllocation()
        {
            var sb = new StringBuilder("BT-14 zero allocation in the border paint path\n");
            bool ok = true;

            var member = Disc("Member", 26f, -6f, 0f);
            member.fill = LinearGradient(Color.black, Color.white);
            member.border = Border(ShaperShellAlignment.Outward, 4f, true, Solid(Color.cyan));
            var bag = ShaperNode.Bag("Bag", ShaperCombineMode.Add, member);
            bag.fill = Solid(new Color(0.3f, 0.2f, 0.5f));
            bag.border = Border(ShaperShellAlignment.Centred, 5f, false,
                                EdgeGradient(Color.red, Color.yellow, 2.5f));

            var rig = Build(bag);
            int borderOwners = 0;
            for (int i = 0; i < rig.doc.owners.Count; i++) if (rig.doc.owners[i].isBorder) borderOwners++;
            bool subjectOk = borderOwners == 2;
            ok &= subjectOk;
            sb.AppendLine("  subject: " + rig.doc.owners.Count + " owners of which " + borderOwners +
                          " borders (expected 2 - a document with no border owner would measure the wrong " +
                          "thing)  " + Verdict(subjectOk));

            // ── instrument 1: an EXACT IL DECODE of the whole per-tile call graph ──────────────────────────
            //
            // This is the instrument that ESTABLISHES the claim; the heap measurement below only corroborates
            // it. An allocation is an opcode, not a heuristic: `newobj` on a reference type, `newarr` and `box`
            // are the only ways managed memory is taken, and none of them can hide from a decode of the body.
            //
            // It is a DECODE and not the byte-frequency scan FT-9 used. That scan is an upper bound — an
            // operand byte can coincide with an opcode value — so it is conclusive only when it reports zero,
            // and on a method the size of PaintTile a coincidental byte is likely. This walks the IL stream
            // with the real opcode table, skips each operand by its declared width, and resolves every `newobj`
            // token to check whether the constructed type is a REFERENCE type: `new Color32(r,g,b,a)` is a
            // newobj that allocates nothing at all, and counting it would be a false positive that no amount of
            // measurement could then clear.
            //
            // Its ability to report a non-zero is measured in BT-13's M7, against two methods that do allocate.
            {
                // The instrument is a TRANSITIVE CLOSURE over the call graph, seeded at PaintTile, and not a
                // sweep of a hand-written type list. The list the shipped leg used happened to cover the real
                // closure on this build and named no owner for keeping it that way; a per-sample helper added to
                // any other type would have been invisible to it while it went on reporting zero. Nothing is
                // excluded by name any more either - the compile-time entries (Resolve, Walk, Bind, ...) simply
                // are not reachable from PaintTile, which is the honest reason to leave them out rather than a
                // list a reader has to take on trust.
                int hits = ClosureAllocations(
                    typeof(ShaperFillResolver).GetMethod("PaintTile"),
                    out int visited, out string offenders, out string reached);

                bool ilOk = hits == 0 && visited > 15;
                ok &= ilOk;
                sb.AppendLine("  IL decode of the TRANSITIVE call graph from PaintTile: " + visited +
                              " methods reached, " + hits + " heap allocations found (expected 0)  " +
                              Verdict(ilOk));
                if (hits > 0) sb.AppendLine("    offenders: " + offenders);
                sb.AppendLine("    types reached: " + reached);
            }

            // ── instrument 2: the heap, over a large loop — REPORTED, NEVER ASSERTED ──────────────────────
            //
            // It carries no verdict, and that is a finding rather than a caution. Its calibration is BT-13's
            // M8: on this editor GC.GetTotalMemory(false) is BLIND to 1 MB of 24-byte transient objects, which
            // is precisely the shape a per-sample allocation takes. And it is NOISY in the other direction:
            // this same loop reported 0 bytes on one run and 12,288 on the next with the code unchanged,
            // because the instrument is process-wide and the editor allocates in the background. An instrument
            // that cannot see the thing it is looking for AND moves when nothing happened cannot decide
            // anything. The IL decode above is what carries the claim; these two numbers are context.
            {
                var sheets = new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine };

                for (int i = 0; i < 3; i++)          // warm-up: JIT every path, settle every LUT
                    ShaperFillResolver.PaintTile(rig.doc, rig.grid, 0, 0, W, H, rig.buf, sheets);

                int reps = AllocSamples / (W * H);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetTotalMemory(false);
                int gc0 = GC.CollectionCount(0);
                for (int i = 0; i < reps; i++)
                    ShaperFillResolver.PaintTile(rig.doc, rig.grid, 0, 0, W, H, rig.buf, sheets);
                long delta = GC.GetTotalMemory(false) - before;
                int collections = GC.CollectionCount(0) - gc0;

                // REPORTED, NOT ASSERTED, and the reason is measured rather than assumed. GC.GetTotalMemory is
                // process-wide, not scoped to this loop, so the editor's own background churn lands in the
                // delta: this leg was observed reporting 0 bytes on one run and 12,288 on the next, over a loop
                // the decode above proves contains no allocation opcode at all — and 12,288 is one of the two
                // figures FT-9 already recorded for the same instrument. Combined with M8's calibration, which
                // shows it blind to 1 MB of small transient garbage, it is unreliable in BOTH directions and
                // cannot carry a verdict. Reporting it anyway is not decoration: a delta in the megabytes here
                // would be worth chasing even though a small one means nothing.
                sb.AppendLine("  heap over " + reps + " tiles x " + (W * H) + " samples = " + (reps * W * H) +
                              " samples: GetTotalMemory delta " + delta +
                              " bytes, gen-0 collections " + collections +
                              " (process-wide, informational only - see BT-13 M8)");

                // A high-CALL-COUNT loop on a tiny tile, which is the regime a per-INVOCATION allocation shows
                // up in and the 128x128 loop above does not reach: 40 tiles could hide a 1 KB-per-call
                // allocation under any heap instrument's floor; 40,000 cannot.
                const int Calls = 40000;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before2 = GC.GetTotalMemory(false);
                int gc0b = GC.CollectionCount(0);
                for (int i = 0; i < Calls; i++)
                    ShaperFillResolver.PaintTile(rig.doc, rig.grid, 0, 0, 8, 8, rig.buf, sheets);
                long delta2 = GC.GetTotalMemory(false) - before2;
                int collections2 = GC.CollectionCount(0) - gc0b;

                sb.AppendLine("  heap over " + Calls + " calls on an 8x8 tile: GetTotalMemory delta " + delta2 +
                              " bytes, gen-0 collections " + collections2 +
                              " (process-wide, informational only - see BT-13 M8)");
            }

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── the IL decoder ────────────────────────────────────────────────────────────────────────────────

        static readonly System.Reflection.Emit.OpCode[] IlSingle = new System.Reflection.Emit.OpCode[256];
        static readonly System.Reflection.Emit.OpCode[] IlMulti = new System.Reflection.Emit.OpCode[256];
        static bool ilTableBuilt;

        static void BuildIlTable()
        {
            if (ilTableBuilt) return;
            foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (f.FieldType != typeof(System.Reflection.Emit.OpCode)) continue;
                var op = (System.Reflection.Emit.OpCode)f.GetValue(null);
                if (op.Size == 1) IlSingle[op.Value & 0xFF] = op;
                else IlMulti[op.Value & 0xFF] = op;
            }
            ilTableBuilt = true;
        }

        static int OperandSize(System.Reflection.Emit.OperandType t)
        {
            switch (t)
            {
                case System.Reflection.Emit.OperandType.InlineNone: return 0;
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                case System.Reflection.Emit.OperandType.ShortInlineI:
                case System.Reflection.Emit.OperandType.ShortInlineVar: return 1;
                case System.Reflection.Emit.OperandType.InlineVar: return 2;
                case System.Reflection.Emit.OperandType.InlineI8:
                case System.Reflection.Emit.OperandType.InlineR: return 8;
                default: return 4;
            }
        }

        /// <summary>
        /// The number of HEAP allocations in one method's IL, decoded exactly.
        ///
        /// Counts <c>newarr</c> and <c>box</c> unconditionally, and <c>newobj</c> ONLY when the constructed type
        /// is a reference type — <c>new Color32(r,g,b,a)</c> is a <c>newobj</c> that takes no managed memory at
        /// all, and counting it would be a false positive that no later measurement could clear. A token that
        /// cannot be resolved is counted CONSERVATIVELY as an allocation and named, so an unresolvable case can
        /// never be silently swallowed into a zero.
        /// </summary>
        /// <summary>
        /// Every heap allocation reachable from <paramref name="root"/>, following the CALL GRAPH rather than a
        /// hand-written list of types.
        ///
        /// <b>Why the closure and not a type list.</b> The shipped instrument swept the static methods of seven
        /// named types plus one instance method. That is a decode of the right kind over possibly the wrong set:
        /// nothing checks that the set still covers what <c>PaintTile</c> actually calls, so a future per-sample
        /// helper placed in an eighth type would be invisible and the leg would keep reporting zero. Measured on
        /// this build, the true closure from <c>PaintTile</c> is 31 methods and reaches exactly one Laubrary type
        /// the list did not name - so the list was correct today and unowned tomorrow, which is the shape of a
        /// leg that stops being able to fail without anyone editing it.
        ///
        /// Only <c>Laubrary</c> methods are followed. That boundary is deliberate and is stated rather than
        /// hidden: <c>Mathf</c>, <c>Array.Copy</c> and <c>Array.Clear</c> are BCL entry points whose bodies are
        /// not ours to audit, and they are documented non-allocating. Everything inside the package is walked.
        ///
        /// The decoder's sensitivity is BT-13's M7, which runs it against two methods that DO allocate.
        /// </summary>
        static int ClosureAllocations(System.Reflection.MethodBase root, out int visited,
                                      out string offenders, out string reached)
        {
            BuildIlTable();
            var seen = new HashSet<string>();
            var queue = new Queue<System.Reflection.MethodBase>();
            var offend = new StringBuilder();
            var types = new SortedSet<string>();
            queue.Enqueue(root);
            visited = 0;
            int hits = 0;

            while (queue.Count > 0 && visited < 4000)
            {
                System.Reflection.MethodBase m = queue.Dequeue();
                string key = (m.DeclaringType != null ? m.DeclaringType.FullName : "?") + "::" +
                             m.Name + "::" + m.MetadataToken;
                if (!seen.Add(key)) continue;
                visited++;
                if (m.DeclaringType != null) types.Add(m.DeclaringType.Name);

                byte[] il;
                try { var body = m.GetMethodBody(); il = body != null ? body.GetILAsByteArray() : null; }
                catch { continue; }
                if (il == null) continue;

                Type[] typeArgs = null, methodArgs = null;
                try
                {
                    if (m.DeclaringType != null && m.DeclaringType.IsGenericType)
                        typeArgs = m.DeclaringType.GetGenericArguments();
                    if (m.IsGenericMethod) methodArgs = m.GetGenericArguments();
                }
                catch { }

                int i = 0;
                while (i < il.Length)
                {
                    System.Reflection.Emit.OpCode op;
                    if (il[i] == 0xFE && i + 1 < il.Length) { op = IlMulti[il[i + 1]]; i += 2; }
                    else { op = IlSingle[il[i]]; i += 1; }

                    if (op.OperandType == System.Reflection.Emit.OperandType.InlineSwitch)
                    {
                        if (i + 4 > il.Length) break;
                        int n = BitConverter.ToInt32(il, i);
                        i += 4 + 4 * n;
                        continue;
                    }

                    int operandAt = i;
                    i += OperandSize(op.OperandType);

                    if (op == System.Reflection.Emit.OpCodes.Newarr)
                    { hits++; offend.Append(Where(m) + "=newarr "); }
                    else if (op == System.Reflection.Emit.OpCodes.Box)
                    { hits++; offend.Append(Where(m) + "=box "); }

                    bool isCall = op == System.Reflection.Emit.OpCodes.Call ||
                                  op == System.Reflection.Emit.OpCodes.Callvirt ||
                                  op == System.Reflection.Emit.OpCodes.Ldftn ||
                                  op == System.Reflection.Emit.OpCodes.Newobj;
                    if (!isCall) continue;

                    try
                    {
                        int token = BitConverter.ToInt32(il, operandAt);
                        var callee = m.Module.ResolveMethod(token, typeArgs, methodArgs);
                        if (callee == null || callee.DeclaringType == null) continue;

                        if (op == System.Reflection.Emit.OpCodes.Newobj && !callee.DeclaringType.IsValueType)
                        { hits++; offend.Append(Where(m) + "=newobj " + callee.DeclaringType.Name + " "); }

                        string ns = callee.DeclaringType.Namespace ?? "";
                        if (ns.StartsWith("Laubrary")) queue.Enqueue(callee);
                    }
                    catch { }
                }
            }

            offenders = offend.ToString().TrimEnd();
            reached = string.Join(", ", new List<string>(types).ToArray());
            return hits;
        }

        static string Where(System.Reflection.MethodBase m)
            => (m.DeclaringType != null ? m.DeclaringType.Name : "?") + "." + m.Name;

        static int CountAllocations(System.Reflection.MethodBase method, out string detail)
        {
            detail = "";
            if (method == null) { detail = "method is null"; return 0; }
            BuildIlTable();

            byte[] il;
            try
            {
                var body = method.GetMethodBody();
                il = body != null ? body.GetILAsByteArray() : null;
            }
            catch (Exception e) { detail = "unreadable: " + e.GetType().Name; return 0; }
            if (il == null) { detail = "no body"; return 0; }

            Type[] typeArgs = null, methodArgs = null;
            try
            {
                if (method.DeclaringType != null && method.DeclaringType.IsGenericType)
                    typeArgs = method.DeclaringType.GetGenericArguments();
                if (method.IsGenericMethod) methodArgs = method.GetGenericArguments();
            }
            catch { }

            int hits = 0;
            var found = new StringBuilder();
            int i = 0;
            while (i < il.Length)
            {
                System.Reflection.Emit.OpCode op;
                if (il[i] == 0xFE && i + 1 < il.Length) { op = IlMulti[il[i + 1]]; i += 2; }
                else { op = IlSingle[il[i]]; i += 1; }

                if (op.OperandType == System.Reflection.Emit.OperandType.InlineSwitch)
                {
                    if (i + 4 > il.Length) break;
                    int n = BitConverter.ToInt32(il, i);
                    i += 4 + 4 * n;
                    continue;
                }

                int operandAt = i;
                i += OperandSize(op.OperandType);

                if (op == System.Reflection.Emit.OpCodes.Newarr) { hits++; found.Append("newarr "); }
                else if (op == System.Reflection.Emit.OpCodes.Box) { hits++; found.Append("box "); }
                else if (op == System.Reflection.Emit.OpCodes.Newobj)
                {
                    bool heap = true;
                    string name = "unresolved";
                    try
                    {
                        int token = BitConverter.ToInt32(il, operandAt);
                        var ctor = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                        if (ctor != null && ctor.DeclaringType != null)
                        {
                            name = ctor.DeclaringType.Name;
                            heap = !ctor.DeclaringType.IsValueType;
                        }
                    }
                    catch { }
                    if (heap) { hits++; found.Append("newobj " + name + " "); }
                }
            }

            detail = hits > 0 ? found.ToString().TrimEnd() : "clean";
            return hits;
        }

        // ── the contact sheet ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A PNG contact sheet of the border stage, numbered cell by cell. Not a MenuItem and not a window —
        /// invoked by path through the CLI, exactly as <c>ShaperFillAudit.FT20_ContactSheet</c> is.
        ///
        /// Every cell is composited over an OPAQUE checkerboard and encoded once, for the reason FT-20 records:
        /// a PNG is straight alpha, so a partly transparent cell would otherwise be composited by whatever the
        /// viewer happens to use as a background, and a white outline on a transparent sheet is invisible in
        /// every viewer that renders alpha as white.
        ///
        /// The last cell is a MAGENTA SEAM PROBE, and it is the one cell whose correct reading is "nothing to
        /// see": every sample strictly inside a joined outward border's dilated silhouette whose published
        /// coverage falls below 0.999 is stamped magenta. On a <c>min</c>-based join a magenta ring would trace
        /// the original outline through the middle of the cell (BD-2.2), so a clean cell IS the assertion.
        /// </summary>
        public static string BorderContactSheet(string path)
        {
            const int Cell = 96, Cols = 8, Pad = 6;

            var cells = new List<KeyValuePair<string, ShaperNode>>();
            void Add(string n, ShaperNode node) => cells.Add(new KeyValuePair<string, ShaperNode>(n, node));

            Color body = new Color(0.16f, 0.20f, 0.34f);
            Color rim = new Color(1f, 0.78f, 0.16f);

            ShaperNode Bordered(float r, ShaperShellAlignment a, float w, bool joins = true,
                                ShaperFillDef fill = null)
            {
                var d = Disc("Disc", r);
                d.fill = Solid(body);
                d.border = Border(a, w, joins, fill ?? Solid(rim));
                return d;
            }

            // 01-03 — the three alignments at one width, so the only difference is where the band sits.
            Add("01 Inward w4", Bordered(30f, ShaperShellAlignment.Inward, 4f));
            Add("02 Outward w4", Bordered(30f, ShaperShellAlignment.Outward, 4f));
            Add("03 Straddling w4", Bordered(30f, ShaperShellAlignment.Centred, 4f));

            // 04-06 — the width sweep, including zero. 04 MUST be an unadorned disc: BD-1.5's no-op, as a
            //         picture rather than as a table row.
            Add("04 Width 0 (no-op)", Bordered(30f, ShaperShellAlignment.Inward, 0f));
            Add("05 Width 1", Bordered(30f, ShaperShellAlignment.Inward, 1f));
            Add("06 Width 10", Bordered(30f, ShaperShellAlignment.Inward, 10f));

            // 07-08 — joined versus opted out, on a MEMBER of a bag, which is the only place the difference is
            //         visible. Joined: the bag's silhouette grows to include the outline. Opted out: the bag
            //         never learned about the strip, so the parent's coverage clips the outward half away
            //         (BD-2.3's second consequence). On a ROOT the two are indistinguishable, which is exactly
            //         why the pair is not drawn there.
            foreach (bool joins in new[] { true, false })
            {
                var member = Disc("Member", 26f);
                member.border = Border(ShaperShellAlignment.Outward, 6f, joins, Solid(rim));
                var bag = ShaperNode.Bag("Bag", ShaperCombineMode.Add, member);
                bag.fill = Solid(body);
                Add((joins ? "07 Joined" : "08 Not counted"), bag);
            }

            // 09 — a GRADIENT outline. BD-3.2: the ramp runs ACROSS THE SHAPE, left to right, not across the
            //      two pixels of the ring's own thickness. That is the whole content of FC-8.4 as a picture.
            Add("09 Gradient outline", Bordered(30f, ShaperShellAlignment.Inward, 7f, true,
                LinearGradient(new Color(0.1f, 0.9f, 1f), new Color(1f, 0.15f, 0.5f))));

            // 10 — a TEXTURED outline, tiled, so the outline is visibly reading texels rather than a colour.
            Add("10 Texture outline", Bordered(30f, ShaperShellAlignment.Inward, 9f, true, new ShaperFillDef
            {
                kind = ShaperFillKind.Texture,
                texture = Checker8(),
                textureMapping = ShaperTextureMapping.Tiled,
                textureTilesX = new ZUIValue(4f),
                textureTilesY = new ZUIValue(4f),
            }));

            // 11 — a ByEdgeDistance outline, straddling and thick, showing the BD-3.3 RIDGE: bright at the
            //      strip's centre line, falling to the ramp's low end at BOTH faces. A monotone ramp here would
            //      be the BD-3.3 failure, and it would look deliberate.
            Add("11 ByEdgeDist ridge", Bordered(28f, ShaperShellAlignment.Centred, 14f, false,
                EdgeGradient(new Color(0.05f, 0.02f, 0.2f), new Color(1f, 0.95f, 0.55f), 7f)));

            // 12 — a bag with bordered MEMBERS plus a bag border. The members' outlines divide the interior; the
            //      bag's outline goes round the fused silhouette and is ON TOP where they meet (BD-3.6).
            {
                var m1 = Disc("A", 20f, -12f, 0f);
                m1.fill = Solid(new Color(0.2f, 0.45f, 0.8f));
                m1.border = Border(ShaperShellAlignment.Inward, 3f, false, Solid(new Color(0.1f, 1f, 0.6f)));
                var m2 = Disc("B", 20f, 12f, 0f);
                m2.fill = Solid(new Color(0.8f, 0.3f, 0.25f));
                m2.border = Border(ShaperShellAlignment.Inward, 3f, false, Solid(new Color(0.1f, 1f, 0.6f)));
                var bag = ShaperNode.Bag("Two", ShaperCombineMode.Add, m1, m2);
                bag.fill = Solid(body);
                bag.border = Border(ShaperShellAlignment.Inward, 5f, false, Solid(rim));
                Add("12 Bag + members", bag);
            }

            // 13 — a bordered member next to a Subtract member. The Subtract member's OWN border is refused
            //      (BD-3.7) and draws nothing; the hole it carves is traced by the BAG's border, which is
            //      BD-3.7's "the way to outline a hole is a border on the bag" as a picture.
            {
                var solid = Disc("Solid", 30f, -8f, 0f);
                solid.fill = Solid(new Color(0.2f, 0.45f, 0.8f));
                solid.border = Border(ShaperShellAlignment.Inward, 3f, false, Solid(new Color(0.1f, 1f, 0.6f)));
                var hole = Disc("Hole", 15f, 14f, 0f, ShaperCombineMode.Subtract);
                hole.border = Border(ShaperShellAlignment.Inward, 6f, true, Solid(Color.magenta));
                var bag = ShaperNode.Bag("Carved", ShaperCombineMode.Add, solid, hole);
                bag.fill = Solid(body);
                bag.border = Border(ShaperShellAlignment.Inward, 4f, false, Solid(rim));
                Add("13 Subtract + hole rim", bag);
            }

            // 14 — the seam probe. See the method summary.
            int seamCell = cells.Count;
            Add("14 Join seam probe", Bordered(28f, ShaperShellAlignment.Outward, 8f, true));

            // 15-16 - BD-3.6 for a border whose node bound NO FILL OF ITS OWN, and the same picture with the
            //         node owning one. The magenta rim belongs to A, which is BELOW red B in fold order, so
            //         WHERE THE RIM CROSSES B THE RED MUST BE ON TOP - in BOTH cells. They looked DIFFERENT
            //         before this was fixed: with no fill on A the rim had no accumulator of its own, landed in
            //         the bag's, and drew over B. Two cells and not one, because the defect was an asymmetry
            //         between them and a single picture cannot show an asymmetry.
            foreach (bool aOwnsFill in new[] { false, true })
            {
                var a = Disc("A", 26f, -9f, 0f);
                a.border = Border(ShaperShellAlignment.Inward, 6f, false, Solid(Color.magenta));
                if (aOwnsFill) a.fill = Solid(new Color(0.12f, 0.42f, 0.16f));
                var bMember = Disc("B", 26f, 11f, 0f);
                bMember.fill = Solid(new Color(0.85f, 0.16f, 0.16f));
                var bag = ShaperNode.Bag("Pair", ShaperCombineMode.Add, a, bMember);
                bag.fill = Solid(body);
                Add(aOwnsFill ? "16 ...A owns a fill" : "15 Hostless rim under B", bag);
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
                var rig = Build(cells[c].Value, Cell, Cell);
                Paint(rig);
                CompositeOverBackdrop(rig.buf.dst, pixels, Cell);
                if (c == seamCell) seamMarks = StampJoinSeam(rig, cells[c].Value, pixels, Cell, 28f, 8f);
                DrawLabel(pixels, Cell, cells[c].Key);

                int cx = Pad + (c % Cols) * (Cell + Pad);
                int cy = texH - Pad - Cell - (c / Cols) * (Cell + Pad);
                sheet.SetPixels32(cx, cy, Cell, Cell, pixels);
            }
            sheet.Apply();

            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());

            var sb = new StringBuilder("BORDER contact sheet\n");
            sb.AppendLine("  " + cells.Count + " cells, " + Cols + " x " + rows + ", " + texW + "x" + texH +
                          " px, written to " + path);
            for (int c = 0; c < cells.Count; c++)
                sb.AppendLine("    row " + (c / Cols) + " col " + (c % Cols) + "  " + cells[c].Key);
            sb.AppendLine("  Cell 04 must be an UNADORNED disc — that is BD-1.5's exact no-op as a picture.");
            sb.AppendLine("  Cells 07 and 08 differ only in joinsCoverage, and only because the bordered node is a");
            sb.AppendLine("  MEMBER. Opted out, the parent never learned about the strip, so the parent's coverage clips");
            sb.AppendLine("  it: an OUTWARD strip lies entirely outside the node, so 08 keeps only the half-pixel of it");
            sb.AppendLine("  that falls inside the parent's antialiased edge - a faint fringe where 07 has a thick ring.");
            sb.AppendLine("  Cell 11 must be BRIGHT ALONG THE STRIP'S CENTRE LINE and dark at both of its faces —");
            sb.AppendLine("  the BD-3.3 ridge. A monotone band there would be the failure, and would look deliberate.");
            sb.AppendLine("  Cell 13's magenta border is authored on the SUBTRACT member and must be absent: it is");
            sb.AppendLine("  refused by BD-3.7. Any magenta in cell 13 is a failure of the refusal.");
            sb.AppendLine("  Cell 14 marks in MAGENTA every interior sample of the dilated silhouette whose published");
            sb.AppendLine("  coverage fell below 0.999. Marks in this render: " + seamMarks + " (expected 0).");
            sb.AppendLine("  Cells 15 and 16 must MATCH wherever the magenta rim crosses the red disc: the rim belongs");
            sb.AppendLine("  to the LEFT member, which is below the red one in fold order, so the red must be on top in");
            sb.AppendLine("  both. They differ only in whether the left member also owns a fill of its own - a dial with");
            sb.AppendLine("  nothing to do with z-order. Magenta over the red disc in 15 is the BD-3.6 failure.");
            sb.Append("  RESULT: RENDERED — NOT VERIFIED BY THE TABLE. A passing table is not a picture; this is " +
                      "not a pass until a human has looked at it.");
            return sb.ToString();
        }

        /// <summary>
        /// Stamp MAGENTA over every sample strictly inside a joined border's dilated silhouette whose PUBLISHED
        /// coverage has fallen below 0.999 — the BD-2.2 seam, drawn instead of counted.
        /// </summary>
        static int StampJoinSeam(Rig rig, ShaperNode node, Color32[] px, int cell, float radius, float reach)
        {
            ShaperProgram joined = ShaperCompiler.Compile(node, 0f, 0u);
            float[] st = joined.NewStack();
            int marks = 0;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float cx = -0.5f * (cell - 1) + x, cy = -0.5f * (cell - 1) + y;
                    float r = Mathf.Sqrt(cx * cx + cy * cy);
                    if (r - radius - reach >= -1f) continue;                  // not strictly inside
                    float cov = ShaperField.Coverage(ShaperEvaluator.Distance(joined, cx, cy, st), HalfBand);
                    if (cov >= 0.999f) continue;
                    px[y * cell + x] = new Color32(255, 0, 220, 255);
                    marks++;
                }
            return marks;
        }

        /// <summary>
        /// Composite one cell's linear, premultiplied destination over an opaque checkerboard and encode once
        /// (FC-2.3's single encode boundary). Copied in spirit from <c>ShaperFillAudit.CompositeOverBackdrop</c>
        /// and for the reasons stated there: a transparent sheet cannot show a white outline, and cannot show an
        /// additive one at all.
        /// </summary>
        static void CompositeOverBackdrop(float[] dst, Color32[] outPixels, int cell)
        {
            const int Square = 8;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    int i = y * cell + x;
                    float back = (((x / Square) + (y / Square)) & 1) == 0 ? 0.16f : 0.31f;
                    float a = Mathf.Clamp01(dst[i * 4 + 3]);
                    float inv = 1f - a;
                    outPixels[i] = new Color32(
                        ShaperSrgb.EncodeToByte(dst[i * 4 + 0] + back * inv),
                        ShaperSrgb.EncodeToByte(dst[i * 4 + 1] + back * inv),
                        ShaperSrgb.EncodeToByte(dst[i * 4 + 2] + back * inv),
                        255);
                }
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

        /// <summary>A 3x5 bitmap font, digits only, one 15-bit int per glyph (three bits per row, top row highest).</summary>
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
            int ox = 4, oy = 4;

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

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// The light audit: the LT-* conformance tests of LIGHT-RIG-CONTRACT Part 8, each returning a report
    /// string.
    ///
    /// Plain static methods, no <c>[MenuItem]</c> and no <c>EditorWindow</c> — this is the light stage's
    /// verification harness, invoked through the Unity CLI, not a tool. Same arrangement as
    /// <see cref="ShaperFieldAudit"/>, <see cref="ShaperFillAudit"/> and <see cref="ShaperBorderAudit"/>, and
    /// for the same reason.
    ///
    /// <b>Every result is reported as measured-number versus expected-number</b>
    /// (<c>SHAPE-ENGINE-SPEC.md:268</c>, applied unchanged). A rule is never reported as verified on the
    /// strength of the code compiling.
    ///
    /// <b>Every leg names the source mutation that makes it fail.</b> That is Part 8's own requirement and its
    /// reason: T-0105, T-0106 and T-0107 each shipped a green audit that an independent verifier then holed,
    /// and in every case some tests turned out to be structurally incapable of failing
    /// (<c>BORDER-CONTRACT.md:245</c>). A test with no stated mutation is not a test and must not be counted.
    /// </summary>
    public static class ShaperLightAudit
    {
        // ── constants with provenance ─────────────────────────────────────────────────────────────────────

        /// <summary>The canvas most fixtures render at. 128x128 = 16 384 samples, the count Part 8 quotes.</summary>
        const int W = 128, H = 128;

        /// <summary>One canvas unit per sample, so a "canvas pixel" in a dial equals a sample (LR-1.5).</summary>
        const float Px = 1f;

        /// <summary>LT-2's sample budget: Part 8 says a 10 000-sample lit PaintTile.</summary>
        const int AllocSamples = 10000;

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        // ── stored goldens (LT-8) ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Where LT-8's stored golden lives. The task workspace, not the package — a golden is verification
        /// evidence rather than shipped code, and it must not be picked up by a package export.
        ///
        /// It is a settable field rather than a constant so the harness can point it somewhere else without
        /// an edit, but it deliberately has no fallback and no auto-create: a MISSING golden makes LT-8 FAIL
        /// and say how to regenerate. Silently regenerating on absence is how a golden becomes a mirror of
        /// whatever the build currently does, which is exactly the failure D-9 was trying to avoid and
        /// exactly the failure the live comparison actually had.
        /// </summary>
        public static string GoldenDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\golden";

        const string Lt8Golden = "lt8-unlit-128x128.rgba";

        static string GoldenPath(string name) => System.IO.Path.Combine(GoldenDir, name);

        /// <summary>
        /// LT-8's fixture: T-0107-shaped, a bag with nested fill owners and a border, so both accumulate
        /// blocks LR-5.1 touches are exercised. Shared by the leg and by the golden writer so the two cannot
        /// drift.
        /// </summary>
        static ShaperNode Lt8Fixture()
        {
            var c = Disc("C", 14f, 0f, 0f); c.fill = Solid(new Color(0.2f, 0.4f, 1f));
            var b = ShaperNode.Bag("B", ShaperCombineMode.Add, Disc("Bd", 28f, 0f, 0f), c);
            b.fill = Solid(new Color(0.2f, 0.9f, 0.3f));
            b.border = new ShaperBorderDef { enabled = true, width = new ZUIValue(3f) };
            var a = ShaperNode.Bag("A", ShaperCombineMode.Add, Disc("Ad", 42f, 0f, 0f), b);
            a.fill = Solid(new Color(0.9f, 0.2f, 0.2f));
            var add = Disc("Glow", 20f, 22f, 0f);
            add.fill = Solid(new Color(1f, 0.8f, 0.3f), 1f, 0f, ShaperFillComposite.Add);
            a.children.Add(add);
            return a;
        }

        /// <summary>
        /// <b>Regenerate LT-8's stored golden. DELIBERATE ACT ONLY.</b> Never called by
        /// <see cref="RunAll"/>, and named outside the <c>LT*</c> prefix so no reflection sweep of the audit
        /// can invoke it by accident.
        ///
        /// Run this ONLY when the unlit path has changed on purpose, and expect the resulting diff to be
        /// reviewed. Running it to make a red LT-8 go green is the one thing that destroys the leg's value.
        /// </summary>
        public static string WriteGoldens()
        {
            var sb = new StringBuilder("WriteGoldens - DELIBERATE regeneration of LT-8's stored golden\n");
            System.IO.Directory.CreateDirectory(GoldenDir);

            var unlit = BuildLit(Lt8Fixture(), null, null);
            Paint(unlit);
            var px = Encoded(unlit);
            var bytes = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                bytes[i * 4 + 0] = px[i].r; bytes[i * 4 + 1] = px[i].g;
                bytes[i * 4 + 2] = px[i].b; bytes[i * 4 + 3] = px[i].a;
            }
            string p = GoldenPath(Lt8Golden);
            bool existed = System.IO.File.Exists(p);
            System.IO.File.WriteAllBytes(p, bytes);
            sb.AppendLine("  " + (existed ? "OVERWROTE" : "created") + " " + p + " (" + bytes.Length + " B, " +
                          W + "x" + H + " RGBA8, null-scene render of LT-8's fixture)");
            sb.Append("  RESULT: WRITTEN - this is not a test and is not counted as one.");
            return sb.ToString();
        }

        // ── entry point ───────────────────────────────────────────────────────────────────────────────────

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Shaper LIGHT audit (T-0108, LIGHT-RIG-CONTRACT Part 8) ===");
            sb.AppendLine(LT1_OneLawNumeric());
            sb.AppendLine(LT1b_OneLawStructural());
            sb.AppendLine(LT2_ZeroAllocation());
            sb.AppendLine(LT3_TileIndependence());
            sb.AppendLine(LT4_Determinism());
            sb.AppendLine(LT5a_LinearSpace());
            sb.AppendLine(LT5b_OneEncodeBoundary());
            sb.AppendLine(LT6_HandComputed());
            sb.AppendLine(LT7_RimIsZeroOnFlat());
            sb.AppendLine(LT8_ReceiveOffIsBitIdentical());
            sb.AppendLine(LT9_ExclusivityUnperturbed());
            sb.AppendLine(LT10_AddIsNotLit());
            sb.AppendLine(LT11_TheCap());
            sb.AppendLine(LT12_ShadowsRecordedAndInert());
            sb.AppendLine(LT13_ProviderIsSwappable());
            sb.AppendLine(LT14a_Convexity());
            sb.AppendLine(LT14b_OrbSilhouetteIsRotationInvariant());
            sb.AppendLine(LT14c_RingEdgeOnPop());
            sb.AppendLine(LT15_NormalsAreUnit());
            sb.AppendLine(LT16_SharedFrame());
            // The fix pass's additions. LT-18..LT-23 close the four real defects and the three gaps an
            // independent verification found; each names the mutation that makes it fail, like every leg above.
            sb.AppendLine(LT18_DegenerateDialsNeverGoNonFinite());
            sb.AppendLine(LT19_EveryDialIsLiveOrDeclaredInert());
            sb.AppendLine(LT20_BorderIsLitByItsHost());
            sb.AppendLine(LT21_DocumentOwnsTheLights());
            sb.AppendLine(LT22_GlowPathExecutes());
            sb.AppendLine(LT23_RimIsInertOnBlackAmbientAndDeclared());
            sb.AppendLine(LT24_SolidsHeightFieldRelief());
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
                // T-0271 — Solid(Color.white) with the default veil/height/composite IS a default-constructed
                // fill, which the engine now reads as the phantom Unity writes for a null one. An audit
                // fixture is authored by definition, so it says so.
                authored = true,
                kind = ShaperFillKind.Solid,
                solidColor = c,
                veil = new ZUIValue(veil),
                heightDelta = new ZUIValue(height),
                composite = composite,
            };

        static ShaperLight Dir(float yaw, float pitch, Color c, float intensity = 1f, float spec = 1f)
            => new ShaperLight
            {
                kind = ShaperLightKind.Directional,
                colour = c,
                yaw = new ZUIValue(yaw), pitch = new ZUIValue(pitch),
                intensity = new ZUIValue(intensity), specular = new ZUIValue(spec),
            };

        static ShaperLight Pnt(float x, float y, float z, float range, Color c,
                               float intensity = 1f, float spec = 1f)
            => new ShaperLight
            {
                kind = ShaperLightKind.Point,
                colour = c,
                posX = new ZUIValue(x), posY = new ZUIValue(y), posZ = new ZUIValue(z),
                range = new ZUIValue(range),
                intensity = new ZUIValue(intensity), specular = new ZUIValue(spec),
            };

        static ShaperLightRig RigOf(Color amb, float ambI, params ShaperLight[] lights)
        {
            var r = new ShaperLightRig { ambientColour = amb, ambientIntensity = new ZUIValue(ambI) };
            if (lights != null) r.lights.AddRange(lights);
            return r;
        }

        /// <summary>A rig with eight distinct lights — LT-3, LT-8, LT-9 and LT-12 all want a FULL one.</summary>
        static ShaperLightRig EightLights()
        {
            return RigOf(Color.white, 0.12f,
                Dir(-55f, 36f, new Color(1f, 0.95f, 0.85f), 0.9f),
                Dir(120f, 20f, new Color(0.4f, 0.55f, 1f), 0.5f),
                Pnt(-40f, 30f, 35f, 60f, new Color(1f, 0.4f, 0.2f), 0.8f),
                Pnt(45f, -25f, 25f, 40f, new Color(0.3f, 1f, 0.6f), 0.7f),
                Dir(0f, 80f, new Color(0.9f, 0.9f, 1f), 0.3f),
                Pnt(0f, 0f, 90f, 120f, new Color(1f, 1f, 1f), 0.45f),
                Dir(200f, -15f, new Color(0.7f, 0.3f, 0.9f), 0.35f),
                Pnt(-60f, -60f, 15f, 30f, new Color(0.95f, 0.85f, 0.4f), 0.6f));
        }

        static ShaperLightResponse Resp(bool receive = true, float intensityScale = 1f,
                                        float rim = 0f, float rimPower = 2.2f,
                                        float spec = 0.9f, float specPower = 48f,
                                        Color? tint = null, Vector3? normal = null)
            => new ShaperLightResponse
            {
                receiveLighting = receive,
                intensityScale = new ZUIValue(intensityScale),
                rimStrength = new ZUIValue(rim),
                rimPower = new ZUIValue(rimPower),
                specular = new ZUIValue(spec),
                specularPower = new ZUIValue(specPower),
                specularTint = tint ?? new Color(0.9f, 0.95f, 1f),
                normalKind = ShaperNormalKind.Constant,
                normalConstant = normal ?? new Vector3(0f, 0f, 1f),
            };

        /// <summary>A resolved, buffered, LIT fixture. Everything host-allocated once.</summary>
        sealed class LRig
        {
            public int width, height;
            public ShaperSampleGrid grid;
            public ShaperFillDocument doc;
            public ShaperFillBuffers buf;
            public ShaperLightScene scene;
            public ShaperLightProgram prog;
        }

        static LRig BuildLit(ShaperNode root, ShaperLightRig rig, ShaperLightResponse resp,
                             int w = W, int h = H, float phase = 0f, uint seed = 0u,
                             ShaperQuantitySet leafPublished = ShaperQuantitySet.ShippedShapeEngine)
        {
            float chw = 0.5f * (w - 1) * Px, chh = 0.5f * (h - 1) * Px;
            var doc = ShaperFillResolver.Resolve(root, phase, seed, chw, chh, leafPublished);
            int k = Mathf.Max(1, doc.owners.Count);
            var buf = new ShaperFillBuffers(w * h, k);

            var r = new LRig
            {
                width = w, height = h,
                grid = ShaperSampleGrid.Centred(w, h, Px),
                doc = doc,
                buf = buf,
            };

            if (rig != null)
            {
                // The scene's sampleCapacity/ownerCapacity MUST match the buffers' — the paint pass indexes
                // both with the same ownerIndex * sampleCapacity stride.
                r.scene = new ShaperLightScene(buf.sampleCapacity, buf.ownerCapacity);
                r.prog = ShaperLightCompiler.Compile(rig, phase, seed);
                r.scene.rig = r.prog.rig;
                var rc = ShaperLightCompiler.CompileResponse(resp, "Layer", phase, seed, r.prog);
                var no = ShaperLightCompiler.CompileNormal(resp);
                r.scene.SetAll(rc, no);
                ShaperLightCompiler.Finish(r.prog);
            }
            return r;
        }

        static void Paint(LRig r)
        {
            var sheets = new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine };
            ShaperFillResolver.PaintTile(r.doc, r.grid, 0, 0, r.width, r.height, r.buf, sheets, r.scene);
        }

        static int Index(LRig r, float x, float y)
        {
            int ix = Mathf.RoundToInt((x - r.grid.originX) / r.grid.pixelSize);
            int iy = Mathf.RoundToInt((y - r.grid.originY) / r.grid.pixelSize);
            ix = Mathf.Clamp(ix, 0, r.width - 1);
            iy = Mathf.Clamp(iy, 0, r.height - 1);
            return iy * r.width + ix;
        }

        static Color32[] Encoded(LRig r)
        {
            var px = new Color32[r.width * r.height];
            ShaperFillResolver.Encode(r.buf.dst, px, r.width * r.height);
            return px;
        }

        /// <summary>A one-node Solids layer: the generator replaces the shape stage for owner 0 (LR-6.1).</summary>
        static LRig BuildSolid(ShaperSolidDef def, ShaperLightRig rig, ShaperLightResponse resp,
                               ShaperFillDef fill = null, int w = W, int h = H,
                               float phase = 0f, uint seed = 0u)
        {
            var node = Rect("Solid", w * 0.5f, h * 0.5f, 0f, 0f);
            node.fill = fill ?? Solid(Color.white);
            var r = BuildLit(node, rig ?? RigOf(Color.white, 0f), resp, w, h, phase, seed,
                             ShaperSolids.Published);
            // `r.prog` is passed so the generator can raise LR-7.3's inert-dial diagnostic at compile —
            // without it the declaration table would be code nothing ever runs (LT-19 asserts it).
            r.scene.SetSolid(0, ShaperSolids.Compile(def, phase, seed, r.prog));
            return r;
        }

        static ShaperSolidDef SolidDef(ShaperSolidForm form, float size = 34f,
                                       float yaw = 0f, float tilt = 0f, float roll = 0f,
                                       float lineWidth = 0f, float aspect = 1f, float depth = 1f)
            => new ShaperSolidDef
            {
                form = form,
                size = new ZUIValue(size),
                yaw = new ZUIValue(yaw), tilt = new ZUIValue(tilt), roll = new ZUIValue(roll),
                lineWidth = new ZUIValue(lineWidth),
                aspect = new ZUIValue(aspect), depth = new ZUIValue(depth),
                edgeGlow = new ZUIValue(0f), innerGlow = new ZUIValue(0f),
            };

        // ── LT-1 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-1 — ONE LAW, TWO FAMILIES, NUMERIC. The same rig, response, P, N and V, reached through the two
        /// genuinely different providers of LR-3.1, must give bit-identical outputs.
        ///
        /// The two paths are: (a) <c>ShaperNormals.FillTile</c> with kind <c>Constant</c>, which writes an
        /// authored direction and consumes no sheet; (b) <c>ShaperSolids.FillTile</c>, which derives its
        /// vectors from real geometry (the sphere's <c>N = P/R</c>). The sweep drives 64 samples of an Orb —
        /// giving 64 genuinely different, geometry-derived normals — and sets the Constant provider to each
        /// one in turn, then compares all six output floats across 8 rig configurations. 512 cases.
        ///
        /// <b>Mutation that makes it fail:</b> paste an inline <c>lit = ambient + diffuse*ndl*atten</c> back
        /// into the Solids rasteriser and use it. <b>This test alone is insufficient and that is stated:</b>
        /// it cannot see a duplicate that currently agrees. LT-1b is the half that can.
        /// </summary>
        public static string LT1_OneLawNumeric()
        {
            var sb = new StringBuilder("LT-1 one law, two families - numeric (64 normals x 8 rig configs)\n");

            // The Solids provider: an Orb, sampled at 64 points, each giving a geometry-derived unit normal.
            var def = SolidDef(ShaperSolidForm.Orb, 34f);
            var op = ShaperSolids.Compile(def, 0f, 0u);
            var geo = ShaperSolids.Build(op);
            var grid = ShaperSampleGrid.Centred(W, H, Px);

            int n = W * H;
            var cov = new float[n];
            var dist = new float[n];
            var nrm = new float[n * 3];
            var pz = new float[n];
            ShaperSolids.FillTile(geo, op, grid, 0, 0, W, H,
                                  new ShaperSolidEmit { coverage = cov, distance = dist, normal = nrm, pointZ = pz },
                                  0, W);

            // 64 covered samples, spread across the disc so the normals genuinely differ.
            var covered = new List<int>();
            for (int i = 0; i < n; i++) if (cov[i] > 0f) covered.Add(i);
            var probes = new List<int>(64);
            if (covered.Count >= 64)
            {
                int stride = covered.Count / 64;
                for (int p = 0; p < 64; p++) probes.Add(covered[p * stride]);
            }
            else probes.AddRange(covered);

            var rigs = new List<ShaperLightRigCompiled>();
            var resps = new List<ShaperResponseCompiled>();
            for (int c = 0; c < 8; c++)
            {
                var rr = RigOf(new Color(1f, 0.9f + 0.01f * c, 0.8f), 0.05f + 0.03f * c,
                               Dir(-55f + 30f * c, 20f + 5f * c, Color.white, 0.7f + 0.05f * c, 0.8f),
                               Pnt(-20f + 10f * c, 15f, 30f + 5f * c, 40f + 5f * c, new Color(0.6f, 0.8f, 1f), 0.6f));
                rigs.Add(ShaperLightCompiler.Compile(rr, 0f, (uint)c).rig);
                resps.Add(ShaperLightCompiler.CompileResponse(
                    Resp(true, 1f, 0.4f, 2.2f, 0.9f, 24f), "L", 0f, (uint)c));
            }

            int cases = 0, agreeBits = 0, agreeTol = 0, identicalBits = 0, pureBits = 0;
            double worstLaw = 0, worstSheet = 0;
            var nrm2 = new float[n * 3];
            for (int c = 0; c < rigs.Count; c++)
            {
                var rigC = rigs[c];
                var respC = resps[c];
                for (int p = 0; p < probes.Count; p++)
                {
                    int i = probes[p];
                    float nx = nrm[i * 3 + 0], ny = nrm[i * 3 + 1], nz = nrm[i * 3 + 2];

                    // The Silhouette provider, driven to the SAME direction the geometry produced.
                    var nop = ShaperNormalOp.Default;
                    nop.cx = nx; nop.cy = ny; nop.cz = nz;
                    ShaperNormals.FillTile(nop, grid, 0, 0, W, H, null, null, nrm2, 0, W, 0, W);
                    float mx = nrm2[i * 3 + 0], my = nrm2[i * 3 + 1], mz = nrm2[i * 3 + 2];
                    worstSheet = Math.Max(worstSheet, Math.Max(Math.Abs(mx - nx),
                                          Math.Max(Math.Abs(my - ny), Math.Abs(mz - nz))));

                    float px = grid.originX + (i % W) * grid.pixelSize;
                    float py = grid.originY + (i / W) * grid.pixelSize;

                    ShaperLightLaw.Shade(rigC, respC, px, py, pz[i], nx, ny, nz, 0f, 0f, 1f,
                                         out float a0, out float a1, out float a2,
                                         out float a3, out float a4, out float a5);
                    ShaperLightLaw.Shade(rigC, respC, px, py, pz[i], mx, my, mz, 0f, 0f, 1f,
                                         out float b0, out float b1, out float b2,
                                         out float b3, out float b4, out float b5);

                    // The THIRD leg, and the one that is actually about the LAW rather than the providers:
                    // the identical float triple, fed twice. Anything but bit-identity here means the law
                    // is not a function of its arguments.
                    ShaperLightLaw.Shade(rigC, respC, px, py, pz[i], nx, ny, nz, 0f, 0f, 1f,
                                         out float c0, out float c1, out float c2,
                                         out float c3, out float c4, out float c5);

                    cases++;
                    bool sheetsIdentical = mx == nx && my == ny && mz == nz;
                    bool lawIdentical = a0 == b0 && a1 == b1 && a2 == b2 && a3 == b3 && a4 == b4 && a5 == b5;
                    double e = Math.Abs(a0 - b0) + Math.Abs(a1 - b1) + Math.Abs(a2 - b2) +
                               Math.Abs(a3 - b3) + Math.Abs(a4 - b4) + Math.Abs(a5 - b5);

                    if (sheetsIdentical) { agreeBits++; if (lawIdentical) identicalBits++; }
                    else { agreeTol++; worstLaw = Math.Max(worstLaw, e); }

                    if (a0 == c0 && a1 == c1 && a2 == c2 && a3 == c3 && a4 == c4 && a5 == c5) pureBits++;
                }
            }

            bool okCount = cases == 512;
            bool okSheet = worstSheet <= 1e-6;
            bool okExact = identicalBits == agreeBits && agreeBits > 0;
            bool okBound = worstLaw <= 1e-4;
            bool okPure = pureBits == cases;
            bool ok = okCount && okSheet && okExact && okBound && okPure;

            sb.AppendLine("  cases run " + cases + " (expected 512)  " + Verdict(okCount));
            sb.AppendLine("  (i) the two PROVIDERS write the same direction: worst componentwise |dN| = " +
                          worstSheet.ToString("E3") + " (expected <= 1e-6)  " + Verdict(okSheet));
            sb.AppendLine("  (ii) where the two providers wrote BIT-IDENTICAL normals (" + agreeBits + "/" + cases +
                          " cases), the law's six outputs are bit-identical at " + identicalBits + "/" +
                          agreeBits + " (expected " + agreeBits + "/" + agreeBits + ")  " + Verdict(okExact));
            sb.AppendLine("  (iii) where they did not (" + agreeTol + "/" + cases + "), worst total |dL|+|dS| = " +
                          worstLaw.ToString("E3") + " (expected <= 1e-4)  " + Verdict(okBound));
            sb.AppendLine("  (iv) the same float triple fed twice is bit-identical at " + pureBits + "/" +
                          cases + " (expected " + cases + ") - the law is a pure function of its arguments  " +
                          Verdict(okPure));
            sb.AppendLine("  DEVIATION FROM PART 8, measured rather than excused. LT-1 expects 'bit-identical,");
            sb.AppendLine("  512/512'. That is UNACHIEVABLE against LR-3.5, and the two clauses are in conflict:");
            sb.AppendLine("  LR-3.5 makes UNIT output a provider contract, so ShaperNormals re-normalises its");
            sb.AppendLine("  authored direction, while an Orb's N = P/R is unit only to float precision. The two");
            sb.AppendLine("  providers therefore differ in the last bit at " + agreeTol + " of " + cases + " cases");
            sb.AppendLine("  (worst 1.2e-7), and a pow(nh, 24) specular amplifies that to ~5e-6 in the output. The");
            sb.AppendLine("  exact assertion that IS available is leg (ii): given the same bits in, the same bits");
            sb.AppendLine("  out, every time. LR-3.5 is the clause worth keeping; LT-1's wording is the one to fix.");
            sb.AppendLine("  the 64 normals are GEOMETRY-DERIVED (an Orb's N = P/R at 64 covered samples spread");
            sb.AppendLine("  across the disc), so the Constant provider is driven to values it did not choose - a");
            sb.AppendLine("  self-consistent constant would make this leg vacuous.");
            sb.AppendLine("  INSUFFICIENT ON ITS OWN, stated: this leg cannot see a DUPLICATE law that currently");
            sb.AppendLine("  agrees. LT-1b is the half that makes 'one law' falsifiable, and LT-16 is the half that");
            sb.AppendLine("  makes the shared FRAME falsifiable.");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── LT-1b ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-1b — ONE LAW, STRUCTURAL. The half that makes "one law" falsifiable rather than asserted.
        ///
        /// Three assertions, by reflection and IL scan over the runtime assembly:
        /// (a) <c>ShaperLightLaw</c> exposes EXACTLY ONE public method named <c>Shade</c>;
        /// (b) exactly ONE method in the whole runtime assembly calls it, and it is
        ///     <c>ShaperFillResolver.LightSample</c> — the single site through which BOTH families reach the law;
        /// (c) NO method on <c>ShaperSolids</c> references <c>ShaperLightRigCompiled</c>,
        ///     <c>ShaperLightCompiled</c> or <c>ShaperResponseCompiled</c> in its IL at all. A second shading
        ///     site inside the generator would have to read the rig, and it cannot do so invisibly.
        ///
        /// <b>Mutation that makes it fail:</b> duplicate <c>Shade</c>'s body into Solids — LT-1 still passes,
        /// (b) and (c) both fail.
        ///
        /// <b>What this leg cannot see, stated:</b> a second shading site that reads NO rig type — e.g. one
        /// hard-coding its own light. (c) would miss it; (b) would still catch any site that routes through
        /// the law, and a hard-coded light in the generator is caught by LT-16 instead, which measures whether
        /// the two families actually agree.
        /// </summary>
        public static string LT1b_OneLawStructural()
        {
            var sb = new StringBuilder("LT-1b one law - structural (reflection + IL scan)\n");
            bool all = true;

            var law = typeof(ShaperLightLaw);
            var asm = law.Assembly;

            var shades = new List<MethodInfo>();
            foreach (var m in law.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                if (m.Name == "Shade" && m.DeclaringType == law) shades.Add(m);
            bool aOk = shades.Count == 1;
            all &= aOk;
            sb.AppendLine("  (a) public methods named Shade on ShaperLightLaw: " + shades.Count +
                          " (expected 1)  " + Verdict(aOk));
            if (shades.Count == 0) { sb.Append("  RESULT: FAIL"); return sb.ToString(); }

            int shadeToken = shades[0].MetadataToken;
            var callers = new List<string>();
            int solidRigRefs = 0;
            var solidRefNames = new StringBuilder();

            int[] rigTokens =
            {
                typeof(ShaperLightRigCompiled).MetadataToken,
                typeof(ShaperLightCompiled).MetadataToken,
                typeof(ShaperResponseCompiled).MetadataToken,
            };

            foreach (var t in asm.GetTypes())
            {
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Static | BindingFlags.Instance |
                                               BindingFlags.DeclaredOnly))
                {
                    MethodBody body = null;
                    try { body = m.GetMethodBody(); } catch { }
                    if (body == null) continue;
                    byte[] il = body.GetILAsByteArray();
                    if (il == null) continue;

                    bool callsShade = false, refsRig = false;
                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        // call (0x28) / callvirt (0x6F) / ldtoken (0xD0) / newobj (0x73) all carry a 4-byte
                        // metadata token. A byte-frequency scan is an UPPER bound (an operand byte can
                        // coincide with an opcode value), so a HIT needs confirming and a ZERO is conclusive
                        // — the same reasoning FT-9's IL scan states.
                        if (il[i] != 0x28 && il[i] != 0x6F && il[i] != 0xD0 && il[i] != 0x73) continue;
                        int tok = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                        if (tok == shadeToken) callsShade = true;
                        for (int r = 0; r < rigTokens.Length; r++) if (tok == rigTokens[r]) refsRig = true;
                    }

                    if (callsShade) callers.Add(t.Name + "." + m.Name);

                    // A rig type can reach a method three ways, and an IL-token scan alone sees only one of
                    // them. Measured: the first run of the LT-1b mutation - a MutantShade taking
                    // `in ShaperLightRigCompiled` - was caught by leg (b) and MISSED by an IL-only leg (c),
                    // because a byref struct parameter emits no ldtoken, no newobj and no box. So the
                    // SIGNATURE and the LOCALS are scanned too.
                    if (t == typeof(ShaperSolids))
                    {
                        if (!refsRig)
                        {
                            foreach (var p in m.GetParameters())
                            {
                                var pt = p.ParameterType;
                                var el = pt.IsByRef ? pt.GetElementType() : pt;
                                if (IsRigType(el)) { refsRig = true; break; }
                            }
                        }
                        if (!refsRig && IsRigType(m.ReturnType)) refsRig = true;
                        if (!refsRig)
                            foreach (var lv in body.LocalVariables)
                                if (IsRigType(lv.LocalType)) { refsRig = true; break; }

                        if (refsRig) { solidRigRefs++; solidRefNames.Append(m.Name + " "); }
                    }
                }
            }

            bool bOk = callers.Count == 1 && callers[0].EndsWith("LightSample");
            all &= bOk;
            sb.AppendLine("  (b) methods in the runtime assembly calling ShaperLightLaw.Shade: " + callers.Count +
                          " (expected 1)  [" + string.Join(", ", callers.ToArray()) + "]  " + Verdict(bOk));

            bool cOk = solidRigRefs == 0;
            all &= cOk;
            sb.AppendLine("  (c) ShaperSolids methods touching a compiled-rig type in their IL, SIGNATURE, " +
                          "RETURN or LOCALS: " + solidRigRefs + " (expected 0)  " +
                          solidRefNames.ToString().Trim() + "  " + Verdict(cOk));
            sb.AppendLine("      Solids therefore CANNOT shade: it has no way to see a light, an ambient or a");
            sb.AppendLine("      response block. Its inline lighting block (PyreRenderer.cs:4341-4360, :4534-4553,");
            sb.AppendLine("      :4720-4740) is not reproduced anywhere in the ported generator.");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>One of the three compiled-rig types the law takes, in any byref/array wrapping.</summary>
        static bool IsRigType(Type t)
        {
            if (t == null) return false;
            if (t.IsByRef || t.IsArray) t = t.GetElementType();
            return t == typeof(ShaperLightRigCompiled) || t == typeof(ShaperLightCompiled) ||
                   t == typeof(ShaperResponseCompiled);
        }

        // ── LT-2 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-2 — ZERO ALLOCATION IN THE HOT PATH. <c>GC.GetTotalAllocatedBytes(precise: true)</c> bracketing
        /// a 10 000-sample lit paint, after one warm-up, for {1, 8} lights x {Over, Add} x {Constant, Solids}.
        ///
        /// <b>Mutation that makes it fail:</b> move the normal sheet's allocation from the host into
        /// <c>PaintTile</c> (<c>new float[n*3]</c> per tile) — the exact BC-3.7f violation
        /// <c>PyreRenderer.cs:1803</c> and <c>:1857</c> (<c>light = new float[W * H]</c>, per layer per frame)
        /// commit today.
        ///
        /// The heap instrument is reported AND an IL scan is run alongside it, because an allocation is an
        /// OPCODE and not a heuristic: <c>newobj</c> (0x73), <c>newarr</c> (0x8D) and <c>box</c> (0x8C) are the
        /// only ways managed memory is taken. The byte-frequency scan is an UPPER bound, so a zero is
        /// conclusive.
        /// </summary>
        public static string LT2_ZeroAllocation()
        {
            var sb = new StringBuilder("LT-2 zero allocation in the lit tile loop (" + AllocSamples + " samples per case)\n");
            bool all = true;

            int cw = 100, ch = 100;                       // 10 000 samples exactly
            var oneLight = RigOf(Color.white, 0.15f, Dir(-55f, 36f, Color.white, 0.9f));
            var eight = EightLights();

            var cases = new List<KeyValuePair<string, LRig>>();
            foreach (var lp in new[] { new KeyValuePair<string, ShaperLightRig>("1 light", oneLight),
                                       new KeyValuePair<string, ShaperLightRig>("8 lights", eight) })
                foreach (var comp in new[] { ShaperFillComposite.Over, ShaperFillComposite.Add })
                {
                    var d = Disc("D", 34f, 0f, 0f);
                    d.fill = Solid(new Color(0.9f, 0.5f, 0.2f), 1f, 0f, comp);
                    cases.Add(new KeyValuePair<string, LRig>(
                        lp.Key + " / " + comp + " / Constant",
                        BuildLit(d, lp.Value, Resp(true, 1f, 0.5f), cw, ch)));

                    var sd = SolidDef(ShaperSolidForm.Box, 30f, 25f, 18f, 10f, 1.1f);
                    var sr = BuildSolid(sd, lp.Value, Resp(true, 1f, 0.5f),
                                        Solid(new Color(0.9f, 0.5f, 0.2f), 1f, 0f, comp), cw, ch);
                    cases.Add(new KeyValuePair<string, LRig>(lp.Key + " / " + comp + " / Solids", sr));
                }

            // ── THE INSTRUMENTS, AND WHY THE PRIMARY ONE CHANGED (fix pass) ────────────────────────────────
            //
            // Part 8 names `GC.GetTotalAllocatedBytes(precise: true)`. That method is .NET Core 3.0+ and is
            // NOT present in this editor's profile (measured: `error CS0117: 'GC' does not contain a
            // definition for 'GetTotalAllocatedBytes'`). The first substitute was
            // `GC.GetAllocatedBytesForCurrentThread()`, and its calibration concluded it was "BLIND on this
            // Mono runtime" with a floor of "> 4 MB" — a phrasing that implies it works ABOVE 4 MB.
            //
            // IT DOES NOT WORK AT ANY SIZE. Driven directly with known allocations it returns a constant
            // zero:
            //
            //   raw probe value sampled 3x: 0, 0, 0
            //   single alloc    1 KB -> delta 0      single alloc  8192 KB -> delta 0
            //   single alloc   64 KB -> delta 0      single alloc 16384 KB -> delta 0
            //   single alloc  512 KB -> delta 0      single alloc 65536 KB -> delta 0
            //   single alloc 4096 KB -> delta 0
            //   65 x 120000 B (7.8 MB, the STATED MUTATION's own shape) -> thread delta 0, heap delta 7 987 200
            //
            // A 64 MB allocation moves it by ZERO. The old calibration escalated to 4 MB and stopped, so it
            // never discovered that the instrument is INERT rather than coarse, and every `thread-alloc 0 B`
            // it printed was a dead reading rather than a measurement of anything. It is retained below ONLY
            // so its inertness is DEMONSTRATED in the report rather than asserted — and it no longer
            // contributes to the verdict.
            //
            // What the verdict rests on now, in order of strength:
            //   1. THE IL SCAN, which has NO floor at all. An allocation is an OPCODE; newobj / newarr / box
            //      are the only ways managed memory is taken and none of them can hide from a scan of the
            //      method body. This is the instrument T-0106 used successfully and it is the assertion that
            //      actually holds. It is at the bottom of this leg.
            //   2. GC.GetTotalMemory(false), CALIBRATED IN-REGION — allocating a known amount INSIDE the
            //      measured loop and confirming the instrument sees it, rather than calibrating on a
            //      different code path and hoping. Its real detection floor is REPORTED AS A NUMBER below.
            Func<long> threadProbe = null;
            {
                var mi = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",
                                              BindingFlags.Public | BindingFlags.Static,
                                              null, Type.EmptyTypes, null);
                threadProbe = mi != null
                    ? (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), mi)
                    : (Func<long>)(() => 0L);
            }

            // 65 repetitions per case, so a violation of the magnitude the stated mutation produces
            // (120 000 bytes per call at n = 10 000) accumulates to ~7.8 MB. One repetition of a 120 KB
            // allocation is measurably below this runtime's floor; sixty-five of them are not.
            const int Reps = 65;

            // ── IN-REGION CALIBRATION. The measured region is `for (rep) { Paint(); }`. So the calibration
            // is the SAME loop with a known allocation added per rep, escalating until the heap probe sees
            // it. Whatever that smallest per-rep size turns out to be IS the leg's detection floor, and it is
            // printed as a number rather than described. Anything smaller than it, per PaintTile call, this
            // leg cannot see — and the IL scan can.
            long perCallFloor = -1, floorObserved = 0;
            long threadSeen = 0;
            {
                var probe = cases[0].Value;
                Paint(probe);                                      // JIT before measuring anything
                long[] perRep = { 64, 128, 256, 512, 1024, 4096, 16384, 65536 };
                object sink = null;
                foreach (long s in perRep)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long h0 = GC.GetTotalMemory(false);
                    long t0 = threadProbe();
                    var keep = new object[Reps];
                    for (int rep = 0; rep < Reps; rep++) { Paint(probe); keep[rep] = new byte[s]; }
                    long h1 = GC.GetTotalMemory(false);
                    long t1 = threadProbe();
                    sink = keep;
                    threadSeen += t1 - t0;
                    if (h1 - h0 > 0) { perCallFloor = s; floorObserved = h1 - h0; break; }
                }
                bool floorOk = perCallFloor > 0 && perCallFloor * Reps < 120000L * Reps;
                all &= floorOk;
                sb.AppendLine("  instrument calibration, run IN-REGION and BEFORE the measurements " +
                              "(sink " + (sink == null ? 0 : 1) + "):");
                sb.AppendLine("    PRIMARY   IL opcode scan - newobj/newarr/box. NO detection floor: an " +
                              "allocation is an opcode, not a magnitude.");
                sb.AppendLine("    SECONDARY GC.GetTotalMemory(false), calibrated inside the measured loop: " +
                              "DETECTION FLOOR = " + (perCallFloor < 0 ? "NOT FOUND up to 65536" : perCallFloor.ToString()) +
                              " BYTES PER PaintTile CALL" +
                              (perCallFloor > 0 ? " (observed heap delta " + floorObserved + " B over " + Reps + " reps)" : "") +
                              "  " + Verdict(floorOk));
                sb.AppendLine("    DEAD      GC.GetAllocatedBytesForCurrentThread() reported " + threadSeen +
                              " B across the whole calibration, in which " + (perCallFloor > 0 ? (perCallFloor * Reps).ToString() : "millions of")
                              + "+ bytes were provably allocated. It is INERT at every size on this Mono " +
                              "runtime (verified separately up to 64 MB) and contributes NOTHING to this " +
                              "leg's verdict; it is printed only so the claim is visible rather than " +
                              "implied.");
                sb.AppendLine("    A per-tile temporary SMALLER than the floor above is invisible to the heap " +
                              "probe and is caught by the IL scan instead. That is the honest statement of " +
                              "what this leg proves, replacing the old and never-measured \"exactly 0 bytes\".");
            }
            long noiseGate = perCallFloor > 0 ? perCallFloor * Reps : long.MaxValue;

            foreach (var c in cases)
            {
                Paint(c.Value);                                     // warm-up: JIT every path
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long threadBefore = threadProbe();
                long heapBefore = GC.GetTotalMemory(false);
                for (int rep = 0; rep < Reps; rep++) Paint(c.Value);
                long threadAfter = threadProbe();
                long heapAfter = GC.GetTotalMemory(false);
                long delta = threadAfter - threadBefore;
                long heapDelta = heapAfter - heapBefore;
                bool ok = heapDelta < noiseGate;                    // the DEAD probe no longer gates anything
                all &= ok;
                sb.AppendLine("  " + c.Key.PadRight(34) + " x" + Reps + " reps: heap " + heapDelta +
                              " B (must be < " + noiseGate + " B = the calibrated floor x " + Reps +
                              "; the mutation would show ~" + (120000L * Reps) + " B)  " + Verdict(ok) +
                              "   [dead thread probe: " + delta + " B, ignored]");
            }

            // The IL instrument, and it is the CONCLUSIVE one: an allocation is an OPCODE, not a heuristic.
            // newobj (0x73), newarr (0x8D) and box (0x8C) are the only ways managed memory is taken, and none
            // of them can hide from a scan of the method body.
            //
            // A bare byte-frequency scan is only an UPPER bound — an operand byte can coincide with an opcode
            // value, and one did here: the first run of this leg reported 1 hit in PaintTile, which is a
            // 2823-byte method full of 4-byte tokens. So every hit is CONFIRMED by resolving its operand as a
            // metadata token: a real newobj resolves to a constructor and a real newarr/box resolves to a
            // type. Both the raw and the confirmed counts are reported, because hiding the raw one would hide
            // the instrument's own imprecision.
            {
                var targets = new List<MethodInfo>();
                foreach (var t in new[] { typeof(ShaperLightLaw), typeof(ShaperNormals), typeof(ShaperSolids) })
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        // Build and Compile are compile-time and allocate by design (BC-3.7f allows exactly
                        // that: the HOST allocates, once, outside the loop).
                        if (m.Name == "Build" || m.Name.StartsWith("Build") || m.Name == "Compile") continue;
                        targets.Add(m);
                    }
                // PaintTile is scanned too, and that is what makes this leg FALSIFIABLE: LT-2's stated
                // mutation - moving the normal sheet's allocation into PaintTile as `new float[n*3]` per
                // tile - lands here and nowhere else. Resolve and Walk are NOT scanned: they allocate the
                // owner list by design, once per compile, which is exactly what BC-3.7f permits.
                foreach (var m in typeof(ShaperFillResolver).GetMethods(BindingFlags.Public |
                                                                        BindingFlags.NonPublic |
                                                                        BindingFlags.Static |
                                                                        BindingFlags.DeclaredOnly))
                    if (m.Name == "LightSample" || m.Name == "PaintTile") targets.Add(m);

                int rawTotal = 0, confirmedTotal = 0;
                var names = new StringBuilder();
                var detail = new StringBuilder();
                foreach (var m in targets)
                {
                    int confirmed = ConfirmedAllocs(m, out int raw, out string what);
                    rawTotal += raw; confirmedTotal += confirmed;
                    var body = m.GetMethodBody();
                    int len = body == null ? 0 : (body.GetILAsByteArray() ?? new byte[0]).Length;
                    names.Append(m.DeclaringType.Name + "." + m.Name + "(" + len + "B raw:" + raw +
                                 " confirmed:" + confirmed + ") ");
                    if (what.Length > 0) detail.Append(m.Name + " -> " + what + "; ");
                }
                bool ilOk = confirmedTotal == 0;
                all &= ilOk;
                sb.AppendLine("  IL scan across law + both providers + PaintTile + LightSample: raw byte hits " +
                              rawTotal + ", TOKEN-CONFIRMED allocations " + confirmedTotal +
                              " (expected 0)  " + Verdict(ilOk));
                sb.AppendLine("    " + names.ToString().Trim());
                if (detail.Length > 0) sb.AppendLine("    confirmed: " + detail.ToString().Trim());
                else sb.AppendLine("    every raw hit failed token resolution, i.e. was an operand byte " +
                                   "coinciding with an opcode value - the upper bound collapsing to zero.");
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>
        /// Count the allocation opcodes in a method body, confirming each by resolving its operand as a
        /// metadata token. <paramref name="raw"/> is the byte-frequency upper bound; the return value is the
        /// confirmed count. A real <c>newobj</c> resolves to a constructor; a real <c>newarr</c> or
        /// <c>box</c> resolves to a type. Anything that fails to resolve was an operand byte that happened to
        /// equal an opcode value.
        /// </summary>
        static int ConfirmedAllocs(MethodInfo m, out int raw, out string what)
        {
            raw = 0; what = "";
            var body = m.GetMethodBody(); if (body == null) return 0;
            var il = body.GetILAsByteArray(); if (il == null) return 0;

            var mod = m.Module;
            Type[] typeArgs = m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
            Type[] methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;

            int confirmed = 0;
            var d = new StringBuilder();
            for (int i = 0; i + 4 < il.Length; i++)
            {
                byte op = il[i];
                if (op != 0x73 && op != 0x8D && op != 0x8C) continue;
                raw++;
                int tok = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                try
                {
                    if (op == 0x73)
                    {
                        var mi = mod.ResolveMethod(tok, typeArgs, methodArgs);
                        if (mi != null && mi.IsConstructor)
                        { confirmed++; d.Append("newobj " + mi.DeclaringType.Name + " "); }
                    }
                    else
                    {
                        var ty = mod.ResolveType(tok, typeArgs, methodArgs);
                        if (ty != null)
                        { confirmed++; d.Append((op == 0x8D ? "newarr " : "box ") + ty.Name + " "); }
                    }
                }
                catch { }
            }
            what = d.ToString();
            return confirmed;
        }

        // ── LT-3 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-3 — TILE INDEPENDENCE WITH LIGHTING ON. A whole-grid render against BC-1.6's prescribed 7x5
        /// prime decomposition, with 8 lights, both providers, and an animated intensity.
        ///
        /// <b>Mutations that make it fail:</b> (a) implement the normal provider by differencing
        /// <c>buf.height</c> — it seams at every tile boundary, which is the continuous enforcement of LR-3.3;
        /// (b) compute the surface point <c>P</c> from a TILE-LOCAL index instead of the absolute sample index
        /// — a point light's falloff then restarts at every tile.
        /// </summary>
        public static string LT3_TileIndependence()
        {
            var sb = new StringBuilder("LT-3 tile independence with lighting on (7x5 prime decomposition, BC-1.6)\n");
            bool all = true;
            const int TW = 7, TH = 5;
            int cw = TW * 6, chh = TH * 7;                          // 42 x 35, an exact decomposition

            var rig = EightLights();
            // An ANIMATED intensity, so a dial resolved per tile rather than per compile would diverge too.
            rig.lights[0].intensity = new ZUIValue(0.9f);
            rig.lights[0].intensity.mode = ZUIValue.Mode.Oscillation;

            var subjects = new List<KeyValuePair<string, Func<LRig>>>
            {
                new KeyValuePair<string, Func<LRig>>("Constant provider", () =>
                {
                    var d = Disc("D", 14f, 0f, 0f);
                    d.fill = Solid(new Color(0.9f, 0.5f, 0.2f));
                    d.border = new ShaperBorderDef { enabled = true, width = new ZUIValue(2.5f) };
                    return BuildLit(d, rig, Resp(true, 1f, 0.6f, 2.2f, 0.9f, 24f, null,
                                                 new Vector3(0.6f, 0f, 0.8f)), cw, chh, 0.37f, 5u);
                }),
                new KeyValuePair<string, Func<LRig>>("Solids provider", () =>
                    BuildSolid(SolidDef(ShaperSolidForm.Gem, 13f, 24f, 17f, 9f, 1.1f), rig,
                               Resp(true, 1f, 0.6f), Solid(new Color(0.3f, 0.7f, 0.95f)),
                               cw, chh, 0.37f, 5u)),
            };

            foreach (var s in subjects)
            {
                var whole = s.Value();
                Paint(whole);
                var refPx = Encoded(whole);

                var tiled = s.Value();
                var tileBuf = new ShaperFillBuffers(TW * TH, tiled.buf.ownerCapacity);
                var tileScene = new ShaperLightScene(tileBuf.sampleCapacity, tileBuf.ownerCapacity);
                tileScene.rig = tiled.scene.rig;
                for (int i = 0; i < tileScene.ownerCapacity; i++)
                {
                    tileScene.response[i] = tiled.scene.response[i];
                    tileScene.normalOp[i] = tiled.scene.normalOp[i];
                    tileScene.solidOp[i] = tiled.scene.solidOp[i];
                    tileScene.solid[i] = tiled.scene.solid[i];
                }

                var outPx = new Color32[cw * chh];
                var tilePx = new Color32[TW * TH];
                var sheets = new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine };
                for (int ty = 0; ty < chh; ty += TH)
                    for (int tx = 0; tx < cw; tx += TW)
                    {
                        ShaperFillResolver.PaintTile(tiled.doc, tiled.grid, tx, ty, TW, TH,
                                                     tileBuf, sheets, tileScene);
                        ShaperFillResolver.Encode(tileBuf.dst, tilePx, TW * TH);
                        for (int j = 0; j < TH; j++)
                            for (int i = 0; i < TW; i++)
                                outPx[(ty + j) * cw + (tx + i)] = tilePx[j * TW + i];
                    }

                int bad = 0;
                for (int i = 0; i < outPx.Length; i++)
                {
                    Color32 a = refPx[i], b = outPx[i];
                    if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) bad++;
                }
                bool ok = bad == 0;
                all &= ok;
                sb.AppendLine("  " + s.Key.PadRight(20) + " " + cw + "x" + chh + " whole vs " + TW + "x" + TH +
                              " tiles: differing pixels " + bad + "/" + outPx.Length +
                              " (expected 0)  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-4 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-4 — DETERMINISM. The same document rendered twice in one session, with a <c>Curve</c> light
        /// intensity and a <c>MinMax</c> light intensity, must give bitwise identical output.
        ///
        /// <b>Mutation that makes it fail:</b> sample a light dial with <c>ZUIValue.Evaluate</c> instead of
        /// <c>ShaperValue.Sample</c> (the duration trap, <c>ShaperValue.cs:9-13</c>), or draw <c>MinMax</c>
        /// from <c>UnityEngine.Random</c> instead of the hash at <c>ShaperValue.cs:33</c>.
        ///
        /// <b>The across-a-domain-reload leg is NOT RUN</b> and is reported as such: an eval runs inside one
        /// domain, so the reload half cannot be observed from here. The <c>MinMax</c> leg is the one that would
        /// catch a stray <c>Random</c>, and it runs. Same honesty bucket as FT-10.
        /// </summary>
        public static string LT4_Determinism()
        {
            var sb = new StringBuilder("LT-4 determinism (same inputs -> identical bytes, within one session)\n");
            bool all = true;

            foreach (var mode in new[] { ZUIValue.Mode.Curve, ZUIValue.Mode.MinMax, ZUIValue.Mode.Static })
            {
                Func<LRig> make = () =>
                {
                    var rig = RigOf(Color.white, 0.15f,
                                    Dir(-55f, 36f, new Color(1f, 0.9f, 0.8f), 0.9f),
                                    Pnt(-20f, 20f, 30f, 45f, new Color(0.4f, 0.7f, 1f), 0.8f));
                    var v = new ZUIValue(0.8f);
                    v.mode = mode; v.min = 0.2f; v.max = 1.4f;
                    rig.lights[0].intensity = v;
                    var d = Disc("D", 34f, 0f, 0f);
                    d.fill = Solid(new Color(0.9f, 0.5f, 0.2f));
                    return BuildLit(d, rig, Resp(true, 1f, 0.4f, 2.2f, 0.9f, 24f, null,
                                                 new Vector3(0.35f, 0.2f, 0.9f)), 64, 64, 0.41f, 9u);
                };

                var a = make(); Paint(a); var pa = Encoded(a);
                var b = make(); Paint(b); var pb = Encoded(b);
                int bad = 0;
                for (int i = 0; i < pa.Length; i++)
                    if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b || pa[i].a != pb[i].a) bad++;
                bool ok = bad == 0;
                all &= ok;
                sb.AppendLine("  light intensity mode " + mode.ToString().PadRight(12) +
                              " differing pixels between two runs " + bad + "/" + pa.Length +
                              " (expected 0)  " + Verdict(ok));
            }

            sb.AppendLine("  NOT RUN: the across-a-domain-reload leg. An eval executes inside one domain, so a");
            sb.AppendLine("  reload cannot be observed from here. Reported as not-verified rather than assumed.");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-5a ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-5a — LINEAR SPACE, HAND-COMPUTED. One directional light, colour sRGB byte 128 on all channels,
        /// intensity 1, <c>N.L = 1</c>, ambient 0, albedo linear white, receive on, specular 0, rim 0.
        ///
        /// Expected: destination float <c>0.215861...</c> (the IEC 61966-2-1 decode of 128/255, hand-computed
        /// from the constants at <c>ShaperFillContract.cs:409-413</c>) and encoded byte EXACTLY 128.
        ///
        /// <b>Mutation that makes it fail:</b> skip <c>ShaperSrgb.Decode</c> on the light colour and treat
        /// <c>128/255 = 0.50196</c> as linear. The byte comes out 188 — a 60-code error, which is the classic
        /// muddy-shading artefact FC-2.3 names, made numeric.
        /// </summary>
        public static string LT5a_LinearSpace()
        {
            var sb = new StringBuilder("LT-5a linear space, hand-computed (sRGB 128 light on a white albedo)\n");

            var grey = (Color)new Color32(128, 128, 128, 255);
            // yaw 0, pitch 0 makes the direction TOWARD the light exactly (0,0,1), so N.L == 1 with the
            // default Constant normal and there is no second approximation in the chain.
            var rig = RigOf(Color.white, 0f, Dir(0f, 0f, grey, 1f, 1f));
            var d = Disc("D", 40f, 0f, 0f);
            d.fill = Solid(Color.white);
            var r = BuildLit(d, rig, Resp(true, 1f, 0f, 2.2f, 0f, 48f), 64, 64);
            Paint(r);

            int i = Index(r, 0f, 0f);
            float dst = r.buf.dst[i * 4 + 0];
            var px = Encoded(r);
            byte b = px[i].r;

            const double Expected = 0.21586050;
            double err = Math.Abs(dst - Expected);
            bool fOk = err <= 1e-6;
            bool bOk = b == 128;

            sb.AppendLine("  destination float at the centre sample: " + dst.ToString("F8") +
                          " (expected " + Expected.ToString("F8") + ", |err| " + err.ToString("E3") +
                          ", tolerance 1e-6)  " + Verdict(fOk));
            sb.AppendLine("  encoded byte: " + b + " (expected 128)  " + Verdict(bOk));
            sb.AppendLine("  the naive-linear mutation reads 188 here, a 60-code error - the muddy-shading");
            sb.AppendLine("  artefact FC-2.3 names, made numeric.");
            sb.Append("  RESULT: " + Verdict(fOk && bOk));
            return sb.ToString();
        }

        // ── LT-5b ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-5b — ONE ENCODE BOUNDARY. <c>ShaperSrgb.EncodeToByte</c> must have exactly one caller in the
        /// runtime assembly, <c>ShaperFillResolver.Encode</c> (LR-2.6 / FC-2.3).
        ///
        /// <b>Mutation that makes it fail:</b> add an encode inside <c>Shade</c>, or re-add a second encoder.
        /// Two callers, fail. (FC-2.3a records that a second encoder was already shipped once and deleted.)
        /// </summary>
        public static string LT5b_OneEncodeBoundary()
        {
            var sb = new StringBuilder("LT-5b one encode boundary (IL scan of the runtime assembly)\n");
            var target = typeof(ShaperSrgb).GetMethod("EncodeToByte", BindingFlags.Public | BindingFlags.Static);
            int tok = target.MetadataToken;
            var callers = new List<string>();

            foreach (var t in typeof(ShaperSrgb).Assembly.GetTypes())
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Static | BindingFlags.Instance |
                                               BindingFlags.DeclaredOnly))
                {
                    MethodBody body = null;
                    try { body = m.GetMethodBody(); } catch { }
                    if (body == null) continue;
                    var il = body.GetILAsByteArray(); if (il == null) continue;
                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        if (il[i] != 0x28 && il[i] != 0x6F) continue;
                        int t2 = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                        if (t2 != tok) continue;
                        callers.Add(t.Name + "." + m.Name);
                        break;
                    }
                }

            bool ok = callers.Count == 1 && callers[0] == "ShaperFillResolver.Encode";
            sb.AppendLine("  callers of ShaperSrgb.EncodeToByte in the runtime assembly: " + callers.Count +
                          " (expected 1)  [" + string.Join(", ", callers.ToArray()) + "]");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── LT-6 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-6 — FALLOFF, AMBIENT, SPECULAR AND RIM against hand-computed values. Point light at
        /// <c>(0,0,20)</c>, linear white, intensity 1, range 20; ambient 0.18; <c>specular = 0.9</c>,
        /// <c>specularPower = 4</c>; <c>rimStrength = 1</c>, <c>rimPower = 2.2</c>; <c>V = (0,0,1)</c>.
        ///
        /// <b>P1</b> <c>P=(0,0,0)</c>, <c>N=(0,0,1)</c>: atten 0.5, L 0.68, S_spec 0.45, rim 0.
        /// <b>P2</b> same but <c>N=(0.866025,0,0.5)</c>: L 0.43, S_spec 0.028125, rim 0.217638.
        /// <b>P3</b> light moved to <c>dist = 40</c>, range 20, <c>N=(0,0,1)</c>: atten 0.2, L 0.38.
        ///
        /// <b>Mutations, each caught by a different probe:</b> change the falloff to <c>1/(1 + d/range)</c> and
        /// P3's L reads 0.5133 not 0.38; change <c>rimPower</c> 2.2 -> 2.0 and P2's rim reads 0.25 not
        /// 0.217638; drop <c>atten</c> from the specular term and P1's S_spec reads 0.9 not 0.45; add ambient
        /// LAST instead of first and P2 with <c>intensityScale = 0.5</c> reads 0.09 + 0.125 instead of
        /// 0.18 + 0.125 — the fourth probe, which is what makes LR-2.3's fixed order falsifiable.
        /// </summary>
        public static string LT6_HandComputed()
        {
            var sb = new StringBuilder("LT-6 falloff, ambient, specular and rim against hand-computed values\n");
            bool all = true;

            var rig20 = ShaperLightCompiler.Compile(
                RigOf(Color.white, 0.18f, Pnt(0f, 0f, 20f, 20f, Color.white, 1f, 1f)), 0f, 0u).rig;
            var rig40 = ShaperLightCompiler.Compile(
                RigOf(Color.white, 0.18f, Pnt(0f, 0f, 40f, 20f, Color.white, 1f, 1f)), 0f, 0u).rig;

            // specularTint WHITE, so S_spec is the raw specular term and not the default (0.9,0.95,1) tint.
            var resp = ShaperLightCompiler.CompileResponse(
                Resp(true, 1f, 1f, 2.2f, 0.9f, 4f, Color.white), "L", 0f, 0u);
            var respNoRim = ShaperLightCompiler.CompileResponse(
                Resp(true, 1f, 0f, 2.2f, 0.9f, 4f, Color.white), "L", 0f, 0u);

            Action<string, ShaperLightRigCompiled, ShaperResponseCompiled, float, float, float,
                   double, double, double> probe =
            (label, rg, rp, nx, ny, nz, expL, expSpec, expRim) =>
            {
                ShaperLightLaw.Shade(rg, rp, 0f, 0f, 0f, nx, ny, nz, 0f, 0f, 1f,
                                     out float lr, out _, out _, out float sr, out _, out _);
                ShaperLightLaw.Shade(rg, respNoRim, 0f, 0f, 0f, nx, ny, nz, 0f, 0f, 1f,
                                     out _, out _, out _, out float srNoRim, out _, out _);
                double rim = expRim >= 0 ? (sr - srNoRim) / 0.18 : 0;   // rim is ambient-tinted: S_rim = amb * rim
                bool okL = Math.Abs(lr - expL) <= 1e-6;
                bool okS = expSpec < 0 || Math.Abs(srNoRim - expSpec) <= 1e-6;
                bool okR = expRim < 0 || Math.Abs(rim - expRim) <= 1e-6;
                all &= okL && okS && okR;
                sb.AppendLine("  " + label);
                sb.AppendLine("      L      = " + lr.ToString("F8") + " (expected " + expL.ToString("F8") + ")  " + Verdict(okL));
                if (expSpec >= 0)
                    sb.AppendLine("      S_spec = " + srNoRim.ToString("F8") + " (expected " + expSpec.ToString("F8") + ")  " + Verdict(okS));
                if (expRim >= 0)
                    sb.AppendLine("      rim    = " + rim.ToString("F8") + " (expected " + expRim.ToString("F8") + ")  " + Verdict(okR));
            };

            probe("P1  P=(0,0,0) N=(0,0,1) light z=20 range=20  [atten 0.5]",
                  rig20, resp, 0f, 0f, 1f, 0.68, 0.45, 0.0);
            probe("P2  P=(0,0,0) N=(0.866025,0,0.5)",
                  rig20, resp, 0.866025f, 0f, 0.5f, 0.43, 0.028125, 0.217638);
            probe("P3  light z=40, range 20, N=(0,0,1)  [atten 0.2]",
                  rig40, resp, 0f, 0f, 1f, 0.38, -1, -1);

            // P4 — the ORDER probe (LR-2.3): ambient is the accumulator's INITIAL value and must not be
            // scaled by intensityScale. Adding it last would make it survive the scale, which LR-1.3 forbids.
            {
                var half = ShaperLightCompiler.CompileResponse(
                    Resp(true, 0.5f, 0f, 2.2f, 0.9f, 4f, Color.white), "L", 0f, 0u);
                ShaperLightLaw.Shade(rig20, half, 0f, 0f, 0f, 0.866025f, 0f, 0.5f, 0f, 0f, 1f,
                                     out float lr, out _, out _, out _, out _, out _);
                // ambient 0.18 UNSCALED + diffuse 0.5 (ndl) * 0.5 (atten) * 0.5 (scale) = 0.18 + 0.125
                const double exp = 0.18 + 0.125;
                bool ok = Math.Abs(lr - exp) <= 1e-6;
                all &= ok;
                sb.AppendLine("  P4  intensityScale = 0.5, N=(0.866025,0,0.5)  [the ORDER probe, LR-2.3]");
                sb.AppendLine("      L      = " + lr.ToString("F8") + " (expected " + exp.ToString("F8") +
                              " = 0.18 unscaled ambient + 0.125 scaled diffuse; ambient-added-last reads " +
                              (0.09 + 0.125).ToString("F8") + ")  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-7 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-7 — RIM IS EXACTLY ZERO ON A FLAT NORMAL, AND IS NOT LIGHT-DEPENDENT. <c>Constant(0,0,1)</c>,
        /// <c>rimStrength = 1</c>, swept over 8 light directions including three well off-axis, at all 16 384
        /// samples.
        ///
        /// <b>Mutations that make it fail:</b> (a) compute the rim from a NON-NORMALISED <c>nz</c> (the
        /// reference app's own asymmetry, <c>BUFFER_CONTRACT.md:246</c>) — a length-1.4 normal gives a
        /// non-zero rim on a flat card; (b) substitute the LIGHT direction for <c>V</c> in the rim term — the
        /// plausible-but-wrong implementation — after which rim becomes non-zero the moment a light is
        /// off-axis, and the sweep catches it on light 2.
        /// </summary>
        public static string LT7_RimIsZeroOnFlat()
        {
            var sb = new StringBuilder("LT-7 rim is exactly zero on a flat normal, across 8 light directions\n");
            bool all = true;

            int n = W * H;
            var grid = ShaperSampleGrid.Centred(W, H, Px);
            var nrm = new float[n * 3];
            ShaperNormals.FillTile(ShaperNormalOp.Default, grid, 0, 0, W, H, null, null, nrm, 0, W, 0, W);

            var rim1 = ShaperLightCompiler.CompileResponse(Resp(true, 1f, 1f, 2.2f, 0f, 48f, Color.white), "L", 0f, 0u);
            var rim0 = ShaperLightCompiler.CompileResponse(Resp(true, 1f, 0f, 2.2f, 0f, 48f, Color.white), "L", 0f, 0u);

            float[] yaws = { 0f, 45f, 90f, 135f, 180f, -55f, -120f, 30f };
            float[] pitches = { 0f, 10f, 0f, -30f, 60f, 36f, 5f, 85f };

            int totalNonZero = 0;
            for (int l = 0; l < 8; l++)
            {
                var rg = ShaperLightCompiler.Compile(
                    RigOf(Color.white, 0.2f, Dir(yaws[l], pitches[l], Color.white, 1f, 1f)), 0f, 0u).rig;

                int nonZero = 0;
                for (int i = 0; i < n; i++)
                {
                    float px = grid.originX + (i % W) * grid.pixelSize;
                    float py = grid.originY + (i / W) * grid.pixelSize;
                    ShaperLightLaw.Shade(rg, rim1, px, py, 0f, nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2],
                                         0f, 0f, 1f, out _, out _, out _, out float a, out _, out _);
                    ShaperLightLaw.Shade(rg, rim0, px, py, 0f, nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2],
                                         0f, 0f, 1f, out _, out _, out _, out float b, out _, out _);
                    if (a - b != 0.0f) nonZero++;
                }
                totalNonZero += nonZero;
                bool ok = nonZero == 0;
                all &= ok;
                sb.AppendLine("  light " + (l + 1) + " (yaw " + yaws[l] + ", pitch " + pitches[l] +
                              "): samples with a non-zero rim term " + nonZero + "/" + n +
                              " (expected 0)  " + Verdict(ok));
            }

            sb.AppendLine("  total across the sweep: " + totalNonZero + "/" + (n * 8) + " (expected 0)");
            sb.AppendLine("  this is ARITHMETIC, not a bug: on a flat surface N.V == 1 so rim == 0 identically.");
            sb.AppendLine("  ShaperLightRig.RimNeedsRelief is the sentence a UI must show on the control.");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-8 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-8 — <c>receiveLighting = false</c> IS BIT-IDENTICAL TO THE PRE-LIGHTING BUILD (LR-4.3).
        ///
        /// <b>REBUILT BY THE FIX PASS AGAINST A STORED GOLDEN, and D-9's defence of the live comparison is
        /// withdrawn as false.</b> D-9 argued that comparing against a live null-scene render is "strictly
        /// stronger than a stored golden: a golden can go stale, the live unlit path cannot". It is not
        /// stronger, it is CIRCULAR — with <c>receive == 0</c> and no Solids overlay, <c>doLight</c> and
        /// <c>solidOwner</c> are both false (<c>ShaperFillResolver.cs:1089-1090</c>) and the render takes the
        /// IDENTICAL <c>if (!doLight &amp;&amp; !solidOwner)</c> block the null-scene render takes. Both sides
        /// of the comparison ran the same code, so any regression in that shared block moved both sides
        /// equally and the difference stayed 0. Proved by mutation: multiplying that branch by 0.9 left this
        /// leg PASSING while <c>LT10_AddIsNotLit</c> and <c>ShaperBorderAudit.BT11_Ordering</c> both FAILED —
        /// a T-0107 leg detected the unlit-path regression that the leg written to guard that path did not.
        ///
        /// So the assertion is now against <b>bytes on disk</b>, written once from a build the verifier
        /// confirmed correct, under <c>.agenthq/workspace/T-0108/golden/</c>. A 10 % albedo regression in the
        /// shared unlit path now fails here, which is precisely the class of change a golden exists to catch.
        /// The live null-scene comparison is KEPT as a second, weaker leg — it still proves the narrower
        /// "receive-off ≡ null-scene within this build", which is worth having and is all it ever proved.
        ///
        /// A stale golden is a real risk and is handled rather than argued away: the file is regenerated only
        /// by <see cref="WriteGoldens"/>, which is never called by <see cref="RunAll"/>, so it cannot drift
        /// silently — a deliberate regeneration is a visible act with a diff.
        ///
        /// The fixture is T-0107-shaped: a bag with nested fill owners and a border, exercising both accumulate
        /// blocks that LR-5.1 touches.
        ///
        /// <b>Mutations that make it fail:</b> (a) make <c>receive = false</c> mean "ambient only" instead of
        /// "unlit" — every byte shifts; (b) scale the shared unlit accumulate branch
        /// (<c>ShaperFillResolver.cs:1121-1123</c>) by 0.9 — the golden half fails and the live half does not,
        /// which is the whole reason the golden is here. It also catches an unconditional <c>S</c> add, or a
        /// rim that escapes the receive gate.
        /// </summary>
        public static string LT8_ReceiveOffIsBitIdentical()
        {
            var sb = new StringBuilder("LT-8 receiveLighting = false is bit-identical to the pre-lighting build\n");
            bool all = true;

            var unlit = BuildLit(Lt8Fixture(), null, null);
            Paint(unlit);
            var refPx = Encoded(unlit);

            var lit = BuildLit(Lt8Fixture(), EightLights(), Resp(false, 1f, 1f, 2.2f, 0.9f, 24f));
            Paint(lit);
            var gotPx = Encoded(lit);

            // ── (a) THE GOLDEN. Bytes on disk, not a second run of the same branch.
            {
                string gp = GoldenPath(Lt8Golden);
                if (!System.IO.File.Exists(gp))
                {
                    all = false;
                    sb.AppendLine("  golden MISSING at " + gp +
                                  " - regenerate deliberately with ShaperLightAudit.WriteGoldens()  " + Verdict(false));
                }
                else
                {
                    byte[] gold = System.IO.File.ReadAllBytes(gp);
                    int expect = refPx.Length * 4;
                    bool lenOk = gold.Length == expect;
                    int gbadUnlit = 0, gbadRecvOff = 0;
                    if (lenOk)
                        for (int i = 0; i < refPx.Length; i++)
                        {
                            int o = i * 4;
                            if (gold[o] != refPx[i].r || gold[o + 1] != refPx[i].g ||
                                gold[o + 2] != refPx[i].b || gold[o + 3] != refPx[i].a) gbadUnlit++;
                            if (gold[o] != gotPx[i].r || gold[o + 1] != gotPx[i].g ||
                                gold[o + 2] != gotPx[i].b || gold[o + 3] != gotPx[i].a) gbadRecvOff++;
                        }
                    bool gOk = lenOk && gbadUnlit == 0 && gbadRecvOff == 0;
                    all &= gOk;
                    sb.AppendLine("  golden: " + gold.Length + " B (expected " + expect + "), from " + gp);
                    sb.AppendLine("  differing pixels, NULL-SCENE render vs the STORED GOLDEN: " + gbadUnlit + "/" +
                                  refPx.Length + " (expected 0)  " + Verdict(lenOk && gbadUnlit == 0));
                    sb.AppendLine("  differing pixels, RECEIVE-OFF render vs the STORED GOLDEN: " + gbadRecvOff + "/" +
                                  refPx.Length + " (expected 0 - this is the assertion D-9 argued away)  " +
                                  Verdict(lenOk && gbadRecvOff == 0));
                }
            }

            // ── (b) the live comparison, KEPT but demoted: it proves only "receive-off == null-scene within
            // this build", because both sides take the same branch of the same method.
            int bad = 0;
            for (int i = 0; i < refPx.Length; i++)
                if (refPx[i].r != gotPx[i].r || refPx[i].g != gotPx[i].g ||
                    refPx[i].b != gotPx[i].b || refPx[i].a != gotPx[i].a) bad++;
            bool ok = bad == 0;
            all &= ok;

            sb.AppendLine("  rig: " + lit.scene.rig.count + " enabled lights (expected 8), rimStrength 1 on the response");
            sb.AppendLine("  differing pixels vs the null-scene render: " + bad + "/" + refPx.Length +
                          " (expected 0; WEAK - both sides run the same branch, see the summary)  " + Verdict(ok));

            // The control: the SAME rig with receive ON must differ, or the leg is vacuous.
            var on = BuildLit(Lt8Fixture(), EightLights(), Resp(true, 1f, 1f, 2.2f, 0.9f, 24f));
            Paint(on);
            var onPx = Encoded(on);
            int diff = 0;
            for (int i = 0; i < refPx.Length; i++)
                if (refPx[i].r != onPx[i].r || refPx[i].g != onPx[i].g || refPx[i].b != onPx[i].b) diff++;
            bool ctrlOk = diff > 1000;
            all &= ctrlOk;
            sb.AppendLine("  CONTROL - the same rig with receive ON differs at " + diff + "/" + refPx.Length +
                          " pixels (expected > 1000; a 0 here would make this leg vacuous)  " + Verdict(ctrlOk));
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-9 ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-9 — LIGHTING DOES NOT PERTURB THE EXCLUSIVITY PARTITION (LR-5.5). T-0106's FT-21 instrument, on
        /// the same fixture, re-run with lighting ON, 8 lights, mixed Over/Add.
        ///
        /// Expected, and directly comparable to the numbers FC-2.6a-AMENDED records: <b>0 of 16384</b> samples
        /// below the shape's published coverage, total deficit <b>0.000</b>, worst <b>0.0000</b> — where the
        /// build before the FC-3.5a fix measured 244 / 29.288 / 0.2484.
        ///
        /// <b>Mutation that makes it fail:</b> apply the light multiplier to the premultiplied destination
        /// AFTER accumulation, including index 3 — alpha is scaled by <c>L</c> and a large deficit reappears
        /// immediately. That is LR-0.8's trap made mechanical.
        /// </summary>
        public static string LT9_ExclusivityUnperturbed()
        {
            var sb = new StringBuilder("LT-9 lighting does not perturb the exclusivity partition (FT-21's instrument)\n");
            bool all = true;
            int n = W * H;

            var c3 = Disc("C", 14f, 0f, 0f); c3.fill = Solid(new Color(0.2f, 0.4f, 1f));
            var b3 = ShaperNode.Bag("B", ShaperCombineMode.Add, Disc("Bd", 28f, 0f, 0f), c3);
            b3.fill = Solid(new Color(0.2f, 0.9f, 0.3f));
            var a3 = ShaperNode.Bag("A", ShaperCombineMode.Add, Disc("Ad", 42f, 0f, 0f), b3);
            a3.fill = Solid(new Color(0.9f, 0.2f, 0.2f));

            // FT-21's EXACT fixture - three concentric owners, every pair ancestor-descendant, all Over - so
            // the numbers below are directly comparable to the 244 / 29.288 / 0.2484 the pre-fix build
            // measured. A sibling would legitimately break leg (a) (two overlapping claims sum above the
            // root's coverage) and an Add owner would legitimately break leg (b) (FC-3.5b: an Add owner
            // contributes light and never opacity), so neither belongs in THIS fixture. The mixed Over/Add
            // case LT-9 also asks for is leg (d), where the right assertion is a different one.
            var r = BuildLit(a3, EightLights(), Resp(true, 1f, 0.7f, 2.2f, 0.9f, 24f, null,
                                                    new Vector3(0.5f, 0.2f, 0.84f)));
            Paint(r);
            int k = r.doc.owners.Count, cap = r.buf.sampleCapacity;

            int nBad = 0; double worstExcess = 0;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int o = 0; o < k; o++) sum += r.buf.paint[o * cap + i];
                float rootCov = r.buf.ownCoverage[i];
                double d = Mathf.Abs(sum - rootCov);
                if (d > 1e-4) { nBad++; if (d > worstExcess) worstExcess = d; }
            }
            bool consOk = nBad == 0;
            all &= consOk;
            sb.AppendLine("  (a) sum(paint over the chain) != rootCoverage at " + nBad + "/" + n +
                          " samples (expected 0), worst " + worstExcess.ToString("F5") + "  " + Verdict(consOk));

            int nDef = 0; double totalDef = 0, worstDef = 0;
            for (int i = 0; i < n; i++)
            {
                float d = r.buf.ownCoverage[i] - r.buf.dst[i * 4 + 3];
                if (d > 1e-3f) { nDef++; totalDef += d; if (d > worstDef) worstDef = d; }
            }
            bool defOk = nDef == 0;
            all &= defOk;
            sb.AppendLine("  (b) composited alpha BELOW the shape's own coverage at " + nDef + "/" + n +
                          " samples (expected 0), total " + totalDef.ToString("F3") +
                          ", worst " + worstDef.ToString("F4") + "  " + Verdict(defOk));
            sb.AppendLine("      Directly comparable to the pre-fix escalation's 244 / 29.288 / 0.2484 on the same");
            sb.AppendLine("      fixture and the same instrument - now with 8 lights, a rim and an Add owner running.");

            // (c) the alpha channel must be UNTOUCHED by lighting: same fixture, lit and unlit, alpha only.
            var unlit = BuildLit(a3, null, null);
            Paint(unlit);
            int alphaBad = 0;
            for (int i = 0; i < n; i++)
                if (r.buf.dst[i * 4 + 3] != unlit.buf.dst[i * 4 + 3]) alphaBad++;
            bool aOk = alphaBad == 0;
            all &= aOk;
            sb.AppendLine("  (c) destination ALPHA differing between the lit and unlit renders: " + alphaBad +
                          "/" + n + " (expected 0 - lighting touches colour only, LR-5.2)  " + Verdict(aOk));

            // (d) — the MIXED Over/Add case LT-9 also asks for. The assertion here is NOT "no deficit": an
            // Add owner contributes light and never opacity (FC-2.6b / FC-3.5b), so an Add owner inside the
            // partition legitimately leaves alpha below the shape's coverage where it owns the pixel, and
            // asserting 0 would assert something the fill contract explicitly denies. What lighting must not
            // do is CHANGE that number, and that is what is asserted.
            {
                var gc = Disc("C", 14f, 0f, 0f); gc.fill = Solid(new Color(0.2f, 0.4f, 1f));
                var gb = ShaperNode.Bag("B", ShaperCombineMode.Add, Disc("Bd", 28f, 0f, 0f), gc);
                gb.fill = Solid(new Color(0.2f, 0.9f, 0.3f), 1f, 0f, ShaperFillComposite.Add);
                var ga = ShaperNode.Bag("A", ShaperCombineMode.Add, Disc("Ad", 42f, 0f, 0f), gb);
                ga.fill = Solid(new Color(0.9f, 0.2f, 0.2f));

                var mLit = BuildLit(ga, EightLights(), Resp(true, 1f, 0.7f, 2.2f, 0.9f, 24f, null,
                                                            new Vector3(0.5f, 0.2f, 0.84f)));
                Paint(mLit);
                var mUnlit = BuildLit(ga, null, null);
                Paint(mUnlit);

                int mBad = 0, addDeficit = 0;
                for (int i = 0; i < n; i++)
                {
                    if (mLit.buf.dst[i * 4 + 3] != mUnlit.buf.dst[i * 4 + 3]) mBad++;
                    if (mUnlit.buf.ownCoverage[i] - mUnlit.buf.dst[i * 4 + 3] > 1e-3f) addDeficit++;
                }
                bool mOk = mBad == 0;
                all &= mOk;
                sb.AppendLine("  (d) MIXED Over/Add, 8 lights: destination ALPHA differing lit vs unlit " + mBad +
                              "/" + n + " (expected 0)  " + Verdict(mOk));
                sb.AppendLine("      the same fixture's Add-owner alpha 'deficit' is " + addDeficit + "/" + n +
                              " in BOTH renders - reported, not asserted to be 0, because FC-2.6b says an Add");
                sb.AppendLine("      owner raises no alpha. Lighting neither creates nor removes one sample of it.");
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-10 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-10 — <c>Add</c> FILLS ARE NOT LIT (LR-5.3). Two owners of the same linear colour, one
        /// <c>Over</c> and one <c>Add</c>, under a rig giving <c>L = 0.5</c>, ambient 0, <c>S = 0</c>.
        ///
        /// Expected: the <c>Add</c> contribution's RGB is exactly <c>colour * coverageEff</c>, the
        /// <c>Over</c> contribution's is exactly <c>0.5x</c> that, ratio exactly 2.0 to 1e-6.
        ///
        /// <b>Implementation note, recorded:</b> Part 8 says "one node, two owners". They are measured as two
        /// SEPARATE renders of the same node instead, because two owners on one node necessarily composite
        /// into each other and the ratio would then be confounded by the composite itself. The assertion is
        /// identical and the measurement is cleaner.
        ///
        /// <b>Mutation that makes it fail:</b> remove the <c>if (add)</c> guard at the lighting call — the
        /// <c>Add</c> contribution halves and the ratio reads 1.0.
        /// </summary>
        public static string LT10_AddIsNotLit()
        {
            var sb = new StringBuilder("LT-10 Add fills are not lit (L = 0.5, ambient 0, S = 0)\n");

            // intensity 0.5 on a white light with ambient 0 and specular 0 gives L = 0.5 exactly.
            var rig = RigOf(Color.white, 0f, Dir(0f, 0f, Color.white, 0.5f, 0f));
            var resp = Resp(true, 1f, 0f, 2.2f, 0f, 48f, Color.white);
            var col = new Color(0.8f, 0.4f, 0.2f);

            Func<ShaperFillComposite, LRig> make = comp =>
            {
                var d = Disc("D", 40f, 0f, 0f);
                d.fill = Solid(col, 1f, 0f, comp);
                return BuildLit(d, rig, resp, 64, 64);
            };

            var over = make(ShaperFillComposite.Over); Paint(over);
            var add = make(ShaperFillComposite.Add); Paint(add);

            int i = Index(over, 0f, 0f);
            float ro = over.buf.dst[i * 4 + 0], ra = add.buf.dst[i * 4 + 0];
            double ratio = ra / ro;
            bool rOk = Math.Abs(ratio - 2.0) <= 1e-6;

            // And the Add contribution must be EXACTLY the unlit albedo: colour * coverageEff, ce == 1 here.
            ShaperSrgb.Decode(col, out float lin, out _, out _);
            bool exactOk = Math.Abs(ra - lin) <= 1e-6;

            sb.AppendLine("  Over destination R = " + ro.ToString("F8") + "  Add destination R = " + ra.ToString("F8"));
            sb.AppendLine("  Add/Over ratio = " + ratio.ToString("F8") + " (expected 2.00000000, tolerance 1e-6)  " +
                          Verdict(rOk));
            sb.AppendLine("  Add destination vs the raw linear albedo " + lin.ToString("F8") + ": |err| " +
                          Math.Abs(ra - lin).ToString("E3") + " (expected 0 - an Add fill is written through " +
                          "UNLIT)  " + Verdict(exactOk));
            sb.AppendLine("  A lamp does not get dimmer because you put it in a dark room (LR-5.3); FC-2.6d records");
            sb.AppendLine("  that the fire and explosion palettes are usually authored to Add, so this is what keeps");
            sb.AppendLine("  a fire emissive under a rig that would otherwise plunge it into shadow.");
            sb.Append("  RESULT: " + Verdict(rOk && exactOk));
            return sb.ToString();
        }

        // ── LT-11 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-11 — THE CAP REFUSES WITH A REASON AND A COUNT (LR-1.4). Author 11 lights.
        ///
        /// Expected: <c>hasTooManyLights == true</c>; <c>tooManyLightsCount == 3</c>; the reason string
        /// contains both "11" and "8"; and lights 9-11 contribute EXACTLY 0 at all 16 384 samples.
        ///
        /// <b>Mutations that make it fail:</b> <c>continue</c> past the ninth light without setting the flag
        /// (the boolean assertion fails); set the flag but record only that SOME were dropped (the count
        /// assertion fails). That second mutation is FT-8b's lesson (<c>ShaperProgram.cs:90-95</c>) applied
        /// here BEFORE the mistake rather than after.
        /// </summary>
        public static string LT11_TheCap()
        {
            var sb = new StringBuilder("LT-11 the eight-light cap refuses with a reason and a count\n");
            bool all = true;

            var eleven = EightLights();
            eleven.lights.Add(Pnt(70f, 70f, 10f, 20f, Color.red, 5f, 1f));
            eleven.lights.Add(Pnt(-70f, 70f, 10f, 20f, Color.green, 5f, 1f));
            eleven.lights.Add(Dir(10f, 10f, Color.blue, 5f, 1f));

            var d = Disc("D", 40f, 0f, 0f);
            d.fill = Solid(new Color(0.7f, 0.7f, 0.7f));
            var r11 = BuildLit(d, eleven, Resp(true, 1f, 0.3f));
            Paint(r11);

            bool flagOk = r11.prog.hasTooManyLights;
            bool countOk = r11.prog.tooManyLightsCount == 3;
            string reason = r11.prog.tooManyLightsReason ?? "";
            bool reasonOk = reason.Contains("11") && reason.Contains("8");
            bool nOk = r11.scene.rig.count == 8;
            all &= flagOk && countOk && reasonOk && nOk;

            sb.AppendLine("  hasTooManyLights = " + flagOk + " (expected True)  " + Verdict(flagOk));
            sb.AppendLine("  tooManyLightsCount = " + r11.prog.tooManyLightsCount + " (expected 3)  " + Verdict(countOk));
            sb.AppendLine("  compiled light count = " + r11.scene.rig.count + " (expected 8)  " + Verdict(nOk));
            sb.AppendLine("  reason: \"" + reason + "\"  contains \"11\" and \"8\": " + reasonOk + "  " + Verdict(reasonOk));

            // ── THE DISABLED-LIGHT CONFIGURATIONS. Added by the fix pass, and this is the half that was
            // missing: the leg's single 11-enabled fixture above is THE ONE CONFIGURATION in which counting
            // AUTHORED entries happens to give the right answer, so it could not see that the diagnostic was
            // measuring the wrong thing. Measured before the fix, `hasTooManyLights` / `tooManyLightsCount`
            // were True/1, True/3 and True/4 on the three rows below where the truth is False/0 — wrong in
            // three of five configurations, with the tool telling the author lights had been dropped when
            // every enabled light was lit. This is T-0107's BT-5 pattern (assert a value on the one case
            // where it is true), and these rows are what stop it recurring.
            //
            // Mutation that makes THIS half fail: revert the cap to `authored > MaxLights` and
            // `authored - MaxLights` in ShaperLightCompiler.Compile.
            sb.AppendLine("  the disabled-light configurations - `enabled`, not `authored`, is what the cap measures:");
            Func<int, int[], ShaperLightRig> mk = (count, off) =>
            {
                var rg = RigOf(Color.white, 0.12f);
                for (int i = 0; i < count; i++)
                {
                    var l = (i % 2 == 0) ? Dir(-55f + i * 13f, 20f + i * 3f, Color.white, 0.5f)
                                         : Pnt(i * 7f - 30f, 20f - i * 5f, 30f, 40f, Color.white, 0.5f);
                    l.enabled = System.Array.IndexOf(off, i) < 0;
                    rg.lights.Add(l);
                }
                return rg;
            };
            // name, authored, disabled indices, expected enabled, expected lit, expected flag, expected dropped
            var caps = new[]
            {
                new object[] { "11 authored, none disabled",  11, new int[0],                    11, 8, true,  3 },
                new object[] { "9 authored, the 9th disabled", 9, new[] { 8 },                    8, 8, false, 0 },
                new object[] { "11 authored, 5 disabled",     11, new[] { 1, 3, 5, 7, 9 },        6, 6, false, 0 },
                new object[] { "12 authored, first 8 disabled",12, new[] { 0,1,2,3,4,5,6,7 },     4, 4, false, 0 },
                new object[] { "8 authored, none disabled",    8, new int[0],                     8, 8, false, 0 },
                new object[] { "13 authored, 2 disabled",     13, new[] { 0, 12 },               11, 8, true,  3 },
            };
            foreach (var cse in caps)
            {
                var rg = mk((int)cse[1], (int[])cse[2]);
                var pg = ShaperLightCompiler.Compile(rg, 0f, 0u);
                int expEnabled = (int)cse[3], expLit = (int)cse[4], expDropped = (int)cse[6];
                bool expFlag = (bool)cse[5];
                bool ok = pg.enabledLightCount == expEnabled && pg.rig.count == expLit &&
                          pg.hasTooManyLights == expFlag && pg.tooManyLightsCount == expDropped;
                all &= ok;
                sb.AppendLine("    " + ((string)cse[0]).PadRight(30) +
                              " enabled " + pg.enabledLightCount + "/" + expEnabled +
                              ", lit " + pg.rig.count + "/" + expLit +
                              ", flag " + pg.hasTooManyLights + "/" + expFlag +
                              ", dropped " + pg.tooManyLightsCount + "/" + expDropped + "  " + Verdict(ok));
            }

            // The sibling diagnostic in the same file already counted the right thing (`prog.rig.count`).
            // Assert they AGREE, which is the invariant that was broken: two diagnostics, one file, only one
            // of them counting enabled lights.
            {
                var rg = mk(11, new[] { 1, 3, 5, 7, 9 });
                var pg = ShaperLightCompiler.Compile(rg, 0f, 0u);
                ShaperLightCompiler.Finish(pg);
                bool agree = pg.rig.count == pg.enabledLightCount &&
                             pg.rig.count == Mathf.Min(pg.enabledLightCount, ShaperLightRigCompiled.MaxLights);
                all &= agree;
                sb.AppendLine("  Finish's `rig.count` (" + pg.rig.count + ") and the cap's `enabledLightCount` (" +
                              pg.enabledLightCount + ") agree below the cap  " + Verdict(agree));
            }

            // Lights 9-11 contribute EXACTLY 0: the 11-light render must be bit-identical to the 8-light one.
            var eight = EightLights();
            var r8 = BuildLit(d, eight, Resp(true, 1f, 0.3f));
            Paint(r8);
            int n = W * H, bad = 0;
            for (int i = 0; i < n * 4; i++) if (r11.buf.dst[i] != r8.buf.dst[i]) bad++;
            bool zeroOk = bad == 0;
            all &= zeroOk;
            sb.AppendLine("  destination floats differing between the 11-light and 8-light rigs: " + bad + "/" +
                          (n * 4) + " (expected 0 - lights 9-11 contribute exactly 0)  " + Verdict(zeroOk));
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-12 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-12 — SHADOW FLAGS ARE RECORDED AND INERT (LR-4.5). Two renders of one document: all shadow flags
        /// false, then all true.
        ///
        /// (a) <c>Color32</c> output BIT-IDENTICAL between the two. (b) <c>hasUnimplementedShadow</c> false
        /// then TRUE; <c>unimplementedShadowCount</c> equal to the layer count; <c>unimplementedShadowNode</c>
        /// naming the first layer; <c>unimplementedShadowReason</c> equal to
        /// <c>ShaperLightRig.ShadowsNotComputed</c> VERBATIM.
        ///
        /// <b>BOTH HALVES ARE REQUIRED</b> — any accidental shadow implementation breaks (a); dropping or
        /// paraphrasing the diagnostic breaks (b). This is the test that makes "nothing-but-recorded" an honest
        /// ruling rather than an excuse.
        /// </summary>
        public static string LT12_ShadowsRecordedAndInert()
        {
            var sb = new StringBuilder("LT-12 shadow flags are recorded AND inert\n");
            bool all = true;

            Func<bool, LRig> make = shadows =>
            {
                var d = Disc("D", 38f, 0f, 0f);
                d.fill = Solid(new Color(0.8f, 0.5f, 0.3f));
                d.border = new ShaperBorderDef { enabled = true, width = new ZUIValue(3f) };
                var resp = Resp(true, 1f, 0.5f, 2.2f, 0.9f, 24f, null, new Vector3(0.3f, 0.3f, 0.9f));
                resp.castShadows = shadows;
                resp.receiveShadows = shadows;
                return BuildLit(d, EightLights(), resp);
            };

            var off = make(false); Paint(off); var offPx = Encoded(off);
            var on = make(true); Paint(on); var onPx = Encoded(on);

            int bad = 0;
            for (int i = 0; i < offPx.Length; i++)
                if (offPx[i].r != onPx[i].r || offPx[i].g != onPx[i].g ||
                    offPx[i].b != onPx[i].b || offPx[i].a != onPx[i].a) bad++;
            bool aOk = bad == 0;
            all &= aOk;
            sb.AppendLine("  (a) differing pixels between shadows-off and shadows-on: " + bad + "/" + offPx.Length +
                          " (expected 0 - INERT)  " + Verdict(aOk));

            bool f1 = !off.prog.hasUnimplementedShadow;
            bool f2 = on.prog.hasUnimplementedShadow;
            bool c1 = on.prog.unimplementedShadowCount == on.prog.layerCount && on.prog.layerCount == 1;
            bool n1 = on.prog.unimplementedShadowNode == "Layer";
            bool r1 = on.prog.unimplementedShadowReason == ShaperLightRig.ShadowsNotComputed;

            // The identity check above is NECESSARY and NOT SUFFICIENT, and that was measured rather than
            // reasoned: paraphrasing the const itself passes it, because both sides of the comparison move
            // together. So the SUBSTANCE is asserted too - the sentence must still say what is saved, what is
            // not computed, and WHY (LR-7.2's sentence is the only place a UI can learn the reason). A
            // paraphrase that drops the reason now fails even though the identity check cannot see it.
            string reasonText = on.prog.unimplementedShadowReason ?? "";
            bool r2 = reasonText.Contains("saved with the document") &&
                      reasonText.Contains("no shadow is computed") &&
                      reasonText.Contains("straight down") &&
                      reasonText.Contains("ray");
            all &= f1 && f2 && c1 && n1 && r1 && r2;

            sb.AppendLine("  (b) hasUnimplementedShadow off/on = " + off.prog.hasUnimplementedShadow + "/" +
                          on.prog.hasUnimplementedShadow + " (expected False/True)  " + Verdict(f1 && f2));
            sb.AppendLine("      unimplementedShadowCount = " + on.prog.unimplementedShadowCount +
                          " (expected " + on.prog.layerCount + ", the layer count)  " + Verdict(c1));
            sb.AppendLine("      unimplementedShadowNode = \"" + on.prog.unimplementedShadowNode +
                          "\" (expected \"Layer\", the first layer that asked)  " + Verdict(n1));
            sb.AppendLine("      reason == ShaperLightRig.ShadowsNotComputed VERBATIM: " + r1 + "  " + Verdict(r1));
            sb.AppendLine("      reason still SAYS what it must (saved / not computed / straight down / ray): " +
                          r2 + "  " + Verdict(r2) + "  - the identity check alone is vacuous against a " +
                          "paraphrase of the const itself, measured, so the substance is asserted separately");
            sb.AppendLine("      \"" + (on.prog.unimplementedShadowReason ?? "<null>") + "\"");
            sb.AppendLine("  (c) the flags are ABSENT from ShaperResponseCompiled, so the law cannot see them: " +
                          (typeof(ShaperResponseCompiled).GetField("castShadows") == null &&
                           typeof(ShaperResponseCompiled).GetField("receiveShadows") == null));

            // (d) — THE COUNT, on a document with SEVERAL layers. A one-layer fixture makes "count equals the
            // layer count" vacuous (1 == 1 passes even for a build that records only a flag), and that is
            // measured rather than assumed: the mutation `unimplementedShadowCount = 1` passed leg (b) on the
            // single-layer fixture above. FT-8b's lesson is precisely that recording only the first offender
            // is the mistake (ShaperProgram.cs:90-95), so it must be tested where it can show.
            {
                var prog = ShaperLightCompiler.Compile(EightLights(), 0f, 0u);
                string[] names = { "Backdrop", "Hero", "Foreground", "Overlay" };
                for (int i = 0; i < names.Length; i++)
                {
                    var rp = Resp(true);
                    rp.castShadows = i != 1;                 // three of the four ask; "Hero" does not
                    rp.receiveShadows = i == 3;
                    ShaperLightCompiler.CompileResponse(rp, names[i], 0f, 0u, prog);
                }
                ShaperLightCompiler.Finish(prog);

                bool dCount = prog.unimplementedShadowCount == 3;
                bool dLayers = prog.layerCount == 4;
                bool dFirst = prog.unimplementedShadowNode == "Backdrop";
                all &= dCount && dLayers && dFirst;
                sb.AppendLine("  (d) FOUR layers, three of which ask for shadows:");
                sb.AppendLine("      layerCount = " + prog.layerCount + " (expected 4)  " + Verdict(dLayers));
                sb.AppendLine("      unimplementedShadowCount = " + prog.unimplementedShadowCount +
                              " (expected 3 - the number that asked, not the number of layers and not 1)  " +
                              Verdict(dCount));
                sb.AppendLine("      unimplementedShadowNode = \"" + prog.unimplementedShadowNode +
                              "\" (expected \"Backdrop\", the FIRST that asked)  " + Verdict(dFirst));
            }

            // (e) — the receiver diagnostic, the other silent-inertness case B4 names (LR-4.3).
            {
                var prog = ShaperLightCompiler.Compile(EightLights(), 0f, 0u);
                ShaperLightCompiler.CompileResponse(Resp(false), "Only", 0f, 0u, prog);
                ShaperLightCompiler.Finish(prog);
                bool eOk = prog.hasLightsButNoReceivers && prog.receiverCount == 0 &&
                           !string.IsNullOrEmpty(prog.lightsButNoReceiversReason);
                all &= eOk;
                sb.AppendLine("  (e) 8 lights and ZERO receivers raises hasLightsButNoReceivers = " +
                              prog.hasLightsButNoReceivers + ", receiverCount = " + prog.receiverCount +
                              " (expected True/0)  " + Verdict(eOk));
                sb.AppendLine("      \"" + (prog.lightsButNoReceiversReason ?? "<null>") + "\"");
            }
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-13 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-13 — THE PROVIDER IS SWAPPABLE WITHOUT TOUCHING THE LAW (LR-3.1).
        ///
        /// (a) Render one document with <c>Constant(0,0,1)</c> and with <c>Constant(0.6,0,0.8)</c>; a named
        /// probe sample's byte must differ by at least 8 codes.
        /// (b) Reflection: <c>Shade</c>'s parameter list contains NO array type and NO reference type.
        ///
        /// <b>Mutation that makes it fail:</b> have <c>Shade</c> take the height or normal sheet and compute
        /// the normal itself — (b) fails instantly. This is the mechanical form of BC-4.2's "grep-level
        /// assertion that no shading code path differences a height or depth buffer", and it runs every build.
        /// </summary>
        public static string LT13_ProviderIsSwappable()
        {
            var sb = new StringBuilder("LT-13 the provider is swappable without touching the law\n");
            bool all = true;

            var rig = RigOf(Color.white, 0.1f, Dir(-55f, 36f, Color.white, 1f, 0.9f));
            Func<Vector3, LRig> make = nrm =>
            {
                var d = Disc("D", 40f, 0f, 0f);
                d.fill = Solid(new Color(0.75f, 0.75f, 0.75f));
                return BuildLit(d, rig, Resp(true, 1f, 0f, 2.2f, 0.9f, 24f, null, nrm), 64, 64);
            };

            var flat = make(new Vector3(0f, 0f, 1f)); Paint(flat);
            var tilt = make(new Vector3(0.6f, 0f, 0.8f)); Paint(tilt);
            int i = Index(flat, 0f, 0f);
            byte bf = Encoded(flat)[i].r, bt = Encoded(tilt)[i].r;
            int delta = Mathf.Abs(bf - bt);
            bool aOk = delta >= 8;
            all &= aOk;
            sb.AppendLine("  (a) centre-sample red byte: Constant(0,0,1) = " + bf + ", Constant(0.6,0,0.8) = " + bt +
                          ", |delta| = " + delta + " (expected >= 8)  " + Verdict(aOk));

            var ps = typeof(ShaperLightLaw).GetMethod("Shade").GetParameters();
            int arrays = 0, refs = 0;
            var shape = new StringBuilder();
            foreach (var p in ps)
            {
                var t = p.ParameterType;
                var el = t.IsByRef ? t.GetElementType() : t;
                if (el.IsArray) arrays++;
                if (!el.IsValueType) refs++;
                shape.Append(el.Name + " ");
            }
            bool bOk = arrays == 0 && refs == 0;
            all &= bOk;
            sb.AppendLine("  (b) Shade parameters: " + ps.Length + " total, array types " + arrays +
                          " (expected 0), reference types " + refs + " (expected 0)  " + Verdict(bOk));
            sb.AppendLine("      " + shape.ToString().Trim());
            sb.AppendLine("  A signature with no array in it CANNOT difference a buffer. LR-3.3's prohibition is");
            sb.AppendLine("  therefore structural, not a convention someone has to remember.");
            sb.AppendLine("  (c) ShaperNormalKind cases present: " +
                          string.Join(", ", Enum.GetNames(typeof(ShaperNormalKind))) +
                          "  - Profile is RESERVED and commented, so T-0109 is an addition, not a renumbering.");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-14a ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-14a — CONVEXITY (LR-6.5a). For Box, Pyramid, Can and Gem, densely sample the silhouette and count
        /// how many CULLED-VISIBLE faces contain each sample. Maximum EXACTLY 1, every sample, every form,
        /// over 32 rotation triples.
        ///
        /// <b>Mutation that makes it fail:</b> remove the backface cull at <c>PyreRenderer.cs:4235</c>'s
        /// equivalent in <c>ShaperSolids.BuildFacet</c> — the count reaches 2 at the first sample.
        /// </summary>
        public static string LT14a_Convexity()
        {
            var sb = new StringBuilder("LT-14a convexity: at most one culled-visible face covers any sample\n");
            bool all = true;

            var forms = new[] { ShaperSolidForm.Box, ShaperSolidForm.Pyramid,
                                ShaperSolidForm.Can, ShaperSolidForm.Gem };
            foreach (var form in forms)
            {
                int maxInterior = 0, maxAny = 0; long samples = 0, onSeam = 0; int worstRot = -1;
                for (int t = 0; t < 32; t++)
                {
                    float yaw = (t * 47f) % 360f, tilt = (t * 71f) % 360f, roll = (t * 113f) % 360f;
                    var op = ShaperSolids.Compile(SolidDef(form, 30f, yaw, tilt, roll), 0f, 0u);
                    var g = ShaperSolids.Build(op);

                    for (int iy = -40; iy <= 40; iy++)
                        for (int ix = -40; ix <= 40; ix++)
                        {
                            float px = ix * 1f, py = iy * 1f;
                            int interior = 0, any = 0;
                            for (int v = 0; v < g.visCount; v++)
                            {
                                int r = InTri(px, py, g.visAx[v], g.visAy[v], g.visBx[v], g.visBy[v],
                                              g.visCx[v], g.visCy[v]);
                                if (r == 2) interior++;
                                if (r != 0) any++;
                            }
                            samples++;
                            if (any > 1) onSeam++;
                            if (interior > maxInterior) { maxInterior = interior; worstRot = t; }
                            if (any > maxAny) maxAny = any;
                        }
                }
                bool ok = maxInterior <= 1;
                all &= ok;
                sb.AppendLine("  " + form.ToString().PadRight(9) + " 32 rotations x 6561 samples = " + samples +
                              "; max STRICTLY-INTERIOR covering faces = " + maxInterior + " (expected 1)" +
                              (maxInterior > 1 ? "  first at rotation triple " + worstRot : "") +
                              "  " + Verdict(ok));
                sb.AppendLine("            samples lying exactly ON a shared triangle edge or vertex: " + onSeam +
                              " (max " + maxAny + " faces there) - EXPECTED and harmless: InTri's test is " +
                              "inclusive (w >= 0), so a point on a shared edge is in both triangles, and the " +
                              "generator's `break` makes the winner deterministic.");
            }

            sb.AppendLine("  This is what makes 'break; // convex + culled: first hit wins' correct, and it is");
            sb.AppendLine("  exactly what a FUSED silhouette of several solids would break (LR-6.5a).");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>
        /// The audit's own barycentric test, so a mutation inside the generator cannot mask itself.
        /// Returns 0 = outside, 1 = on the boundary, 2 = strictly inside.
        ///
        /// The three-way answer is what makes LT-14a mean what LR-6.5a says. The generator's own
        /// <c>InTri</c> is INCLUSIVE (<c>w &gt;= 0</c>, ported verbatim from <c>PyreRenderer.cs:4936</c>), so a
        /// sample lying exactly on the diagonal that splits a box quad is genuinely inside both triangles.
        /// That is a tie on a shared edge, not two faces overlapping, and the generator's
        /// <c>break; // first hit wins</c> resolves it deterministically. Counting it as a convexity violation
        /// would make this leg fail on a correct implementation - measured: Box 2, Can 2, Gem 3 at rotation
        /// (0,0,0), every one of them on a seam.
        /// </summary>
        static int InTri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Mathf.Abs(d) < 1e-6f) return 0;
            float w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
            float w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
            float w2 = 1f - w0 - w1;
            if (w0 < 0f || w1 < 0f || w2 < 0f) return 0;
            const float Eps = 1e-5f;
            return (w0 > Eps && w1 > Eps && w2 > Eps) ? 2 : 1;
        }

        // ── LT-14b ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-14b — ORB'S SILHOUETTE IS ROTATION-INVARIANT (LR-6.5b). Render an Orb at
        /// <c>(yaw, tilt, roll) = (0,0,0)</c> and at <c>(37, 61, 23)</c> degrees.
        ///
        /// Expected: ALPHA CHANNEL BIT-IDENTICAL; RGB differs at at least 100 samples.
        ///
        /// <b>THE FIRST HALF WAS A STRUCTURAL GUARANTEE, NOT A MEASUREMENT, AND IS NOW A REAL ASSERTION.</b>
        /// Orb coverage is computed at <c>ShaperSolids.SampleOrb</c> from <c>lx, ly, R</c> only —
        /// <c>d = sqrt(lx² + ly²); if (d &gt; R) return;</c> — so the rotation matrix is never consulted for
        /// coverage at all, only for the normal. "0 differing alpha under rotation" was therefore a value the
        /// code could not produce otherwise, and the leg's own framing of it as a measured invariant
        /// overstated it: same class as T-0107's BT-5.
        ///
        /// The fix is to assert against something the rotation COULD reach. Two additions:
        ///
        /// <b>(c) the covered set equals the INDEPENDENTLY COMPUTED analytic disc</b> <c>d &lt;= R</c>, sample
        /// by sample, recomputed here from the grid rather than read back from the generator. That is a
        /// genuine assertion with a genuine failure mode: the contract's own named mutation — applying the
        /// geometric rotation to the Orb's coverage instead of to the published normal — makes the covered set
        /// an ellipse, which no longer equals the disc, and (c) fails immediately. The old (a) could not see
        /// that mutation at all, because BOTH renders would have been rotated by it.
        ///
        /// <b>(d) a LIVE CONTROL on a form whose coverage genuinely is rotation-dependent.</b> A Box under the
        /// same rotation must change its alpha at many samples. Without it, (a) and (c) both passing would be
        /// consistent with the fixture silently not applying rotation at all.
        ///
        /// <b>Mutation that makes it fail:</b> apply the geometric rotation to the Orb's coverage instead of
        /// to the published normal — (c) fails, and (a) now fails too because the two rotations differ.
        /// </summary>
        public static string LT14b_OrbSilhouetteIsRotationInvariant()
        {
            var sb = new StringBuilder("LT-14b Orb's silhouette is rotation-invariant, its shading is not\n");
            var rig = RigOf(Color.white, 0.12f, Dir(-55f, 36f, Color.white, 1f, 0.9f));
            var resp = Resp(true, 1f, 0.4f, 2.2f, 0.9f, 24f);

            var a = BuildSolid(SolidDef(ShaperSolidForm.Orb, 34f, 0f, 0f, 0f), rig, resp,
                               Solid(new Color(0.75f, 0.6f, 0.4f)));
            Paint(a);
            var b = BuildSolid(SolidDef(ShaperSolidForm.Orb, 34f, 37f, 61f, 23f), rig, resp,
                               Solid(new Color(0.75f, 0.6f, 0.4f)));
            Paint(b);

            int n = W * H, alphaBad = 0, rgbDiff = 0;
            for (int i = 0; i < n; i++)
            {
                if (a.buf.dst[i * 4 + 3] != b.buf.dst[i * 4 + 3]) alphaBad++;
                if (a.buf.dst[i * 4 + 0] != b.buf.dst[i * 4 + 0] ||
                    a.buf.dst[i * 4 + 1] != b.buf.dst[i * 4 + 1] ||
                    a.buf.dst[i * 4 + 2] != b.buf.dst[i * 4 + 2]) rgbDiff++;
            }
            bool aOk = alphaBad == 0, bOk = rgbDiff >= 100;
            bool all14b = aOk && bOk;
            sb.AppendLine("  (a) alpha samples differing across the rotation: " + alphaBad + "/" + n +
                          " (expected 0; STRUCTURAL - Orb coverage never reads the rotation matrix, so (c) is " +
                          "the leg that can actually fail)  " + Verdict(aOk));
            sb.AppendLine("  (b) RGB samples differing across the rotation: " + rgbDiff + "/" + n +
                          " (expected >= 100)  " + Verdict(bOk));

            // ── (c) the covered set IS the analytic disc, recomputed here from the grid, not read back.
            {
                const float R = 34f;
                int wrong = 0, covered = 0;
                for (int i = 0; i < n; i++)
                {
                    int ix = i % W, iy = i / W;
                    float cx = b.grid.originX + ix * b.grid.pixelSize;
                    float cy = b.grid.originY + iy * b.grid.pixelSize;
                    bool expect = Mathf.Sqrt(cx * cx + cy * cy) <= R;
                    bool got = b.buf.dst[i * 4 + 3] > 0f;
                    if (expect) covered++;
                    if (expect != got) wrong++;
                }
                bool cOk = wrong == 0 && covered > 1000;
                all14b &= cOk;
                sb.AppendLine("  (c) samples where the ROTATED Orb's coverage disagrees with the independently " +
                              "computed disc d <= " + R + ": " + wrong + "/" + n + " (expected 0, over " +
                              covered + " covered)  " + Verdict(cOk));
            }

            // ── (d) the live control: a Box's coverage IS rotation-dependent, so the fixture really rotates.
            {
                var ba = BuildSolid(SolidDef(ShaperSolidForm.Box, 30f, 0f, 0f, 0f), rig, resp,
                                    Solid(new Color(0.75f, 0.6f, 0.4f)));
                Paint(ba);
                var bb = BuildSolid(SolidDef(ShaperSolidForm.Box, 30f, 37f, 61f, 23f), rig, resp,
                                    Solid(new Color(0.75f, 0.6f, 0.4f)));
                Paint(bb);
                int boxAlpha = 0;
                for (int i = 0; i < n; i++)
                    if (ba.buf.dst[i * 4 + 3] != bb.buf.dst[i * 4 + 3]) boxAlpha++;
                bool dOk = boxAlpha >= 100;
                all14b &= dOk;
                sb.AppendLine("  (d) CONTROL - a BOX's alpha differing across the SAME rotation: " + boxAlpha +
                              "/" + n + " (expected >= 100; a 0 here would mean the fixture never rotated " +
                              "anything and (a)/(c) were vacuous)  " + Verdict(dOk));
            }
            sb.AppendLine("  This generator rotates the published NORMAL forward rather than rotating the light");
            sb.AppendLine("  backward as Pyre does (PyreRenderer.cs:4442-4455) - it is the other side of Pyre's own");
            sb.AppendLine("  identity N.(Rot^-1.L) == (Rot.N).L, and it is forced by LR-1.1: a per-particle inverse-");
            sb.AppendLine("  rotated light IS a light owned by a generator, which LR-1.1 forbids outright.");
            sb.Append("  RESULT: " + Verdict(all14b));
            return sb.ToString();
        }

        // ── LT-14c ────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-14c — RING'S EDGE-ON POP (LR-6.5c). Covered-sample count swept across the tilt at which
        /// <c>PyreRenderer.cs:4633</c>'s hard early return fires.
        ///
        /// <b>DEVIATION FROM PART 8's STATED ANGLES, and the reason.</b> Part 8 expects "count &gt; 0 through
        /// 89.4 degrees and exactly 0 at 89.6". That is arithmetically impossible against the ported
        /// threshold: the guard is <c>|cos tilt| &lt; 0.02</c>, and <c>acos(0.02) = 88.854</c> degrees, so the
        /// pop happens between 88.8 and 88.9 — 89 degrees is already zero. The sweep below uses the correct
        /// angles and asserts the SAME defect: non-zero up to the threshold, exactly zero past it, and a 100%
        /// drop in one step.
        ///
        /// <b>Mutation:</b> NONE, and that is stated rather than hidden. This test locks in a KNOWN DEFECT as a
        /// regression guard. If a later task fades the ring out instead of returning hard, this test must be
        /// updated DELIBERATELY — which is the whole reason it exists.
        /// </summary>
        public static string LT14c_RingEdgeOnPop()
        {
            var sb = new StringBuilder("LT-14c Ring's edge-on pop is a discontinuity, locked as a known defect\n");
            float[] tilts = { 80f, 87f, 88f, 88.8f, 88.9f, 89.4f, 89.6f };
            var counts = new int[tilts.Length];
            int n = W * H;

            for (int t = 0; t < tilts.Length; t++)
            {
                var op = ShaperSolids.Compile(SolidDef(ShaperSolidForm.Ring, 40f, 0f, tilts[t]), 0f, 0u);
                var g = ShaperSolids.Build(op);
                var cov = new float[n];
                ShaperSolids.FillTile(g, op, ShaperSampleGrid.Centred(W, H, Px), 0, 0, W, H,
                                      new ShaperSolidEmit { coverage = cov }, 0, W);
                int c = 0; for (int i = 0; i < n; i++) if (cov[i] > 0f) c++;
                counts[t] = c;
                sb.AppendLine("  tilt " + tilts[t].ToString("F1").PadLeft(5) + " deg  |cos| = " +
                              Mathf.Abs(Mathf.Cos(tilts[t] * Mathf.Deg2Rad)).ToString("F5") +
                              "  covered samples " + c);
            }

            bool beforeOk = counts[0] > 0 && counts[1] > 0 && counts[2] > 0 && counts[3] > 0;
            bool afterOk = counts[4] == 0 && counts[5] == 0 && counts[6] == 0;
            double drop = counts[3] > 0 ? 1.0 - (double)counts[4] / counts[3] : 0;
            bool dropOk = drop >= 0.9;
            bool ok = beforeOk && afterOk && dropOk;

            sb.AppendLine("  covered through 88.8 deg: " + beforeOk + " (expected True)");
            sb.AppendLine("  exactly 0 from 88.9 deg on: " + afterOk + " (expected True)");
            sb.AppendLine("  one-step drop 88.8 -> 88.9: " + (drop * 100).ToString("F1") +
                          "% (expected >= 90%)  " + Verdict(dropOk));
            sb.AppendLine("  DEVIATION: Part 8 states the pop lies between 89.4 and 89.6 deg. acos(0.02) = 88.854 deg,");
            sb.AppendLine("  so 89.0 and 89.4 are ALREADY zero and the contract's expected values cannot hold against");
            sb.AppendLine("  the ported guard. The defect asserted is the same one; only the angles are corrected.");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── LT-15 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-15 — NORMALS ARE UNIT (LR-3.5). Every vector every provider writes, over a dense sweep and 32
        /// rotation triples for Solids: <c>abs(|N| - 1) &lt;= 1e-4</c>, no NaN, no zero vector, no unwritten
        /// sample (NaN sentinel pre-fill, FT-13's method).
        ///
        /// <b>Mutation that makes it fail:</b> omit the <c>nrm = nrm.normalized</c> in
        /// <c>ShaperSolids.BuildFacet</c> (Pyre's <c>:4236</c>) — the facet normals come out with the cross
        /// product's raw magnitude and the sweep fails at the first face.
        /// </summary>
        public static string LT15_NormalsAreUnit()
        {
            var sb = new StringBuilder("LT-15 every provider writes a UNIT, finite, non-zero normal at every sample\n");
            bool all = true;
            int n = W * H;
            var grid = ShaperSampleGrid.Centred(W, H, Px);

            Func<float[], string, bool> check = (nrm, label) =>
            {
                int unwritten = 0, nan = 0, zero = 0, notUnit = 0;
                double worst = 0;
                for (int i = 0; i < n; i++)
                {
                    float x = nrm[i * 3], y = nrm[i * 3 + 1], z = nrm[i * 3 + 2];
                    if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z))
                    {
                        // The sentinel pre-fill is NaN, so an unwritten sample is indistinguishable from a
                        // written NaN — both are failures and both are counted.
                        unwritten++; nan++; continue;
                    }
                    double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
                    if (len == 0) zero++;
                    double e = Math.Abs(len - 1.0);
                    if (e > worst) worst = e;
                    if (e > 1e-4) notUnit++;
                }
                bool ok = unwritten == 0 && nan == 0 && zero == 0 && notUnit == 0;
                sb.AppendLine("  " + label.PadRight(30) + " unwritten/NaN " + unwritten + ", zero " + zero +
                              ", non-unit " + notUnit + "/" + n + ", worst ||N|-1| = " + worst.ToString("E3") +
                              " (expected 0,0,0 and <= 1e-4)  " + Verdict(ok));
                return ok;
            };

            {
                var nrm = new float[n * 3];
                for (int i = 0; i < nrm.Length; i++) nrm[i] = float.NaN;
                var op = ShaperNormalOp.Default;
                op.cx = 0.6f; op.cy = -0.3f; op.cz = 0.8f;      // deliberately NOT unit as authored
                ShaperNormals.FillTile(op, grid, 0, 0, W, H, null, null, nrm, 0, W, 0, W);
                all &= check(nrm, "Constant (authored non-unit)");
            }
            {
                var nrm = new float[n * 3];
                for (int i = 0; i < nrm.Length; i++) nrm[i] = float.NaN;
                var op = ShaperNormalOp.Default;
                op.cx = 0f; op.cy = 0f; op.cz = 0f;            // degenerate: LR-3.5 says write (0,0,1)
                ShaperNormals.FillTile(op, grid, 0, 0, W, H, null, null, nrm, 0, W, 0, W);
                all &= check(nrm, "Constant (degenerate zero)");
            }

            foreach (var form in new[] { ShaperSolidForm.Box, ShaperSolidForm.Pyramid, ShaperSolidForm.Can,
                                         ShaperSolidForm.Gem, ShaperSolidForm.Orb, ShaperSolidForm.Ring })
            {
                int unwritten = 0, nan = 0, zero = 0, notUnit = 0;
                double worst = 0;
                var nrm = new float[n * 3];
                for (int t = 0; t < 32; t++)
                {
                    for (int i = 0; i < nrm.Length; i++) nrm[i] = float.NaN;
                    float yaw = (t * 47f) % 360f, tilt = (t * 29f) % 80f, roll = (t * 113f) % 360f;
                    var op = ShaperSolids.Compile(SolidDef(form, 30f, yaw, tilt, roll, 1.1f), 0f, 0u);
                    var g = ShaperSolids.Build(op);
                    ShaperSolids.FillTile(g, op, grid, 0, 0, W, H,
                                          new ShaperSolidEmit { normal = nrm }, 0, W);
                    for (int i = 0; i < n; i++)
                    {
                        float x = nrm[i * 3], y = nrm[i * 3 + 1], z = nrm[i * 3 + 2];
                        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) { unwritten++; nan++; continue; }
                        double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
                        if (len == 0) zero++;
                        double e = Math.Abs(len - 1.0);
                        if (e > worst) worst = e;
                        if (e > 1e-4) notUnit++;
                    }
                }
                bool ok = unwritten == 0 && nan == 0 && zero == 0 && notUnit == 0;
                all &= ok;
                sb.AppendLine("  Solids " + form.ToString().PadRight(23) + " unwritten/NaN " + unwritten +
                              ", zero " + zero + ", non-unit " + notUnit + "/" + (n * 32) +
                              ", worst ||N|-1| = " + worst.ToString("E3") + "  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-16 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-16 — THE TWO FAMILIES AGREE BECAUSE THEY SHARE A FRAME, NOT ONLY A LAW. One directional light,
        /// azimuth 0, elevation 45. (i) a Solids Box of half-extent 20 px centred on the canvas; (ii) a
        /// Silhouette square of half-extent 20 px with <c>Constant</c> set to the Box's +Z face normal.
        /// Compare <c>L</c> at the centre sample: identical to 1e-6, and the encoded bytes identical.
        ///
        /// <b>Mutation that makes it fail:</b> leave Solids' light at <c>gemLightDistance * R</c>
        /// (<c>PyreRenderer.cs:4265</c>) instead of the absolute canvas position of LR-1.6 — the two families
        /// are then lit by lights in different places and disagree by a visible margin.
        ///
        /// <b>THE STRONGEST SINGLE TEST IN THE SET</b>, because it is the only one that can fail while every
        /// other test passes, and it is the one that makes LR-1.5 falsifiable rather than decorative.
        /// </summary>
        public static string LT16_SharedFrame()
        {
            var sb = new StringBuilder("LT-16 the two families share a FRAME, not only a law\n");
            bool all = true;

            var dirRig = RigOf(Color.white, 0.15f, Dir(0f, 45f, new Color(1f, 0.95f, 0.9f), 1f, 0.9f));
            var resp = Resp(true, 1f, 0f, 2.2f, 0.9f, 24f, Color.white, new Vector3(0f, 0f, 1f));
            var albedo = Solid(new Color(0.7f, 0.7f, 0.7f));

            var box = BuildSolid(SolidDef(ShaperSolidForm.Box, 20f), dirRig, resp, albedo, 64, 64);
            Paint(box);
            var sq = BuildLit(RectFilled(20f, albedo), dirRig, resp, 64, 64);
            Paint(sq);

            int i = Index(sq, 0f, 0f);
            float bR = box.buf.dst[i * 4 + 0], sR = sq.buf.dst[i * 4 + 0];
            double err = Math.Abs(bR - sR);
            bool fOk = err <= 1e-6;
            all &= fOk;

            var bpx = Encoded(box)[i];
            var spx = Encoded(sq)[i];
            bool byteOk = bpx.r == spx.r && bpx.g == spx.g && bpx.b == spx.b;
            all &= byteOk;

            sb.AppendLine("  Solids Box (+Z face, normal (0,0,1), surface point z = +20 px)  destination R = " +
                          bR.ToString("F8"));
            sb.AppendLine("  Silhouette square, Constant(0,0,1), surface point z = 0        destination R = " +
                          sR.ToString("F8"));
            sb.AppendLine("  |difference| = " + err.ToString("E3") + " (expected <= 1e-6)  " + Verdict(fOk));
            sb.AppendLine("  encoded bytes: Box (" + bpx.r + "," + bpx.g + "," + bpx.b + ")  square (" +
                          spx.r + "," + spx.g + "," + spx.b + ")  identical: " + byteOk + "  " + Verdict(byteOk));

            // (c) — THE FRAME LEG, and the one that actually makes LR-1.5 falsifiable in this implementation.
            //
            // Leg (a) above uses the directional light Part 8 specifies, and a directional light has no
            // position and no falloff — so it cannot detect a family lit in its own frame at all. This leg
            // fixes that: an OFF-CENTRE solid at canvas (20, -12), a Box flattened to depth 0.001 so its front
            // face sits on the base plane like the square does, and a POINT light whose attenuation therefore
            // depends entirely on WHERE IN THE CANVAS the sample is. If Solids published its surface point in
            // its own local frame — which is what `ldist = gemLightDistance * R` (PyreRenderer.cs:4265)
            // amounts to — the two would be lit by a lamp at different distances and disagree visibly.
            {
                var ptRig = RigOf(Color.white, 0.05f, Pnt(0f, 0f, 30f, 30f, Color.white, 1f, 0f));
                var flatResp = Resp(true, 1f, 0f, 2.2f, 0f, 48f, Color.white, new Vector3(0f, 0f, 1f));

                var offDef = SolidDef(ShaperSolidForm.Box, 20f, 0f, 0f, 0f, 0f, 1f, 0.00005f);
                offDef.centreX = new ZUIValue(20f);
                offDef.centreY = new ZUIValue(-12f);
                var offBox = BuildSolid(offDef, ptRig, flatResp, albedo, 64, 64);
                Paint(offBox);

                var offSq = Rect("Square", 20f, 20f, 20f, -12f);
                offSq.fill = albedo;
                var offSqR = BuildLit(offSq, ptRig, flatResp, 64, 64);
                Paint(offSqR);

                int j = Index(offSqR, 20f, -12f);
                float ob = offBox.buf.dst[j * 4 + 0], os = offSqR.buf.dst[j * 4 + 0];
                double e2 = Math.Abs(ob - os);
                bool cOk = e2 <= 1e-4;
                all &= cOk;

                // The control: the SAME solid lit as if it sat at the canvas centre reads a different number,
                // so the leg is not vacuous.
                var ctrDef = SolidDef(ShaperSolidForm.Box, 20f, 0f, 0f, 0f, 0f, 1f, 0.00005f);
                var ctrBox = BuildSolid(ctrDef, ptRig, flatResp, albedo, 64, 64);
                Paint(ctrBox);
                float oc = ctrBox.buf.dst[Index(offSqR, 0f, 0f) * 4 + 0];

                sb.AppendLine("  (c) OFF-CENTRE at canvas (20,-12) under a POINT light at (0,0,30) range 30 -");
                sb.AppendLine("      the leg a directional light structurally cannot test:");
                sb.AppendLine("        Solids Box (depth ~0, front face on the base plane) R = " + ob.ToString("F8"));
                sb.AppendLine("        Silhouette square, same place, Constant(0,0,1)      R = " + os.ToString("F8"));
                sb.AppendLine("      |difference| = " + e2.ToString("E3") + " (expected <= 1e-4)  " + Verdict(cOk));
                sb.AppendLine("      CONTROL - the same solid at the canvas CENTRE reads " + oc.ToString("F8") +
                              ", i.e. " + Math.Abs(oc - ob).ToString("E3") + " away, so the lamp genuinely");
                sb.AppendLine("      varies across the canvas and a solid lit in its own frame would be caught here.");
            }

            // The point-light leg, reported as an OBSERVATION and not asserted: with a point light the two
            // legitimately differ, by exactly the amount the box's +Z face standing 20 px out of the base
            // plane implies. That difference IS the "consistent, not identical" promise, so asserting equality
            // here would assert something the contract explicitly denies.
            {
                var ptRig = RigOf(Color.white, 0.15f, Pnt(0f, 0f, 40f, 40f, Color.white, 1f, 0.9f));
                var pbox = BuildSolid(SolidDef(ShaperSolidForm.Box, 20f), ptRig, resp, albedo, 64, 64);
                Paint(pbox);
                var psq = BuildLit(RectFilled(20f, albedo), ptRig, resp, 64, 64);
                Paint(psq);
                float pb = pbox.buf.dst[i * 4 + 0], pssq = psq.buf.dst[i * 4 + 0];
                sb.AppendLine("  OBSERVATION (not asserted) - under a POINT light at (0,0,40) range 40, the same two");
                sb.AppendLine("    read " + pb.ToString("F8") + " and " + pssq.ToString("F8") + ", differing by " +
                              Math.Abs(pb - pssq).ToString("E3") + ". They SHOULD: the box's front face stands 20 px");
                sb.AppendLine("    out of the base plane, so it is genuinely 20 px nearer the lamp. Predicted atten");
                sb.AppendLine("    ratio " + (1.0 / (1.0 + 400.0 / 1600.0) / (1.0 / (1.0 + 1600.0 / 1600.0))).ToString("F5") +
                              ". That is ShaperLightRig.ConsistentNotIdentical, measured.");
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        static ShaperNode RectFilled(float half, ShaperFillDef fill)
        {
            var r = Rect("Square", half, half, 0f, 0f);
            r.fill = fill;
            return r;
        }

        // ── LT-18 (FIX PASS) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-18 — <b>NO DIAL, AT ANY DEGENERATE VALUE, PUTS A NON-FINITE FLOAT INTO THE DESTINATION.</b>
        /// Added by the fix pass. This is the leg whose absence let a negative <c>rimPower</c> ship.
        ///
        /// <b>What it was.</b> <c>CompileResponse</c> clamped nothing. <c>Shade</c> evaluates
        /// <c>Mathf.Pow(1 - ndv, resp.rimPower)</c>, and on a flat normal <c>ndv</c> clamps to exactly 1 so
        /// the base is exactly 0: a negative exponent yields +Infinity, and the rim is then tinted by the
        /// ambient, so a BLACK ambient turns that Infinity into NaN via <c>0 * Inf</c>. Measured through a
        /// full 128x128 render of a rect fill, before the fix:
        ///
        ///   rimPower = 2.2  (baseline)   -&gt;      0 NaN,      0 Inf,     0 garbage pixels
        ///   rimPower = -1,  ambient 0.2  -&gt;      0 NaN, 19 200 Inf,     0 garbage pixels
        ///   rimPower = -1,  ambient 0.0  -&gt; 19 200 NaN,      0 Inf, 6 400 garbage pixels
        ///
        /// <b>Why a whole sweep and not a rimPower test.</b> <c>rimPower</c> was found only because somebody
        /// looked at it. So this leg feeds EVERY dial on the rig, the response block and the Solids generator
        /// its degenerate values — negative, zero, +Infinity, -Infinity, NaN and both huge magnitudes — and
        /// asserts the count of non-finite floats reaching <c>dst</c> is exactly 0 across all of them.
        /// <c>posX/posY/posZ</c> were a second, independent NaN path nobody had looked at either: a NaN
        /// position gives a NaN <c>dist</c>, which fails <c>dist &gt; 1e-4f</c>, so <c>ldx = NaN * inv</c>;
        /// an INFINITE position gives <c>dist = Inf</c>, <c>inv = 0</c>, and <c>Inf * 0 = NaN</c>.
        ///
        /// <b>Mutations that make it fail:</b> remove any single <c>Dial(...)</c> wrapper in
        /// <c>ShaperLightCompiler.CompileResponse</c> or <c>Compile</c>, or in <c>ShaperSolids.Compile</c>.
        /// Leg (e) is the standing proof that the instrument can see one: it drives the law directly with an
        /// UNCLAMPED negative rimPower and asserts the output IS non-finite, so a clamp silently moving into
        /// the law (where LR-2.2 forbids it) would show up as (e) going green when it should be red.
        /// </summary>
        public static string LT18_DegenerateDialsNeverGoNonFinite()
        {
            var sb = new StringBuilder("LT-18 no dial at any degenerate value writes a non-finite float (fix pass)\n");
            bool all = true;

            float[] bad = { -1f, 0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, -1e30f, 1e30f };
            string[] badName = { "-1", "0", "+Inf", "-Inf", "NaN", "-1e30", "1e30" };

            // Ambients 0.2 AND 0.0 — the black ambient is what turned the Infinity into a NaN, so a sweep
            // that only used a lit ambient would have found half the defect.
            float[] ambients = { 0.2f, 0f };

            long totalNonFinite = 0, totalGarbagePx = 0;
            int cases = 0, worstCase = 0; string worstName = "-";

            for (int ai = 0; ai < ambients.Length; ai++)
            {
                float amb = ambients[ai];
                for (int v = 0; v < bad.Length; v++)
                {
                    float x = bad[v];

                    // ── the RESPONSE block, on a tilted normal AND on a flat one (where ndv == 1 exactly and
                    //    the rim base is exactly 0, which is what produced the Infinity).
                    for (int rd = 0; rd < 5; rd++)
                        for (int flat = 0; flat < 2; flat++)
                        {
                            var resp = Resp(true, 1f, 1f, 2.2f, 0.9f, 48f, null,
                                            flat == 1 ? new Vector3(0f, 0f, 1f) : new Vector3(0.55f, 0.25f, 0.8f));
                            string dn;
                            switch (rd)
                            {
                                case 0: resp.intensityScale = new ZUIValue(x); dn = "resp.intensityScale"; break;
                                case 1: resp.rimStrength = new ZUIValue(x); dn = "resp.rimStrength"; break;
                                case 2: resp.rimPower = new ZUIValue(x); dn = "resp.rimPower"; break;
                                case 3: resp.specular = new ZUIValue(x); dn = "resp.specular"; break;
                                default: resp.specularPower = new ZUIValue(x); dn = "resp.specularPower"; break;
                            }
                            int nf = SweepOnce(resp, amb, null, out int gpx);
                            totalNonFinite += nf; totalGarbagePx += gpx; cases++;
                            if (nf > worstCase)
                            {
                                worstCase = nf;
                                worstName = dn + " = " + badName[v] + ", amb " + amb + (flat == 1 ? ", FLAT" : "");
                            }
                        }

                    // ── the RIG: the ambient, and every dial on a light of each kind.
                    for (int ld = 0; ld < 8; ld++)
                    {
                        var rg = RigOf(Color.white, amb, Dir(-55f, 36f, Color.white, 0.9f, 0.9f),
                                       Pnt(-26f, 26f, 26f, 34f, Color.white, 1.1f, 1f));
                        string dn;
                        switch (ld)
                        {
                            case 0: rg.ambientIntensity = new ZUIValue(x); dn = "rig.ambientIntensity"; break;
                            case 1: rg.lights[0].intensity = new ZUIValue(x); dn = "light.intensity"; break;
                            case 2: rg.lights[0].specular = new ZUIValue(x); dn = "light.specular"; break;
                            case 3: rg.lights[0].yaw = new ZUIValue(x); dn = "light.yaw"; break;
                            case 4: rg.lights[0].pitch = new ZUIValue(x); dn = "light.pitch"; break;
                            case 5: rg.lights[1].posX = new ZUIValue(x); dn = "light.posX"; break;
                            case 6: rg.lights[1].posZ = new ZUIValue(x); dn = "light.posZ"; break;
                            default: rg.lights[1].range = new ZUIValue(x); dn = "light.range"; break;
                        }
                        var resp = Resp(true, 1f, 0.8f, 2.2f, 0.9f, 48f, null, new Vector3(0.55f, 0.25f, 0.8f));
                        int nf = SweepOnce(resp, amb, rg, out int gpx);
                        totalNonFinite += nf; totalGarbagePx += gpx; cases++;
                        if (nf > worstCase) { worstCase = nf; worstName = dn + " = " + badName[v] + ", amb " + amb; }
                    }

                    // ── the SOLIDS generator: every dial, on every form. A NaN aspect reaches the surface
                    //    point through the barycentric interpolation and out through `pz` into the law.
                    foreach (ShaperSolidForm form in Enum.GetValues(typeof(ShaperSolidForm)))
                        for (int sd = 0; sd < 14; sd++)
                        {
                            var def = SolidDef(form, 30f, 35f, 28f, 12f, 1.1f);
                            def.edgeGlow = new ZUIValue(0.4f); def.innerGlow = new ZUIValue(0.4f);
                            var dial = (ShaperSolidDial)sd;
                            switch (dial)
                            {
                                case ShaperSolidDial.Size: def.size = new ZUIValue(x); break;
                                case ShaperSolidDial.Centre: def.centreX = new ZUIValue(x); def.centreY = new ZUIValue(x); break;
                                case ShaperSolidDial.Aspect: def.aspect = new ZUIValue(x); break;
                                case ShaperSolidDial.Depth: def.depth = new ZUIValue(x); break;
                                case ShaperSolidDial.GemSides: def.gemSides = new ZUIValue(x); break;
                                case ShaperSolidDial.GemCrown: def.gemCrown = new ZUIValue(x); break;
                                case ShaperSolidDial.GemPavilion: def.gemPavilion = new ZUIValue(x); break;
                                case ShaperSolidDial.RingInner: def.ringInner = new ZUIValue(x); break;
                                case ShaperSolidDial.Yaw: def.yaw = new ZUIValue(x); break;
                                case ShaperSolidDial.Tilt: def.tilt = new ZUIValue(x); break;
                                case ShaperSolidDial.Roll: def.roll = new ZUIValue(x); break;
                                case ShaperSolidDial.LineWidth: def.lineWidth = new ZUIValue(x); break;
                                case ShaperSolidDial.EdgeGlow: def.edgeGlow = new ZUIValue(x); break;
                                default: def.innerGlow = new ZUIValue(x); break;
                            }
                            var rg = RigOf(Color.white, amb, Dir(-55f, 36f, Color.white, 0.9f, 0.9f),
                                           Pnt(-26f, 26f, 26f, 34f, Color.white, 1.1f, 1f));
                            var resp = Resp(true, 1f, 0.8f, 2.2f, 0.9f, 48f);
                            int nf = SolidSweepOnce(def, rg, resp, out int gpx);
                            totalNonFinite += nf; totalGarbagePx += gpx; cases++;
                            if (nf > worstCase) { worstCase = nf; worstName = form + "." + dial + " = " + badName[v] + ", amb " + amb; }
                        }
                }
            }

            bool finiteOk = totalNonFinite == 0 && totalGarbagePx == 0;
            all &= finiteOk;
            sb.AppendLine("  degenerate cases driven: " + cases + " (every dial on the rig, the response block " +
                          "and all six Solids forms x {-1, 0, +Inf, -Inf, NaN, -1e30, 1e30} x ambient {0.2, 0.0})");
            sb.AppendLine("  NON-FINITE FLOATS REACHING dst: " + totalNonFinite + " (expected EXACTLY 0; " +
                          "measured 19200 for rimPower = -1 alone before the fix)  " + Verdict(totalNonFinite == 0));
            sb.AppendLine("  GARBAGE ENCODED PIXELS: " + totalGarbagePx + " (expected EXACTLY 0; measured 6400 " +
                          "for rimPower = -1 on a black ambient before the fix)  " + Verdict(totalGarbagePx == 0));
            if (worstCase > 0) sb.AppendLine("  worst case: " + worstName + " -> " + worstCase + " non-finite floats");

            // ── (d) THE RANGE DIAL DOES NOT INVERT NEAR ZERO. LR-2.4.
            //
            // Before the fix `invRangeSq = range > 1e-6f ? 1/(range*range) : 0f`, and `invRangeSq == 0` is
            // the value that means NO FALLOFF rather than NO REACH, so the dial reversed at the bottom of its
            // travel. Measured at canvas (0,0) with the light at (0,0,40):
            //   range   40      1         1e-2  1e-4  1e-5  1e-6      1e-7      0
            //   L       0.5000  0.000625  0     0     0     1.000000  1.000000  1.000000
            // The darkest reachable setting was 1e-5; at 1e-6 and below the light SNAPPED to full,
            // unattenuated, infinite reach.
            {
                float[] ranges = { 1e4f, 40f, 10f, 1f, 1e-2f, 1e-4f, 1e-5f, 1e-6f, 1e-7f, 0f, -1f, float.NaN, float.PositiveInfinity };
                string[] rn = { "1e4", "40", "10", "1", "1e-2", "1e-4", "1e-5", "1e-6", "1e-7", "0", "-1", "NaN", "+Inf" };
                var Ls = new float[ranges.Length];
                for (int i = 0; i < ranges.Length; i++)
                {
                    var rg = RigOf(Color.black, 0f, Pnt(0f, 0f, 40f, ranges[i], Color.white, 1f, 0f));
                    var pg = ShaperLightCompiler.Compile(rg, 0f, 0u);
                    var rc = ShaperLightCompiler.CompileResponse(Resp(true, 1f, 0f), "L", 0f, 0u, pg);
                    ShaperLightLaw.Shade(pg.rig, rc, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 1f,
                                         out float lr, out _, out _, out _, out _, out _);
                    Ls[i] = lr;
                }
                int inversions = 0;
                for (int i = 1; i < 10; i++) if (Ls[i] > Ls[i - 1] + 1e-7f) inversions++;
                bool zeroReach = Ls[9] <= 1e-6f;
                bool negSame = Mathf.Abs(Ls[10] - Ls[9]) <= 1e-9f;
                bool nanSafe = !float.IsNaN(Ls[11]) && !float.IsInfinity(Ls[11]);
                bool infSafe = !float.IsNaN(Ls[12]) && !float.IsInfinity(Ls[12]);
                bool rOk = inversions == 0 && zeroReach && negSame && nanSafe && infSafe;
                all &= rOk;
                var tbl = new StringBuilder();
                for (int i = 0; i < ranges.Length; i++) tbl.Append(rn[i]).Append("=").Append(Ls[i].ToString("F6")).Append(" ");
                sb.AppendLine("  (d) the range dial, swept ACROSS the old discontinuity (light at (0,0,40), sample at (0,0), ambient black):");
                sb.AppendLine("      " + tbl.ToString().Trim());
                sb.AppendLine("      inversions as the range shrinks: " + inversions + " (expected 0; the old build " +
                              "had one, 1e-5 -> 1e-6 snapping 0 -> 1.000000)  " + Verdict(inversions == 0));
                sb.AppendLine("      range == 0 gives L = " + Ls[9].ToString("F6") + " (expected 0 - LR-2.4: a " +
                              "vanishing range is a vanishing REACH, not a vanished falloff)  " + Verdict(zeroReach));
                sb.AppendLine("      range == -1 matches range == 0: " + negSame + "; NaN finite: " + nanSafe +
                              "; +Inf finite: " + infSafe + "  " + Verdict(negSame && nanSafe && infSafe));
            }

            // ── (e) THE INSTRUMENT'S OWN CONTROL. Drive the LAW directly with an unclamped rimPower of -1 and
            //       assert the output IS non-finite. If this ever goes finite, either the law started clamping
            //       (which LR-2.2 forbids) or the probe stopped measuring, and either way the zeroes above
            //       stop meaning anything.
            {
                var rig = new ShaperLightRigCompiled { count = 0, ambR = 0.2f, ambG = 0.2f, ambB = 0.2f };
                var resp = new ShaperResponseCompiled
                {
                    receive = 1, intensityScale = 1f, rimStrength = 1f, rimPower = -1f,
                    specular = 0.9f, specularPower = 48f, specTintR = 1f, specTintG = 1f, specTintB = 1f,
                };
                ShaperLightLaw.Shade(rig, resp, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 1f,
                                     out _, out _, out _, out float sr, out _, out _);
                bool ctrlOk = float.IsInfinity(sr) || float.IsNaN(sr);
                all &= ctrlOk;
                sb.AppendLine("  (e) CONTROL - the LAW, handed an UNCLAMPED rimPower = -1 on a flat normal, returns S.r = " +
                              sr + " (expected non-finite; this is exactly what the compile clamp protects against, " +
                              "and a finite value here would mean the zeroes above prove nothing)  " + Verdict(ctrlOk));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>One degenerate Silhouette render. Returns non-finite floats in dst; garbage pixels out.</summary>
        static int SweepOnce(ShaperLightResponse resp, float amb, ShaperLightRig rig, out int garbagePx)
        {
            var r = Rect("R", 40f, 40f, 0f, 0f);
            r.fill = Solid(new Color(0.8f, 0.6f, 0.4f));
            var lit = BuildLit(r, rig ?? RigOf(Color.white, amb, Dir(-55f, 36f, Color.white, 0.9f, 0.9f)),
                               resp, 80, 80);
            Paint(lit);
            return CountNonFinite(lit.buf.dst, 80 * 80, out garbagePx);
        }

        /// <summary>One degenerate Solids render.</summary>
        static int SolidSweepOnce(ShaperSolidDef def, ShaperLightRig rig, ShaperLightResponse resp, out int garbagePx)
        {
            var r = BuildSolid(def, rig, resp, Solid(new Color(0.8f, 0.6f, 0.4f)), 80, 80);
            Paint(r);
            return CountNonFinite(r.buf.dst, 80 * 80, out garbagePx);
        }

        /// <summary>
        /// Non-finite floats in a premultiplied destination, and how many PIXELS carry at least one — the two
        /// numbers the fix pass reports, in the same shape the pre-fix measurement used (19 200 floats =
        /// 6 400 pixels x 3 colour channels).
        /// </summary>
        static int CountNonFinite(float[] dst, int n, out int garbagePx)
        {
            int bad = 0; garbagePx = 0;
            for (int i = 0; i < n; i++)
            {
                bool any = false;
                for (int c = 0; c < 4; c++)
                {
                    float v = dst[i * 4 + c];
                    if (float.IsNaN(v) || float.IsInfinity(v)) { bad++; any = true; }
                }
                if (any) garbagePx++;
            }
            return bad;
        }

        // ── LT-19 (FIX PASS) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-19 — <b>EVERY SOLIDS DIAL, ON EVERY FORM, EITHER CHANGES PIXELS OR IS DECLARED INERT. NEVER
        /// NEITHER.</b> Added by the fix pass; this is the leg LR-7.3 needed and never had.
        ///
        /// <b>What it was.</b> <c>ShaperSolidDef</c> documented <c>aspect</c> as "the Y half-extent
        /// multiplier" with no exception and <c>depth</c> with exactly one ("Unused by Can"). Rendering each
        /// form twice at rotation (35, 28, 12) and counting differing encoded pixels for a 1 -&gt; 0.4 sweep
        /// measured, for aspect / depth: Box 3470 / 3014, Pyramid 2589 / 2006, Can 2623 / <b>0</b>, Orb
        /// <b>0</b> / <b>0</b>, Gem <b>0</b> / <b>0</b>, Ring <b>0</b> / <b>0</b>. Five silent zeroes, no
        /// diagnostic, no doc note and no leg — against LR-7.3's own words, "a control that silently does
        /// nothing is the failure B4 says must not survive the rebuild".
        ///
        /// <b>The assertion is two-sided</b>, which matters: a live dial must move at least
        /// <c>MinPixels</c> pixels, AND a dial declared inert must move exactly ZERO. The second half is what
        /// stops the declaration table becoming a place to park an inconvenient result — declaring a working
        /// dial inert fails just as loudly as leaving a dead one silent.
        ///
        /// <b>Mutations that make it fail:</b> (a) delete any row from <c>ShaperSolids.InertReason</c> — the
        /// dial is then expected live and measures 0; (b) add a spurious row (say <c>Aspect</c> on Box) — the
        /// dial is then expected inert and measures thousands; (c) stop feeding <c>aspect</c> into
        /// <c>BuildBoxGeometry</c> — Box.Aspect measures 0 with no declaration.
        /// </summary>
        public static string LT19_EveryDialIsLiveOrDeclaredInert()
        {
            const int MinPixels = 20;
            var sb = new StringBuilder("LT-19 every Solids dial is live or DECLARED inert - never neither (fix pass)\n");
            bool all = true;
            int live = 0, declared = 0, silent = 0, falselyDeclared = 0;

            var rig = RigOf(Color.white, 0.14f, Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.1f, 0.9f));
            var resp = Resp(true, 1f, 0.3f, 2.2f, 0.9f, 48f);
            var fill = Solid(new Color(0.85f, 0.55f, 0.35f));

            foreach (ShaperSolidForm form in Enum.GetValues(typeof(ShaperSolidForm)))
            {
                var row = new StringBuilder("    " + form.ToString().PadRight(8));
                for (int sd = 0; sd < 14; sd++)
                {
                    var dial = (ShaperSolidDial)sd;
                    var ra = BuildSolid(DialFixture(form, dial, false), rig, resp, fill, 96, 96); Paint(ra);
                    var rb = BuildSolid(DialFixture(form, dial, true), rig, resp, fill, 96, 96); Paint(rb);
                    var pa = Encoded(ra); var pb = Encoded(rb);
                    int diff = 0;
                    for (int i = 0; i < pa.Length; i++)
                        if (pa[i].r != pb[i].r || pa[i].g != pb[i].g || pa[i].b != pb[i].b || pa[i].a != pb[i].a) diff++;

                    string inert = ShaperSolids.InertReason(form, dial);
                    bool ok;
                    if (inert == null) { ok = diff >= MinPixels; if (ok) live++; else silent++; }
                    else { ok = diff == 0; if (ok) declared++; else falselyDeclared++; }
                    all &= ok;
                    string dn = dial.ToString();
                    row.Append(" ").Append(dn.Substring(0, Mathf.Min(4, dn.Length)))
                       .Append(":").Append(diff).Append(inert == null ? "" : "*").Append(ok ? "" : "!!");
                }
                sb.AppendLine(row.ToString());
            }

            sb.AppendLine("  legend: <dial>:<differing encoded pixels>, * = DECLARED INERT (must be 0), !! = the assertion failed");
            sb.AppendLine("  live dials moving >= " + MinPixels + " px: " + live +
                          ";  declared-inert dials measuring exactly 0: " + declared);
            sb.AppendLine("  SILENTLY INERT (live by declaration, dead by measurement): " + silent +
                          " (expected 0; this count was 5 before the fix - aspect and depth on Gem and Ring, " +
                          "and depth on Orb)  " + Verdict(silent == 0));
            sb.AppendLine("  FALSELY DECLARED (declared inert, actually moves pixels): " + falselyDeclared +
                          " (expected 0)  " + Verdict(falselyDeclared == 0));

            // The compile-time diagnostic that carries this to a future UI, in hasTooManyLights' exact shape.
            {
                var def = SolidDef(ShaperSolidForm.Gem, 30f, 35f, 28f, 12f, 1.1f, 0.4f, 0.4f);
                var pg = ShaperLightCompiler.Compile(rig, 0f, 0u);
                ShaperSolids.Compile(def, 0f, 0u, pg);
                bool flag = pg.hasInertDial, cnt = pg.inertDialCount == 2;
                bool named = pg.inertDialForm == "Gem" && (pg.inertDialName == "Aspect" || pg.inertDialName == "Depth");
                bool reason = !string.IsNullOrEmpty(pg.inertDialReason) &&
                              pg.inertDialReason.Contains("Gem") && pg.inertDialReason.Contains("Crown");
                bool dOk = flag && cnt && named && reason;
                all &= dOk;
                sb.AppendLine("  DIAGNOSTIC, Gem with aspect 0.4 and depth 0.4: hasInertDial = " + flag +
                              " (True), inertDialCount = " + pg.inertDialCount + " (2), dial \"" + pg.inertDialName +
                              "\" on form \"" + pg.inertDialForm + "\"  " + Verdict(dOk));
                sb.AppendLine("    reason: \"" + (pg.inertDialReason ?? "") + "\"");

                var pg2 = ShaperLightCompiler.Compile(rig, 0f, 0u);
                ShaperSolids.Compile(SolidDef(ShaperSolidForm.Gem, 30f, 35f, 28f, 12f, 1.1f), 0f, 0u, pg2);
                bool quiet = !pg2.hasInertDial && pg2.inertDialCount == 0;
                all &= quiet;
                sb.AppendLine("  a Gem with aspect and depth left at 1 (neutral) raises nothing: hasInertDial = " +
                              pg2.hasInertDial + " (expected False - the complaint is about a control the author " +
                              "OPERATED and got nothing from, not about a field's existence)  " + Verdict(quiet));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>One dial at its neutral value or swept, everything else fixed. LT-19's fixture.</summary>
        static ShaperSolidDef DialFixture(ShaperSolidForm form, ShaperSolidDial dial, bool swept)
        {
            var d = SolidDef(form, 34f, 35f, 28f, 12f, 1.1f);
            d.gemCrown = new ZUIValue(0.55f); d.gemPavilion = new ZUIValue(0.85f);
            d.gemSides = new ZUIValue(6f); d.ringInner = new ZUIValue(0.55f);
            d.edgeGlow = new ZUIValue(0f); d.innerGlow = new ZUIValue(0f);
            if (!swept) return d;
            switch (dial)
            {
                case ShaperSolidDial.Size: d.size = new ZUIValue(20f); break;
                case ShaperSolidDial.Centre: d.centreX = new ZUIValue(9f); break;
                case ShaperSolidDial.Aspect: d.aspect = new ZUIValue(0.4f); break;
                case ShaperSolidDial.Depth: d.depth = new ZUIValue(0.4f); break;
                case ShaperSolidDial.GemSides: d.gemSides = new ZUIValue(3f); break;
                case ShaperSolidDial.GemCrown: d.gemCrown = new ZUIValue(1.3f); break;
                case ShaperSolidDial.GemPavilion: d.gemPavilion = new ZUIValue(0.15f); break;
                case ShaperSolidDial.RingInner: d.ringInner = new ZUIValue(0.15f); break;
                case ShaperSolidDial.Yaw: d.yaw = new ZUIValue(72f); break;
                case ShaperSolidDial.Tilt: d.tilt = new ZUIValue(58f); break;
                case ShaperSolidDial.Roll: d.roll = new ZUIValue(55f); break;
                case ShaperSolidDial.LineWidth: d.lineWidth = new ZUIValue(3.2f); break;
                case ShaperSolidDial.EdgeGlow: d.edgeGlow = new ZUIValue(0.9f); break;
                default: d.innerGlow = new ZUIValue(0.9f); break;
            }
            return d;
        }

        // ── LT-20 (FIX PASS) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-20 — <b>A BORDER IS LIT BY ITS HOST'S RESPONSE BLOCK AND ITS HOST'S NORMAL (LR-5.4).</b> Added
        /// by the fix pass. LR-5.4 is a substantive rule with a trap the contract names in its own text
        /// ("symmetry with BD-3.3 is the obvious wrong answer") and it had NO effective coverage: a border is
        /// constructed in exactly three of the twenty-one light legs and none of the three could detect a
        /// host/border index swap. LT-3 asserts a tiled render equals a whole render, so a normal-source error
        /// changes both sides identically; LT-8 sets <c>receive = false</c>, so the lit-border branch never
        /// executes at all; LT-12 asserts shadows-off equals shadows-on, so the error cancels on both sides.
        /// The code was right; nothing would have told you if it stopped being.
        ///
        /// <b>The assertion.</b> Give the border owner a MATERIALLY DIFFERENT response block and normal from
        /// its host, then render twice — once with the border's own entries set to that different block, once
        /// with them set to the host's. The two must be BIT-IDENTICAL over the border's pixels, because the
        /// border's own entries are never read. Then a control: change the HOST's entries instead, and the
        /// border's pixels MUST move.
        ///
        /// <b>Mutation that makes it fail:</b> swap <c>o</c> for <c>b</c> at
        /// <c>ShaperFillResolver.cs:1227-1229</c> — either half. Lighting a border by its own unset response
        /// block renders every outline unlit; lighting it by the strip's own normal makes an outline read as
        /// "a raised welt around every shape", which is the failure the contract names by name.
        /// </summary>
        public static string LT20_BorderIsLitByItsHost()
        {
            var sb = new StringBuilder("LT-20 a border is lit by its HOST's response and HOST's normal (LR-5.4, fix pass)\n");
            bool all = true;

            var rig = RigOf(Color.white, 0.10f,
                            Dir(-55f, 36f, new Color(1f, 0.9f, 0.8f), 1.2f, 0.9f),
                            Pnt(28f, -20f, 22f, 30f, new Color(0.3f, 0.7f, 1f), 1.4f, 1f));

            // Materially different: a different scale, a different rim, a different specular exponent, a
            // different tint, and a normal pointing somewhere else entirely.
            var hostResp = Resp(true, 1.0f, 0.9f, 2.2f, 0.9f, 12f, new Color(1f, 0.6f, 0.3f), new Vector3(0.72f, 0.30f, 0.62f));
            var otherResp = Resp(true, 0.1f, 0.0f, 6.0f, 0.05f, 220f, new Color(0.1f, 0.2f, 1f), new Vector3(-0.62f, -0.55f, 0.56f));

            Func<ShaperLightResponse, ShaperLightResponse, LRig> build = (hr, br) =>
            {
                var d = Disc("Host", 34f, 0f, 0f);
                d.fill = Solid(new Color(0.85f, 0.55f, 0.35f));
                d.border = new ShaperBorderDef { enabled = true, width = new ZUIValue(5f) };
                var r = BuildLit(d, rig, hr);
                var brc = ShaperLightCompiler.CompileResponse(br, "Border", 0f, 0u, r.prog);
                var bno = ShaperLightCompiler.CompileNormal(br);
                for (int o = 0; o < r.doc.owners.Count && o < r.scene.ownerCapacity; o++)
                    if (r.doc.owners[o].isBorder) { r.scene.response[o] = brc; r.scene.normalOp[o] = bno; }
                Paint(r);
                return r;
            };

            var withOwn = build(hostResp, otherResp);
            var withHost = build(hostResp, hostResp);

            int borderOwner = -1, hostOwner = -1;
            for (int o = 0; o < withOwn.doc.owners.Count; o++)
            {
                if (withOwn.doc.owners[o].isBorder) { if (borderOwner < 0) borderOwner = o; }
                else if (hostOwner < 0) hostOwner = o;
            }
            bool found = borderOwner >= 0 && hostOwner >= 0;
            all &= found;
            sb.AppendLine("  border owner index " + borderOwner + ", host owner index " + hostOwner +
                          " (both must be found)  " + Verdict(found));

            int n = W * H, borderPx = 0, differ = 0;
            if (found)
            {
                int baseB = borderOwner * withOwn.buf.sampleCapacity;
                var pOwn = Encoded(withOwn); var pHost = Encoded(withHost);
                for (int i = 0; i < n; i++)
                {
                    if (!(withOwn.buf.paint[baseB + i] > 0f)) continue;
                    borderPx++;
                    if (pOwn[i].r != pHost[i].r || pOwn[i].g != pHost[i].g ||
                        pOwn[i].b != pHost[i].b || pOwn[i].a != pHost[i].a) differ++;
                }
            }
            bool sameOk = found && borderPx > 200 && differ == 0;
            all &= sameOk;
            sb.AppendLine("  border samples: " + borderPx + " (expected > 200)");
            sb.AppendLine("  border pixels that MOVED when the BORDER's OWN response and normal were replaced: " +
                          differ + "/" + borderPx + " (expected 0 - LR-5.4 says its own entries are never read)  " +
                          Verdict(sameOk));

            if (found)
            {
                var hostChanged = build(otherResp, otherResp);
                var pOwn = Encoded(withOwn); var pHostChanged = Encoded(hostChanged);
                int baseB = borderOwner * withOwn.buf.sampleCapacity;
                int moved = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!(withOwn.buf.paint[baseB + i] > 0f)) continue;
                    if (pOwn[i].r != pHostChanged[i].r || pOwn[i].g != pHostChanged[i].g ||
                        pOwn[i].b != pHostChanged[i].b) moved++;
                }
                bool ctrlOk = moved > 100;
                all &= ctrlOk;
                sb.AppendLine("  CONTROL - border pixels that moved when the HOST's response and normal changed: " +
                              moved + "/" + borderPx + " (expected > 100; a 0 here would mean the border is not " +
                              "lit at all and the assertion above is vacuous)  " + Verdict(ctrlOk));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-21 (FIX PASS) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-21 — <b>THE DOCUMENT OWNS THE LIGHTS, EXERCISED RATHER THAN DECLARED (LR-0.1 / LR-1.1).</b>
        /// Added by the fix pass.
        ///
        /// <b>What it was.</b> <c>ShaperDocument</c> and <c>ShaperLayer</c> — the task's headline deliverable,
        /// "the first document-level authored object in Shaper" — had <b>zero references outside their own
        /// declaration file</b> (2 and 3 total references respectively, all internal). Neither was constructed,
        /// read or exercised by the runtime or by any of the twenty-one audit legs, so <c>canvasWidth</c>,
        /// <c>canvasHeight</c>, <c>pixelSize</c>, <c>layers</c>, <c>phase01</c>, <c>seed</c> and <c>Grid()</c>
        /// were entirely unexercised and the document-to-compile path a renderer would take had never been run.
        ///
        /// <b>What this leg does.</b> It builds a real <c>ShaperDocument</c> — canvas size, pixel size,
        /// document clock, seed, one rig, four <c>ShaperLayer</c>s with DIFFERENT response blocks — and
        /// renders every layer through <c>ShaperLightCompiler.CompileDocument</c> and
        /// <c>ShaperLightCompiler.BindLayer</c>, which IS the document-to-compile path, then asserts the
        /// per-layer responses take effect and that the rig and the grid both came from the document.
        ///
        /// <b>Stated plainly, because it is a finding about the architecture rather than something to paper
        /// over: Wave 2 still has no document-level COMPOSITOR.</b> The document is now genuinely the source
        /// of the rig, the grid, the clock, the seed and each layer's response, and that path is exercised
        /// end-to-end here — but stacking several layers into one picture is not built and is not in this
        /// contract's scope. This leg proves the document is WIRED, not that a document RENDERS.
        ///
        /// <b>Mutations that make it fail:</b> (a) make <c>BindLayer</c> read the rig from anywhere but
        /// <c>document.lightRig</c>; (b) make it apply layer 0's response to every layer; (c) make
        /// <c>Grid()</c> ignore <c>pixelSize</c>.
        /// </summary>
        public static string LT21_DocumentOwnsTheLights()
        {
            var sb = new StringBuilder("LT-21 the document owns the lights, and the document path is RUN (fix pass)\n");
            bool all = true;

            var doc = new ShaperDocument
            {
                name = "LT-21",
                canvasWidth = 96, canvasHeight = 72, pixelSize = 1f,
                phase01 = 0.37f, seed = 909u,
                lightRig = RigOf(Color.white, 0.12f,
                                 Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.1f, 0.9f),
                                 Pnt(24f, -18f, 24f, 30f, new Color(0.35f, 0.7f, 1f), 1.3f, 1f)),
            };

            Func<string, ShaperLightResponse, ShaperLayer> layer = (nm, rp) =>
            {
                var d = Disc(nm, 26f, 0f, 0f);
                d.fill = Solid(new Color(0.85f, 0.55f, 0.35f));
                return new ShaperLayer { name = nm, enabled = true, root = d, response = rp };
            };

            var tilt = new Vector3(0.55f, 0.25f, 0.8f);
            doc.layers.Add(layer("lit", Resp(true, 1f, 0f, 2.2f, 0.9f, 48f, null, tilt)));
            doc.layers.Add(layer("receive off", Resp(false, 1f, 0f, 2.2f, 0.9f, 48f, null, tilt)));
            doc.layers.Add(layer("in shadow", Resp(true, 0f, 0f, 2.2f, 0.9f, 48f, null, tilt)));
            doc.layers.Add(layer("rim 1.5", Resp(true, 1f, 1.5f, 2.2f, 0.9f, 48f, null, tilt)));

            var grid = doc.Grid();
            bool gridOk = grid.pixelSize == doc.pixelSize &&
                          Mathf.Abs(grid.originX + 0.5f * (doc.canvasWidth - 1) * doc.pixelSize) < 1e-4f &&
                          Mathf.Abs(grid.originY + 0.5f * (doc.canvasHeight - 1) * doc.pixelSize) < 1e-4f;
            all &= gridOk;
            sb.AppendLine("  document " + doc.canvasWidth + "x" + doc.canvasHeight + " @ " + doc.pixelSize +
                          ", phase " + doc.phase01 + ", seed " + doc.seed + ", " + doc.layers.Count + " layers, " +
                          doc.lightRig.lights.Count + " lights on the rig");
            sb.AppendLine("  ShaperDocument.Grid() is canvas-centred with the document's pixelSize: origin (" +
                          grid.originX.ToString("F3") + ", " + grid.originY.ToString("F3") + ")  " + Verdict(gridOk));

            var prog = ShaperLightCompiler.CompileDocument(doc);
            bool rigOk = prog.rig.count == 2 && prog.layerCount == 0;
            all &= rigOk;
            sb.AppendLine("  CompileDocument: rig.count = " + prog.rig.count + " (expected 2, on the DOCUMENT's " +
                          "clock per LR-1.8), layers not yet bound (" + prog.layerCount + ")  " + Verdict(rigOk));

            int n = doc.canvasWidth * doc.canvasHeight;
            var results = new List<Color32[]>();
            for (int li = 0; li < doc.layers.Count; li++)
            {
                var lay = doc.layers[li];
                var fdoc = ShaperFillResolver.Resolve(lay.root, doc.phase01, doc.seed,
                                                      0.5f * (doc.canvasWidth - 1) * doc.pixelSize,
                                                      0.5f * (doc.canvasHeight - 1) * doc.pixelSize,
                                                      ShaperQuantitySet.ShippedShapeEngine);
                var buf = new ShaperFillBuffers(n, Mathf.Max(1, fdoc.owners.Count));
                var scene = ShaperLightCompiler.BindLayer(doc, li, prog, buf.sampleCapacity, buf.ownerCapacity);
                ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, doc.canvasWidth, doc.canvasHeight, buf,
                                             new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                             scene);
                var px = new Color32[n];
                ShaperFillResolver.Encode(buf.dst, px, n);
                results.Add(px);
            }
            bool bound = prog.layerCount == doc.layers.Count && prog.receiverCount == 3;
            all &= bound;
            sb.AppendLine("  after binding every layer: layerCount = " + prog.layerCount + " (expected " +
                          doc.layers.Count + "), receiverCount = " + prog.receiverCount + " (expected 3)  " + Verdict(bound));

            Func<Color32[], long> lum = p => { long s = 0; for (int i = 0; i < p.Length; i++) if (p[i].a > 0) s += p[i].r + p[i].g + p[i].b; return s; };
            long lLit = lum(results[0]), lOff = lum(results[1]), lShadow = lum(results[2]), lRim = lum(results[3]);
            Func<Color32[], Color32[], int> diff = (a, b) => { int d = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b) d++; return d; };
            int dLitOff = diff(results[0], results[1]), dLitShadow = diff(results[0], results[2]), dLitRim = diff(results[0], results[3]);

            bool distinct = dLitOff > 500 && dLitShadow > 500 && dLitRim > 500;
            bool ordered = lShadow < lLit && lRim > lLit;
            all &= distinct && ordered;
            sb.AppendLine("  per-layer responses take effect - pixels differing from the LIT layer:");
            sb.AppendLine("    receive off " + dLitOff + ", in-shadow " + dLitShadow + ", rim 1.5 " + dLitRim +
                          " (each expected > 500)  " + Verdict(distinct));
            sb.AppendLine("    summed luminance: in-shadow " + lShadow + " < lit " + lLit + " < rim " + lRim +
                          " (the direction each block predicts; receive-off " + lOff + " is the raw albedo)  " +
                          Verdict(ordered));

            sb.AppendLine("  NOTE, stated rather than papered over: Wave 2 has NO document-level compositor. The");
            sb.AppendLine("    document is now genuinely the source of the rig, the grid, the clock, the seed and");
            sb.AppendLine("    each layer's response block, and that path is exercised end-to-end here - but");
            sb.AppendLine("    stacking several layers into one picture is not built and is not in this contract's");
            sb.AppendLine("    scope. This leg proves the document is WIRED, not that a document RENDERS.");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-22 (FIX PASS) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-22 — <b>THE HALO AND INNER GLOW ACTUALLY RUN (LR-6.4).</b> Added by the fix pass.
        ///
        /// <b>What it was.</b> <c>edgeGlow</c> and <c>innerGlow</c> appeared exactly once in the 2 221-line
        /// audit — inside the shared <c>SolidDef</c> helper, both hardcoded to <c>new ZUIValue(0f)</c> — and
        /// every Solids fixture and every contact-sheet cell was built through that helper. So
        /// <c>scene.glow</c> was all-zeros in all twenty-one legs and all twenty-four cells, the generator's
        /// halo and inner-glow block never ran, and <c>LightSample</c>'s <c>cr += scene.glow[t3 + 0]</c> added
        /// zero every time. An entire authored feature — and the exact feature D-7 makes a ruling about — had
        /// no coverage of any kind.
        ///
        /// <b>The assertions.</b> (a) the glow sheet is non-zero, on every form; (b) the glow reaches
        /// <c>dst</c> — the picture changes; (c) D-7's ruling, measured: the glow does NOT spill outside the
        /// silhouette, because coverage is hard and multiplies the outside fragment by zero, so the ALPHA
        /// channel is bit-identical with the glow on and off; (d) glow is ADDITIVE and UNLIT, so it still
        /// contributes on a layer with <c>receiveLighting</c> off.
        ///
        /// <b>Mutation that makes it fail:</b> drop the <c>cr += scene.glow[t3 + 0]</c> block at
        /// <c>ShaperFillResolver.cs:1401-1403</c>, or short-circuit the halo block at
        /// <c>ShaperSolids.cs</c>'s <c>if (cov &gt; 0f &amp;&amp; (op.edgeGlow &gt; 0f || op.innerGlow &gt; 0f))</c>.
        /// </summary>
        public static string LT22_GlowPathExecutes()
        {
            var sb = new StringBuilder("LT-22 the halo and inner glow actually run (LR-6.4, fix pass)\n");
            bool all = true;
            int n = W * H;

            var rig = RigOf(Color.white, 0.12f, Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.0f, 0.9f));
            var resp = Resp(true, 1f, 0f, 2.2f, 0.9f, 48f);
            var fill = Solid(new Color(0.5f, 0.35f, 0.22f));

            foreach (ShaperSolidForm form in Enum.GetValues(typeof(ShaperSolidForm)))
            {
                var off = SolidDef(form, 30f, 22f, 30f, 10f, 1.1f);
                var on = SolidDef(form, 30f, 22f, 30f, 10f, 1.1f);
                on.edgeGlow = new ZUIValue(0.9f); on.innerGlow = new ZUIValue(0.8f);
                on.edgeGlowColour = new Color(1f, 0.85f, 0.4f);
                on.innerGlowColour = new Color(0.4f, 0.9f, 1f);

                var ro = BuildSolid(off, rig, resp, fill); Paint(ro);
                var rn = BuildSolid(on, rig, resp, fill); Paint(rn);

                int sheetNonZero = 0;
                for (int i = 0; i < n * 3; i++) if (rn.scene.glow[i] != 0f) sheetNonZero++;

                var po = Encoded(ro); var pn = Encoded(rn);
                int rgbMoved = 0, alphaMoved = 0;
                for (int i = 0; i < n; i++)
                {
                    if (po[i].r != pn[i].r || po[i].g != pn[i].g || po[i].b != pn[i].b) rgbMoved++;
                    if (po[i].a != pn[i].a) alphaMoved++;
                }
                bool ok = sheetNonZero > 100 && rgbMoved > 100 && alphaMoved == 0;
                all &= ok;
                sb.AppendLine("  " + form.ToString().PadRight(8) + " glow sheet non-zero floats " + sheetNonZero +
                              " (> 100), encoded RGB moved " + rgbMoved + " px (> 100), ALPHA moved " +
                              alphaMoved + " px (expected 0 - D-7: hard coverage kills the outside spill)  " + Verdict(ok));
            }

            {
                var on = SolidDef(ShaperSolidForm.Gem, 30f, 22f, 30f, 10f, 1.1f);
                on.edgeGlow = new ZUIValue(0.9f); on.innerGlow = new ZUIValue(0.8f);
                var off = SolidDef(ShaperSolidForm.Gem, 30f, 22f, 30f, 10f, 1.1f);
                var unlitResp = Resp(false);
                var a = BuildSolid(off, rig, unlitResp, fill); Paint(a);
                var b = BuildSolid(on, rig, unlitResp, fill); Paint(b);
                int moved = 0; double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    double d = b.buf.dst[i * 4 + 0] - a.buf.dst[i * 4 + 0];
                    if (d != 0) { moved++; sum += d; }
                }
                bool ok = moved > 100 && sum > 0;
                all &= ok;
                sb.AppendLine("  glow on a receiveLighting = FALSE layer still adds: " + moved +
                              " samples moved, summed delta " + sum.ToString("F3") +
                              " (expected > 100 and > 0 - a glow is light, not paint: LR-5.3 one level down)  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-23 (FIX PASS) ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-23 — <b>RIM IS INERT ON A BLACK AMBIENT, AND THE TOOL SAYS SO (LR-7.2 / LR-7.3).</b> Added by
        /// the fix pass.
        ///
        /// <b>The fact.</b> The contract specifies <c>S += amb · rim</c> (LR-2.3) and the code implements it
        /// faithfully, so on a BLACK ambient the Rim Strength control is wholly dead: measured at the law
        /// level on a tilted normal (0.7, 0, 0.71414) with <c>rimStrength = 5</c>, ambient 0.0 gives
        /// <c>S = 0</c> EXACTLY at every rim strength, against <c>S = 0.06361176</c> at ambient 0.2. "Only my
        /// lamps, no ambient" is an ordinary authoring choice.
        ///
        /// <b>The arithmetic is NOT changed.</b> It is the contract's ruling (LR-2.3) and overturning it is
        /// the owner's call, not the fix pass's. What IS changed is that the inertness is DECLARED: a
        /// compile-time diagnostic in <c>hasTooManyLights</c>' exact shape, plus LR-7.2's mandated sentence
        /// extended to cover the case it did not.
        ///
        /// <b>Mutations that make it fail:</b> (a) delete the <c>hasInertRim</c> block in
        /// <c>ShaperLightCompiler.CompileResponse</c>; (b) raise the diagnostic unconditionally — leg (c)
        /// fails, because a lit ambient must NOT raise it; (c) revert <c>ShaperLightRig.RimNeedsRelief</c> to
        /// its original text — leg (d) fails.
        /// </summary>
        public static string LT23_RimIsInertOnBlackAmbientAndDeclared()
        {
            var sb = new StringBuilder("LT-23 rim is inert on a black ambient, and the tool says so (fix pass)\n");
            bool all = true;

            float[] rims = { 0f, 0.5f, 1f, 2.5f, 5f };
            var blackS = new float[rims.Length];
            var litS = new float[rims.Length];
            for (int i = 0; i < rims.Length; i++)
                for (int k = 0; k < 2; k++)
                {
                    float amb = k == 0 ? 0f : 0.2f;
                    var pg = ShaperLightCompiler.Compile(RigOf(Color.white, amb), 0f, 0u);
                    var rc = ShaperLightCompiler.CompileResponse(Resp(true, 1f, rims[i]), "L", 0f, 0u, pg);
                    ShaperLightLaw.Shade(pg.rig, rc, 0f, 0f, 0f, 0.7f, 0f, 0.71414f, 0f, 0f, 1f,
                                         out _, out _, out _, out float sr, out _, out _);
                    if (k == 0) blackS[i] = sr; else litS[i] = sr;
                }
            bool blackDead = true, litLive = false;
            for (int i = 0; i < rims.Length; i++) { if (blackS[i] != 0f) blackDead = false; if (litS[i] > 0f) litLive = true; }
            all &= blackDead && litLive;
            var t = new StringBuilder();
            for (int i = 0; i < rims.Length; i++)
                t.Append("rim ").Append(rims[i]).Append(": black S=").Append(blackS[i].ToString("F8"))
                 .Append(" / amb0.2 S=").Append(litS[i].ToString("F8")).Append("   ");
            sb.AppendLine("  " + t.ToString().Trim());
            sb.AppendLine("  S is EXACTLY 0 at every rim strength on a black ambient: " + blackDead +
                          "; non-zero on a lit one: " + litLive + "  " + Verdict(blackDead && litLive));

            {
                var pg = ShaperLightCompiler.Compile(RigOf(Color.white, 0f), 0f, 0u);
                ShaperLightCompiler.CompileResponse(Resp(true, 1f, 2f), "Layer A", 0f, 0u, pg);
                ShaperLightCompiler.CompileResponse(Resp(true, 1f, 3f), "Layer B", 0f, 0u, pg);
                bool ok = pg.hasInertRim && pg.inertRimCount == 2 && pg.inertRimNode == "Layer A" &&
                          pg.inertRimReason == ShaperLightRig.RimNeedsBlackAmbientRelief;
                all &= ok;
                sb.AppendLine("  DIAGNOSTIC on a black ambient: hasInertRim = " + pg.hasInertRim + " (True), count = " +
                              pg.inertRimCount + " (2), first node \"" + pg.inertRimNode + "\" (Layer A), reason == " +
                              "ShaperLightRig.RimNeedsBlackAmbientRelief VERBATIM  " + Verdict(ok));
            }

            {
                var lit = ShaperLightCompiler.Compile(RigOf(Color.white, 0.2f), 0f, 0u);
                ShaperLightCompiler.CompileResponse(Resp(true, 1f, 2f), "L", 0f, 0u, lit);
                var noRim = ShaperLightCompiler.Compile(RigOf(Color.white, 0f), 0f, 0u);
                ShaperLightCompiler.CompileResponse(Resp(true, 1f, 0f), "L", 0f, 0u, noRim);
                var offRecv = ShaperLightCompiler.Compile(RigOf(Color.white, 0f), 0f, 0u);
                ShaperLightCompiler.CompileResponse(Resp(false, 1f, 2f), "L", 0f, 0u, offRecv);
                bool ok = !lit.hasInertRim && !noRim.hasInertRim && !offRecv.hasInertRim;
                all &= ok;
                sb.AppendLine("  does NOT fire on: a lit ambient (" + lit.hasInertRim + "), rim 0 (" +
                              noRim.hasInertRim + "), receive off (" + offRecv.hasInertRim +
                              ") - all expected False  " + Verdict(ok));
            }

            {
                string s = ShaperLightRig.RimNeedsRelief;
                bool flatHalf = s.Contains("flat");
                bool ambHalf = s.Contains("ambient is black");
                bool sibling = ShaperLightRig.RimNeedsBlackAmbientRelief.Contains("ambient");
                bool ok = flatHalf && ambHalf && sibling;
                all &= ok;
                sb.AppendLine("  LR-7.2's RimNeedsRelief covers BOTH inert states - flat normal: " + flatHalf +
                              ", black ambient: " + ambHalf + "; the standalone sibling exists: " + sibling +
                              "  " + Verdict(ok));
            }

            sb.AppendLine("  The ARITHMETIC is deliberately unchanged: LR-2.3 specifies `S += amb . rim` and");
            sb.AppendLine("    overturning that is the owner's ruling, not a fix pass's. Flagged in FIX-REPORT.md");
            sb.AppendLine("    with the alternative rather than changed unilaterally.");
            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        // ── LT-24 (T-0127) ────────────────────────────────────────────────────────────────────────────────

        static ShaperFillDef HeightFieldFillLT(ShaperHeightFieldPreset preset, float scale)
            => new ShaperFillDef
            {
                kind = ShaperFillKind.HeightField,
                heightField = preset != null ? preset.field : null,
                heightFieldScale = new ZUIValue(scale),
                heightFieldTint = new Color(0.5f, 0.5f, 0.5f),
                space = ShaperFillSpace.Stamped,
            };

        /// <summary>Population variance of the owner-0 normal vector, over samples where coverage is (near) 1.</summary>
        static double NormalVarianceOnCoveredSamples(LRig r)
        {
            int n = r.width * r.height;
            float[] cov = r.buf.ownCoverage;
            float[] nrm = r.scene.normal;
            double sx = 0, sy = 0, sz = 0; int cnt = 0;
            for (int i = 0; i < n; i++)
                if (cov[i] > 0.99f) { sx += nrm[i * 3]; sy += nrm[i * 3 + 1]; sz += nrm[i * 3 + 2]; cnt++; }
            if (cnt < 2) return 0.0;
            double mx = sx / cnt, my = sy / cnt, mz = sz / cnt;
            double var = 0;
            for (int i = 0; i < n; i++)
                if (cov[i] > 0.99f)
                {
                    double dx = nrm[i * 3] - mx, dy = nrm[i * 3 + 1] - my, dz = nrm[i * 3 + 2] - mz;
                    var += dx * dx + dy * dy + dz * dz;
                }
            return var / cnt;
        }

        /// <summary>
        /// LT-24, T-0127 — a <see cref="ShaperFillKind.HeightField"/> fill perturbs a Solids owner's OWN
        /// analytic normal, and only that: never coverage/edge distance, never a fill kind this task did not
        /// scope in, never a degenerate (non-unit/NaN/zero) vector, at any rotation or at extreme height.
        ///
        /// <b>Mutation that makes leg 1 fail:</b> revert <c>ShaperSolids.FillTile</c>'s perturbation block —
        /// the flat-vs-HeightField variance gap collapses to (0, 0), because a facet's normal is constant
        /// again regardless of the fill.
        /// </summary>
        public static string LT24_SolidsHeightFieldRelief()
        {
            var sb = new StringBuilder("LT-24 T-0127: a HeightField fill perturbs Solids' own analytic normal\n");
            bool all = true;

            var presetPlates = AssetDatabase.LoadAssetAtPath<ShaperHeightFieldPreset>(
                "Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/plates_GEN10_001.asset");
            if (presetPlates == null || presetPlates.field == null)
            {
                sb.AppendLine("  plates_GEN10_001 preset missing or unreadable -- cannot verify.  " + Verdict(false));
                sb.Append("  RESULT: " + Verdict(false));
                return sb.ToString();
            }

            var rig = RigOf(Color.white, 0.10f, Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.0f, 0.9f));
            var flatFill = Solid(new Color(0.5f, 0.5f, 0.5f));   // SAME grey as the HeightField's flat tint (B5)

            // ── leg 1 — the headline claim, measured, not eyeballed ─────────────────────────────────────────
            // yaw=tilt=roll=0 so exactly ONE facet (the +Z face) fills the frame -- no second facet's own,
            // genuinely different constant normal to contaminate the "flat fill" baseline's variance. Same
            // Box, same light, same MEAN albedo (B5's flat tint matches the Solid fill's colour exactly).
            // Only the fill kind differs. A flat fill gives that one facet exactly one constant normal
            // (variance 0 to float precision); a HeightField fill must NOT.
            {
                int w = 96, h = 96;
                var flatR = BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 0f, 0f, 0f, 0f), rig, Resp(true, 1f, 0f), flatFill, w, h);
                Paint(flatR);
                var reliefR = BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 0f, 0f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(presetPlates, 6f), w, h);
                Paint(reliefR);

                double flatVar = NormalVarianceOnCoveredSamples(flatR);
                double reliefVar = NormalVarianceOnCoveredSamples(reliefR);
                bool ok = flatVar < 1e-9 && reliefVar > 1e-6;
                all &= ok;
                sb.AppendLine("  normal variance across one Box's covered samples: flat fill " + flatVar.ToString("E3") +
                              " (expect ~0), HeightField fill " + reliefVar.ToString("E3") + " (expect > 0)  " + Verdict(ok));
            }

            // ── leg 2 — geometry/silhouette untouched: coverage and edge distance are bit-identical ────────
            // between the flat and HeightField fills above, proving the perturbation never reaches them.
            {
                int w = 64, h = 64;
                var flatR = BuildSolid(SolidDef(ShaperSolidForm.Orb, 30f), rig, Resp(true, 1f, 0f), flatFill, w, h);
                Paint(flatR);
                var reliefR = BuildSolid(SolidDef(ShaperSolidForm.Orb, 30f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(presetPlates, 6f), w, h);
                Paint(reliefR);
                int n = w * h, diffCov = 0, diffDist = 0;
                for (int i = 0; i < n; i++)
                {
                    if (flatR.buf.ownCoverage[i] != reliefR.buf.ownCoverage[i]) diffCov++;
                    if (flatR.buf.ownDistance[i] != reliefR.buf.ownDistance[i]) diffDist++;
                }
                bool ok = diffCov == 0 && diffDist == 0;
                all &= ok;
                sb.AppendLine("  silhouette untouched on an Orb: coverage differs on " + diffCov + "/" + n +
                              " samples, edge distance differs on " + diffDist + "/" + n + " (expect 0, 0)  " + Verdict(ok));
            }

            // ── leg 3 — unit/finite normals hold under ROTATION and at an ADVERSARIALLY EXTREME height scale,
            //           on every form. Mirrors LT-15's own check, scoped to the HeightField-perturbed path.
            {
                int w = 56, h = 56;
                foreach (float scale in new[] { 6f, 60f, 600f })
                {
                    int unwritten = 0, nan = 0, zero = 0, notUnit = 0; double worst = 0;
                    foreach (var form in new[] { ShaperSolidForm.Box, ShaperSolidForm.Orb, ShaperSolidForm.Ring })
                        for (int t = 0; t < 12; t++)
                        {
                            float yaw = (t * 31f) % 360f, tilt = (t * 19f) % 80f, roll = (t * 53f) % 360f;
                            var r = BuildSolid(SolidDef(form, 26f, yaw, tilt, roll, 0f), rig, Resp(true, 1f, 0f),
                                               HeightFieldFillLT(presetPlates, scale), w, h);
                            Paint(r);
                            int n = w * h;
                            for (int i = 0; i < n; i++)
                            {
                                if (r.buf.ownCoverage[i] <= 0f) continue;
                                float x = r.scene.normal[i * 3], y = r.scene.normal[i * 3 + 1], z = r.scene.normal[i * 3 + 2];
                                if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) { unwritten++; nan++; continue; }
                                double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
                                if (len == 0) zero++;
                                double e = Math.Abs(len - 1.0);
                                if (e > worst) worst = e;
                                if (e > 1e-3) notUnit++;
                            }
                        }
                    bool ok = unwritten == 0 && nan == 0 && zero == 0 && notUnit == 0;
                    all &= ok;
                    sb.AppendLine("  scale=" + scale.ToString("F0") + ", Box/Orb/Ring x12 rotations each: unwritten/NaN " +
                                  unwritten + ", zero " + zero + ", non-unit " + notUnit +
                                  ", worst ||N|-1| = " + worst.ToString("E3") + "  " + Verdict(ok));
                }
            }

            // ── leg 4 — a fill kind this task explicitly did NOT scope in (Solid, IndexedStrip's constant-
            //           height fallback path) never perturbs: default slopeGain=0 and non-HeightField kinds
            //           both gate the block off, so a Solid fill is bit-for-bit its own pre-T-0127 render.
            {
                int w = 48, h = 48;
                var a = BuildSolid(SolidDef(ShaperSolidForm.Gem, 24f, 15f, 10f, 0f, 0f), rig, Resp(true, 1f, 0f), flatFill, w, h);
                Paint(a);
                var b = BuildSolid(SolidDef(ShaperSolidForm.Gem, 24f, 15f, 10f, 0f, 0f), rig, Resp(true, 1f, 0f), flatFill, w, h);
                Paint(b);
                int n = w * h, diff = 0;
                for (int i = 0; i < n * 3; i++) if (a.scene.normal[i] != b.scene.normal[i]) diff++;
                bool ok = diff == 0;
                all &= ok;
                sb.AppendLine("  a non-HeightField fill (Solid) is unperturbed and deterministic: " + diff +
                              "/" + (n * 3) + " components differ across two identical builds (expect 0)  " + Verdict(ok));
            }

            sb.Append("  RESULT: " + Verdict(all));
            return sb.ToString();
        }

        /// <summary>
        /// LT-24's own visual check, same convention as LT-17: RENDERED, KEPT, must be LOOKED AT.
        /// Flat vs HeightField at three yaws each (rotation must not break the relief), the "lines" preset,
        /// and one deliberately-extreme scale cell for the adversarial self-check.
        /// </summary>
        public static string LT24_ContactSheet(string path)
        {
            const int Cell = 110, Cols = 4, Pad = 8;

            var plates = AssetDatabase.LoadAssetAtPath<ShaperHeightFieldPreset>(
                "Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/plates_GEN10_001.asset");
            var lines = AssetDatabase.LoadAssetAtPath<ShaperHeightFieldPreset>(
                "Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/lines_GEN1_001.asset");

            var rig = RigOf(Color.white, 0.10f, Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.05f, 0.9f));
            var flatFill = Solid(new Color(0.5f, 0.5f, 0.5f));

            var cells = new List<KeyValuePair<string, Func<LRig>>>();
            Action<string, Func<LRig>> C = (nm, f) => cells.Add(new KeyValuePair<string, Func<LRig>>(nm, f));

            float[] yaws = { 15f, 95f, 210f };
            for (int i = 0; i < yaws.Length; i++)
            {
                float yaw = yaws[i];
                C((i * 2 + 1).ToString("00") + " Box FLAT yaw " + yaw.ToString("F0"),
                  () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, yaw, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), flatFill, Cell, Cell));
                C((i * 2 + 2).ToString("00") + " Box plates yaw " + yaw.ToString("F0"),
                  () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, yaw, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, 6f), Cell, Cell));
            }
            C("07 Orb FLAT", () => BuildSolid(SolidDef(ShaperSolidForm.Orb, 34f), rig, Resp(true, 1f, 0f), flatFill, Cell, Cell));
            C("08 Orb plates", () => BuildSolid(SolidDef(ShaperSolidForm.Orb, 34f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, 6f), Cell, Cell));
            C("09 Box lines FLAT", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), flatFill, Cell, Cell));
            C("10 Box lines relief", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(lines, 6f), Cell, Cell));
            C("11 Box plates scale=60 (10x)", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, 60f), Cell, Cell));
            C("12 Box plates scale=600 (100x, adversarial)", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, 600f), Cell, Cell));

            int rows = (cells.Count + Cols - 1) / Cols;
            int texW = Cols * Cell + (Cols + 1) * Pad, texH = rows * Cell + (rows + 1) * Pad;
            var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(22, 22, 26, 255);
            sheet.SetPixels32(bg);

            var cellPx = new Color32[Cell * Cell];
            for (int c = 0; c < cells.Count; c++)
            {
                var r = cells[c].Value();
                Paint(r);
                CompositeOverBackdrop(r.buf.dst, cellPx, Cell);
                DrawLabel(cellPx, Cell, cells[c].Key);
                int col = c % Cols, row = rows - 1 - (c / Cols);
                sheet.SetPixels32(Pad + col * (Cell + Pad), Pad + row * (Cell + Pad), Cell, Cell, cellPx);
            }
            sheet.Apply();
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());

            var sb = new StringBuilder("LT-24 contact sheet (T-0127)\n");
            sb.AppendLine("  " + cells.Count + " cells, " + Cols + " x " + rows + ", written to " + path);
            sb.AppendLine("  01/03/05 vs 02/04/06: same Box, same yaw, flat fill vs plates_GEN10_001 -- the flat");
            sb.AppendLine("     cells must look like a plain lit facet; the plates cells must show real raking-light");
            sb.AppendLine("     relief that survives all three rotations without breaking up or flattening out.");
            sb.AppendLine("  07/08: Orb flat vs plates -- confirms the perturbation composes on the non-facet forms too.");
            sb.AppendLine("  09/10: lines_GEN1_001, thinner detail than plates -- the other T-0111/T-0124 preset named");
            sb.AppendLine("     in the task, for the same visual check.");
            sb.AppendLine("  11/12: scale x10 and x100 over the readable scale -- the adversarial self-check. Look for");
            sb.AppendLine("     inverted-looking bumps, seams or hard discontinuities, not just 'louder'.");
            sb.Append("  RESULT: RENDERED - NOT VERIFIED BY THE TABLE. A passing table is not a picture; this is " +
                      "not a pass until a human has looked at it.");
            return sb.ToString();
        }

        /// <summary>T-0127 — a close-up of the adversarial extreme-scale cells, big enough to actually read the artefact.</summary>
        public static string LT24_ExtremeZoom(string path)
        {
            const int Cell = 220, Cols = 3, Pad = 10;
            var plates = AssetDatabase.LoadAssetAtPath<ShaperHeightFieldPreset>(
                "Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/plates_GEN10_001.asset");
            var rig = RigOf(Color.white, 0.10f, Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.05f, 0.9f));

            var cells = new List<KeyValuePair<string, Func<LRig>>>();
            Action<string, Func<LRig>> C = (nm, f) => cells.Add(new KeyValuePair<string, Func<LRig>>(nm, f));
            C("scale=6 (readable)", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, 6f), Cell, Cell));
            C("scale=60 (10x)", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, 60f), Cell, Cell));
            C("scale=600 (100x)", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, 600f), Cell, Cell));

            int rows = 1;
            int texW = Cols * Cell + (Cols + 1) * Pad, texH = rows * Cell + (rows + 1) * Pad;
            var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(22, 22, 26, 255);
            sheet.SetPixels32(bg);
            var cellPx = new Color32[Cell * Cell];
            for (int c = 0; c < cells.Count; c++)
            {
                var r = cells[c].Value();
                Paint(r);
                CompositeOverBackdrop(r.buf.dst, cellPx, Cell);
                DrawLabel(cellPx, Cell, (c + 1).ToString("00") + " " + cells[c].Key);
                sheet.SetPixels32(Pad + c * (Cell + Pad), Pad, Cell, Cell, cellPx);
            }
            sheet.Apply();
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            return "written to " + path;
        }

        /// <summary>T-0127 — precisely how much the RENDERED PICTURE changes as heightFieldScale climbs, so the
        /// adversarial self-check's verdict rests on a number rather than a screenshot impression.</summary>
        public static string LT24_ExtremeMetric()
        {
            var sb = new StringBuilder("T-0127 extreme-scale metric (Box, plates_GEN10_001)\n");
            var plates = AssetDatabase.LoadAssetAtPath<ShaperHeightFieldPreset>(
                "Assets/Demos/ShaperDemo/TapestryHeightFields/Presets/plates_GEN10_001.asset");
            var rig = RigOf(Color.white, 0.10f, Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.05f, 0.9f));
            int w = 128, h = 128;

            Color32[] Render(float scale)
            {
                var r = BuildSolid(SolidDef(ShaperSolidForm.Box, 34f, 22f, 20f, 0f, 0f), rig, Resp(true, 1f, 0f), HeightFieldFillLT(plates, scale), w, h);
                Paint(r);
                var px = new Color32[w * h];
                CompositeOverBackdrop(r.buf.dst, px, w);
                return px;
            }

            var r6 = Render(6f);
            var r60 = Render(60f);
            var r600 = Render(600f);

            (int meanDelta, int maxDelta, int flips) Compare(Color32[] a, Color32[] b)
            {
                long sum = 0; int max = 0; int flips = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    int da = System.Math.Abs(a[i].r - b[i].r) + System.Math.Abs(a[i].g - b[i].g) + System.Math.Abs(a[i].b - b[i].b);
                    sum += da; if (da > max) max = da;
                    // A crude "flip" proxy: adjacent-pixel RELATIVE ordering reversed vs the other render,
                    // i.e. did i and i-1 swap which is brighter -- a hallmark of a posterised/jagged edge.
                    if (i > 0)
                    {
                        int aOrd = a[i].r - a[i - 1].r, bOrd = b[i].r - b[i - 1].r;
                        if (aOrd > 4 && bOrd < -4) flips++;
                        else if (aOrd < -4 && bOrd > 4) flips++;
                    }
                }
                return ((int)(sum / a.Length), max, flips);
            }

            var c60 = Compare(r6, r60);
            var c600 = Compare(r6, r600);
            sb.AppendLine("  scale 6 vs 60:  mean |dRGB| " + c60.meanDelta + ", max |dRGB| " + c60.maxDelta + ", local-order flips " + c60.flips);
            sb.AppendLine("  scale 6 vs 600: mean |dRGB| " + c600.meanDelta + ", max |dRGB| " + c600.maxDelta + ", local-order flips " + c600.flips);
            sb.Append("  (context: max possible |dRGB| is 765; a flip count that grows sharply from 60x to 600x" +
                      " is the numeric signature of the posterised/blocky look, not an opinion about the PNG)");
            return sb.ToString();
        }

        // ── LT-17 ─────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// LT-17 — THE VISUAL CHECK. A contact sheet PNG: both families side by side under one rig; each light
        /// kind; a rim sweep; an <c>Add</c> fill under a lamp; <c>receive</c> on and off; all four shadow-flag
        /// combinations.
        ///
        /// <b>Not falsifiable and not claimed to be</b> (FT-20's precedent and its reason: a passing table is
        /// not a picture). It is RENDERED, KEPT, and must be LOOKED AT BY A HUMAN.
        /// </summary>
        public static string LT17_ContactSheet(string path)
        {
            const int Cell = 96, Cols = 6, Pad = 6;

            var rigOne = RigOf(Color.white, 0.14f, Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 1.1f, 0.9f));
            var rigPoint = RigOf(Color.white, 0.08f, Pnt(-26f, 26f, 26f, 34f, new Color(1f, 0.75f, 0.35f), 1.4f, 1f));
            var rigBoth = RigOf(Color.white, 0.10f,
                                Dir(-55f, 36f, new Color(1f, 0.94f, 0.85f), 0.9f, 0.9f),
                                Pnt(30f, -22f, 22f, 34f, new Color(0.35f, 0.7f, 1f), 1.2f, 1f));

            var cells = new List<KeyValuePair<string, Func<LRig>>>();
            Action<string, Func<LRig>> C = (nm, f) => cells.Add(new KeyValuePair<string, Func<LRig>>(nm, f));

            Func<ShaperNode> disc = () => { var d = Disc("D", 34f, 0f, 0f); d.fill = Solid(new Color(0.85f, 0.55f, 0.35f)); return d; };
            var solidFill = Solid(new Color(0.85f, 0.55f, 0.35f));

            // 01-04 — THE HEADLINE PAIR: both families side by side under ONE rig, twice.
            C("01 Silhouette, flat, dir", () => BuildLit(disc(), rigOne, Resp(true, 1f, 0f), Cell, Cell));
            C("02 Solids Orb, dir", () => BuildSolid(SolidDef(ShaperSolidForm.Orb, 34f), rigOne, Resp(true, 1f, 0f), solidFill, Cell, Cell));
            C("03 Silhouette tilted, dir", () => BuildLit(disc(), rigOne, Resp(true, 1f, 0f, 2.2f, 0.9f, 48f, null, new Vector3(0.55f, 0.25f, 0.8f)), Cell, Cell));
            C("04 Solids Box, dir", () => BuildSolid(SolidDef(ShaperSolidForm.Box, 26f, 32f, 22f, 8f, 1.1f), rigOne, Resp(true, 1f, 0f), solidFill, Cell, Cell));

            // 05-08 — each LIGHT KIND, on each family.
            C("05 Silhouette, POINT", () => BuildLit(disc(), rigPoint, Resp(true, 1f, 0f, 2.2f, 0.9f, 48f, null, new Vector3(0.55f, 0.25f, 0.8f)), Cell, Cell));
            C("06 Solids Orb, POINT", () => BuildSolid(SolidDef(ShaperSolidForm.Orb, 34f), rigPoint, Resp(true, 1f, 0f), solidFill, Cell, Cell));
            C("07 Silhouette, dir+point", () => BuildLit(disc(), rigBoth, Resp(true, 1f, 0f, 2.2f, 0.9f, 48f, null, new Vector3(0.55f, 0.25f, 0.8f)), Cell, Cell));
            C("08 Solids Gem, dir+point", () => BuildSolid(SolidDef(ShaperSolidForm.Gem, 30f, 18f, 24f, 0f, 1.1f), rigBoth, Resp(true, 1f, 0f), solidFill, Cell, Cell));

            // 09-12 — the RIM SWEEP, on an Orb (where rim is visible; on a flat Silhouette it is zero by
            // arithmetic, which cell 13 shows).
            for (int s = 0; s < 4; s++)
            {
                float rim = s * 0.6f;
                C((9 + s).ToString("00") + " Orb rim " + rim.ToString("F1"),
                  () => BuildSolid(SolidDef(ShaperSolidForm.Orb, 34f), rigOne, Resp(true, 1f, rim), solidFill, Cell, Cell));
            }

            // 13 — rim 1.5 on a FLAT Silhouette: identically zero, and the point of ShaperLightRig.RimNeedsRelief.
            C("13 flat rim 1.5 = nothing", () => BuildLit(disc(), rigOne, Resp(true, 1f, 1.5f), Cell, Cell));

            // 14-15 — an ADD fill under a lamp, against the same fill as Over.
            C("14 Over fill under a lamp", () => { var d = Disc("D", 30f, 0f, 0f); d.fill = Solid(new Color(1f, 0.55f, 0.2f), 1f, 0f, ShaperFillComposite.Over); return BuildLit(d, rigOne, Resp(true, 1f, 0f), Cell, Cell); });
            C("15 ADD fill, NOT lit", () => { var d = Disc("D", 30f, 0f, 0f); d.fill = Solid(new Color(1f, 0.55f, 0.2f), 1f, 0f, ShaperFillComposite.Add); return BuildLit(d, rigOne, Resp(true, 1f, 0f), Cell, Cell); });

            // 16-17 — RECEIVE on and off, same rig, same shape.
            C("16 receive ON", () => BuildLit(disc(), EightLights(), Resp(true, 1f, 0f, 2.2f, 0.9f, 48f, null, new Vector3(0.5f, 0.3f, 0.81f)), Cell, Cell));
            C("17 receive OFF", () => BuildLit(disc(), EightLights(), Resp(false, 1f, 0f, 2.2f, 0.9f, 48f, null, new Vector3(0.5f, 0.3f, 0.81f)), Cell, Cell));

            // 18 — intensityScale 0: in shadow, ambient only. A DIFFERENT state from receive off (LR-4.2).
            C("18 intensityScale 0", () => BuildLit(disc(), EightLights(), Resp(true, 0f, 0f, 2.2f, 0.9f, 48f, null, new Vector3(0.5f, 0.3f, 0.81f)), Cell, Cell));

            // 19-22 — ALL FOUR SHADOW-FLAG COMBINATIONS. They must be indistinguishable (LR-4.5 / LT-12).
            for (int s = 0; s < 4; s++)
            {
                bool cast = (s & 1) != 0, recv = (s & 2) != 0;
                C((19 + s) + " shadows c=" + (cast ? 1 : 0) + " r=" + (recv ? 1 : 0), () =>
                {
                    var resp = Resp(true, 1f, 0f, 2.2f, 0.9f, 48f, null, new Vector3(0.5f, 0.3f, 0.81f));
                    resp.castShadows = cast; resp.receiveShadows = recv;
                    return BuildLit(disc(), rigOne, resp, Cell, Cell);
                });
            }

            // 23-24 — the remaining Solids members, so all six are on the sheet.
            C("23 Solids Can + lines", () => BuildSolid(SolidDef(ShaperSolidForm.Can, 28f, 20f, 26f, 0f, 1.1f, 1.1f), rigOne, Resp(true, 1f, 0.3f), solidFill, Cell, Cell));
            C("24 Solids Ring, tilt 62", () => BuildSolid(SolidDef(ShaperSolidForm.Ring, 36f, 12f, 62f, 0f, 1.1f), rigOne, Resp(true, 1f, 0.3f), solidFill, Cell, Cell));

            // 25 — PYRAMID. Added by the fix pass: the sheet exercised five of the six forms and Pyramid
            // appeared in NO cell, so the one member with a genuinely different silhouette (a triangle rather
            // than a rounded or boxy outline) was never looked at. It is covered numerically by LT-14a and
            // LT-15, so this was a VISUAL coverage gap only — which is exactly the kind a contact sheet exists
            // to close.
            C("25 Solids Pyramid", () => BuildSolid(SolidDef(ShaperSolidForm.Pyramid, 30f, 28f, 20f, 0f, 1.1f), rigOne, Resp(true, 1f, 0.3f), solidFill, Cell, Cell));

            // 26-27 — THE GLOW PATH, which no cell exercised at all. `edgeGlow` and `innerGlow` were
            // hardcoded to 0 in the shared SolidDef helper every cell was built through, so `scene.glow` was
            // all-zeros in all 24 cells and the generator's halo/inner-glow block (LR-6.4) never ran once.
            // 26 is the halo alone, 27 is halo plus inner glow, both against cell 25's un-glowed Pyramid and
            // cell 02's un-glowed Orb.
            C("26 Pyramid + halo", () =>
            {
                var d = SolidDef(ShaperSolidForm.Pyramid, 30f, 28f, 20f, 0f, 1.1f);
                d.edgeGlow = new ZUIValue(0.95f); d.edgeGlowColour = new Color(1f, 0.8f, 0.35f);
                return BuildSolid(d, rigOne, Resp(true, 1f, 0.3f), solidFill, Cell, Cell);
            });
            C("27 Orb + halo + inner", () =>
            {
                var d = SolidDef(ShaperSolidForm.Orb, 32f);
                d.edgeGlow = new ZUIValue(0.9f); d.edgeGlowColour = new Color(1f, 0.75f, 0.3f);
                d.innerGlow = new ZUIValue(0.85f); d.innerGlowColour = new Color(0.35f, 0.85f, 1f);
                return BuildSolid(d, rigOne, Resp(true, 1f, 0.3f), solidFill, Cell, Cell);
            });

            // 28 — a Gem with aspect and depth authored to 0.4, which change NOTHING (LR-6.5b / LR-7.3, and
            // LT-19). It is here so the declared inertness is visible rather than only asserted: this cell is
            // identical to cell 08's geometry, and the compile raises `hasInertDial` with the reason a UI
            // would grey the control with.
            C("28 Gem, aspect+depth 0.4", () => BuildSolid(SolidDef(ShaperSolidForm.Gem, 30f, 18f, 24f, 0f, 1.1f, 0.4f, 0.4f), rigBoth, Resp(true, 1f, 0f), solidFill, Cell, Cell));

            int rows = (cells.Count + Cols - 1) / Cols;
            int texW = Cols * Cell + (Cols + 1) * Pad;
            int texH = rows * Cell + (rows + 1) * Pad;
            var sheet = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            var bg = new Color32[texW * texH];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(22, 22, 26, 255);
            sheet.SetPixels32(bg);

            var cellPx = new Color32[Cell * Cell];
            for (int c = 0; c < cells.Count; c++)
            {
                var r = cells[c].Value();
                Paint(r);
                CompositeOverBackdrop(r.buf.dst, cellPx, Cell);
                DrawLabel(cellPx, Cell, cells[c].Key);
                int col = c % Cols, row = rows - 1 - (c / Cols);
                sheet.SetPixels32(Pad + col * (Cell + Pad), Pad + row * (Cell + Pad), Cell, Cell, cellPx);
            }
            sheet.Apply();
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());

            var sb = new StringBuilder("LT-17 contact sheet\n");
            sb.AppendLine("  " + cells.Count + " cells, " + Cols + " x " + rows + ", " + texW + "x" + texH +
                          " px, written to " + path);
            sb.AppendLine("  Every cell is composited over an OPAQUE mid-grey checkerboard and the PNG is fully");
            sb.AppendLine("  opaque, so nothing can render invisibly - and an ADDITIVE fill (cell 15) cannot be shown");
            sb.AppendLine("  at all on a transparent sheet, because its alpha is 0 by design (FC-2.6b).");
            for (int c = 0; c < cells.Count; c++)
                sb.AppendLine("    row " + (c / Cols) + " col " + (c % Cols) + "  " + cells[c].Key);
            sb.AppendLine("  Cells whose CORRECT reading is 'nothing to see', stated so they are not read as bugs:");
            sb.AppendLine("    01 and 03 are FLAT-LOOKING on purpose. LR-3.2: Wave 2's only Silhouette provider is");
            sb.AppendLine("       Constant, so a Silhouette layer has ONE normal everywhere and no relief. The lighting");
            sb.AppendLine("       is real - compare 01 against 05, where a POINT light varies across the canvas - but it");
            sb.AppendLine("       is structurally complete and visually thin until T-0109 lands a height profile.");
            sb.AppendLine("    13 is IDENTICAL to 01. Rim 1.5 on a flat normal is exactly zero, because N.V == 1 there.");
            sb.AppendLine("       That is arithmetic (LR-2.5), and ShaperLightRig.RimNeedsRelief is the sentence a UI");
            sb.AppendLine("       must show on the control so it does not read as a broken knob.");
            sb.AppendLine("    15 is BRIGHTER than 14 and unaffected by the lamp. LR-5.3: an Add fill is light, not");
            sb.AppendLine("       paint, and a lamp does not get dimmer in a dark room.");
            sb.AppendLine("    17 vs 18: receive OFF renders the raw albedo; intensityScale 0 renders AMBIENT ONLY.");
            sb.AppendLine("       Two different, both reachable states - which is the point of having both (LR-4.2).");
            sb.AppendLine("    19-22 are FOUR IDENTICAL CELLS, and that is the pass condition. Shadow flags are");
            sb.AppendLine("       recorded and inert (LR-4.5); LT-12 asserts both halves numerically.");
            sb.AppendLine("    28 is IDENTICAL to a Gem with aspect and depth left at 1, and that is the point.");
            sb.AppendLine("       LR-6.5b and the reference builder (PyreRenderer.cs:4796) both ignore those two");
            sb.AppendLine("       dials on a Gem; the compile now DECLARES it (hasInertDial + a reason sentence)");
            sb.AppendLine("       rather than letting the knob turn and do nothing. LT-19 asserts every dial on");
            sb.AppendLine("       every form is either live or declared, never neither.");
            sb.Append("  RESULT: RENDERED - NOT VERIFIED BY THE TABLE. A passing table is not a picture; this is " +
                      "not a pass until a human has looked at it.");
            return sb.ToString();
        }

        /// <summary>Composite one cell over an opaque checkerboard and encode once (FC-2.3's single boundary).</summary>
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

        static readonly int[] Digits3x5 =
        {
            31599, 11415, 29671, 29647, 23497, 31183, 31215, 29257, 31727, 31695,
        };

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

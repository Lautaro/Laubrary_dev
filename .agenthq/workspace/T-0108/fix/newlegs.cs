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


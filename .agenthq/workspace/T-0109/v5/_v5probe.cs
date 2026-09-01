// T-0109 FIX ROUND 3 (V1/V2/V3) — confirmation probe. Statement body for `unity command eval_file`.

string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v5\";
System.IO.Directory.CreateDirectory(outDir);
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);

System.Func<float, float, float, Laubrary.Shaper.ShaperProgram> plate = (hw, hh, r) =>
    Laubrary.Shaper.ShaperCompiler.Compile(Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef {
        kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = hw, rectHalfH = hh, rectCornerRadius = r }));

System.Func<Laubrary.Shaper.ShaperProgram, Laubrary.Shaper.ShaperExtrusionTechnique, Laubrary.Shaper.ShaperBevelTechnique, float, float, float, float, Laubrary.Shaper.ShaperHeightOp> opFor =
    (prog, tech, bevel, depth, amount, steps, curve) =>
{
    var def = new Laubrary.Shaper.ShaperHeightDef {
        technique = tech, bevel = bevel,
        depth = new ZUIValue(depth), angle = new ZUIValue(45f), steps = new ZUIValue(steps),
        curve = new ZUIValue(curve), taper = new ZUIValue(1f),
        bevelAmount = new ZUIValue(amount), bevelSteps = new ZUIValue(3f) };
    return Laubrary.Shaper.ShaperHeightCompiler.Compile(def, prog, 1f, 0f);
};

// Member's exact predicate (HS-1.1), re-implemented independently as the oracle.
System.Func<Laubrary.Shaper.ShaperProgram, float[], Laubrary.Shaper.ShaperHeightOp, float, float, float, bool> inside =
    (prog, stack, hop, px, py, pz) =>
{
    float above = pz - hop.baseZ;
    if (above < 0f) return false;
    float d = Laubrary.Shaper.ShaperEvaluator.Distance(prog, px, py, stack);
    if (d > 0f || Laubrary.Shaper.ShaperField.IsEmpty(d)) return false;
    float t = Laubrary.Shaper.ShaperHeight.T(hop, d);
    float g = Laubrary.Shaper.ShaperHeight.Composed(hop, t, 0f, 0f);
    if (!(g > 0f)) return false;
    return above <= hop.body * g;
};

// ── V1 minimal repro (v3/w3_B.txt): the span [552.3819, 576.5567] used to vanish entirely ────────────
{
    var prog = plate(200f, 200f, 0f);
    var hop = opFor(prog, Laubrary.Shaper.ShaperExtrusionTechnique.Stepped, Laubrary.Shaper.ShaperBevelTechnique.None, 290f, 0f, 8f, 1f);
    var scene = new Laubrary.Shaper.ShaperResolveScene();
    scene.Add(prog, hop);
    var cr = new Laubrary.Shaper.ShaperCrossing[64];
    var r = Laubrary.Shaper.ShaperResolve.Query(scene, -400f, 0f, 248.571732f, 0.95394f, 0f, -0.3f, cr);
    sb.AppendLine("V1 repro: branch=" + r.branch + " count=" + r.count + " truncated=" + r.truncated);
    for (int i = 0; i < r.count; i++)
        sb.AppendLine("   [" + i + "] rayT=" + cr[i].rayT.ToString("F4") + " entering=" + cr[i].entering);
    float[] truth = { 314.4854f, 550.3496f, 552.3819f, 576.5567f };
    bool ok = r.count == 4;
    if (ok) for (int i = 0; i < 4; i++) if (UnityEngine.Mathf.Abs(cr[i].rayT - truth[i]) > 0.05f) ok = false;
    sb.AppendLine("V1 VERDICT: " + (ok ? "PASS - all four truth crossings present" : "FAIL - span still lost"));
}

// ── V1 breadth: brute-force oracle at 0.05 px over the 13 affected step counts ───────────────────────
{
    int lost = 0, rays = 0, transitions = 0; float worstGap = 0f; string worstAt = "";
    var cr = new Laubrary.Shaper.ShaperCrossing[256];
    int[] ns = { 8, 12, 13, 14, 15, 18, 20, 22, 23, 26, 27, 29, 31 };
    for (int q = 0; q < ns.Length; q++)
    {
        var prog = plate(200f, 200f, 0f);
        var hop = opFor(prog, Laubrary.Shaper.ShaperExtrusionTechnique.Stepped, Laubrary.Shaper.ShaperBevelTechnique.None, 290f, 0f, (float)ns[q], 1f);
        var scene = new Laubrary.Shaper.ShaperResolveScene();
        scene.Add(prog, hop);
        var stack = prog.NewStack();
        for (int iz = 0; iz < 24; iz++)
        {
            float oz = 10f + iz * 12f;
            for (int ia = 0; ia < 8; ia++)
            {
                float ang = -0.12f - ia * 0.09f;
                float dxr = UnityEngine.Mathf.Sqrt(1f - ang * ang);
                rays++;
                var r = Laubrary.Shaper.ShaperResolve.Query(scene, -400f, 0f, oz, dxr, 0f, ang, cr);
                bool prev = false;
                for (float t = 0f; t <= 1200f; t += 0.05f)
                {
                    float px = -400f + t * dxr, pz = oz + t * ang;
                    bool ins = inside(prog, stack, hop, px, 0f, pz);
                    if (ins == prev) continue;
                    prev = ins; transitions++;
                    float best = float.MaxValue;
                    for (int i = 0; i < r.count; i++) best = UnityEngine.Mathf.Min(best, UnityEngine.Mathf.Abs(cr[i].rayT - t));
                    if (best > 0.5f)
                    {
                        lost++;
                        if (best > worstGap) { worstGap = best; worstAt = "n=" + ns[q] + " oz=" + oz + " ang=" + ang.ToString("F2") + " t=" + t.ToString("F3"); }
                    }
                }
            }
        }
    }
    sb.AppendLine("V1 breadth: " + rays + " rays, " + transitions + " true transitions, LOST = " + lost +
                  ", worst gap = " + worstGap.ToString("F4") + " px  " + worstAt);
    sb.AppendLine("V1 breadth VERDICT: " + (lost == 0 ? "PASS" : "FAIL"));
}

// ── V3: the closed form is reachable only on the exact axis ──────────────────────────────────────────
{
    var prog = plate(200f, 200f, 0f);
    var hop = opFor(prog, Laubrary.Shaper.ShaperExtrusionTechnique.Flat, Laubrary.Shaper.ShaperBevelTechnique.Ogee, 150f, 0.35f, 4f, 1f);
    var scene = new Laubrary.Shaper.ShaperResolveScene();
    scene.Add(prog, hop);
    var cr = new Laubrary.Shaper.ShaperCrossing[64];
    var exact = Laubrary.Shaper.ShaperResolve.Query(scene, -190f, 0f, 400f, 0f, 0f, -1f, cr);
    float exactT = exact.count > 0 ? cr[0].rayT : -1f;
    var nearly = Laubrary.Shaper.ShaperResolve.Query(scene, -190f, 0f, 400f, 2e-5f, 0f, -1f, cr);
    float nearlyT = nearly.count > 0 ? cr[0].rayT : -1f;
    sb.AppendLine("V3: exact (0,0,-1)   branch=" + exact.branch + " firstT=" + exactT.ToString("F4"));
    sb.AppendLine("V3: 2e-5 off axis    branch=" + nearly.branch + " firstT=" + nearlyT.ToString("F4"));
    bool v3ok = exact.branch == Laubrary.Shaper.ShaperResolveBranch.StraightDown && nearly.branch == Laubrary.Shaper.ShaperResolveBranch.General;
    sb.AppendLine("V3 VERDICT: " + (v3ok ? "PASS - closed form on the axis only" : "FAIL"));
}

// ── V2: the four latent (n,k) risers, swept through the +/- ulp neighbourhood of their own tread ─────
{
    int bad = 0, probes = 0; float worst = 0f; string worstAt = "";
    int[] nn = { 22, 23, 23, 29 }; int[] kk = { 13, 7, 14, 15 };
    var prog = plate(200f, 200f, 0f);
    for (int p = 0; p < 4; p++)
    {
        int n = nn[p], k = kk[p];
        var hop = opFor(prog, Laubrary.Shaper.ShaperExtrusionTechnique.Stepped, Laubrary.Shaper.ShaperBevelTechnique.None, 290f, 0f, (float)n, 1f);
        float treadZeta = UnityEngine.Mathf.Min(1f, k / (float)(n - 1));
        for (int u = -8; u <= 8; u++)
        {
            float zeta = treadZeta;
            for (int i = 0; i < System.Math.Abs(u); i++)
                zeta = System.BitConverter.Int32BitsToSingle(System.BitConverter.SingleToInt32Bits(zeta) + (u < 0 ? -1 : 1));
            if (zeta <= 0f || zeta > 1f) continue;
            float tauMin = Laubrary.Shaper.ShaperHeight.InverseLowerBound(hop, zeta);
            if (float.IsPositiveInfinity(tauMin)) continue;
            probes++;
            for (float t = 0f; t < tauMin; t += 1f / (n * 128f))
            {
                if (Laubrary.Shaper.ShaperHeight.Composed(hop, t, 0f, 0f) >= zeta)
                {
                    float over = tauMin - t;
                    bad++;
                    if (over > worst) { worst = over; worstAt = "n=" + n + " k=" + k + " zeta=" + zeta.ToString("R"); }
                    break;
                }
            }
        }
    }
    sb.AppendLine("V2 traps: " + probes + " probes, containing-prism violations = " + bad +
                  ", worst over-report = " + worst.ToString("E4") + " t-units  " + worstAt);
    sb.AppendLine("V2 VERDICT: " + (bad == 0 ? "PASS" : "FAIL"));
}

System.IO.File.WriteAllText(outDir + "V5-PROBE.txt", sb.ToString());
return sb.ToString();

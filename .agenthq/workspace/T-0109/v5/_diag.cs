string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v5\";
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);

var kids = new Laubrary.Shaper.ShaperNode[3];
kids[0] = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = 120f, rectHalfH = 60f }, "plate", Laubrary.Shaper.ShaperCombineMode.Add);
var s1 = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = 8f, rectHalfH = 120f }, "slot", Laubrary.Shaper.ShaperCombineMode.Subtract);
s1.transform.translate = new UnityEngine.Vector2(-50f, 0f);
var s2 = Laubrary.Shaper.ShaperNode.Primitive(new Laubrary.Shaper.ShaperPrimitiveDef { kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect, rectHalfW = 8f, rectHalfH = 120f }, "slot", Laubrary.Shaper.ShaperCombineMode.Subtract);
s2.transform.translate = new UnityEngine.Vector2(50f, 0f);
kids[1] = s1; kids[2] = s2;
var root = Laubrary.Shaper.ShaperNode.Bag("sweptplate", Laubrary.Shaper.ShaperCombineMode.Add, kids);
var prog = Laubrary.Shaper.ShaperCompiler.Compile(root);
var stack = prog.NewStack();

System.Func<int, Laubrary.Shaper.ShaperHeightOp> mk = (n) => {
    var def = new Laubrary.Shaper.ShaperHeightDef {
        technique = Laubrary.Shaper.ShaperExtrusionTechnique.Stepped, bevel = Laubrary.Shaper.ShaperBevelTechnique.None,
        depth = new ZUIValue(90f), angle = new ZUIValue(45f), steps = new ZUIValue((float)n),
        curve = new ZUIValue(1f), taper = new ZUIValue(1f), bevelAmount = new ZUIValue(0.25f), bevelSteps = new ZUIValue(3f) };
    return Laubrary.Shaper.ShaperHeightCompiler.Compile(def, prog, 1f, 0f);
};

System.Func<Laubrary.Shaper.ShaperHeightOp, float, float, float, bool> ins = (hop, px, py, pz) => {
    float above = pz - hop.baseZ;
    if (above < 0f || above > hop.body * hop.supG) return false;
    float d = Laubrary.Shaper.ShaperEvaluator.Distance(prog, px, py, stack);
    if (d > 0f || Laubrary.Shaper.ShaperField.IsEmpty(d)) return false;
    float t = Laubrary.Shaper.ShaperHeight.T(hop, d);
    float g = Laubrary.Shaper.ShaperHeight.Composed(hop, t, 0f, 0f);
    if (!(g > 0f)) return false;
    return above <= hop.body * g;
};

var cr = new Laubrary.Shaper.ShaperCrossing[256];
// full re-scan of the AIMED arm to list every omission, not just the worst
sb.AppendLine("=== all omissions in the AIMED arm, 4..31 ===");
for (int n = 4; n <= 31; n++)
{
    var hop = mk(n);
    var scene = new Laubrary.Shaper.ShaperResolveScene(); scene.Add(prog, hop);
    for (int r = 0; r < 36; r++)
    {
        float tilt = (r % 4) * 14f + 2f;
        float rad = tilt * UnityEngine.Mathf.Deg2Rad;
        float ddx = UnityEngine.Mathf.Cos(rad), ddz = -UnityEngine.Mathf.Sin(rad);
        float ox = -130f, oy = (r % 3 - 1) * 14f;
        float zf = ((r / 4) % UnityEngine.Mathf.Max(1, n)) / (float)UnityEngine.Mathf.Max(1, n - 1);
        float oz = hop.baseZ + zf * hop.body * hop.supG + 70f * UnityEngine.Mathf.Tan(rad);
        var rr = Laubrary.Shaper.ShaperResolve.Query(scene, ox, oy, oz, ddx, 0f, ddz, cr);
        // truth scan
        bool prev = ins(hop, ox, oy, oz);
        var truth = new System.Collections.Generic.List<float>();
        if (prev) truth.Add(0f);
        for (int i = 1; i <= 100000; i++)
        {
            float s = 400f * i / 100000f;
            bool m = ins(hop, ox + s * ddx, oy, oz + s * ddz);
            if (m != prev) {
                float lo = 400f * (i - 1) / 100000f, hi = s;
                for (int k = 0; k < 40; k++) { float mid = 0.5f * (lo + hi); if (ins(hop, ox + mid * ddx, oy, oz + mid * ddz) == prev) lo = mid; else hi = mid; }
                truth.Add(hi); prev = m;
            }
        }
        for (int j = 0; j < truth.Count; j++)
        {
            float best = float.MaxValue;
            for (int i = 0; i < rr.count; i++) best = UnityEngine.Mathf.Min(best, UnityEngine.Mathf.Abs(truth[j] - cr[i].rayT));
            if (best > 0.05f)
            {
                sb.AppendLine("  OMISSION n=" + n + " r=" + r + " tilt=" + tilt + " oy=" + oy + " zf=" + zf.ToString("F6") +
                              " oz=" + oz.ToString("R") + " missed truth t=" + truth[j].ToString("F4") + " gap=" + best.ToString("F4") +
                              " emitted=" + rr.count + " truth=" + truth.Count + " capped=" + rr.bracketCapped + " exhausted=" + rr.stepsExhausted + " trunc=" + rr.truncated);
                sb.Append("     emitted:");
                for (int i = 0; i < rr.count; i++) sb.Append(" " + cr[i].rayT.ToString("F4") + (cr[i].entering ? "I" : "O") + "/" + cr[i].kind);
                sb.AppendLine();
                sb.Append("     truth  :");
                for (int i = 0; i < truth.Count; i++) sb.Append(" " + truth[i].ToString("F4"));
                sb.AppendLine();
                sb.AppendLine("     hop: body=" + hop.body + " supG=" + hop.supG + " baseZ=" + hop.baseZ + " span=" + hop.span + " n=" + hop.n);
            }
        }
    }
}
System.IO.File.WriteAllText(outDir + "DIAG.txt", sb.ToString());
sb.AppendLine("DIAGDONE");
System.IO.File.WriteAllText(outDir + "DIAG.txt", sb.ToString());
return "ok";

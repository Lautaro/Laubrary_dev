// T-0337 §1 — the twenty dials round 20 named "dead at defaults, guard not traced", re-measured at the
// frames and gate states the ENGINE's own `if` says they act in.  Round 20 measured every one of them at
// frames 2-4 of 8; several of these act only LATE in the clock (a cooling exponent, an escape speed) or only
// EARLY (an ignition flash), so a three-frame window in the middle was blind to them by construction.
//
// Every case here is one bounded experiment: set ONE named value, measure over ALL 8 frames, restore.
// Nothing is left mutated.  Output: out/corrections.tsv, plus a summary line.
var sb = new System.Text.StringBuilder();
var log = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);
var sw = System.Diagnostics.Stopwatch.StartNew();

System.Func<object, float, UnityEngine.Color32[]> render = (src, ph) => {
    var buf = new UnityEngine.Color32[W * H];
    renderM.Invoke(src, new object[] { W, H, ph, (uint)SEED, buf });
    return buf;
};
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => {
    int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n;
};
System.Func<UnityEngine.Color32[], int> litOf = a => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].a != 0) n++; return n; };

// build a source around a fresh form
System.Func<string, object[]> mk = formName => {
    var ft = ZType(formName);
    var form = System.Activator.CreateInstance(ft);
    var src = System.Activator.CreateInstance(srcT);
    srcT.GetField("form", BFi).SetValue(src, form);
    srcT.GetField("frames", BFi).SetValue(src, N);
    return new object[] { form, src };
};
System.Func<object, string, System.Reflection.FieldInfo> fi = (o, n) => o.GetType().GetField(n, BFi);
System.Func<object, string, object> get = (o, n) => fi(o, n).GetValue(o);
System.Action<object, string, object> set = (o, n, v) => fi(o, n).SetValue(o, v);
// a ZUIValue's staticValue
System.Action<object, float> setZ = (zv, v) => zv.GetType().GetProperty("staticValue", BFi).SetValue(zv, v);
System.Func<object, float> getZ = zv => (float)zv.GetType().GetProperty("staticValue", BFi).GetValue(zv);

// Run ONE experiment: baseline over all 8 frames, mutate, re-measure, restore. Returns the per-frame diffs.
System.Func<object, System.Action, System.Action, string, string, int[]> exp = null;
exp = (src, mutate, restore, caseName, note) => {
    var basis = new UnityEngine.Color32[N][];
    var lit = new int[N];
    for (int i = 0; i < N; i++) { basis[i] = render(src, (float)i / (N - 1)); lit[i] = litOf(basis[i]); }
    mutate();
    var d = new int[N];
    for (int i = 0; i < N; i++) d[i] = diff(basis[i], render(src, (float)i / (N - 1)));
    restore();
    int max = 0; string per = "";
    for (int i = 0; i < N; i++) { if (d[i] > max) max = d[i]; per += (i == 0 ? "" : "·") + d[i]; }
    string litS = ""; for (int i = 0; i < N; i++) litS += (i == 0 ? "" : "·") + lit[i];
    sb.Append(caseName).Append('\t').Append(max).Append('\t').Append(per).Append('\t').Append(litS).Append('\t').Append(note).Append('\n');
    log.Append(caseName).Append(": max=").Append(max).Append(" perFrame=").Append(per).Append("  [lit ").Append(litS).Append("]\n");
    return d;
};

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// ARC BURST
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var p = mk("ArcBurstForm"); var form = p[0]; var src = p[1];

    // 1. keepHueFloor — PyreArcBurst.cs:273 KeepHue is unconditional; the only reason it can do nothing is
    //    that no stroke is a ghost (PyreArcBurst.cs:402, `if (rng.Random() >= p) return 1.0;`).
    {
        var zv = get(form, "keepHueFloor"); float ov = getZ(zv);
        exp(src, () => setZ(zv, 0f), () => setZ(zv, ov), "ArcBurst.keepHueFloor @ Bolt defaults", "0.36 -> 0");
        var bolt = get(form, "bolt");
        int otr = (int)get(bolt, "trunks");
        set(bolt, "trunks", otr + 6);
        exp(src, () => setZ(zv, 0f), () => setZ(zv, ov), "ArcBurst.keepHueFloor @ Bolt trunks+6", "more trunks -> more ghosts");
        set(bolt, "trunks", otr);
    }

    // 2/3. ghostDeepLo / ghostDeepHi — guarded by the ACTIVE layout's ghostDeepP (PyreArcBurst.cs:403).
    {
        var bolt = get(form, "bolt");
        float odp = (float)get(bolt, "ghostDeepP");
        float olo = (float)get(form, "ghostDeepLo"), ohi = (float)get(form, "ghostDeepHi");
        exp(src, () => set(form, "ghostDeepLo", 0.29f), () => set(form, "ghostDeepLo", olo),
            "ArcBurst.ghostDeepLo @ Bolt defaults (ghostDeepP=0)", "expected 0 — guard shut");
        set(bolt, "ghostDeepP", 0.9f);
        exp(src, () => set(form, "ghostDeepLo", 0.29f), () => set(form, "ghostDeepLo", olo),
            "ArcBurst.ghostDeepLo @ Bolt ghostDeepP=0.9", "guard opened");
        exp(src, () => set(form, "ghostDeepHi", 0.39f), () => set(form, "ghostDeepHi", ohi),
            "ArcBurst.ghostDeepHi @ Bolt ghostDeepP=0.9", "guard opened");
        set(bolt, "ghostDeepP", odp);
    }

    // 4/5. lattice.flyLo / flyHi — only act for t > lattice.burstStart (PyreArcBurst.cs:725-727).
    {
        var lay = fi(form, "layout");
        var ol = lay.GetValue(form);
        lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Lattice"));
        var lat = get(form, "lattice");
        float oLo = (float)get(lat, "flyLo"), oHi = (float)get(lat, "flyHi"), obs = (float)get(lat, "burstStart");
        exp(src, () => set(lat, "flyLo", 0f), () => set(lat, "flyLo", oLo),
            "ArcBurst.lattice.flyLo @ Lattice defaults (burstStart=0.60)", "0.7 -> 0, all 8 frames");
        exp(src, () => set(lat, "flyHi", 2f), () => set(lat, "flyHi", oHi),
            "ArcBurst.lattice.flyHi @ Lattice defaults", "1.25 -> 2, all 8 frames");
        set(lat, "burstStart", 1f);
        exp(src, () => set(lat, "flyLo", 0f), () => set(lat, "flyLo", oLo),
            "ArcBurst.lattice.flyLo @ burstStart=1.0", "the guard actually shut");
        set(lat, "burstStart", obs);

        // 6. cage.coolK — only acts for t > cage.coolStart = 0.88 (PyreArcBurst.cs:274, 863).
        lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Cage"));
        var cage = get(form, "cage");
        float ock = (float)get(cage, "coolK"), ocs = (float)get(cage, "coolStart");
        exp(src, () => set(cage, "coolK", 3f), () => set(cage, "coolK", ock),
            "ArcBurst.cage.coolK @ Cage defaults (coolStart=0.88, 8 frames)", "1.4 -> 3");
        set(cage, "coolStart", 0.2f);
        exp(src, () => set(cage, "coolK", 3f), () => set(cage, "coolK", ock),
            "ArcBurst.cage.coolK @ coolStart=0.2", "guard opened early");
        set(cage, "coolStart", ocs);
        lay.SetValue(form, ol);
    }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// PLASMA BLOOM
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var p = mk("PlasmaBloomForm"); var form = p[0]; var src = p[1];
    var dX = get(form, "driftX"); var dY = get(form, "driftY"); var dA = get(form, "driftAmt");
    float odx = getZ(dX), ody = getZ(dY), oda = getZ(dA);

    // 7/8/9. driftEase / driftLin / driftLag — behind the mutual drift pair (PyrePlasmaBloom.cs:206-208, 233).
    {
        float oe = (float)get(form, "driftEase"), ol2 = (float)get(form, "driftLin");
        var dl = get(form, "driftLag"); float odl = getZ(dl);
        exp(src, () => set(form, "driftEase", 2f), () => set(form, "driftEase", oe),
            "PlasmaBloom.driftEase @ defaults", "expected 0 — drift shut");
        setZ(dX, 1f); setZ(dA, 0.8f);
        exp(src, () => set(form, "driftEase", 2f), () => set(form, "driftEase", oe),
            "PlasmaBloom.driftEase @ driftX=1 driftAmt=0.8", "guards opened");
        exp(src, () => set(form, "driftLin", 1f), () => set(form, "driftLin", ol2),
            "PlasmaBloom.driftLin @ driftX=1 driftAmt=0.8", "guards opened");
        exp(src, () => setZ(dl, 1f), () => setZ(dl, odl),
            "PlasmaBloom.driftLag @ driftX=1 driftAmt=0.8", "guards opened");
        setZ(dX, odx); setZ(dA, oda);
        // one side alone must NOT be enough — that is what makes the pair mutual
        setZ(dX, 1f);
        exp(src, () => set(form, "driftEase", 2f), () => set(form, "driftEase", oe),
            "PlasmaBloom.driftEase @ driftX=1 only", "expected 0 — driftAmt still 0");
        setZ(dX, odx); setZ(dA, 0.8f);
        exp(src, () => set(form, "driftEase", 2f), () => set(form, "driftEase", oe),
            "PlasmaBloom.driftEase @ driftAmt=0.8 only", "expected 0 — driftX/Y still 0");
        setZ(dA, oda);
    }

    // 10-15. mode / gateGain / plume* — all gated on lobes >= 2 (PyrePlasmaBloom.cs:327-328, 383, 395).
    {
        var md = fi(form, "mode"); var omd = md.GetValue(form);
        int olb = (int)get(form, "lobes");
        exp(src, () => md.SetValue(form, System.Enum.Parse(md.FieldType, "Plume")), () => md.SetValue(form, omd),
            "PlasmaBloom.mode Bloom->Plume @ lobes=0", "expected 0 — Plume needs Lobe Count >= 2");
        set(form, "lobes", 4);
        exp(src, () => md.SetValue(form, System.Enum.Parse(md.FieldType, "Plume")), () => md.SetValue(form, omd),
            "PlasmaBloom.mode Bloom->Plume @ lobes=4", "guard opened");

        // gateGain — visible only in lobeMode=Noise; dead unless lobes >= 2 as well
        var lm = fi(form, "lobeMode"); var olm = lm.GetValue(form);
        var gg = get(form, "gateGain"); float ogg = getZ(gg);
        lm.SetValue(form, System.Enum.Parse(lm.FieldType, "Noise"));
        set(form, "lobes", olb);
        exp(src, () => setZ(gg, 0.5f), () => setZ(gg, ogg),
            "PlasmaBloom.gateGain @ lobeMode=Noise, lobes=0", "expected 0 — the whole lobe block is skipped");
        set(form, "lobes", 4);
        exp(src, () => setZ(gg, 0.5f), () => setZ(gg, ogg),
            "PlasmaBloom.gateGain @ lobeMode=Noise, lobes=4", "guard opened");
        lm.SetValue(form, olm);

        // the four plume dials, in Plume mode, at lobes=0 then lobes=4
        md.SetValue(form, System.Enum.Parse(md.FieldType, "Plume"));
        string[] pn = { "plumeAmp", "plumeReach", "plumeW", "plumeVary" };
        float[] pv = { 1.5f, 2f, 2f, 1f };
        for (int q = 0; q < pn.Length; q++)
        {
            var zv = get(form, pn[q]); float ov = getZ(zv); float nv = pv[q]; var cap = zv;
            set(form, "lobes", olb);
            exp(src, () => setZ(cap, nv), () => setZ(cap, ov), "PlasmaBloom." + pn[q] + " @ Plume, lobes=0", "expected 0");
            set(form, "lobes", 4);
            if (pn[q] == "plumeVary") setZ(get(form, "plumeAmp"), 1.5f);   // Vary only moves a tongue that exists
            exp(src, () => setZ(cap, nv), () => setZ(cap, ov), "PlasmaBloom." + pn[q] + " @ Plume, lobes=4", "guard opened");
            if (pn[q] == "plumeVary") setZ(get(form, "plumeAmp"), 0f);
        }
        // plumeVary's second guard: warp (PyrePlasmaBloom.cs:399) — satisfied at defaults, shut it to prove it
        {
            var wv = get(form, "warp"); float ow = getZ(wv);
            var pa = get(form, "plumeAmp"); float opa = getZ(pa); setZ(pa, 1.5f);
            var zv = get(form, "plumeVary"); float ov = getZ(zv);
            setZ(wv, 0f);
            exp(src, () => setZ(zv, 1f), () => setZ(zv, ov), "PlasmaBloom.plumeVary @ Plume, lobes=4, warp=0", "expected 0 — second guard shut");
            setZ(wv, ow); setZ(pa, opa);
        }
        md.SetValue(form, omd); set(form, "lobes", olb);
    }

    // 16-18. chunks/embers/motes .swirl — gated by the FORM-ROOT swirl (PyrePlasmaBloom.cs:245).
    {
        float osw = (float)get(form, "swirl");
        string[] pops = { "chunks", "embers", "motes" };
        foreach (var pop in pops)
        {
            var po = get(form, pop); float ops = (float)get(po, "swirl"); var cap = po;
            set(form, "swirl", osw);
            exp(src, () => set(cap, "swirl", 0f), () => set(cap, "swirl", ops), "PlasmaBloom." + pop + ".swirl @ form swirl=0", "expected 0");
            set(form, "swirl", 3f);
            exp(src, () => set(cap, "swirl", 0f), () => set(cap, "swirl", ops), "PlasmaBloom." + pop + ".swirl @ form swirl=3", "guard opened");
        }
        set(form, "swirl", osw);
    }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FORK BLAST
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var p = mk("ForkBlastForm"); var form = p[0]; var src = p[1];

    // 19. flash — self-guarded only (PyreForkBlast.cs:412); acts in the first 12 % of EACH blast's own life.
    {
        var zv = get(form, "flash"); float ov = getZ(zv);
        exp(src, () => setZ(zv, 0f), () => setZ(zv, ov), "ForkBlast.flash @ defaults, all 8 frames", "0.7 -> 0");
        exp(src, () => setZ(zv, 1f), () => setZ(zv, ov), "ForkBlast.flash 0.7 -> 1, all 8 frames", "");
    }

    // 20. opacity — PyreForkBlast.cs:386-387: an exponent on the FILL's alpha ceiling. Pow(1, k) == 1, so it
    //     can only act where the hosted layer's Shape Fill actually has alpha below 1 somewhere.
    {
        float ov = (float)get(form, "opacity");
        exp(src, () => set(form, "opacity", 1.5f), () => set(form, "opacity", ov),
            "ForkBlast.opacity @ the source's DEFAULT fill", "0.55 -> 1.5");

        // now the same dial against a fill whose alpha dips
        var fill = srcT.GetField("shapeFill", BFi).GetValue(src);
        string dump = "fill=" + (fill == null ? "null" : fill.GetType().Name);
        bool dipped = false;
        if (fill != null)
        {
            // find a ZuiGradient/stop list on the fill and pull the first stop's alpha down
            foreach (var f2 in fill.GetType().GetFields(BFi))
            {
                var v = f2.GetValue(fill); if (v == null) continue;
                var stopsF = v.GetType().GetField("stops", BFi);
                var stops = stopsF == null ? null : stopsF.GetValue(v) as System.Collections.IList;
                if (stops == null || stops.Count == 0) continue;
                var st = stops[0];
                var cF = st.GetType().GetField("color", BFi); if (cF == null) continue;
                var oc = (UnityEngine.Color)cF.GetValue(st);
                var lo = oc; lo.a = 0.4f; cF.SetValue(st, lo);
                if (st.GetType().IsValueType) stops[0] = st;
                dipped = true; dump += " dippedVia=" + f2.Name;
                exp(src, () => set(form, "opacity", 1.5f), () => set(form, "opacity", ov),
                    "ForkBlast.opacity @ a fill whose first stop alpha = 0.4", "0.55 -> 1.5");
                if (st.GetType().IsValueType) { var back = stops[0]; cF.SetValue(back, oc); stops[0] = back; }
                else cF.SetValue(st, oc);
                break;
            }
        }
        log.Append("ForkBlast fill probe: ").Append(dump).Append(" dipped=").Append(dipped).Append('\n');
    }
}

System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\corrections.tsv",
    "case\tmaxDiff\tperFrameDiff\tbaselineLit\tnote\n" + sb.ToString());
return log.ToString() + "\nelapsed=" + sw.ElapsedMilliseconds + "ms";

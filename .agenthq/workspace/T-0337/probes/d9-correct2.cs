// T-0337 §1 pass 2 — the cases d8 left ambiguous, each with the ONE further condition the engine's own
// source says is missing. Bounded: fixed case list, every value restored.
var sb = new System.Text.StringBuilder();
var log = new System.Text.StringBuilder();
const int W = 64, H = 64, SEED = 1234567;
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
System.Func<string, int, object[]> mk = (formName, frames) => {
    var form = System.Activator.CreateInstance(ZType(formName));
    var src = System.Activator.CreateInstance(srcT);
    srcT.GetField("form", BFi).SetValue(src, form);
    srcT.GetField("frames", BFi).SetValue(src, frames);
    return new object[] { form, src };
};
System.Func<object, string, System.Reflection.FieldInfo> fi = (o, n) => o.GetType().GetField(n, BFi);
System.Func<object, string, object> get = (o, n) => fi(o, n).GetValue(o);
System.Action<object, string, object> set = (o, n, v) => fi(o, n).SetValue(o, v);
System.Action<object, float> setZ = (zv, v) => zv.GetType().GetProperty("staticValue", BFi).SetValue(zv, v);
System.Func<object, float> getZ = zv => (float)zv.GetType().GetProperty("staticValue", BFi).GetValue(zv);

System.Action<object, int, System.Action, System.Action, string, string> exp = (src, n, mutate, restore, caseName, note) => {
    var basis = new UnityEngine.Color32[n][]; var lit = new int[n];
    for (int i = 0; i < n; i++) { basis[i] = render(src, n > 1 ? (float)i / (n - 1) : 0f); lit[i] = litOf(basis[i]); }
    mutate();
    var d = new int[n];
    for (int i = 0; i < n; i++) d[i] = diff(basis[i], render(src, n > 1 ? (float)i / (n - 1) : 0f));
    restore();
    int max = 0; string per = "", litS = "";
    for (int i = 0; i < n; i++) { if (d[i] > max) max = d[i]; per += (i == 0 ? "" : "·") + d[i]; litS += (i == 0 ? "" : "·") + lit[i]; }
    sb.Append(caseName).Append('\t').Append(n).Append('\t').Append(max).Append('\t').Append(per).Append('\t').Append(litS).Append('\t').Append(note).Append('\n');
    log.Append(caseName).Append(" [N=").Append(n).Append("]: max=").Append(max).Append('\n');
};

// ── ArcBurst: are ghostDeepLo/Hi guarded, or is it simply that Bolt's default seed makes no ghosts? ──
{
    var p = mk("ArcBurstForm", 8); var form = p[0]; var src = p[1];
    var bolt = get(form, "bolt");
    int otr = (int)get(bolt, "trunks");
    float odp = (float)get(bolt, "ghostDeepP");
    float olo = (float)get(form, "ghostDeepLo");
    set(bolt, "trunks", otr + 6);                       // enough trunks that some ARE ghosts (d8 proved this)
    exp(src, 8, () => set(form, "ghostDeepLo", 0.29f), () => set(form, "ghostDeepLo", olo),
        "ArcBurst.ghostDeepLo @ trunks+6, ghostDeepP=0 (default)", "expected 0 — deep guard shut");
    set(bolt, "ghostDeepP", 0.9f);
    exp(src, 8, () => set(form, "ghostDeepLo", 0.29f), () => set(form, "ghostDeepLo", olo),
        "ArcBurst.ghostDeepLo @ trunks+6, ghostDeepP=0.9", "deep guard opened");
    float ohi = (float)get(form, "ghostDeepHi");
    exp(src, 8, () => set(form, "ghostDeepHi", 0.39f), () => set(form, "ghostDeepHi", ohi),
        "ArcBurst.ghostDeepHi @ trunks+6, ghostDeepP=0.9", "deep guard opened");
    set(bolt, "ghostDeepP", odp); set(bolt, "trunks", otr);
    // and the same two at Core, a layout whose ghostDeepP ships at 0.18
    var lay = fi(form, "layout"); var ol = lay.GetValue(form);
    lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Core"));
    exp(src, 8, () => set(form, "ghostDeepLo", 0.29f), () => set(form, "ghostDeepLo", olo),
        "ArcBurst.ghostDeepLo @ layout=Core (ghostDeepP ships 0.18)", "no edit but the layout");
    lay.SetValue(form, ol);
    // cage.coolK on a LONGER document — the window (coolStart, 1) has to contain a frame
    lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Cage"));
    var cage = get(form, "cage"); float ock = (float)get(cage, "coolK");
    srcT.GetField("frames", BFi).SetValue(src, 16);
    exp(src, 16, () => set(cage, "coolK", 3f), () => set(cage, "coolK", ock),
        "ArcBurst.cage.coolK @ Cage defaults, 16-frame document", "coolStart 0.88 — frame 15/15 only? ");
    srcT.GetField("frames", BFi).SetValue(src, 32);
    exp(src, 32, () => set(cage, "coolK", 3f), () => set(cage, "coolK", ock),
        "ArcBurst.cage.coolK @ Cage defaults, 32-frame document", "");
    srcT.GetField("frames", BFi).SetValue(src, 8);
    lay.SetValue(form, ol);
}

// ── PlasmaBloom: mode / plumeReach / plumeW need a tongue that EXISTS (plumeAmp > 0) ──
{
    var p = mk("PlasmaBloomForm", 8); var form = p[0]; var src = p[1];
    var md = fi(form, "mode"); var omd = md.GetValue(form);
    int olb = (int)get(form, "lobes");
    var pa = get(form, "plumeAmp"); float opa = getZ(pa);
    var la = get(form, "lobeAmp"); float ola = getZ(la);

    set(form, "lobes", 4); setZ(pa, 1.2f);
    exp(src, 8, () => md.SetValue(form, System.Enum.Parse(md.FieldType, "Plume")), () => md.SetValue(form, omd),
        "PlasmaBloom.mode @ lobes=4, plumeAmp=1.2", "Plume finally has a tongue to add");
    setZ(pa, opa);
    setZ(la, 0.8f);
    exp(src, 8, () => md.SetValue(form, System.Enum.Parse(md.FieldType, "Plume")), () => md.SetValue(form, omd),
        "PlasmaBloom.mode @ lobes=4, lobeAmp=0.8 (Bloom's own lobe gate)", "the OTHER branch differs");
    setZ(la, ola);

    md.SetValue(form, System.Enum.Parse(md.FieldType, "Plume"));
    setZ(pa, 1.2f);
    var pr = get(form, "plumeReach"); float opr = getZ(pr);
    exp(src, 8, () => setZ(pr, 2f), () => setZ(pr, opr), "PlasmaBloom.plumeReach @ Plume, lobes=4, plumeAmp=1.2", "");
    var pw = get(form, "plumeW"); float opw = getZ(pw);
    exp(src, 8, () => setZ(pw, 2f), () => setZ(pw, opw), "PlasmaBloom.plumeW @ Plume, lobes=4, plumeAmp=1.2", "");
    var gg = get(form, "gateGain"); float ogg = getZ(gg);
    var lm = fi(form, "lobeMode"); var olm = lm.GetValue(form);
    lm.SetValue(form, System.Enum.Parse(lm.FieldType, "Noise"));
    exp(src, 8, () => setZ(gg, 0.5f), () => setZ(gg, ogg), "PlasmaBloom.gateGain @ Plume, lobes=4, plumeAmp=1.2, Noise", "");
    md.SetValue(form, omd); setZ(pa, opa); setZ(la, 0.8f);
    exp(src, 8, () => setZ(gg, 0.5f), () => setZ(gg, ogg), "PlasmaBloom.gateGain @ Bloom, lobes=4, lobeAmp=0.8, Noise", "");
    setZ(la, ola); lm.SetValue(form, olm); set(form, "lobes", olb);
}

// ── ForkBlast: flash on a longer document, and opacity against a fill whose alpha is below 1 ──
{
    foreach (int n in new int[] { 8, 16, 32, 64 })
    {
        var p = mk("ForkBlastForm", n); var form = p[0]; var src = p[1];
        var zv = get(form, "flash"); float ov = getZ(zv);
        exp(src, n, () => setZ(zv, 0f), () => setZ(zv, ov), "ForkBlast.flash 0.7 -> 0", "how long must the clip be for the 12% window to contain a frame?");
    }
    var q = mk("ForkBlastForm", 8); var f2 = q[0]; var s2 = q[1];
    float oo = (float)get(f2, "opacity");
    var fill = srcT.GetField("shapeFill", BFi).GetValue(s2);
    var mF = fi(fill, "mode"); var cF = fi(fill, "color");
    log.Append("fill mode=").Append(mF.GetValue(fill)).Append(" color=").Append(cF.GetValue(fill)).Append('\n');
    var oc = (UnityEngine.Color)cF.GetValue(fill);
    var half = oc; half.a = 0.5f; cF.SetValue(fill, half);
    exp(s2, 8, () => set(f2, "opacity", 1.5f), () => set(f2, "opacity", oo),
        "ForkBlast.opacity @ a Shape Fill with alpha 0.5", "0.55 -> 1.5");
    cF.SetValue(fill, oc);
    exp(s2, 8, () => set(f2, "opacity", 1.5f), () => set(f2, "opacity", oo),
        "ForkBlast.opacity @ the default Solid white fill (alpha 1)", "restored — expected 0");
}

System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\corrections2.tsv",
    "case\tframes\tmaxDiff\tperFrameDiff\tbaselineLit\tnote\n" + sb.ToString());
return log.ToString() + "elapsed=" + sw.ElapsedMilliseconds + "ms";

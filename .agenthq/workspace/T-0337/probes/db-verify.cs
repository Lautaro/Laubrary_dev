// T-0337 — the thirteen new greyings, read back off the DRAWER's own tables: greyed at the form's own
// factory defaults, live the moment the named guard is raised, and the reason on the control's own tooltip.
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var BFs = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

System.Type drawerT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    System.Type outer = null;
    try { outer = a.GetType("Laubrary.PyreShaper.Editor.PyreFormShaperUI"); } catch { }
    if (outer == null) continue;
    foreach (var n in outer.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
        if (n.Name == "Drawer") drawerT = n;
    if (drawerT != null) break;
}
if (drawerT == null) return "DRAWER NOT FOUND";
var reasonM = drawerT.GetMethod("DialInertReason", BFs);
var tipM    = drawerT.GetMethod("DialTooltip", BFs);
var guardM  = drawerT.GetMethod("IsDialInertGuard", BFs);
var rootF   = drawerT.GetField("_reflectingForm", BFs);

var refT = ZType("ZuiReflect");
var fieldsOfM = refT.GetMethod("FieldsOf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
System.Func<System.Type, string, System.Reflection.FieldInfo> find = (t, n) => {
    foreach (var f in fieldsOfM.Invoke(null, new object[] { t }) as System.Reflection.FieldInfo[]) if (f.Name == n) return f;
    return null;
};
System.Func<object, string, object> get = (o, n) => o.GetType().GetField(n, BFi).GetValue(o);
System.Action<object, string, object> set = (o, n, v) => o.GetType().GetField(n, BFi).SetValue(o, v);
System.Action<object, float> setZ = (zv, v) => zv.GetType().GetProperty("staticValue", BFi).SetValue(zv, v);

// report(ownerObject, fieldName, label) → greyed? + the reason
System.Func<object, string, string, string> ask = (owner, fname, label) => {
    var f = find(owner.GetType(), fname);
    if (f == null) return label + "\tFIELD NOT FOUND\t";
    string r = reasonM.Invoke(null, new object[] { f, owner }) as string;
    return label + '\t' + (string.IsNullOrEmpty(r) ? "LIVE" : "GREYED") + '\t' + (r ?? "");
};

int greyedAtDefaults = 0, freedByGuard = 0, rows = 0;
System.Action<object, string, string, System.Action, System.Action> pair = (owner, fname, label, open, close) => {
    string a = ask(owner, fname, label + " @ defaults");
    if (a.Contains("\tGREYED\t")) greyedAtDefaults++;
    sb.Append(a).Append('\n'); rows++;
    open();
    string b = ask(owner, fname, label + " @ guard raised");
    if (b.Contains("\tLIVE\t")) freedByGuard++;
    sb.Append(b).Append('\n'); rows++;
    close();
};

// ── Arc Burst ────────────────────────────────────────────────────────────────────────────────────────
{
    var form = System.Activator.CreateInstance(ZType("ArcBurstForm"));
    var bolt = get(form, "bolt");
    pair(form, "ghostDeepLo", "ArcBurst.ghostDeepLo (Bolt)", () => set(bolt, "ghostDeepP", 0.5f), () => set(bolt, "ghostDeepP", 0f));
    pair(form, "ghostDeepHi", "ArcBurst.ghostDeepHi (Bolt)", () => set(bolt, "ghostDeepP", 0.5f), () => set(bolt, "ghostDeepP", 0f));
    // and on a layout that already ships it open, plus the two that have no ghosts at all
    var lay = form.GetType().GetField("layout", BFi);
    lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Core"));
    sb.Append(ask(form, "ghostDeepLo", "ArcBurst.ghostDeepLo @ layout=Core (ships 0.18)")).Append('\n'); rows++;
    lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Crown"));
    sb.Append(ask(form, "ghostDeepLo", "ArcBurst.ghostDeepLo @ layout=Crown (no ghosts at all)")).Append('\n'); rows++;
    lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Lattice"));
    sb.Append(ask(form, "ghostDeepHi", "ArcBurst.ghostDeepHi @ layout=Lattice (no ghosts at all)")).Append('\n'); rows++;
    lay.SetValue(form, System.Enum.Parse(lay.FieldType, "Bolt"));
    // the four that must stay LIVE
    sb.Append(ask(form, "keepHueFloor", "ArcBurst.keepHueFloor (must stay LIVE)")).Append('\n'); rows++;
    var lat = get(form, "lattice");
    sb.Append(ask(lat, "flyLo", "lattice.flyLo (must stay LIVE)")).Append('\n'); rows++;
    sb.Append(ask(lat, "flyHi", "lattice.flyHi (must stay LIVE)")).Append('\n'); rows++;
    var cage = get(form, "cage");
    sb.Append(ask(cage, "coolK", "cage.coolK (must stay LIVE)")).Append('\n'); rows++;
}

// ── Plasma Bloom ─────────────────────────────────────────────────────────────────────────────────────
{
    var form = System.Activator.CreateInstance(ZType("PlasmaBloomForm"));
    var dX = get(form, "driftX"); var dA = get(form, "driftAmt");
    foreach (var n in new string[] { "driftEase", "driftLin", "driftLag" })
        pair(form, n, "PlasmaBloom." + n, () => { setZ(dX, 1f); setZ(dA, 0.8f); }, () => { setZ(dX, 0f); setZ(dA, 0f); });
    // one side of the mutual pair alone must NOT free them — that is the pair still being a pair
    setZ(dX, 1f);
    sb.Append(ask(form, "driftEase", "PlasmaBloom.driftEase @ Drift X alone")).Append('\n'); rows++;
    setZ(dX, 0f);

    var la = get(form, "lobeAmp"); var pa = get(form, "plumeAmp");
    pair(form, "mode", "PlasmaBloom.mode", () => { set(form, "lobes", 4); setZ(la, 0.8f); }, () => { set(form, "lobes", 0); setZ(la, 0f); });
    pair(form, "gateGain", "PlasmaBloom.gateGain", () => { set(form, "lobes", 4); setZ(la, 0.8f); }, () => { set(form, "lobes", 0); setZ(la, 0f); });
    pair(form, "plumeAmp", "PlasmaBloom.plumeAmp", () => set(form, "lobes", 4), () => set(form, "lobes", 0));
    foreach (var n in new string[] { "plumeReach", "plumeW" })
        pair(form, n, "PlasmaBloom." + n, () => { set(form, "lobes", 4); setZ(pa, 1.2f); }, () => { set(form, "lobes", 0); setZ(pa, 0f); });
    pair(form, "plumeVary", "PlasmaBloom.plumeVary", () => { set(form, "lobes", 4); setZ(pa, 1.2f); }, () => { set(form, "lobes", 0); setZ(pa, 0f); });
    // lobes=2 exactly is the boundary the AtLeast read exists for
    set(form, "lobes", 1);
    sb.Append(ask(form, "plumeAmp", "PlasmaBloom.plumeAmp @ lobes=1 (one lobe is no lobes)")).Append('\n'); rows++;
    set(form, "lobes", 2);
    sb.Append(ask(form, "plumeAmp", "PlasmaBloom.plumeAmp @ lobes=2 (the boundary)")).Append('\n'); rows++;
    set(form, "lobes", 0);
    // plumeVary's third guard
    set(form, "lobes", 4); setZ(pa, 1.2f); setZ(get(form, "warp"), 0f);
    sb.Append(ask(form, "plumeVary", "PlasmaBloom.plumeVary @ warp=0 (third guard shut)")).Append('\n'); rows++;
    setZ(get(form, "warp"), 0.26f); set(form, "lobes", 0); setZ(pa, 0f);

    // the three populations — the guard is on the FORM, reached through the drawer's own reflecting-form slot
    foreach (var pop in new string[] { "chunks", "embers", "motes" })
    {
        var po = get(form, pop);
        rootF.SetValue(null, form);
        string a = ask(po, "swirl", "PlasmaBloom." + pop + ".swirl @ form Swirl=0");
        if (a.Contains("\tGREYED\t")) greyedAtDefaults++;
        sb.Append(a).Append('\n'); rows++;
        set(form, "swirl", 3f);
        string b = ask(po, "swirl", "PlasmaBloom." + pop + ".swirl @ form Swirl=3");
        if (b.Contains("\tLIVE\t")) freedByGuard++;
        sb.Append(b).Append('\n'); rows++;
        set(form, "swirl", 0f);
        rootF.SetValue(null, null);
        sb.Append(ask(po, "swirl", "PlasmaBloom." + pop + ".swirl with NO card being built (must fail OPEN)")).Append('\n'); rows++;
    }
}

// ── Fork Blast — both must stay LIVE, with the sentence on the tooltip ────────────────────────────────
{
    var form = System.Activator.CreateInstance(ZType("ForkBlastForm"));
    foreach (var n in new string[] { "flash", "opacity" })
    {
        sb.Append(ask(form, n, "ForkBlast." + n + " (must stay LIVE)")).Append('\n'); rows++;
        var f = find(form.GetType(), n);
        sb.Append("  tooltip: ").Append((tipM.Invoke(null, new object[] { f }) as string ?? "<none>")).Append('\n');
    }
}

// ── the new guard fields must all be declared as guards, or a freed dial stays grey ───────────────────
{
    var want = new System.Collections.Generic.List<string[]>
    {
        new string[]{"ArcBurstForm","layout"}, new string[]{"BoltSettings","ghostDeepP"},
        new string[]{"CoreSettings","ghostDeepP"}, new string[]{"CageSettings","ghostDeepP"},
        new string[]{"PlasmaBloomForm","driftX"}, new string[]{"PlasmaBloomForm","driftY"},
        new string[]{"PlasmaBloomForm","driftAmt"}, new string[]{"PlasmaBloomForm","lobes"},
        new string[]{"PlasmaBloomForm","lobeAmp"}, new string[]{"PlasmaBloomForm","plumeAmp"},
        new string[]{"PlasmaBloomForm","warp"}, new string[]{"PlasmaBloomForm","swirl"},
    };
    foreach (var w in want)
    {
        System.Type t = ZType(w[0]);
        if (t == null) { foreach (var tt in ZType("ArcBurstForm").GetNestedTypes()) if (tt.Name == w[0]) t = tt; }
        var f = t == null ? null : find(t, w[1]);
        bool g = f != null && (bool)guardM.Invoke(null, new object[] { f });
        sb.Append("guard ").Append(w[0]).Append('.').Append(w[1]).Append('\t').Append(f == null ? "FIELD NOT FOUND" : (g ? "yes" : "NO — a freed dial would stay grey")).Append('\t').Append('\n');
        rows++;
    }
}

System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\verify-greying.tsv",
    "case\tstate\treason\n" + sb.ToString());
return "rows=" + rows + " greyedAtDefaults=" + greyedAtDefaults + " freedByGuard=" + freedByGuard + "\n" + sb.ToString();

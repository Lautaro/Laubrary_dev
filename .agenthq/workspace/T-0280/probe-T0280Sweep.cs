// T-0280 curve-aware, gate-aware dial sweep.
// __GEN__  = generator token
// __STAGE__ = A (32x32 mid) | B (native, multi-frame) | C (native, multi-frame, gates open)
// __PATHS__ = ;-separated field paths
var report = new System.Text.StringBuilder();
var sw = new System.Diagnostics.Stopwatch(); sw.Start();
string GEN = "__GEN__";
string STAGE = "__STAGE__";
string[] PATHS = "__PATHS__".Split(';');

Laubrary.Shaper.ShaperDocument doc = null;
int[] testFrames = null;

int CountDiff(UnityEngine.Color32[] a, UnityEngine.Color32[] b)
{
    if (a == null || b == null) return -1;
    int n = System.Math.Min(a.Length, b.Length), c = 0;
    for (int i = 0; i < n; i++) { var x = a[i]; var y = b[i]; if (x.r != y.r || x.g != y.g || x.b != y.b || x.a != y.a) c++; }
    return c;
}
int Lit(UnityEngine.Color32[] a) { int c = 0; if (a != null) foreach (var p in a) if (p.a > 0) c++; return c; }

System.Func<UnityEngine.Color32[][]> RenderAll = () =>
{
    var r = new UnityEngine.Color32[testFrames.Length][];
    for (int i = 0; i < testFrames.Length; i++) r[i] = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, testFrames[i]);
    return r;
};
System.Func<UnityEngine.Color32[][], UnityEngine.Color32[][], int> DiffAll = (a, b) =>
{
    int m = 0; for (int i = 0; i < a.Length; i++) { int d = CountDiff(a[i], b[i]); if (d > m) m = d; } return m;
};

// ── the object graph ───────────────────────────────────────────────────────────────────────
Laubrary.Pyre.PyreForm MakeForm(string g)
{
    switch (g)
    {
        case "Orb": return new Laubrary.Pyre.Forms.Kiln.OrbForm();
        case "Torch": return new Laubrary.Pyre.Forms.Kiln.TorchForm();
        case "Jet": return new Laubrary.Pyre.Forms.Kiln.JetForm();
        case "RadialJet": return new Laubrary.Pyre.Forms.Kiln.RadialJetForm();
        case "ExplosiveJet": return new Laubrary.Pyre.Forms.Kiln.ExplosiveJetForm();
    }
    return null;
}

object ROOT = null;                       // the object paths are rooted at ("form" or "src")
Laubrary.Shaper.IShaperCompositeSource SRC = null;
string rootName = "form";

if (GEN == "Fire") { var s = new Laubrary.PyreShaper.FireCompositeSource(); SRC = s; ROOT = s; rootName = "src"; }
else if (GEN == "Fireball") { var s = new Laubrary.PyreShaper.FireballCompositeSource(); SRC = s; ROOT = s; rootName = "src"; }
else { var f = MakeForm(GEN); var s = new Laubrary.PyreShaper.PyreFormCompositeSource { form = f }; SRC = s; ROOT = f; rootName = "form"; }

// canvas: A = 32x32; B/C = the generator's own native frame where it declares one
int CW = 32, CH = 32, BW = 96, BH = 96; float HX = 64f, HY = 64f;
int frames = 3;
if (STAGE != "A")
{
    int nw = 128, nh = 128;
    var settingsField = ROOT.GetType().GetField(GEN == "Jet" ? "gout" : GEN == "RadialJet" ? "corona" : GEN == "ExplosiveJet" ? "detonate" : GEN == "Torch" ? "barbs" : "emberdrift");
    if (settingsField != null)
    {
        var so = settingsField.GetValue(ROOT);
        var fw = so?.GetType().GetField("w"); var fh = so?.GetType().GetField("h");
        if (fw != null && fh != null && fw.FieldType == typeof(int)) { nw = (int)fw.GetValue(so); nh = (int)fh.GetValue(so); }
    }
    if (nw < 32) nw = 128; if (nh < 32) nh = 128;
    CW = nw; CH = nh; BW = nw; BH = nh; HX = nw * 0.5f; HY = nh * 0.5f;
    frames = 5;
}

doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
doc.canvasWidth = CW; doc.canvasHeight = CH; doc.pixelSize = 1f;
doc.frameCount = frames; doc.frameRate = 12f; doc.seed = 12345u;
var node0 = new Laubrary.Shaper.ShaperNode { name = "N", kind = Laubrary.Shaper.ShaperNodeKind.Composite,
    composite = new Laubrary.Shaper.ShaperCompositeDef { source = SRC, halfExtentX = HX, halfExtentY = HY, bakeWidth = BW, bakeHeight = BH } };
doc.layers = new System.Collections.Generic.List<Laubrary.Shaper.ShaperLayer> {
    new Laubrary.Shaper.ShaperLayer { name = "L", enabled = true, root = node0 } };
testFrames = STAGE == "A" ? new[] { 1 } : (STAGE == "D" ? new[] { 0, 1, 2, 3, 4 } : new[] { 1, 2, 3 });

// ── stage C: open the gates the source says these clusters sit behind ──────────────────────
object Sub(object o, string n) { var f = o?.GetType().GetField(n); return f?.GetValue(o); }
void SetF(object o, string n, object v) { var f = o?.GetType().GetField(n); if (f == null) return; try { f.SetValue(o, System.Convert.ChangeType(v, f.FieldType)); } catch { } }
void SetZ(object o, string n, float v) { var f = o?.GetType().GetField(n); if (f == null || f.FieldType != typeof(ZUIValue)) return; var z = f.GetValue(o) as ZUIValue; if (z == null) { z = new ZUIValue(v); f.SetValue(o, z); } z.mode = ZUIValue.Mode.Static; z.staticValue = v; }

var gatesOpened = new System.Collections.Generic.List<string>();
var GATES = new System.Collections.Generic.HashSet<string>("__GATES__".Split(','));
bool G(string k) => GATES.Contains(k) || GATES.Contains("all");
if (STAGE == "C")
{
    if (GEN == "Jet" || GEN == "RadialJet" || GEN == "ExplosiveJet")
    {
        var s = Sub(ROOT, GEN == "Jet" ? "gout" : GEN == "RadialJet" ? "corona" : "detonate");
        if (G("ring")) { SetF(s, "ringN", 3); SetZ(s, "ringAmp", 2.0f); SetF(s, "ringGrow", 0.5f); SetZ(s, "ringR0", 6f); SetZ(s, "ringReach", 0.6f); SetF(s, "ringLife", 0.8f); SetF(s, "ringK", 12); gatesOpened.Add("ringN=3"); }
        if (G("ringflat")) { SetF(s, "ringFlat", true); SetF(s, "ringArc", 140f); gatesOpened.Add("ringFlat"); }
        if (G("pulse")) { SetF(s, "pulseN", 2); SetZ(s, "pulseDepth", 0.7f); gatesOpened.Add("pulse"); }
        if (G("shock")) { SetF(s, "shockN", 3f); SetZ(s, "shockDepth", 0.5f); gatesOpened.Add("shock"); }
        if (G("sweep")) { SetZ(s, "sweep", 20f); SetF(s, "sweep", 20f); SetF(s, "sweepN", 1); gatesOpened.Add("sweep"); }
        if (G("rootr")) { SetZ(s, "rootR", 4f); SetF(s, "rootR", 4f); gatesOpened.Add("rootR=4"); }
        if (G("srck")) { SetF(s, "rootK", 8); SetZ(s, "srcR", 0.12f); SetF(s, "srcR", 0.12f); gatesOpened.Add("srcR=0.12,rootK=8"); }
        if (G("lobe")) { SetF(s, "lobes", 4); SetZ(s, "lobeDepth", 0.5f); SetF(s, "lobeDepth", 0.5f); SetZ(s, "lobeKick", 0.3f); SetF(s, "lobeKick", 0.3f); gatesOpened.Add("lobes=4"); }
        var fl = G("flash") ? Sub(s, "flash") : null; if (fl != null) { SetZ(fl, "radius", 8f); SetF(fl, "radius", 8f); SetZ(fl, "amp", 2f); SetF(fl, "amp", 2f); SetF(fl, "life", 0.5f); SetF(fl, "grow", 0.6f); SetF(fl, "elong", 1.4f); gatesOpened.Add("flash"); }
        // a second detonation, so `share` has something to be a share OF
        var bf = G("blast") ? s?.GetType().GetField("blasts") : null;
        var bv = bf?.GetValue(s);
        if (bv is System.Collections.IList bl2 && bl2.Count > 0)
        {
            var et = bl2[0].GetType();
            var clone = System.Activator.CreateInstance(et);
            foreach (var ff in et.GetFields()) ff.SetValue(clone, ff.GetValue(bl2[0]));
            SetF(clone, "at", 0.5f);
            if (bl2.Count < 2) { if (bl2.IsFixedSize) { } else bl2.Add(clone); }
            gatesOpened.Add("blasts=" + bl2.Count);
        }
        else if (bv is System.Array ba2 && ba2.Length > 0)
        {
            var et = ba2.GetValue(0).GetType();
            var arr = System.Array.CreateInstance(et, 2);
            arr.SetValue(ba2.GetValue(0), 0);
            var clone = System.Activator.CreateInstance(et);
            foreach (var ff in et.GetFields()) ff.SetValue(clone, ff.GetValue(ba2.GetValue(0)));
            SetF(clone, "at", 0.5f);
            arr.SetValue(clone, 1);
            bf.SetValue(s, arr);
            gatesOpened.Add("blasts=2");
        }
    }
    else if (GEN == "Torch")
    {
        var s = Sub(ROOT, "barbs");
        if (G("lash")) { SetZ(s, "lash", 8f); gatesOpened.Add("lash=8"); }
        if (G("pulse")) { SetZ(s, "pulse", 0.5f); SetZ(s, "pulseGain", 0.35f); SetZ(s, "bulge", 0.4f); gatesOpened.Add("pulse/gain/bulge on"); }
        if (G("curl")) { SetF(s, "curl", 8f); gatesOpened.Add("curl=8"); }
    }
    else if (GEN == "Orb")
    {
        SetF(ROOT, "despeckle", true);
        gatesOpened.Add("despeckle=true");
    }
    else if (GEN == "Fire" || GEN == "Fireball")
    {
        SetF(ROOT, "arms", 3);
        gatesOpened.Add("arms=3");
    }
}

// ── path resolution ────────────────────────────────────────────────────────────────────────
bool Resolve(string path, out object owner, out System.Reflection.FieldInfo fi)
{
    owner = null; fi = null;
    var parts = path.Split('.');
    object cur = ROOT;
    for (int i = 1; i < parts.Length; i++)
    {
        string p = parts[i]; int idx = -1;
        int br = p.IndexOf('[');
        if (br >= 0) { idx = int.Parse(p.Substring(br + 1, p.Length - br - 2)); p = p.Substring(0, br); }
        var f = cur?.GetType().GetField(p);
        if (f == null) return false;
        if (i == parts.Length - 1 && idx < 0) { owner = cur; fi = f; return true; }
        object v = f.GetValue(cur);
        if (idx >= 0)
        {
            if (v is System.Array a) { if (a.Length <= idx) return false; v = a.GetValue(idx); }
            else if (v is System.Collections.IList l) { if (l.Count <= idx) return false; v = l[idx]; }
            else return false;
        }
        if (v == null) return false;
        cur = v;
    }
    return false;
}

// candidate perturbations for a field, honouring [Range]
System.Collections.Generic.List<object> Candidates(System.Reflection.FieldInfo f, object orig)
{
    var outv = new System.Collections.Generic.List<object>();
    var r = (UnityEngine.RangeAttribute)System.Attribute.GetCustomAttribute(f, typeof(UnityEngine.RangeAttribute));
    var t = f.FieldType;
    if (t == typeof(float))
    {
        float o = (float)orig;
        if (r != null) { outv.Add(r.min); outv.Add(r.max); outv.Add((r.min + r.max) * 0.5f); }
        else { outv.Add(o + 7.5f); outv.Add(o - 7.5f); outv.Add(o * 3f + 1f); outv.Add(0f); }
    }
    else if (t == typeof(int))
    {
        int o = (int)orig;
        if (r != null) { outv.Add((int)r.min); outv.Add((int)r.max); outv.Add((int)((r.min + r.max) * 0.5f)); }
        else { outv.Add(o + 3); outv.Add(o - 3); outv.Add(o * 3 + 1); outv.Add(0); }
    }
    else if (t == typeof(bool)) outv.Add(!(bool)orig);
    else if (t.IsEnum) { foreach (var v in System.Enum.GetValues(t)) if (!v.Equals(orig)) outv.Add(v); }
    var final = new System.Collections.Generic.List<object>();
    foreach (var v in outv) if (!v.Equals(orig) && !final.Contains(v)) final.Add(v);
    return final;
}

var baseAll = RenderAll();
report.AppendLine("GEN=" + GEN + " STAGE=" + STAGE + " canvas=" + CW + "x" + CH + " frames=" + frames
    + " test=" + string.Join(",", System.Array.ConvertAll(testFrames, x => x.ToString()))
    + " litF1=" + Lit(baseAll[0]) + "/" + (CW * CH) + " gates=" + string.Join("|", gatesOpened));

foreach (var rawPath in PATHS)
{
    string path = rawPath.Trim(); if (path.Length == 0) continue;
    if (sw.ElapsedMilliseconds > 220000) { report.AppendLine("TIMEBOX-STOP at " + path); break; }
    object owner; System.Reflection.FieldInfo f;
    if (!Resolve(path, out owner, out f)) { report.AppendLine("R\t" + path + "\tUNRESOLVED\t0"); continue; }

    int best = 0; string bestAt = "";
    if (f.FieldType == typeof(ZUIValue))
    {
        var zv = f.GetValue(owner) as ZUIValue;
        if (zv == null) { report.AppendLine("R\t" + path + "\tNULL-ZUIVALUE\t0"); continue; }
        var snap = new ZUIValue(); snap.CopyFrom(zv);
        var r = (UnityEngine.RangeAttribute)System.Attribute.GetCustomAttribute(f, typeof(UnityEngine.RangeAttribute));
        float o = zv.staticValue;
        var vals = new System.Collections.Generic.List<float>();
        if (r != null) { vals.Add(r.min); vals.Add(r.max); vals.Add((r.min + r.max) * 0.5f); }
        else { vals.Add(o + 5f + System.Math.Abs(o) * 0.5f); vals.Add(o - 5f - System.Math.Abs(o) * 0.5f); vals.Add(0f); vals.Add(o * 3f + 1f); }
        foreach (var v in vals)
        {
            // curve-aware: force Static for the test whatever mode it was authored in, then restore verbatim
            zv.mode = ZUIValue.Mode.Static; zv.staticValue = v;
            int d = DiffAll(baseAll, RenderAll());
            if (d > best) { best = d; bestAt = snap.mode + "->Static " + v; }
            if (best > 0) break;
        }
        zv.CopyFrom(snap);
        report.AppendLine("R\t" + path + "\t" + (best > 0 ? "LIVE" : "DEAD") + "\t" + best + "\t" + bestAt + "\tmode=" + snap.mode);
    }
    else
    {
        object orig = f.GetValue(owner);
        foreach (var v in Candidates(f, orig))
        {
            try { f.SetValue(owner, v); } catch { continue; }
            int d = DiffAll(baseAll, RenderAll());
            if (d > best) { best = d; bestAt = System.Convert.ToString(orig) + "->" + System.Convert.ToString(v); }
            if (best > 0) break;
        }
        f.SetValue(owner, orig);
        report.AppendLine("R\t" + path + "\t" + (best > 0 ? "LIVE" : "DEAD") + "\t" + best + "\t" + bestAt + "\t" + f.FieldType.Name);
    }
}

UnityEngine.Object.DestroyImmediate(doc);
report.AppendLine("elapsedMs=" + sw.ElapsedMilliseconds);
return report.ToString();

// A19 — T-0280's generator sweep, re-run one form per call: native canvas, three phases, every dial
// driven to its own [Range] ends (ZUIValue snapshotted and forced Static so a Curve dial is exercised too).
// EditorPrefs A19.gen = generator token, A19.lo / A19.hi = the field-index window for this batch.
var SB = new System.Text.StringBuilder();
var sw = new System.Diagnostics.Stopwatch(); sw.Start();
string GEN = UnityEditor.EditorPrefs.GetString("A19.gen");
int LO = UnityEditor.EditorPrefs.GetInt("A19.lo", 0), HI = UnityEditor.EditorPrefs.GetInt("A19.hi", 9999);

System.Func<string, object> MakeSource = g =>
{
    if (g == "Fire") return new Laubrary.PyreShaper.FireCompositeSource();
    if (g == "Fireball") return new Laubrary.PyreShaper.FireballCompositeSource();
    Laubrary.Pyre.PyreForm f = null;
    if (g == "Orb") f = new Laubrary.Pyre.Forms.Kiln.OrbForm();
    else if (g == "Torch") f = new Laubrary.Pyre.Forms.Kiln.TorchForm();
    else if (g == "Jet") f = new Laubrary.Pyre.Forms.Kiln.JetForm();
    else if (g == "RadialJet") f = new Laubrary.Pyre.Forms.Kiln.RadialJetForm();
    else if (g == "ExplosiveJet") f = new Laubrary.Pyre.Forms.Kiln.ExplosiveJetForm();
    else if (g == "Inferno") f = new Laubrary.Pyre.Forms.Kiln.InfernoForm();
    else if (g == "ForkBlast") f = new Laubrary.Pyre.Forms.Kiln.ForkBlastForm();
    else if (g == "ArcBurst") f = new Laubrary.Pyre.Forms.Kiln.ArcBurstForm();
    else if (g == "PlasmaBloom") f = new Laubrary.Pyre.Forms.Kiln.PlasmaBloomForm();
    return new Laubrary.PyreShaper.PyreFormCompositeSource { form = f };
};
var SRC = (Laubrary.Shaper.IShaperCompositeSource)MakeSource(GEN);
object ROOT = SRC is Laubrary.PyreShaper.PyreFormCompositeSource pfs ? (object)pfs.form : SRC;

int nw = 128, nh = 128;
string settingsName = GEN == "Jet" ? "gout" : GEN == "RadialJet" ? "corona" : GEN == "ExplosiveJet" ? "detonate"
                    : GEN == "Torch" ? "barbs" : GEN == "Orb" ? "emberdrift" : null;
if (settingsName != null)
{
    var so = ROOT.GetType().GetField(settingsName)?.GetValue(ROOT);
    var fw = so?.GetType().GetField("w"); var fh = so?.GetType().GetField("h");
    if (fw != null && fh != null && fw.FieldType == typeof(int)) { nw = (int)fw.GetValue(so); nh = (int)fh.GetValue(so); }
}
var doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
doc.canvasWidth = nw; doc.canvasHeight = nh; doc.pixelSize = 1f;
doc.frameCount = 5; doc.frameRate = 12f; doc.seed = 12345u;
var node0 = new Laubrary.Shaper.ShaperNode { name = "N", kind = Laubrary.Shaper.ShaperNodeKind.Composite,
    composite = new Laubrary.Shaper.ShaperCompositeDef { source = SRC, halfExtentX = nw * 0.5f, halfExtentY = nh * 0.5f, bakeWidth = nw, bakeHeight = nh } };
doc.layers = new System.Collections.Generic.List<Laubrary.Shaper.ShaperLayer> { new Laubrary.Shaper.ShaperLayer { name = "L", enabled = true, root = node0 } };
int[] FR = { 1, 2, 3 };

System.Func<UnityEngine.Color32[][]> RenderAll = () =>
{ var r = new UnityEngine.Color32[FR.Length][]; for (int i = 0; i < FR.Length; i++) r[i] = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, FR[i]); return r; };
System.Func<UnityEngine.Color32[][], UnityEngine.Color32[][], int> DiffAll = (a, b) =>
{ int m = 0; for (int i = 0; i < a.Length; i++) { int c = 0; var x = a[i]; var y = b[i]; for (int j = 0; j < x.Length; j++) if (!x[j].Equals(y[j])) c++; if (c > m) m = c; } return m; };

// ── collect every dial the card can reach: public instance fields, recursing into serializable owners ──
var dials = new System.Collections.Generic.List<System.Tuple<object, System.Reflection.FieldInfo, string>>();
var seenOwners = new System.Collections.Generic.HashSet<object>();
System.Action<object, string, int> Gather = null;
Gather = (o, path, depth) =>
{
    if (o == null || depth > 3) return;
    if (!seenOwners.Add(o)) return;
    foreach (var f in o.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
    {
        var ft = f.FieldType;
        string p = path == "" ? f.Name : path + "." + f.Name;
        if (ft == typeof(float) || ft == typeof(int) || ft == typeof(bool) || ft.IsEnum || ft == typeof(ZUIValue))
        { dials.Add(System.Tuple.Create(o, f, p)); continue; }
        if (ft.IsPrimitive || ft == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(ft)) continue;
        if (ft.Name.Contains("Ramp") || ft.Name.Contains("Gradient") || ft.Name.Contains("Fill") || ft.Name.Contains("Curve")) continue;
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(ft)) continue;
        if (ft.IsClass || (ft.IsValueType && !ft.IsPrimitive))
        { object v = null; try { v = f.GetValue(o); } catch { } if (v != null && !ft.IsValueType) Gather(v, p, depth + 1); }
    }
};
Gather(ROOT, "", 0);
SB.Append("GEN=").Append(GEN).Append(" canvas=").Append(nw).Append('x').Append(nh)
  .Append(" dials=").Append(dials.Count).Append(" batch=[").Append(LO).Append(',').Append(System.Math.Min(HI, dials.Count)).Append(")\n");

var baseline = RenderAll();
int litB = 0; foreach (var c in baseline[0]) if (c.a > 0) litB++;
SB.Append("baseline lit(frame1)=").Append(litB).Append('\n');

for (int i = LO; i < dials.Count && i < HI; i++)
{
    if (sw.Elapsed.TotalSeconds > 22) { SB.Append("STOPPED at index ").Append(i).Append(" (time)\n"); break; }
    var owner = dials[i].Item1; var f = dials[i].Item2; string path = dials[i].Item3;
    var ra = (UnityEngine.RangeAttribute)System.Attribute.GetCustomAttribute(f, typeof(UnityEngine.RangeAttribute));
    int best = 0;
    object snap = null; try { snap = f.GetValue(owner); } catch { continue; }
    var probes = new System.Collections.Generic.List<System.Action>();
    var restore = new System.Action(() => { try { f.SetValue(owner, snap); } catch { } });
    if (f.FieldType == typeof(float))
    { float lo = ra != null ? ra.min : -1f, hi = ra != null ? ra.max : 1f; probes.Add(() => f.SetValue(owner, lo)); probes.Add(() => f.SetValue(owner, hi)); }
    else if (f.FieldType == typeof(int))
    { int lo = ra != null ? (int)ra.min : 0, hi = ra != null ? (int)ra.max : 8; probes.Add(() => f.SetValue(owner, lo)); probes.Add(() => f.SetValue(owner, hi)); }
    else if (f.FieldType == typeof(bool))
    { bool b0 = (bool)snap; probes.Add(() => f.SetValue(owner, !b0)); }
    else if (f.FieldType.IsEnum)
    { foreach (var v in System.Enum.GetValues(f.FieldType)) { var vv = v; if (!vv.Equals(snap)) { probes.Add(() => f.SetValue(owner, vv)); } } }
    else if (f.FieldType == typeof(ZUIValue))
    {
        var z = snap as ZUIValue; if (z == null) continue;
        var keepMode = z.mode; float keepVal = z.staticValue;
        float lo = ra != null ? ra.min : 0f, hi = ra != null ? ra.max : (keepVal == 0f ? 1f : keepVal * 2f);
        probes.Add(() => { z.mode = ZUIValue.Mode.Static; z.staticValue = lo; });
        probes.Add(() => { z.mode = ZUIValue.Mode.Static; z.staticValue = hi; });
        restore = () => { z.mode = keepMode; z.staticValue = keepVal; };
    }
    int tried = 0;
    foreach (var p in probes)
    {
        if (tried++ >= 4) break;
        p(); int d = DiffAll(baseline, RenderAll()); restore();
        if (d > best) best = d;
    }
    restore();
    SB.Append(path).Append('\t').Append(f.FieldType.Name).Append('\t').Append(best).Append('\n');
}
UnityEngine.Object.DestroyImmediate(doc);
SB.Append("elapsed=").Append(sw.Elapsed.TotalSeconds.ToString("0.0")).Append("s\n");
return SB.ToString();

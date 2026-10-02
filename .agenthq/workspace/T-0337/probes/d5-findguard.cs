// T-0337 §2 pass 4 — for each dial still dead at LIVE frames, find WHICH sibling opens it.
// Bounded on purpose (the unbounded version of this is what wedged the editor for 75 minutes):
// one candidate value per sibling, one perturbation value for the dial, three live frames.
var sb = new System.Text.StringBuilder();
const int W = 48, H = 48, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var refT = ZType("ZuiReflect");
var fieldsOfM = refT.GetMethod("FieldsOf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);
System.Func<System.Type, System.Reflection.FieldInfo[]> fieldsOf = t => fieldsOfM.Invoke(null, new object[] { t }) as System.Reflection.FieldInfo[];

// form | path | gate (applied first)
string[] targets = {
"ArcBurstForm|keepHueFloor|","ArcBurstForm|ghostDeepLo|","ArcBurstForm|ghostDeepHi|",
"ArcBurstForm|bolt.ghostLo|layout=Bolt","ArcBurstForm|bolt.ghostHi|layout=Bolt",
"ArcBurstForm|bolt.ghostDeepP|layout=Bolt",
"ArcBurstForm|lattice.flyLo|layout=Lattice","ArcBurstForm|lattice.flyHi|layout=Lattice",
"ArcBurstForm|cage.coolK|layout=Cage",
"PlasmaBloomForm|driftAmt|","PlasmaBloomForm|driftEase|","PlasmaBloomForm|driftLin|","PlasmaBloomForm|driftLag|",
"PlasmaBloomForm|mode|","PlasmaBloomForm|lobes|","PlasmaBloomForm|gateGain|lobes=6",
"PlasmaBloomForm|plumeAmp|mode=Plume","PlasmaBloomForm|plumeReach|mode=Plume",
"PlasmaBloomForm|plumeW|mode=Plume","PlasmaBloomForm|plumeVary|mode=Plume",
"PlasmaBloomForm|chunks.swirl|","PlasmaBloomForm|embers.swirl|","PlasmaBloomForm|motes.swirl|","PlasmaBloomForm|flash|",
"ForkBlastForm|flash|","ForkBlastForm|aim|","ForkBlastForm|opacity|",
};

sb.Append("form\tpath\tgate\topenedBy\tpixels\n");

foreach (var spec in targets)
{
  var bits = spec.Split('|');
  string formName = bits[0], path = bits[1], gate = bits[2];
  var ft = ZType(formName);
  string found = ""; int foundPx = 0;

  // candidate sibling openers: the OWNER's own fields, plus (for a nested path) the form's own fields
  var openers = new System.Collections.Generic.List<string>();
  {
    var probeForm = System.Activator.CreateInstance(ft);
    object ow = probeForm; var sg = path.Split('.');
    for (int s = 0; s < sg.Length - 1; s++) { System.Reflection.FieldInfo q = null;
      foreach (var x in fieldsOf(ow.GetType())) if (x.Name == sg[s]) { q = x; break; }
      if (q == null) break; var nv = q.GetValue(ow); if (nv == null) { nv = System.Activator.CreateInstance(q.FieldType); q.SetValue(ow, nv); } ow = nv; }
    string prefix = path.Contains(".") ? path.Substring(0, path.LastIndexOf('.') + 1) : "";
    foreach (var x in fieldsOf(ow.GetType())) {
      if (prefix + x.Name == path) continue;
      if (x.FieldType == typeof(float) || x.FieldType == typeof(int) || x.FieldType == typeof(bool) || x.FieldType.Name == "ZUIValue")
        openers.Add(prefix + x.Name);
    }
  }

  foreach (var op in openers)
  {
    if (foundPx > 0) break;
    var form = System.Activator.CreateInstance(ft);
    var src = System.Activator.CreateInstance(srcT);
    srcT.GetField("form", BFi).SetValue(src, form);
    srcT.GetField("frames", BFi).SetValue(src, N);
    System.Func<string, object, System.Reflection.FieldInfo> resolve = null;
    System.Func<string, object[]> walkTo = p => {
      object ow = form; System.Reflection.FieldInfo q = null; var sg = p.Split('.');
      for (int s = 0; s < sg.Length; s++) { q = null;
        foreach (var x in fieldsOf(ow.GetType())) if (x.Name == sg[s]) { q = x; break; }
        if (q == null) return null;
        if (s < sg.Length - 1) { var nv = q.GetValue(ow); if (nv == null) { nv = System.Activator.CreateInstance(q.FieldType); q.SetValue(ow, nv); } ow = nv; } }
      return new object[]{ ow, q }; };

    if (!string.IsNullOrEmpty(gate)) { var kv = gate.Split('=');
      var gr = walkTo(kv[0]);
      if (gr != null) { var go = gr[0]; var gq = (System.Reflection.FieldInfo)gr[1];
        try { if (gq.FieldType.IsEnum) gq.SetValue(go, System.Enum.Parse(gq.FieldType, kv[1], true));
              else if (gq.FieldType == typeof(int)) gq.SetValue(go, int.Parse(kv[1]));
              else if (gq.FieldType == typeof(float)) gq.SetValue(go, float.Parse(kv[1]));
              else if (gq.FieldType == typeof(bool)) gq.SetValue(go, bool.Parse(kv[1])); } catch { } } }

    // open the candidate sibling
    var orr = walkTo(op); if (orr == null) continue;
    var oo = orr[0]; var oq = (System.Reflection.FieldInfo)orr[1];
    try {
      if (oq.FieldType == typeof(float)) oq.SetValue(oo, 1f);
      else if (oq.FieldType == typeof(int)) oq.SetValue(oo, 4);
      else if (oq.FieldType == typeof(bool)) oq.SetValue(oo, !(bool)oq.GetValue(oo));
      else if (oq.FieldType.Name == "ZUIValue") { var v = oq.GetValue(oo);
        if (v == null) { v = System.Activator.CreateInstance(oq.FieldType, new object[]{1f}); oq.SetValue(oo, v); }
        else oq.FieldType.GetProperty("staticValue", BFi).SetValue(v, 1f); }
    } catch { continue; }

    var tr = walkTo(path); if (tr == null) continue;
    var to = tr[0]; var tq = (System.Reflection.FieldInfo)tr[1];
    float[] phases = { 2f/(N-1), 3f/(N-1), 4f/(N-1) };
    var baseline = new UnityEngine.Color32[phases.Length][];
    for (int i = 0; i < phases.Length; i++) { var b = new UnityEngine.Color32[W*H]; renderM.Invoke(src, new object[]{W,H,phases[i],(uint)SEED,b}); baseline[i] = b; }
    System.Func<int> measure = () => { int worst = 0;
      for (int i = 0; i < phases.Length; i++) { var b = new UnityEngine.Color32[W*H]; renderM.Invoke(src, new object[]{W,H,phases[i],(uint)SEED,b});
        int n = 0; for (int k = 0; k < b.Length; k++) { var p = baseline[i][k]; if (p.r!=b[k].r||p.g!=b[k].g||p.b!=b[k].b||p.a!=b[k].a) n++; }
        if (n > worst) worst = n; } return worst; };

    int px = 0;
    var t = tq.FieldType;
    try {
      if (t.Name == "ZUIValue") { var v = tq.GetValue(to); if (v == null) { v = System.Activator.CreateInstance(t, new object[]{0f}); tq.SetValue(to, v); }
        var svP = t.GetProperty("staticValue", BFi); var mdP = t.GetProperty("mode", BFi);
        mdP.SetValue(v, System.Enum.Parse(mdP.PropertyType, "Static"));
        float ov = (float)svP.GetValue(v); svP.SetValue(v, ov == 1f ? 4f : 1f); px = measure(); }
      else if (t == typeof(float)) { float ov = (float)tq.GetValue(to); tq.SetValue(to, ov == 1f ? 4f : 1f); px = measure(); }
      else if (t == typeof(int)) { int ov = (int)tq.GetValue(to); tq.SetValue(to, ov == 4 ? 6 : 4); px = measure(); }
      else if (t == typeof(bool)) { tq.SetValue(to, !(bool)tq.GetValue(to)); px = measure(); }
      else if (t.IsEnum) { var ov = tq.GetValue(to);
        foreach (var vv in System.Enum.GetValues(t)) { if (vv.Equals(ov)) continue; tq.SetValue(to, vv); px = measure(); if (px > 0) break; } }
    } catch { px = 0; }
    if (px > 0) { found = op; foundPx = px; }
  }

  sb.Append(formName).Append('\t').Append(path).Append('\t').Append(gate).Append('\t')
    .Append(foundPx > 0 ? found : "(no single sibling opened it)").Append('\t').Append(foundPx).Append('\n');
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\declared-guards.tsv", sb.ToString());
return "rows=" + (sb.ToString().Split('\n').Length - 2);

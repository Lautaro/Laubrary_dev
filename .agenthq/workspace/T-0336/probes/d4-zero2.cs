// T-0336 §2 pass 3 — the zero rows, re-measured at LIVE frames.
// The control probe (d3-cover.cs) found that all three forms paint NOTHING at phase 0 and phase 1 — frame 0
// and the last frame are blank by design (ignite / burnt out). Passes 1 and 2 sampled phases 0 / 0.5 / 1, so
// two of their three comparison frames were blank pictures and the third was the only real evidence. Every
// zero below is therefore re-taken at frames 2, 3 and 4 of 8 (phases 0.286 / 0.429 / 0.571), where every form
// is lit, and then again with the ONE guard dial the form's own defaults leave at zero.
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var refT = ZType("ZuiReflect");
var fieldsOfM = refT.GetMethod("FieldsOf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);
System.Func<System.Type, System.Reflection.FieldInfo[]> fieldsOf = t => fieldsOfM.Invoke(null, new object[] { t }) as System.Reflection.FieldInfo[];

// form | path | gate to set first (field=value, "" = none) | guard to open if still zero (field=value, "" = none)
string[] targets = {
"ArcBurstForm|keepHueFloor||",
"ArcBurstForm|ghostDeepLo||",
"ArcBurstForm|ghostDeepHi||",
"ArcBurstForm|bolt.ghostLo|layout=Bolt|",
"ArcBurstForm|bolt.ghostHi|layout=Bolt|",
"ArcBurstForm|bolt.ghostDeepP|layout=Bolt|",
"ArcBurstForm|bolt.flashAmp|layout=Bolt|",
"ArcBurstForm|lattice.flyLo|layout=Lattice|",
"ArcBurstForm|lattice.flyHi|layout=Lattice|",
"ArcBurstForm|cage.coolK|layout=Cage|",
"PlasmaBloomForm|biasDir||biasAmt=1",
"PlasmaBloomForm|biasK||biasAmt=1",
"PlasmaBloomForm|halfDir||halfAmt=1",
"PlasmaBloomForm|halfSoft||halfAmt=1",
"PlasmaBloomForm|halfK||halfAmt=1",
"PlasmaBloomForm|driftX||driftAmt=4",
"PlasmaBloomForm|driftY||driftAmt=4",
"PlasmaBloomForm|driftAmt||",
"PlasmaBloomForm|driftEase||driftAmt=4",
"PlasmaBloomForm|driftLin||driftAmt=4",
"PlasmaBloomForm|driftLag||driftAmt=4",
"PlasmaBloomForm|mode||",
"PlasmaBloomForm|lobes||",
"PlasmaBloomForm|lobeMode|lobes=6|lobeAmp=1",
"PlasmaBloomForm|lobePow|lobes=6|lobeAmp=1",
"PlasmaBloomForm|lobePh|lobes=6|lobeAmp=1",
"PlasmaBloomForm|gateGain|lobes=6|lobeAmp=1",
"PlasmaBloomForm|plumeAmp|mode=Plume|",
"PlasmaBloomForm|plumeReach|mode=Plume|",
"PlasmaBloomForm|plumeW|mode=Plume|",
"PlasmaBloomForm|plumeVary|mode=Plume|",
"PlasmaBloomForm|chunks.swirl||",
"PlasmaBloomForm|embers.swirl||",
"PlasmaBloomForm|motes.swirl||",
"PlasmaBloomForm|flash||",
"ForkBlastForm|flash||",
"ForkBlastForm|aim||",
"ForkBlastForm|opacity||",
"ForkBlastForm|gobSizePx||gobs=6",
"ForkBlastForm|gobReach||gobs=6",
"ForkBlastForm|gobSwell||gobs=6",
"ForkBlastForm|gobLife||gobs=6",
"ForkBlastForm|gobAmount||gobs=6",
"ForkBlastForm|gobTiming||gobs=6",
};

sb.Append("form\tpath\tgate\tguard\tpixelsAtLiveFrames\tpixelsWithGuardOpen\tvalueTried\n");

foreach (var spec in targets)
{
  var bits = spec.Split('|');
  string formName = bits[0], path = bits[1], gate = bits[2], guard = bits[3];
  var ft = ZType(formName);

  System.Func<string, string, string, int> run = (gspec, hspec, tag) =>
  {
    var form = System.Activator.CreateInstance(ft);
    var src = System.Activator.CreateInstance(srcT);
    srcT.GetField("form", BFi).SetValue(src, form);
    srcT.GetField("frames", BFi).SetValue(src, N);
    System.Action<string> apply = s => {
      if (string.IsNullOrEmpty(s)) return;
      var kv = s.Split('='); System.Reflection.FieldInfo q = null;
      foreach (var x in fieldsOf(ft)) if (x.Name == kv[0]) { q = x; break; }
      if (q == null) return;
      try {
        if (q.FieldType.IsEnum) q.SetValue(form, System.Enum.Parse(q.FieldType, kv[1], true));
        else if (q.FieldType == typeof(bool)) q.SetValue(form, bool.Parse(kv[1]));
        else if (q.FieldType == typeof(int)) q.SetValue(form, int.Parse(kv[1]));
        else if (q.FieldType == typeof(float)) q.SetValue(form, float.Parse(kv[1]));
        else if (q.FieldType.Name == "ZUIValue") { var v = q.GetValue(form);
          if (v == null) { v = System.Activator.CreateInstance(q.FieldType, new object[]{0f}); q.SetValue(form, v); }
          q.FieldType.GetProperty("staticValue", BFi).SetValue(v, float.Parse(kv[1])); }
      } catch { }
    };
    apply(gspec); apply(hspec);

    object owner = form; System.Reflection.FieldInfo fi = null;
    var segs = path.Split('.');
    for (int s = 0; s < segs.Length; s++) {
      fi = null;
      foreach (var q in fieldsOf(owner.GetType())) if (q.Name == segs[s]) { fi = q; break; }
      if (fi == null) break;
      if (s < segs.Length - 1) { var nv = fi.GetValue(owner); if (nv == null) { nv = System.Activator.CreateInstance(fi.FieldType); fi.SetValue(owner, nv); } owner = nv; }
    }
    if (fi == null) return -1;

    float[] phases = { 2f/(N-1), 3f/(N-1), 4f/(N-1) };
    var baseline = new UnityEngine.Color32[phases.Length][];
    for (int i = 0; i < phases.Length; i++) { var b = new UnityEngine.Color32[W*H]; renderM.Invoke(src, new object[]{W,H,phases[i],(uint)SEED,b}); baseline[i] = b; }
    System.Func<int> measure = () => {
      int worst = 0;
      for (int i = 0; i < phases.Length; i++) {
        var b = new UnityEngine.Color32[W*H]; renderM.Invoke(src, new object[]{W,H,phases[i],(uint)SEED,b});
        int n = 0; for (int k = 0; k < b.Length; k++) { var p = baseline[i][k]; if (p.r!=b[k].r||p.g!=b[k].g||p.b!=b[k].b||p.a!=b[k].a) n++; }
        if (n > worst) worst = n; }
      return worst; };

    int best = 0; var t = fi.FieldType;
    float[] cands = { 1f, 4f, 16f, 90f, 0.25f, 0f, -1f };
    if (t.Name == "ZUIValue") {
      var v = fi.GetValue(owner); if (v == null) { v = System.Activator.CreateInstance(t, new object[]{0f}); fi.SetValue(owner, v); }
      var svP = t.GetProperty("staticValue", BFi); var mdP = t.GetProperty("mode", BFi);
      var om = mdP.GetValue(v); float ov = (float)svP.GetValue(v);
      mdP.SetValue(v, System.Enum.Parse(mdP.PropertyType, "Static"));
      foreach (var c in cands) { if (UnityEngine.Mathf.Approximately(c, ov)) continue; svP.SetValue(v, c); int d = measure(); if (d > best) best = d; if (best > 0) break; }
      mdP.SetValue(v, om); svP.SetValue(v, ov);
    } else if (t == typeof(float)) {
      float ov = (float)fi.GetValue(owner);
      foreach (var c in cands) { if (UnityEngine.Mathf.Approximately(c, ov)) continue; fi.SetValue(owner, c); int d = measure(); if (d > best) best = d; if (best > 0) break; }
      fi.SetValue(owner, ov);
    } else if (t == typeof(int)) {
      int ov = (int)fi.GetValue(owner);
      foreach (var c in new int[]{3,6,1,0,12}) { if (c == ov) continue; fi.SetValue(owner, c); int d = measure(); if (d > best) best = d; if (best > 0) break; }
      fi.SetValue(owner, ov);
    } else if (t == typeof(bool)) {
      bool ov = (bool)fi.GetValue(owner); fi.SetValue(owner, !ov); best = measure(); fi.SetValue(owner, ov);
    } else if (t.IsEnum) {
      var ov = fi.GetValue(owner);
      foreach (var vv in System.Enum.GetValues(t)) { if (vv.Equals(ov)) continue; fi.SetValue(owner, vv); int d = measure(); if (d > best) best = d; if (best > 0) break; }
      fi.SetValue(owner, ov);
    } else if (t.Name == "PyreRamp") {
      var rv = fi.GetValue(owner);
      if (rv != null) { var stopsF = t.GetField("stops", BFi);
        var stops = stopsF == null ? null : stopsF.GetValue(rv) as System.Collections.IList;
        if (stops != null && stops.Count > 0) { var st0 = stops[0];
          foreach (var sf in st0.GetType().GetFields(BFi)) if (sf.FieldType == typeof(UnityEngine.Color)) {
            var oc = sf.GetValue(st0); sf.SetValue(st0, UnityEngine.Color.magenta); best = measure(); sf.SetValue(st0, oc); break; } } }
    }
    return best;
  };

  int a = run(gate, "", "live");
  int b2 = string.IsNullOrEmpty(guard) ? a : run(gate, guard, "guard");
  sb.Append(formName).Append('\t').Append(path).Append('\t').Append(gate).Append('\t').Append(guard).Append('\t')
    .Append(a).Append('\t').Append(b2).Append('\t').Append('\n');
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0336\out\declared-zero-live.tsv", sb.ToString());
return "rows=" + (sb.ToString().Split('\n').Length - 2);

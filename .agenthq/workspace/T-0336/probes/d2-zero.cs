// T-0336 §2 pass 2 (BOUNDED) — the dials pass 1 measured at 0 pixels.
// For each: (a) what the drawer TELLS THE USER — PyreFormShaperUI's own DialInertReason (greyed + reason)
//               and DialTooltip (does the tooltip name the condition?);
//           (b) a bounded pixel retry across every value of the form's own gate enums (no brute-force
//               sibling scan — round 20's first attempt at that ran the editor for 75 minutes and had to be
//               killed; the sibling story is told by (a), which is what the card actually asks for).
var sb = new System.Text.StringBuilder();
const int W = 48, H = 48, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var BFsp = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var refT = ZType("ZuiReflect");
var fieldsOfM = refT.GetMethod("FieldsOf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);
var drawerT = ZType("PyreFormShaperUI").GetNestedType("Drawer", System.Reflection.BindingFlags.NonPublic);
var inertM = drawerT.GetMethod("DialInertReason", BFsp);
var tipM = drawerT.GetMethod("DialTooltip", BFsp);
var guardM = drawerT.GetMethod("IsDialInertGuard", BFsp);
System.Func<System.Type, System.Reflection.FieldInfo[]> fieldsOf = t => fieldsOfM.Invoke(null, new object[] { t }) as System.Reflection.FieldInfo[];

string[] targets = {
"ArcBurstForm|keepHueFloor","ArcBurstForm|ghostDeepLo","ArcBurstForm|ghostDeepHi",
"ArcBurstForm|bolt.ghostLo","ArcBurstForm|bolt.ghostHi","ArcBurstForm|bolt.ghostDeepP","ArcBurstForm|bolt.flashAmp",
"ArcBurstForm|lattice.flyLo","ArcBurstForm|lattice.flyHi","ArcBurstForm|cage.coolK",
"PlasmaBloomForm|biasDir","PlasmaBloomForm|biasK","PlasmaBloomForm|halfDir","PlasmaBloomForm|halfSoft","PlasmaBloomForm|halfK",
"PlasmaBloomForm|driftX","PlasmaBloomForm|driftY","PlasmaBloomForm|driftAmt","PlasmaBloomForm|driftEase","PlasmaBloomForm|driftLin","PlasmaBloomForm|driftLag",
"PlasmaBloomForm|mode","PlasmaBloomForm|lobes","PlasmaBloomForm|lobeMode","PlasmaBloomForm|lobePow","PlasmaBloomForm|lobePh",
"PlasmaBloomForm|gateGain","PlasmaBloomForm|plumeAmp","PlasmaBloomForm|plumeReach","PlasmaBloomForm|plumeW","PlasmaBloomForm|plumeVary",
"PlasmaBloomForm|chunks.swirl","PlasmaBloomForm|embers.swirl","PlasmaBloomForm|motes.swirl","PlasmaBloomForm|flash",
"PlasmaBloomForm|hueA","PlasmaBloomForm|hueB",
"ForkBlastForm|flash","ForkBlastForm|aim","ForkBlastForm|opacity",
"ForkBlastForm|gobSizePx","ForkBlastForm|gobReach","ForkBlastForm|gobSwell","ForkBlastForm|gobLife","ForkBlastForm|gobAmount","ForkBlastForm|gobTiming",
};
// gate ENUM / bool fields on each form whose every value is driven
var gateFields = new System.Collections.Generic.Dictionary<string,string[]> {
  { "ArcBurstForm",    new string[]{ "layout" } },
  { "PlasmaBloomForm", new string[]{ "mode", "lobeMode", "autoFit" } },
  { "ForkBlastForm",   new string[]{ "autoExposure" } },
};

sb.Append("form\tpath\tbestPixels\twhere\tisInertGuard\tinertReasonAtDefaults\ttooltipNamesCondition\ttooltipTail\n");

foreach (var spec in targets)
{
  var bits = spec.Split('|');
  string formName = bits[0], path = bits[1];
  var ft = ZType(formName);
  var form = System.Activator.CreateInstance(ft);
  var src = System.Activator.CreateInstance(srcT);
  srcT.GetField("form", BFi).SetValue(src, form);
  srcT.GetField("frames", BFi).SetValue(src, N);
  System.Func<float, UnityEngine.Color32[]> render = ph => {
    var buf = new UnityEngine.Color32[W * H];
    renderM.Invoke(src, new object[] { W, H, ph, (uint)SEED, buf }); return buf; };
  System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => {
    int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r!=b[i].r||a[i].g!=b[i].g||a[i].b!=b[i].b||a[i].a!=b[i].a) n++; return n; };

  object owner = form; System.Reflection.FieldInfo fi = null;
  var segs = path.Split('.');
  for (int s = 0; s < segs.Length; s++) {
    fi = null;
    foreach (var q in fieldsOf(owner.GetType())) if (q.Name == segs[s]) { fi = q; break; }
    if (fi == null) break;
    if (s < segs.Length - 1) { var nv = fi.GetValue(owner); if (nv == null) { nv = System.Activator.CreateInstance(fi.FieldType); fi.SetValue(owner, nv); } owner = nv; }
  }
  if (fi == null) { sb.Append(formName).Append('\t').Append(path).Append("\t?\tFIELD NOT FOUND\t\t\t\t\n"); continue; }

  string inert = null, tip = null; bool isGuard = false;
  try { inert = inertM.Invoke(null, new object[]{ fi, owner }) as string; } catch { }
  try { tip = tipM.Invoke(null, new object[]{ fi }) as string; } catch { }
  try { isGuard = (bool)guardM.Invoke(null, new object[]{ fi }); } catch { }
  if (tip == null) { var ta = (UnityEngine.TooltipAttribute)System.Attribute.GetCustomAttribute(fi, typeof(UnityEngine.TooltipAttribute)); tip = ta == null ? "" : ta.tooltip; }
  bool tipCond = tip != null && (tip.Contains("Nothing until") || tip.Contains("No effect") || tip.Contains("no effect")
                 || tip.Contains("only when") || tip.Contains("Only ") || tip.Contains("does nothing")
                 || tip.Contains("Needs ") || tip.Contains("above 0") || tip.Contains("non-zero") || tip.Contains("Has no effect"));

  System.Func<int> tryHere = () => {
    var b0 = render(0f); var bM = render(0.6f);
    int loc = 0; var t = fi.FieldType;
    System.Func<System.Action,int> pr = m => { try { m(); return System.Math.Max(diff(bM, render(0.6f)), diff(b0, render(0f))); } catch { return 0; } };
    float[] cands = { 1f, 8f, 90f, 0f };
    if (t.Name == "ZUIValue") {
      var v = fi.GetValue(owner); if (v == null) { v = System.Activator.CreateInstance(t, new object[]{0f}); fi.SetValue(owner, v); }
      var svP = t.GetProperty("staticValue", BFi); var mdP = t.GetProperty("mode", BFi);
      var om = mdP.GetValue(v); float ov = (float)svP.GetValue(v);
      mdP.SetValue(v, System.Enum.Parse(mdP.PropertyType, "Static"));
      foreach (var c in cands) { if (UnityEngine.Mathf.Approximately(c, ov)) continue; var cc=c; int d=pr(()=>svP.SetValue(v,cc)); if (d>loc) loc=d; if (loc>0) break; }
      mdP.SetValue(v, om); svP.SetValue(v, ov);
    } else if (t == typeof(float)) {
      float ov = (float)fi.GetValue(owner);
      foreach (var c in cands) { if (UnityEngine.Mathf.Approximately(c, ov)) continue; var cc=c; int d=pr(()=>fi.SetValue(owner,cc)); if (d>loc) loc=d; if (loc>0) break; }
      fi.SetValue(owner, ov);
    } else if (t == typeof(int)) {
      int ov = (int)fi.GetValue(owner);
      foreach (var c in new int[]{3,6,0,1}) { if (c==ov) continue; var cc=c; int d=pr(()=>fi.SetValue(owner,cc)); if (d>loc) loc=d; if (loc>0) break; }
      fi.SetValue(owner, ov);
    } else if (t == typeof(bool)) {
      bool ov = (bool)fi.GetValue(owner); loc = pr(()=>fi.SetValue(owner, !ov)); fi.SetValue(owner, ov);
    } else if (t.IsEnum) {
      var ov = fi.GetValue(owner);
      foreach (var vv in System.Enum.GetValues(t)) { if (vv.Equals(ov)) continue; var cap=vv; int d=pr(()=>fi.SetValue(owner,cap)); if (d>loc) loc=d; if (loc>0) break; }
      fi.SetValue(owner, ov);
    } else if (t.Name == "PyreRamp") {
      var rv = fi.GetValue(owner);
      if (rv != null) {
        var stopsF = t.GetField("stops", BFi);
        var stops = stopsF == null ? null : stopsF.GetValue(rv) as System.Collections.IList;
        if (stops != null && stops.Count > 0) {
          var st0 = stops[0];
          foreach (var sf in st0.GetType().GetFields(BFi)) if (sf.FieldType == typeof(UnityEngine.Color)) {
            var oc = sf.GetValue(st0); loc = pr(()=>sf.SetValue(st0, UnityEngine.Color.magenta)); sf.SetValue(st0, oc); break; }
        }
        if (loc == 0) { var spF = t.GetField("space", BFi);
          if (spF != null) { var os = spF.GetValue(rv);
            foreach (var vv in System.Enum.GetValues(spF.FieldType)) { if (vv.Equals(os)) continue; var cap=vv; int d=pr(()=>spF.SetValue(rv,cap)); if (d>loc) loc=d; if (loc>0) break; }
            spF.SetValue(rv, os); } }
      }
    }
    return loc;
  };

  int best = tryHere(); string where = best > 0 ? "factory defaults" : "";
  if (best == 0)
  {
    string[] gs; gateFields.TryGetValue(formName, out gs);
    if (gs != null) foreach (var gn in gs)
    {
      if (best > 0) break;
      System.Reflection.FieldInfo gf = null;
      foreach (var q in fieldsOf(form.GetType())) if (q.Name == gn) { gf = q; break; }
      if (gf == null) continue;
      var sav = gf.GetValue(form);
      if (gf.FieldType.IsEnum) {
        foreach (var vv in System.Enum.GetValues(gf.FieldType)) {
          gf.SetValue(form, vv); int d = tryHere();
          if (d > best) { best = d; where = gn + "=" + vv; }
          if (best > 0) break; }
      } else if (gf.FieldType == typeof(bool)) {
        gf.SetValue(form, !(bool)sav); int d = tryHere();
        if (d > best) { best = d; where = gn + "=" + (!(bool)sav); }
      }
      gf.SetValue(form, sav);
    }
  }

  sb.Append(formName).Append('\t').Append(path).Append('\t').Append(best).Append('\t').Append(where).Append('\t')
    .Append(isGuard).Append('\t').Append(inert == null ? "(null — guard open, or not live-checkable)" : inert).Append('\t')
    .Append(tipCond ? "YES" : "no").Append('\t')
    .Append(tip == null ? "" : (tip.Length > 160 ? tip.Substring(tip.Length - 160) : tip)).Append('\n');
}

System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0336\out\declared-zero.tsv", sb.ToString());
return "rows=" + (sb.ToString().Split('\n').Length - 2);

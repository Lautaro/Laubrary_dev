// CONTROL: does the hosted form actually paint anything at the phases the sweep samples?
// A dial that moves 0 pixels on a blank picture is a measurement fault, not an inert dial.
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);
foreach (var fn in new string[]{"ArcBurstForm","PlasmaBloomForm","ForkBlastForm"})
{
  var ft = ZType(fn);
  var form = System.Activator.CreateInstance(ft);
  var src = System.Activator.CreateInstance(srcT);
  srcT.GetField("form", BFi).SetValue(src, form);
  srcT.GetField("frames", BFi).SetValue(src, N);
  sb.Append(fn).Append(": ");
  for (int f = 0; f < N; f++) {
    float ph = (float)f/(N-1);
    var buf = new UnityEngine.Color32[W*H];
    renderM.Invoke(src, new object[]{ W, H, ph, (uint)SEED, buf });
    int lit = 0; foreach (var p in buf) if (p.a > 0) lit++;
    sb.Append("f").Append(f).Append("=").Append(lit).Append(" ");
  }
  sb.Append("\n");
  // and a few named defaults that the sweep's zero rows depend on
  foreach (var nm in new string[]{"gobs","lobes","mode","driftAmt","biasAmt","halfAmt","autoFit","clockStart","clockEnd","puffs","layout"}) {
    var q = ft.GetField(nm, BFi); if (q == null) continue;
    var v = q.GetValue(form);
    if (v != null && v.GetType().Name == "ZUIValue") v = v.GetType().GetProperty("staticValue", BFi).GetValue(v);
    sb.Append("   ").Append(nm).Append(" = ").Append(v).Append("\n");
  }
}
return sb.ToString();

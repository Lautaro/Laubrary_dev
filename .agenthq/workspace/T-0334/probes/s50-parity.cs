// T-0272 lineage — hosted Pyre parity, re-run at HEAD.
// Reference : a one-layer Pyre spec at Pyre's factory defaults, through PyreRenderer.RenderFrame.
// Candidate : the composite source's own Render at the same canvas, seed and phase.
// 64x64, seed 1234567, 3 frames of an 8-frame document (0 / mid / last).
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var catT = ZType("PyreCompositeCatalog"); if (catT == null) return "no PyreCompositeCatalog";
var srcT = ZType("PyreFormCompositeSource"); var layT = ZType("PyreLayerCompositeSource");
var pyreT = ZType("Pyre"); var pyreLayerT = ZType("PyreLayer"); var rendT = ZType("PyreRenderer");
var BFs = System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static;
var BFi = System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance;
var renderFrame = rendT.GetMethod("RenderFrame", BFs);
var all = catT.GetField("All", BFs).GetValue(null) as System.Array;

// every PyreForm subclass, keyed by DisplayName
var forms = new System.Collections.Generic.Dictionary<string, System.Type>();
var formBase = ZType("PyreForm");
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
  System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) { if (t.IsAbstract || !formBase.IsAssignableFrom(t)) continue;
    object inst = null; try { inst = System.Activator.CreateInstance(t); } catch { continue; }
    var dn = t.GetProperty("DisplayName", BFi); if (dn == null) continue;
    var name = dn.GetValue(inst) as string; if (!string.IsNullOrEmpty(name) && !forms.ContainsKey(name)) forms[name] = t; } }

System.Func<UnityEngine.Color32[],UnityEngine.Color32[],string> diff = (a, b) => {
  int vis = 0, exact = 0, maxCh = 0, covA = 0, covB = 0;
  for (int i = 0; i < a.Length && i < b.Length; i++) {
    if (a[i].a > 0) covA++; if (b[i].a > 0) covB++;
    bool same = a[i].r==b[i].r && a[i].g==b[i].g && a[i].b==b[i].b && a[i].a==b[i].a;
    if (!same) { exact++;
      if (a[i].a > 0 || b[i].a > 0) { vis++;
        int m = UnityEngine.Mathf.Max(UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(a[i].r-b[i].r), UnityEngine.Mathf.Abs(a[i].g-b[i].g)),
                UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(a[i].b-b[i].b), UnityEngine.Mathf.Abs(a[i].a-b[i].a)));
        if (m > maxCh) maxCh = m; } } }
  return vis + "\t" + exact + "\t" + maxCh + "\t" + covA + "\t" + covB; };

sb.Append("family\tsource\tframe\tvisibleDiff\texactDiff\tmaxCh\tcovPyre\tcovShaper\n");
foreach (var entryObj in all) {
  string display = entryObj.GetType().GetField("displayName", BFi).GetValue(entryObj) as string;
  System.Type ft; if (!forms.TryGetValue(display, out ft)) { sb.Append("Form\t").Append(display).Append("\t-\tNO FORM TYPE\n"); continue; }
  for (int k = 0; k < 3; k++) {
    int fi = k == 0 ? 0 : k == 1 ? N/2 : N-1;
    float phase = N <= 1 ? 0f : (float)fi / (N - 1);
    // reference
    var spec = UnityEngine.ScriptableObject.CreateInstance(pyreT);
    pyreT.GetField("canvasSize", BFi).SetValue(spec, W);
    pyreT.GetField("frameCount", BFi).SetValue(spec, N);
    pyreT.GetField("seed", BFi).SetValue(spec, SEED);
    var layer = System.Activator.CreateInstance(pyreLayerT);
    pyreLayerT.GetField("matteEnabled", BFi).SetValue(layer, false);
    pyreLayerT.GetField("form", BFi).SetValue(layer, System.Activator.CreateInstance(ft));
    var listT = typeof(System.Collections.Generic.List<>).MakeGenericType(pyreLayerT);
    var list = System.Activator.CreateInstance(listT);
    listT.GetMethod("Add").Invoke(list, new object[]{ layer });
    pyreT.GetField("layers", BFi).SetValue(spec, list);
    var refPx = renderFrame.Invoke(null, new object[]{ spec, fi }) as UnityEngine.Color32[];
    // candidate
    var src = System.Activator.CreateInstance(srcT);
    srcT.GetField("form", BFi).SetValue(src, System.Activator.CreateInstance(ft));
    srcT.GetField("frames", BFi).SetValue(src, N);
    var buf = new UnityEngine.Color32[W*H];
    srcT.GetMethod("Render", BFi).Invoke(src, new object[]{ W, H, phase, (uint)SEED, buf });
    sb.Append("Form\t").Append(display).Append("\t").Append(fi).Append("\t").Append(diff(refPx, buf)).Append("\n");
    UnityEngine.Object.DestroyImmediate(spec);
  }
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0334\out\parity-forms.tsv", sb.ToString());
return sb.ToString();

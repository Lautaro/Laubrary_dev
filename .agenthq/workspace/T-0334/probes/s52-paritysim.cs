// The two stateful simulations, same method: Pyre's own Fire/Fireball layer vs the composite sources.
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var pyreT = ZType("Pyre"); var pyreLayerT = ZType("PyreLayer"); var rendT = ZType("PyreRenderer");
var shapeEnum = ZType("ShapeForm");
var BFs = System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static;
var BFi = System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance;
var renderFrame = rendT.GetMethod("RenderFrame", BFs);
System.Func<UnityEngine.Color32[],UnityEngine.Color32[],string> diff = (a,b) => {
  int vis=0,exact=0,maxCh=0,covA=0,covB=0;
  for (int i=0;i<a.Length&&i<b.Length;i++){ if(a[i].a>0)covA++; if(b[i].a>0)covB++;
    if(a[i].r!=b[i].r||a[i].g!=b[i].g||a[i].b!=b[i].b||a[i].a!=b[i].a){exact++;
      if(a[i].a>0||b[i].a>0){vis++;
        int m=UnityEngine.Mathf.Max(UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(a[i].r-b[i].r),UnityEngine.Mathf.Abs(a[i].g-b[i].g)),
              UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(a[i].b-b[i].b),UnityEngine.Mathf.Abs(a[i].a-b[i].a)));
        if(m>maxCh)maxCh=m; } } }
  return vis+"\t"+exact+"\t"+maxCh+"\t"+covA+"\t"+covB; };
sb.Append("family\tsource\tframe\tvisibleDiff\texactDiff\tmaxCh\tcovPyre\tcovShaper\n");
string[] names = { "Fire", "Fireball" };
string[] types = { "FireCompositeSource", "FireballCompositeSource" };
for (int s2 = 0; s2 < 2; s2++) {
  var srcT = ZType(types[s2]); if (srcT == null) { sb.Append("Simulation\t").Append(names[s2]).Append("\tNO TYPE\n"); continue; }
  var framesF = srcT.GetField("frames", BFi);
  for (int k=0;k<3;k++) {
    int fi = k==0?0:k==1?N/2:N-1; float phase = (float)fi/(N-1);
    var spec = UnityEngine.ScriptableObject.CreateInstance(pyreT);
    pyreT.GetField("canvasSize",BFi).SetValue(spec,W); pyreT.GetField("frameCount",BFi).SetValue(spec,N);
    pyreT.GetField("seed",BFi).SetValue(spec,SEED);
    var layer = System.Activator.CreateInstance(pyreLayerT);
    pyreLayerT.GetField("matteEnabled",BFi).SetValue(layer,false);
    pyreLayerT.GetField("shapeForm",BFi).SetValue(layer, System.Enum.Parse(shapeEnum, names[s2]));
    var listT = typeof(System.Collections.Generic.List<>).MakeGenericType(pyreLayerT);
    var list = System.Activator.CreateInstance(listT); listT.GetMethod("Add").Invoke(list,new object[]{layer});
    pyreT.GetField("layers",BFi).SetValue(spec,list);
    var refPx = renderFrame.Invoke(null,new object[]{spec,fi}) as UnityEngine.Color32[];
    var src = System.Activator.CreateInstance(srcT);
    if (framesF != null) framesF.SetValue(src, N);
    var buf = new UnityEngine.Color32[W*H];
    srcT.GetMethod("Render",BFi).Invoke(src,new object[]{W,H,phase,(uint)SEED,buf});
    sb.Append("Simulation\t").Append(names[s2]).Append("\t").Append(fi).Append("\t").Append(diff(refPx,buf)).Append("\n");
    UnityEngine.Object.DestroyImmediate(spec);
  }
}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0334\out\parity-sims.tsv", sb.ToString());
return sb.ToString();

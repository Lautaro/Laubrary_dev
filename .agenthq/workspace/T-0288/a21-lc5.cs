var SB = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer = (n,d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[]{n,d});
System.Func<int,Laubrary.Shaper.ShaperDocument> mk = n => {
  var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
  d.layers.Add(NewLayer("Layer 1", d)); d.lightRig.ambientIntensity=new ZUIValue(0f);
  for (int i=0;i<n;i++) d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name="L"+i, enabled=true, kind=Laubrary.Shaper.ShaperLightKind.Point, posX=new ZUIValue((i%4)*20f-30f), posY=new ZUIValue((i/4)*20f-10f), colour = i>=8?UnityEngine.Color.red:UnityEngine.Color.white, intensity=new ZUIValue(0.08f), range=new ZUIValue(200f) });
  return d; };
System.Func<Laubrary.Shaper.ShaperDocument,int,UnityEngine.Color32[]> R = (d,f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d,f);
System.Func<UnityEngine.Color32[],UnityEngine.Color32[],int> diff = (a,b) => { int n=0; for(int i=0;i<a.Length;i++) if (a[i].r!=b[i].r||a[i].g!=b[i].g||a[i].b!=b[i].b||a[i].a!=b[i].a) n++; return n; };
var d8 = mk(8); var d9 = mk(9); var d10 = mk(10);
var p8 = R(d8,0); var p9 = R(d9,0); var p10 = R(d10,0);
SB.Append("8 vs 9 lights diff = ").Append(diff(p8,p9)).Append('\n');
SB.Append("8 vs 10 lights diff = ").Append(diff(p8,p10)).Append('\n');
var d7 = mk(7); var p7 = R(d7,0);
SB.Append("7 vs 8 lights diff = ").Append(diff(p7,p8)).Append('\n');
UnityEngine.Object.DestroyImmediate(d7);UnityEngine.Object.DestroyImmediate(d8);UnityEngine.Object.DestroyImmediate(d9);UnityEngine.Object.DestroyImmediate(d10);
return SB.ToString();

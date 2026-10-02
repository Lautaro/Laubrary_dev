var SB = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer = (n,d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[]{n,d});
System.Func<Laubrary.Shaper.ShaperDocument> fresh = () => {
  var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
  d.layers.Add(NewLayer("Layer 1", d));
  d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
  return d; };
System.Func<Laubrary.Shaper.ShaperDocument,int,UnityEngine.Color32[]> R = (d,f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d,f);
System.Func<UnityEngine.Color32[],UnityEngine.Color32[],int> diff = (a,b) => { int n=0; for(int i=0;i<a.Length;i++) if (a[i].r!=b[i].r||a[i].g!=b[i].g||a[i].b!=b[i].b||a[i].a!=b[i].a) n++; return n; };
System.Func<UnityEngine.Color32[],int> lit = a => { int n=0; foreach(var c in a) if (c.a>0) n++; return n; };

System.Action<string,System.Action<Laubrary.Shaper.ShaperLight,Laubrary.Shaper.ShaperLightRig>,bool> T = (label, mut, asPoint) => {
  var d = fresh(); var L = d.lightRig.lights[0];
  if (asPoint) L.kind = Laubrary.Shaper.ShaperLightKind.Point;
  var b0 = R(d,0); mut(L, d.lightRig); var a0 = R(d,0);
  SB.Append(label).Append('\t').Append(asPoint?"Point":"Directional").Append('\t').Append(diff(b0,a0)).Append('\t').Append(lit(b0)).Append("->").Append(lit(a0)).Append('\n');
  UnityEngine.Object.DestroyImmediate(d); };

foreach (bool pt in new[]{false,true}) {
  T("enabled off", (l,r)=>l.enabled=false, pt);
  T("colour red", (l,r)=>l.colour=UnityEngine.Color.red, pt);
  T("intensity 0.943->3", (l,r)=>l.intensity=new ZUIValue(3f), pt);
  T("specular 0.9->0", (l,r)=>l.specular=new ZUIValue(0f), pt);
  T("yaw -55->120", (l,r)=>l.yaw=new ZUIValue(120f), pt);
  T("pitch 36->-80", (l,r)=>l.pitch=new ZUIValue(-80f), pt);
  T("posX 0->40", (l,r)=>l.posX=new ZUIValue(40f), pt);
  T("posY 0->40", (l,r)=>l.posY=new ZUIValue(40f), pt);
  T("posZ 40->200", (l,r)=>l.posZ=new ZUIValue(200f), pt);
  T("range 40->400", (l,r)=>l.range=new ZUIValue(400f), pt);
  T("ambientColour red", (l,r)=>r.ambientColour=UnityEngine.Color.red, pt);
  T("ambient x 0.18->1.5", (l,r)=>r.ambientIntensity=new ZUIValue(1.5f), pt);
  T("kind flip", (l,r)=>l.kind = l.kind==Laubrary.Shaper.ShaperLightKind.Point?Laubrary.Shaper.ShaperLightKind.Directional:Laubrary.Shaper.ShaperLightKind.Point, pt);
  T("+2nd light", (l,r)=>r.lights.Add(new Laubrary.Shaper.ShaperLight{ name="L2", kind=Laubrary.Shaper.ShaperLightKind.Point, posX=new ZUIValue(30f), posY=new ZUIValue(20f) }), pt);
  SB.Append("--\n");
}
return SB.ToString();

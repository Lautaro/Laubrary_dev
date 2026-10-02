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
{ var d = fresh(); var resp = d.layers[0].response;
  SB.Append("resp.specular=").Append(Laubrary.Shaper.ShaperValue.Sample(resp.specular,0f,0u,0f)).Append(" power=").Append(Laubrary.Shaper.ShaperValue.Sample(resp.specularPower,0f,0u,0f)).Append(" rim=").Append(Laubrary.Shaper.ShaperValue.Sample(resp.rimStrength,0f,0u,0f)).Append(" scale=").Append(Laubrary.Shaper.ShaperValue.Sample(resp.intensityScale,0f,0u,0f)).Append('\n');
  UnityEngine.Object.DestroyImmediate(d); }
System.Func<float,float,bool,int> t = (yaw,pitch,withHeight) => {
  var d = fresh(); var L = d.lightRig.lights[0]; L.yaw=new ZUIValue(yaw); L.pitch=new ZUIValue(pitch);
  if (withHeight) { d.layers[0].height = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome, depth = new ZUIValue(8f) }; d.layers[0].response.normalKind = Laubrary.Shaper.ShaperNormalKind.Profile; }
  var b = R(d,0); L.specular = new ZUIValue(0f); var a = R(d,0); int n = diff(b,a); UnityEngine.Object.DestroyImmediate(d); return n; };
SB.Append("Directional yaw-55 pitch36 flat  spec0.9->0 diff=").Append(t(-55f,36f,false)).Append('\n');
SB.Append("Directional yaw  0 pitch90 flat  spec0.9->0 diff=").Append(t(0f,90f,false)).Append('\n');
SB.Append("Directional yaw-55 pitch36 DOME  spec0.9->0 diff=").Append(t(-55f,36f,true)).Append('\n');
SB.Append("Directional yaw  0 pitch70 flat  spec0.9->0 diff=").Append(t(0f,70f,false)).Append('\n');
SB.Append("Directional yaw  0 pitch60 flat  spec0.9->0 diff=").Append(t(0f,60f,false)).Append('\n');
return SB.ToString();

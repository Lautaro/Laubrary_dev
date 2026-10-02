// Hash all 16 frames twice: (a) straight from the renderer, (b) through the window's preview stage
// (which goes via the frame cache). A phase whose picture repeats in (b) but not (a) is a cache bug.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null, stageF=null, frameF=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (assetF==null) assetF=t.GetField("asset",BFi); if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); }
var doc = assetF.GetValue(w);
var bakerT = ZType("ShaperBaker");
var render = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<UnityEngine.Color32[], string> hash = px => { unchecked { uint h=2166136261u; foreach (var p in px){h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u;} return h.ToString("X8"); } };
var stage = stageF.GetValue(w);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
int save = (int)frameF.GetValue(w);
var sb = new System.Text.StringBuilder();
sb.Append("frame  renderer   stage\n");
for (int f = 0; f < 16; f++) {
  var px = render.Invoke(null, new object[]{ doc, f }) as UnityEngine.Color32[];
  string hr = hash(px);
  frameF.SetValue(w, f); refresh.Invoke(stage, null);
  var tex = texF.GetValue(stage) as UnityEngine.Texture2D;
  string hs = tex == null ? "notex" : hash(tex.GetPixels32());
  sb.Append(f.ToString("00")).Append("     ").Append(hr).Append("   ").Append(hs).Append(hr==hs?"":"   <-- MISMATCH").Append("\n");
}
frameF.SetValue(w, save); refresh.Invoke(stage, null);
return sb.ToString();

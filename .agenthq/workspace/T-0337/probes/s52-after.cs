// A LATER round trip than s43's SetAsset. Nothing is rebound, nothing is edited: the same four frames are
// asked for again, plus the cache's own ComputeFrame, plus whatever the stage thinks its frame is.
var win = ZWin("ShaperWindow"); if (win == null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null, frameF=null, assetF=null;
for (var t=win.GetType(); t!=null; t=t.BaseType) { if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); if (assetF==null) assetF=t.GetField("asset",BFi); }
var stage = stageF.GetValue(win); var st = stage.GetType();
var texF = st.GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refreshM = st.GetMethod("Refresh");
var cacheF = st.GetField("_frameCache", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var docF   = st.GetField("_doc",  System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var frmF   = st.GetField("_frame",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
System.Func<string> hash = () => { var tex = texF.GetValue(stage) as UnityEngine.Texture2D; if (tex==null) return "notex";
  var px = tex.GetPixels32(); unchecked { uint h=2166136261u; int lit=0; foreach (var p in px){ if(p.a>8) lit++; h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u; } return h.ToString("X8")+"/"+lit; } };
var sb = new System.Text.StringBuilder();
var a = assetF.GetValue(win) as UnityEngine.Object;
sb.Append("bound asset=").Append(a==null?"<null>":a.name).Append(" instanceID=").Append(a==null?0:a.GetInstanceID()).Append('\n');
var frmD = frmF==null?null:frmF.GetValue(stage) as System.Delegate;
var docD = docF==null?null:docF.GetValue(stage) as System.Delegate;
sb.Append("win.currentFrame=").Append(frameF.GetValue(win))
  .Append("  stage._frame() = ").Append(frmD==null?"<none>":frmD.DynamicInvoke().ToString())
  .Append("  stage._doc() = ").Append(docD==null?"<none>":(docD.DynamicInvoke()==null?"<null>":((UnityEngine.Object)docD.DynamicInvoke()).GetInstanceID().ToString())).Append('\n');
sb.Append("the SAME four frames, one round trip after the rebind:\n  ");
foreach (int f in new int[]{0,4,8,12}) { frameF.SetValue(win, f); refreshM.Invoke(stage, null); sb.Append('f').Append(f).Append('=').Append(hash()).Append(' '); }
sb.Append('\n');
var cache = cacheF==null?null:cacheF.GetValue(stage);
object doc = docD==null?null:docD.DynamicInvoke();
if (cache!=null && doc!=null) { var cf = cache.GetType().GetMethod("ComputeFrame");
  sb.Append("cache.ComputeFrame directly: ");
  foreach (int f in new int[]{0,4,8,12}) { var px = cf.Invoke(cache, new object[]{f, doc}) as UnityEngine.Color32[];
    if (px==null) { sb.Append('f').Append(f).Append("=NULL "); continue; }
    unchecked { uint h=2166136261u; int lit=0; foreach (var p in px){ if(p.a>8) lit++; h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u; }
      sb.Append('f').Append(f).Append('=').Append(h.ToString("X8")).Append('/').Append(lit).Append(' '); } }
  sb.Append('\n'); }
return sb.ToString();

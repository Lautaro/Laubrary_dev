// Does PLAYING leave the preview cache holding frames that do not match the renderer?
// Step A (T334.phase=pre): compare renderer vs stage for a few frames, then start a 3s play.
// Step B (T334.phase=post): compare again, WITHOUT invalidating anything.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null, stageF=null, frameF=null, playF=null, tickF=null, accF=null;
System.Reflection.MethodInfo tickM=null;
for (var t=win.GetType(); t!=null; t=t.BaseType){ if(assetF==null) assetF=t.GetField("asset",BFi); if(stageF==null) stageF=t.GetField("stage",BFi);
  if(frameF==null) frameF=t.GetField("currentFrame",BFi); if(playF==null) playF=t.GetField("playing",BFi);
  if(tickF==null) tickF=t.GetField("lastPlayTick",BFi); if(accF==null) accF=t.GetField("playAcc",BFi); if(tickM==null) tickM=t.GetMethod("PlaybackTick",BFi); }
var doc = assetF.GetValue(win);
var bakerT = ZType("ShaperBaker");
var render = bakerT.GetMethod("RenderFrame", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
System.Func<UnityEngine.Color32[],string> H = px => { unchecked { uint h=2166136261u; foreach (var p in px){h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u;} return h.ToString("X8"); } };
var stage = stageF.GetValue(win);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
var sb = new System.Text.StringBuilder();
int bad = 0;
foreach (int f in new int[]{0,2,4,8,12,15}) {
  string hr = H(render.Invoke(null, new object[]{doc,f}) as UnityEngine.Color32[]);
  frameF.SetValue(win, f); refresh.Invoke(stage, null);
  var tex = texF.GetValue(stage) as UnityEngine.Texture2D;
  string hs = tex==null?"notex":H(tex.GetPixels32());
  if (hr!=hs) bad++;
  sb.Append("f").Append(f).Append(" renderer=").Append(hr).Append(" stage=").Append(hs).Append(hr==hs?" ok":"  <-- STALE").Append("\n"); }
sb.Append("mismatches=").Append(bad).Append("\n");
if (UnityEditor.EditorPrefs.GetString("T334.phase","pre") == "pre") {
  var del = (UnityEditor.EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(typeof(UnityEditor.EditorApplication.CallbackFunction), win, tickM);
  playF.SetValue(win, true); if (tickF!=null) tickF.SetValue(win, UnityEditor.EditorApplication.timeSinceStartup); if (accF!=null) accF.SetValue(win, 0f);
  UnityEditor.EditorApplication.update += del;
  double start = UnityEditor.EditorApplication.timeSinceStartup;
  UnityEditor.EditorApplication.CallbackFunction stop = null;
  stop = () => { if (UnityEditor.EditorApplication.timeSinceStartup - start < 3.0) return;
    UnityEditor.EditorApplication.update -= stop; UnityEditor.EditorApplication.update -= del; playF.SetValue(win, false); };
  UnityEditor.EditorApplication.update += stop;
  sb.Append("started a 3s play — rerun with T334.phase=post\n");
}
return sb.ToString();

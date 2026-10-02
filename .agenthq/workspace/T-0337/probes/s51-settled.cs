// A LATER round trip. EditorApplication.update has run many times since s50 parked the stage at frame 12
// with the cache dropped, so the prebaker has had time to fill it. Nothing is edited here: the stage is
// asked for the SAME frame it was parked on, with and without a Refresh.
var win = ZWin("ShaperWindow"); if (win == null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null, frameF=null;
for (var t=win.GetType(); t!=null; t=t.BaseType) { if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); }
var stage = stageF.GetValue(win); var st = stage.GetType();
var texF = st.GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refreshM = st.GetMethod("Refresh");
System.Func<string> hash = () => { var tex = texF.GetValue(stage) as UnityEngine.Texture2D; if (tex==null) return "notex";
  var px = tex.GetPixels32(); unchecked { uint h=2166136261u; int lit=0; foreach (var p in px){ if(p.a>8) lit++; h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u; } return h.ToString("X8")+"/"+lit; } };
var sb = new System.Text.StringBuilder();
sb.Append("frame parked on = ").Append(frameF.GetValue(win)).Append('\n');
sb.Append("hash s50 read in its OWN round trip  = ").Append(UnityEditor.EditorPrefs.GetString("T337.stageHash","<unset>")).Append('\n');
sb.Append("hash now, WITHOUT touching anything  = ").Append(hash()).Append('\n');
refreshM.Invoke(stage, null);
sb.Append("hash now, after one more Refresh     = ").Append(hash()).Append('\n');
sb.Append("\nall 16 frames, each Refreshed and read in THIS round trip:\n  ");
for (int f=0; f<16; f++) { frameF.SetValue(win, f); refreshM.Invoke(stage, null); sb.Append(hash()).Append(f==15?"":" | "); }
return sb.ToString();

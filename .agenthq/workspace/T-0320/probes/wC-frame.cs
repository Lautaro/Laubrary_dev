// Set the preview frame and report the stage hash + a non-transparent pixel count (is anything drawn?).
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null, frameF=null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); }
int f = int.Parse(UnityEditor.EditorPrefs.GetString("T320.frame","0"));
if (frameF != null) frameF.SetValue(win, f);
var stage = stageF.GetValue(win);
stage.GetType().GetMethod("Refresh").Invoke(stage, null);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var tex = texF.GetValue(stage) as UnityEngine.Texture2D;
if (tex == null) return "frame " + f + ": no texture";
var px = tex.GetPixels32(); int lit = 0; unchecked { uint h = 2166136261u;
  foreach (var p in px) { if (p.a > 8) lit++; h = (h ^ p.r) * 16777619u; h = (h ^ p.g) * 16777619u; h = (h ^ p.b) * 16777619u; h = (h ^ p.a) * 16777619u; }
  return "frame " + f + " hash=" + h.ToString("X8") + " lit=" + lit + "/" + px.Length + " tex=" + tex.width + "x" + tex.height; }

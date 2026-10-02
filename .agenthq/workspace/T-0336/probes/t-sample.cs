// PAUSE first, then hash the stage — a frame hashed while the transport is still moving is round 19's
// third probe fault. Reports elapsed since T336.t0, the frame reached, the hash and the lit-pixel count.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
UnityEngine.UIElements.Button tb = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause"))) { tb = b; break; } }
if (tb != null && tb.text.Contains("Pause")) ZPress(w, tb);
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null, frameF=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); }
var stage = stageF.GetValue(w);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
refresh.Invoke(stage, null);
var tex = texF.GetValue(stage) as UnityEngine.Texture2D;
string h = "notex"; int lit = 0;
if (tex != null) { var px = tex.GetPixels32(); unchecked { uint hh=2166136261u; foreach (var p in px){ if (p.a>8) lit++; hh=(hh^p.r)*16777619u;hh=(hh^p.g)*16777619u;hh=(hh^p.b)*16777619u;hh=(hh^p.a)*16777619u; } h = hh.ToString("X8"); } }
sb.Append("t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T336.t0",0)).ToString("F2"))
  .Append(" frame=").Append(frameF.GetValue(w)).Append(" hash=").Append(h).Append(" lit=").Append(lit)
  .Append(" btnNow='").Append(tb==null?"?":tb.text).Append("'");
return sb.ToString();

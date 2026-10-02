var w = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement)) { var l=e as UnityEngine.UIElements.Label; if (l==null||!ZDrawn(l)||string.IsNullOrEmpty(l.text)) continue;
  if (l.text.StartsWith("frame ")) sb.Append(l.text).Append(" @t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T321.t0",0)).ToString("F2")).Append("\n"); }
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
UnityEngine.UIElements.VisualElement stage=null;
foreach (var e in ZAll(w.rootVisualElement)) if (e.GetType().Name=="ShaperPreviewStage") stage=e;
var tex = stage.GetType().GetField("_tex", BFi).GetValue(stage) as Texture2D;
if (tex!=null) { var px = tex.GetPixels32(); int lit=0; uint h=2166136261;
  foreach (var c in px) { if (c.a>8) lit++; unchecked { h=(h^c.r)*16777619; h=(h^c.g)*16777619; h=(h^c.b)*16777619; h=(h^c.a)*16777619; } }
  sb.Append("tex ").Append(tex.width).Append("x").Append(tex.height).Append(" lit=").Append(lit).Append(" hash=").Append(h.ToString("X8")).Append("\n"); }
else sb.Append("no tex\n");
return sb.ToString();

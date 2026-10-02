var sb=new System.Text.StringBuilder();
var w=ZWin("ShaperWindow"); if (w==null) return "no shaper";
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
sb.Append("t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T323.t0",0)).ToString("F2")).Append(" ");
foreach (var e in ZAll(w.rootVisualElement)) { var te=e as UnityEngine.UIElements.TextElement; if (te!=null && ZDrawn(te) && te.text!=null && te.text.StartsWith("frame ")) sb.Append("readout='").Append(te.text).Append("' "); }
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && (b.text.Contains("Play")||b.text.Contains("Pause"))) sb.Append("btn='").Append(b.text).Append("' "); }
UnityEngine.Texture2D tex=null;
foreach (var e in ZAll(w.rootVisualElement)) { var im=e as UnityEngine.UIElements.Image; if (im!=null && ZDrawn(im) && im.worldBound.width>60) { var t2=im.image as UnityEngine.Texture2D; if (t2!=null) tex=t2; } }
if (tex!=null) { int lit=0; uint h=2166136261u;
  try { var px=tex.GetPixels32(); foreach (var p in px) { if (p.a>5) lit++; h=(h^(uint)(p.r*3+p.g*5+p.b*7+p.a*11))*16777619u; } }
  catch { sb.Append("(unreadable) "); }
  sb.Append("tex=").Append(tex.width).Append("x").Append(tex.height).Append(" lit=").Append(lit).Append(" hash=").Append(h.ToString("X8"));
} else sb.Append("no texture");
string rep=ZAudit(w,"shaper-sample");
sb.Append(" | captionShort=").Append(ZCount["captionShort"]).Append(" ovfX=").Append(ZCount["overflowParentX"]);
return sb.ToString();

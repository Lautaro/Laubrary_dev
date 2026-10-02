var w = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo ff=null; for (var t=w.GetType(); t!=null&&ff==null; t=t.BaseType) ff=t.GetField("frame",BFi);
sb.Append("t=").Append((UnityEditor.EditorApplication.timeSinceStartup - UnityEditor.EditorPrefs.GetFloat("T324.t0",0)).ToString("F2"))
  .Append(" frame=").Append(ff!=null?ff.GetValue(w).ToString():"?");
UnityEngine.UIElements.Button play=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text!=null && (b.text.Contains("Play")||b.text.Contains("Pause"))) play=b; }
sb.Append(" btn='").Append(play!=null?play.text:"?").Append("'\n");
// caption/clip audit while PLAYING
string rep = ZAudit(w, "shaper-playing");
sb.Append("captionShort=").Append(ZCount["captionShort"]).Append(" overflowX=").Append(ZCount["overflowParentX"]).Append("\n");
return sb.ToString();

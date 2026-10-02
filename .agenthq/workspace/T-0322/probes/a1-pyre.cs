var sb=new System.Text.StringBuilder();
var t = ZType("PyreWindow");
var w = ZWin("PyreWindow");
if (w==null) { t.GetMethod("Open", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Invoke(null,null); w = ZWin("PyreWindow"); sb.Append("opened\n"); }
w.position = new Rect(20, 40, 900, 900);
w.titleContent = new GUIContent("PyreCapTag");
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var af = w.GetType().GetField("asset", BF2); var a = af.GetValue(w);
sb.Append("asset=").Append(a==null?"null":((UnityEngine.Object)a).name).Append("\n");
if (a!=null) { a.GetType().GetField("previewZoom").SetValue(a, 6f);
  var ff = w.GetType().GetField("frame", BF2); if (ff!=null) ff.SetValue(w, 4);
  w.GetType().GetMethod("Rebuild", BF2).Invoke(w, null); }
UnityEditor.EditorPrefs.SetString("T320.capWin","PyreWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0322/shots/a1-pyre-zoom6.png");
w.Focus(); w.Repaint();
sb.Append("pos=").Append(w.position);
return sb.ToString();

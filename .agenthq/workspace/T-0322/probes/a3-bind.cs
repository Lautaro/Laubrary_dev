var sb=new System.Text.StringBuilder();
sb.Append(ZBind("PyreWindow","Assets/Shaper/AuditT322Pyre.asset")).Append("\n");
var w = ZWin("PyreWindow");
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var a = w.GetType().GetField("asset", BF2).GetValue(w);
sb.Append("asset=").Append(a==null?"null":((UnityEngine.Object)a).name).Append("\n");
if (a!=null) { a.GetType().GetField("previewZoom").SetValue(a, 6f);
  var ff = w.GetType().GetField("frame", BF2); if (ff!=null) ff.SetValue(w, 4);
  w.GetType().GetMethod("Rebuild", BF2).Invoke(w, null); }
w.position = new Rect(20, 40, 900, 880);
w.titleContent = new GUIContent("PyreCapTag");
UnityEditor.EditorPrefs.SetString("T320.capWin","PyreWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0322/shots/a1-pyre-zoom6.png");
w.Focus(); w.Repaint();
sb.Append("pos=").Append(w.position);
return sb.ToString();

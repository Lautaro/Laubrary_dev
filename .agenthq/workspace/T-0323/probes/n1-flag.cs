var sb=new System.Text.StringBuilder();
var w=ZWin("MirageWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var f=w.GetType().GetField("_rebuildQueued", BFi);
sb.Append("_rebuildQueued=").Append(f.GetValue(w)).Append("\n");
w.Focus(); w.Repaint();
UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
return sb.ToString();

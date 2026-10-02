var w = ZWin("ChunkWindow"); var sb=new System.Text.StringBuilder();
w.position = new Rect(20,20,900,880);
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
int n=0; foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) { isOpen.SetValue(e,true); n++; }
var boxT = ZType("ZuiBox"); var openP = boxT.GetProperty("IsOpen") ?? boxT.GetProperty("Open");
int nb=0; if (openP!=null && openP.CanWrite) foreach (var e in ZAll(w.rootVisualElement)) if (boxT.IsInstanceOfType(e)) { openP.SetValue(e,true); nb++; }
sb.Append("sections=").Append(n).Append(" boxes=").Append(nb).Append("\n");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var a = af.GetValue(w) as UnityEngine.Object;
sb.Append("asset=").Append(a!=null?UnityEditor.AssetDatabase.GetAssetPath(a):"NULL").Append("\n");
w.Repaint();
return sb.ToString();

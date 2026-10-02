var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo aF=null; for (var t=win.GetType(); t!=null && aF==null; t=t.BaseType) aF=t.GetField("asset",BFi);
var a = aF.GetValue(win) as UnityEngine.Object;
sb.Append("bound=").Append(a!=null?UnityEditor.AssetDatabase.GetAssetPath(a):"NULL").Append("\n");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=0;Canvas=0;Layers=0;Shape=1;Fill=1;Swarm=0;SpriteFX=0;Lights=0;Tags=0");
win.GetType().GetMethod("Rebuild", BFi).Invoke(win,null);
win.position = new Rect(20,20,900,900);
return sb.ToString();

var win = ZWin("ChunkWindow"); if (win==null) return "no chunks";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo setAsset = null;
for (var t = win.GetType(); t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
var a = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Demos/ChunksDemo/WallDebris.asset");
setAsset.Invoke(win, new object[]{ a });
// open every section
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
int n = 0;
foreach (var e in ZAll(win.rootVisualElement)) if (secT.IsInstanceOfType(e)) { isOpen.SetValue(e, true); n++; }
win.Repaint();
return "bound=" + (a!=null?a.name:"null") + " sections=" + n;

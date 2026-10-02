var win = ZWin("LauminaryBrowserWindow"); if (win == null) return "NO BROWSER";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var selF = win.GetType().GetField("_selected", BFi);
var lauT = ZType("Lauminary");
UnityEngine.Object pick = null;
if (UnityEditor.EditorPrefs.GetString("T318.tsState","A") == "B")
    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:" + lauT.Name))
    { pick = UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(g), lauT); if (pick != null) break; }
selF.SetValue(win, pick);
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType) rb = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly);
if (rb != null) rb.Invoke(win, null);
win.Repaint();
return "state=" + UnityEditor.EditorPrefs.GetString("T318.tsState","A") + " selected=" + (pick==null?"<null>":pick.name);

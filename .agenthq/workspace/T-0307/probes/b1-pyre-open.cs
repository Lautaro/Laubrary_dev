// Opens Pyre from its menu item, binds it to Assets/Pyre/New Pyre Plus.asset, sizes it 1700x900.
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var pyreT = FT("PyreWindow");
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Pyre");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
if (win == null) return "NO PYRE WINDOW";
win.position = new UnityEngine.Rect(40, 40, 1700, 900);
win.Show(); win.Repaint();

var spec = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Pyre/New Pyre Plus.asset");
System.Reflection.MethodInfo bind = null;
for (var t = pyreT; t != null && bind == null; t = t.BaseType)
    foreach (var m in t.GetMethods(BFi | System.Reflection.BindingFlags.DeclaredOnly))
        if (m.Name == "Bind" && m.GetParameters().Length == 1) { bind = m; break; }
string bound = "<no Bind(1)>";
if (bind != null) { bind.Invoke(win, new object[] { spec }); bound = "bound"; }
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var cur = curP == null ? null : curP.GetValue(win) as UnityEngine.Object;
win.Repaint();
return "pyreOpen pos=" + win.position + " " + bound + " current=" + (cur == null ? "<null>" : cur.name);

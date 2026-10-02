System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
System.Reflection.MethodInfo rebuild = null;
for (var t = pyreT; t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();
return "rebuilt at " + win.position.width + "x" + win.position.height;

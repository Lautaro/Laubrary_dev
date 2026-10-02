// Sets the Pyre dial pane width to EditorPrefs "T0304.w", rebuilds, clears the console.
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
if (win == null) return "NO PYRE WINDOW";
float w = float.Parse(UnityEditor.EditorPrefs.GetString("T0304.w", "560"));
pyreT.GetField("leftPaneWidth", BFi).SetValue(win, w);
System.Reflection.MethodInfo rebuild = null;
for (var t = pyreT; t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();
var lp = pyreT.GetField("leftPane", BFi).GetValue(win) as UnityEngine.UIElements.VisualElement;
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return "pane=" + w + " resolved=" + (lp == null ? -1f : lp.resolvedStyle.width);

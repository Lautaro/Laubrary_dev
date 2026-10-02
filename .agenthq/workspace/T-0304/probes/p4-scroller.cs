// arg-free: flips the left pane ScrollView's vertical scroller visibility to AlwaysVisible, clears console.
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
var lp = pyreT.GetField("leftPane", BFi).GetValue(win) as UnityEngine.UIElements.ScrollView;
lp.verticalScrollerVisibility = UnityEngine.UIElements.ScrollerVisibility.AlwaysVisible;
win.Repaint();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return "scroller AlwaysVisible, console cleared";

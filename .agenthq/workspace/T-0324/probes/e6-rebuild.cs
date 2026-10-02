// Drive the window's deferred body build directly (RebuildBody goes through delayCall, which does not fire here).
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "MirageWindow");
var w = ZWin(wn); if (w == null) return "no " + wn;
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy|System.Reflection.BindingFlags.DeclaredOnly;
System.Reflection.MethodInfo rb = null;
for (var t = w.GetType(); t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
if (rb != null) rb.Invoke(w, null);
w.Repaint();
return wn + " rebuilt, elements=" + ZAll(w.rootVisualElement).Count;

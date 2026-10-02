// Open a window cold: close every instance of the type first, open a fresh one, leave it UNBOUND (its
// library / empty state), and place it where the by-eye channel can read it.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "LatheWindow");
float ww = UnityEditor.EditorPrefs.GetFloat("T324.w", 900f);
float wh = UnityEditor.EditorPrefs.GetFloat("T324.h", 880f);
int closed = 0;
foreach (var w0 in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w0.GetType().Name == wn) { w0.Close(); closed++; }
var win = ZOpen(wn);
if (win == null) return "could not open " + wn;
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy|System.Reflection.BindingFlags.DeclaredOnly;
System.Reflection.MethodInfo sa = null, rb = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (sa == null) sa = t.GetMethod("SetAsset", BFi); if (rb == null) rb = t.GetMethod("Rebuild", BFi); }
if (sa != null) sa.Invoke(win, new object[]{ null });
if (rb != null) rb.Invoke(win, null);
win.position = new Rect(40, 20, ww, wh);
win.Repaint();
UnityEditor.EditorPrefs.SetString("T320.capWin", wn);
return "closed " + closed + ", opened " + wn + " unbound at " + win.position;

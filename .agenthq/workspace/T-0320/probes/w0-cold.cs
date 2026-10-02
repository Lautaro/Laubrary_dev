// Cold start: close every Shaper window, then open it from its own menu item. Nothing is bound.
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w.GetType().Name == "ShaperWindow") w.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = ZWin("ShaperWindow"); if (win == null) return "MENU DID NOT OPEN A WINDOW";
win.position = new UnityEngine.Rect(0, 20, 1400, 900);
win.titleContent = new GUIContent("T320Tag");
win.Show(); win.Focus(); win.Repaint();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo assetP = null; System.Reflection.FieldInfo assetF = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (assetF == null) assetF = t.GetField("asset", BFi); }
return "opened " + win.position + " bound=" + (assetF != null ? (assetF.GetValue(win)?.ToString() ?? "null") : "?");

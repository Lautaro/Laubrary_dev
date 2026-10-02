var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo f = null;
for (var t = win.GetType(); t != null && f == null; t = t.BaseType) f = t.GetField("playing", BFi|System.Reflection.BindingFlags.DeclaredOnly);
f.SetValue(win, UnityEditor.EditorPrefs.GetString("T320.play","0") == "1");
win.Repaint();
return "playing=" + f.GetValue(win);

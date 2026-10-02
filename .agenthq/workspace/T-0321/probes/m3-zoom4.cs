var win = ZWin("PyreWindow");
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var af = win.GetType().GetField("asset", BF2); var a = af.GetValue(win);
a.GetType().GetField("previewZoom").SetValue(a, 6f);
var ff = win.GetType().GetField("frame", BF2); ff.SetValue(win, 4);
win.GetType().GetMethod("Rebuild", BF2).Invoke(win, null);
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre8.png");
win.Repaint();
return "zoom6 frame4";

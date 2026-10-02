UnityEditor.EditorPrefs.SetString("ShaperCap.name", UnityEditor.EditorPrefs.GetString("A25.shot","x"));
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var f = win.GetType().GetProperty("docked", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
return "name set; docked=" + (f == null ? "?" : f.GetValue(win).ToString()) + " title=" + win.titleContent.text;

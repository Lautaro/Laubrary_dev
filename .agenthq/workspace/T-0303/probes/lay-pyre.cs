System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
float ww = UnityEditor.EditorPrefs.GetFloat("A25.pw", 820f), hh = UnityEditor.EditorPrefs.GetFloat("A25.ph", 520f);
win.position = new UnityEngine.Rect(40, 40, ww, hh);
win.Focus(); win.Repaint();
return "pyre set to " + win.position.width + "x" + win.position.height;

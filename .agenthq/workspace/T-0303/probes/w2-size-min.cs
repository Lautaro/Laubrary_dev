var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
win.position = new UnityEngine.Rect(40, 40, 820, 520);
win.Repaint();
return sb.Append("asked 820x520 -> ").Append(win.position.width.ToString("F1")).Append("x").Append(win.position.height.ToString("F1")).Append("\n").ToString();

System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
var sb = new System.Text.StringBuilder();
for (int i = 0; i < all.Count; i++) { var r = all[i].worldBound; sb.Append(i).Append('=').Append(r.x.ToString("F1")).Append(',').Append(r.y.ToString("F1")).Append(',').Append(r.width.ToString("F1")).Append(',').Append(r.height.ToString("F1")).Append(';'); }
UnityEditor.SessionState.SetString("A25.snap", sb.ToString());
return "snapshot " + all.Count + " elements";

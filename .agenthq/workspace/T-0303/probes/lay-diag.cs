var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
if (win == null) { pyreT.GetMethod("Open", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
  foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0; }
var lpwF = pyreT.GetField("leftPaneWidth", BFi);
var lpF = pyreT.GetField("leftPane", BFi);
var lp = lpF.GetValue(win) as UnityEngine.UIElements.VisualElement;
sb.Append("window=").Append(win.position.width.ToString("F1")).Append("x").Append(win.position.height.ToString("F1")).Append("\n");
sb.Append("leftPaneWidth intent=").Append(lpwF.GetValue(win)).Append("\n");
float cap = UnityEngine.Mathf.Min(4f*360f+3f*6f, UnityEngine.Mathf.Max(360f, win.position.width - 260f));
float want = UnityEngine.Mathf.Clamp((float)lpwF.GetValue(win), 360f, cap);
sb.Append("cap=").Append(cap).Append(" want=").Append(want).Append(" resolved=").Append(lp == null ? -1f : lp.resolvedStyle.width).Append("\n");
sb.Append("style.width=").Append(lp == null ? "?" : lp.style.width.ToString()).Append("\n");
// what is the flow's width and column count inside?
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
foreach (var v in all) if (v.GetType().Name == "ZuiColumnFlow")
{ var row = v.childCount > 0 ? v[0] : null; sb.Append("flow width=").Append(v.resolvedStyle.width.ToString("F1")).Append(" columns=").Append(row == null ? -1 : row.childCount).Append("\n"); break; }
return sb.ToString();

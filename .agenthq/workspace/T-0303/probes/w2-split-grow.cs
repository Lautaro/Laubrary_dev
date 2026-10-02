var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;

// 1) try to resize BELOW the declared minimum - minSize must stop it
win.position = new UnityEngine.Rect(40, 40, 200, 120);
sb.Append("asked for 200x120 -> actual ").Append(win.position.width.ToString("F1")).Append("x").Append(win.position.height.ToString("F1"))
  .Append("  (minSize ").Append(win.minSize.x).Append("x").Append(win.minSize.y).Append(")\n");
var lpwF = pyreT.GetField("leftPaneWidth", BFi);
var leftPaneF = pyreT.GetField("leftPane", BFi);
var left = leftPaneF.GetValue(win) as UnityEngine.UIElements.VisualElement;
sb.Append("  at the floor: leftPaneWidth intent=").Append(lpwF.GetValue(win)).Append(" resolved=").Append(left == null ? -1f : left.resolvedStyle.width).Append("\n");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<string> Off = () => {
    var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
    int off = 0, tot = 0;
    foreach (var v in all) { if (!(v is UnityEngine.UIElements.Button b)) continue; tot++; if (v.worldBound.xMin >= win.position.width) off++; }
    return off + " of " + tot;
};
sb.Append("  buttons off the right edge at the floor = ").Append(Off()).Append("\n");

// 2) widen with NO rebuild - the pane must regrow toward the persisted intent
win.position = new UnityEngine.Rect(40, 40, 1900, 1100);
sb.Append("widened to 1900x1100 (no Rebuild): intent=").Append(lpwF.GetValue(win)).Append(" resolved=").Append(left == null ? -1f : left.resolvedStyle.width).Append("\n");
sb.Append("  buttons off the right edge = ").Append(Off()).Append("\n");
return sb.ToString();

System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var shT = FT("ShaperWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == shT) win = w0;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
var sb = new System.Text.StringBuilder();
foreach (var v in all)
{
    if (v.GetType().Name != "ZuiSection") continue;
    var kids = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(v, kids);
    string txt = "";
    foreach (var f in kids) { var lb = f as UnityEngine.UIElements.Label; if (lb != null && !string.IsNullOrEmpty(lb.text)) { txt = lb.text; break; } }
    sb.Append("SECTION '").Append(txt).Append("' n=").Append(kids.Count).Append(" display=").Append(v.resolvedStyle.display).Append(" h=").Append(v.resolvedStyle.height.ToString("F1")).Append("\n");
}
int imgs = 0;
foreach (var v in all) { var im = v as UnityEngine.UIElements.Image; if (im != null) { imgs++; sb.Append("IMG h=").Append(im.resolvedStyle.height.ToString("F2")).Append(" styleH=").Append(im.style.height.ToString()).Append(" w=").Append(im.resolvedStyle.width.ToString("F2")).Append("\n"); } }
sb.Append("images=").Append(imgs).Append(" elements=").Append(all.Count).Append("\n");
return sb.ToString();

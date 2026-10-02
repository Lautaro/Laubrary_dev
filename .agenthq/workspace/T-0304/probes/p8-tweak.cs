// Applies EditorPrefs "T0304.tweak" to every Image inside a ZuiGradientControl's output row in the Pyre window.
//   basis0  -> flexBasis 0
//   minw0   -> minWidth 0
//   nomeasure -> width 100% via flexBasis 0 + flexShrink 1
//   scalefit -> scaleMode ScaleToFit
//   none    -> clear inline flexBasis/minWidth back
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
string tweak = UnityEditor.EditorPrefs.GetString("T0304.tweak", "none");
int n = 0;
var sb = new System.Text.StringBuilder();
foreach (var v in all)
{
    var img = v as UnityEngine.UIElements.Image;
    if (img == null) continue;
    n++;
    if (tweak == "basis0") img.style.flexBasis = 0f;
    else if (tweak == "minw0") img.style.minWidth = 0f;
    else if (tweak == "both") { img.style.flexBasis = 0f; img.style.minWidth = 0f; }
    else if (tweak == "scalefit") img.scaleMode = UnityEngine.ScaleMode.ScaleToFit;
    else if (tweak == "noimage") img.image = null;
    else if (tweak == "h24") img.style.height = 24f;
    else if (tweak == "h16") img.style.height = 16f;
    else if (tweak == "hclear") img.style.height = UnityEngine.UIElements.StyleKeyword.Null;
    else if (tweak == "fixedw") img.style.width = 300f;
    else if (tweak == "clearw") img.style.width = UnityEngine.UIElements.StyleKeyword.Null;
    else if (tweak == "nogrow") { img.style.flexGrow = 0f; img.style.flexShrink = 0f; }
    else if (tweak == "none") { img.style.flexBasis = UnityEngine.UIElements.StyleKeyword.Null; img.style.minWidth = UnityEngine.UIElements.StyleKeyword.Null; }
    if (n <= 6) sb.Append("img w=").Append(img.resolvedStyle.width.ToString("F2")).Append(" h=").Append(img.resolvedStyle.height.ToString("F2"))
        .Append(" grow=").Append(img.resolvedStyle.flexGrow).Append(" shrink=").Append(img.resolvedStyle.flexShrink)
        .Append(" basis=").Append(img.resolvedStyle.flexBasis.ToString()).Append(" minW=").Append(img.resolvedStyle.minWidth.ToString())
        .Append(" tex=").Append(img.image == null ? "null" : (img.image.width + "x" + img.image.height))
        .Append(" scale=").Append(img.scaleMode).Append("\n");
}
sb.Append("images=").Append(n).Append(" tweak=").Append(tweak).Append("\n");
win.Repaint();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();

// Max stamp residual (|inline height - what the children need|) over every laid-out .zui-field in the
// window named by EditorPrefs "T0304.win". Fields that are not laid out (need == 0) are reported separately.
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
string wname = UnityEditor.EditorPrefs.GetString("T0304.win", "PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType().Name == wname) win = w0;
if (win == null) return "NO WINDOW " + wname;
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
float worst = 0f; string worstWho = ""; int laid = 0, unlaid = 0, unstamped = 0;
foreach (var v in all)
{
    if (!v.ClassListContains("zui-field")) continue;
    if (v.hierarchy.childCount < 2) continue;
    var lb = v.hierarchy[0] as UnityEngine.UIElements.Label;
    if (lb == null) continue;
    var ctrl = v.hierarchy[1];
    float ch = ctrl.resolvedStyle.height, lh = lb.resolvedStyle.height;
    if (float.IsNaN(ch) || float.IsNaN(lh)) continue;
    float need = UnityEngine.Mathf.Max(ch + ctrl.resolvedStyle.marginTop + ctrl.resolvedStyle.marginBottom,
                                       lh + lb.resolvedStyle.marginTop + lb.resolvedStyle.marginBottom)
               + v.resolvedStyle.paddingTop + v.resolvedStyle.paddingBottom;
    if (need <= 0f) { unlaid++; continue; }
    laid++;
    var cur = v.style.height;
    if (cur.keyword != UnityEngine.UIElements.StyleKeyword.Undefined) { unstamped++; continue; }
    float d = UnityEngine.Mathf.Abs(cur.value.value - need);
    if (d > worst) { worst = d; worstWho = lb.text + " (" + ctrl.GetType().Name + ")"; }
}
return wname + ": laid-out fields=" + laid + " (not laid out=" + unlaid + ", never stamped=" + unstamped
     + ")  worst stamp residual=" + worst.ToString("F3") + "px on '" + worstWho + "'";

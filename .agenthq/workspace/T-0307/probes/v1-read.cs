// Reads the Shaper Views bar exactly as it is drawn: every control, its text and tooltip, the picker's
// choices and value, and the current fold state of every ZuiBox in the window (the thing a view captures).
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);

UnityEngine.UIElements.VisualElement bar = null;
foreach (var v in all) if (v.GetType().Name == "ZuiViewBar") bar = v;
sb.Append("viewBar found=").Append(bar != null).Append("\n");
if (bar == null) return sb.ToString();

var barAll = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(bar, barAll);
foreach (var v in barAll)
{
    if (v is UnityEngine.UIElements.Button b)
        sb.Append("BTN  '").Append(b.text).Append("'  enabled=").Append(b.enabledInHierarchy)
          .Append("  tip=").Append(b.tooltip == null ? "" : (b.tooltip.Length > 70 ? b.tooltip.Substring(0, 70) + "…" : b.tooltip)).Append("\n");
    else if (v is UnityEngine.UIElements.DropdownField df)
    {
        sb.Append("PICK value='").Append(df.value).Append("' choices=[");
        foreach (var c in df.choices) sb.Append(c).Append(";");
        sb.Append("]\n");
    }
    else if (v is UnityEngine.UIElements.TextField tf)
        sb.Append("TEXT value='").Append(tf.value).Append("' tip=").Append(tf.tooltip == null ? "" : (tf.tooltip.Length > 60 ? tf.tooltip.Substring(0, 60) + "…" : tf.tooltip)).Append("\n");
    else if (v is UnityEngine.UIElements.Label lb && !string.IsNullOrEmpty(lb.text))
        sb.Append("LBL  '").Append(lb.text).Append("'\n");
}

// what a view captures: every ZuiBox's fold state, in both panes
int boxes = 0, open = 0;
var folds = new System.Text.StringBuilder();
foreach (var v in all)
{
    if (v.GetType().Name != "ZuiBox") continue;
    boxes++;
    var p = v.GetType().GetProperty("IsOpen", BFi) ?? v.GetType().GetProperty("Expanded", BFi);
    bool ex = p != null && (bool)p.GetValue(v);
    if (ex) open++;
    folds.Append(ex ? "1" : "0");
}
sb.Append("ZuiBoxes=").Append(boxes).Append(" open=").Append(open).Append(" pattern=").Append(folds.ToString()).Append("\n");

var store = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/ShaperViews.asset");
sb.Append("views asset on disk=").Append(store != null).Append("\n");
sb.Append("PREF Shaper.lastView=").Append(UnityEditor.EditorPrefs.GetString("Shaper.lastView", "<none>")).Append("\n");
return sb.ToString();

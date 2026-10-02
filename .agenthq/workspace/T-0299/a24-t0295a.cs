var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);

System.Func<UnityEngine.UIElements.VisualElement, UnityEngine.UIElements.TwoPaneSplitView> FindSplit = null;
FindSplit = e => { if (e is UnityEngine.UIElements.TwoPaneSplitView tp) return tp;
    for (int i = 0; i < e.hierarchy.childCount; i++) { var r = FindSplit(e.hierarchy[i]); if (r != null) return r; } return null; };
var split = FindSplit(win.rootVisualElement);
var leftPane = split.ElementAt(0); var rightPane = split.ElementAt(1);
var leftSet = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(leftPane, leftSet);
var rightSet = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(rightPane, rightSet);

var boxT = System.Type.GetType("ZuiBox, ZUI.Editor");
if (boxT == null) foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == "ZuiBox") boxT = t;
sb.Append("ZuiBox type=").Append(boxT == null ? "NULL" : boxT.AssemblyQualifiedName).Append("\n");
var openP = boxT.GetProperty("IsOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
var titleF = boxT.GetField("_title", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var keyF = boxT.GetField("_key", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
sb.Append("openP=").Append(openP != null).Append(" titleF=").Append(titleF != null).Append(" keyF=").Append(keyF != null).Append("\n");

System.Action<string, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> dump = (label, set) =>
{
    int n = 0;
    foreach (var v in set) if (boxT.IsInstanceOfType(v))
    { n++; sb.Append(label).Append(" BOX '").Append(titleF == null ? "?" : titleF.GetValue(v)).Append("' key='").Append(keyF == null ? "?" : keyF.GetValue(v)).Append("' open=").Append(openP.GetValue(v)).Append("\n"); }
    sb.Append(label).Append(" boxCount=").Append(n).Append("\n");
};
dump("LEFT", leftSet);
dump("RIGHT", rightSet);
return sb.ToString();

var win = ZWin("ShaperWindow");
var sb=new System.Text.StringBuilder();
sb.Append("pos=").Append(win.position).Append("\n");
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  if (b.text=="Apply"||b.text=="Update"||b.text=="Delete view"||b.text=="Save as"||b.text=="Rename") {
    sb.Append("press '").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append(" -> ").Append(ZClick(b)).Append("\n"); } }
sb.Append("viewsAssetAfter=").Append(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/ShaperViews.asset")!=null).Append("\n");
sb.Append("lastView='").Append(UnityEditor.EditorPrefs.GetString("Shaper.lastView","<none>")).Append("'\n");
return sb.ToString();

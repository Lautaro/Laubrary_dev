var sb=new System.Text.StringBuilder();
var win = ZWin("ShaperWindow");
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  if (b.text=="Apply"||b.text=="Update"||b.text=="Delete view"||b.text=="Rename view"||b.text=="Save as")
    sb.Append("'").Append(b.text).Append("' enabled=").Append(b.enabledInHierarchy).Append(" tip='").Append(ZTip(b)).Append("'\n"); }
foreach (var e in ZAll(win.rootVisualElement)) if (e is UnityEngine.UIElements.DropdownField df && ZDrawn(df)) sb.Append("choices=[").Append(string.Join(",", df.choices)).Append("] val='").Append(df.value).Append("'\n");
UnityEditor.EditorPrefs.SetString("T320.capWin","ShaperWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0322/shots/d3-views-live.png");
win.Focus(); win.Repaint();
return sb.ToString();

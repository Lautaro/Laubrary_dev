var sb=new System.Text.StringBuilder();
var win = ZWin("ShaperWindow");
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  if (b.text=="Apply"||b.text=="Update"||b.text=="Delete view"||b.text=="Rename view"||b.text=="Save as")
    sb.Append("'").Append(b.text).Append("' enabled=").Append(b.enabledInHierarchy).Append(" tip='").Append(ZTip(b)).Append("'\n"); }
foreach (var e in ZAll(win.rootVisualElement)) { if (e is UnityEngine.UIElements.DropdownField df && ZDrawn(df)) sb.Append("dropdown choices=").Append(df.choices.Count).Append(" val='").Append(df.value).Append("' tip='").Append(ZTip(df)).Append("'\n");
  if (e is UnityEngine.UIElements.TextField tf && ZDrawn(tf)) sb.Append("textfield lbl='").Append(tf.label).Append("' val='").Append(tf.value).Append("' tip='").Append(ZTip(tf)).Append("'\n"); }
UnityEditor.EditorPrefs.SetString("T320.capWin","ShaperWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0322/shots/d1-views-empty.png");
win.Focus(); win.Repaint();
return sb.ToString();

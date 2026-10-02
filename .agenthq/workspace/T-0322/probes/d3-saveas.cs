var sb=new System.Text.StringBuilder();
var win = ZWin("ShaperWindow");
UnityEngine.UIElements.TextField nameF=null; UnityEngine.UIElements.Button saveAs=null;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (e is UnityEngine.UIElements.TextField tf && ZDrawn(tf) && nameF==null && string.IsNullOrEmpty(tf.label)) nameF=tf;
  var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Save as") saveAs=b; }
if (nameF==null||saveAs==null) return "nameF="+(nameF!=null)+" saveAs="+(saveAs!=null);
nameF.value = "AuditT322";
sb.Append("saveAsEnabledAfterTyping=").Append(saveAs.enabledInHierarchy).Append("\n");
ZClick(saveAs);
win.Repaint();
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  if (b.text=="Apply"||b.text=="Update"||b.text=="Delete view"||b.text=="Rename view"||b.text=="Save as")
    sb.Append("'").Append(b.text).Append("' enabled=").Append(b.enabledInHierarchy).Append(" tip='").Append(ZTip(b)).Append("'\n"); }
foreach (var e in ZAll(win.rootVisualElement)) if (e is UnityEngine.UIElements.DropdownField df && ZDrawn(df)) sb.Append("choices=[").Append(string.Join(",", df.choices)).Append("] val='").Append(df.value).Append("'\n");
sb.Append("store=").Append(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/ShaperViews.asset")!=null).Append("\n");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0322/shots/d3-views-live.png");
return sb.ToString();

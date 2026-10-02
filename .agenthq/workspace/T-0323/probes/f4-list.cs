var sb=new System.Text.StringBuilder();
string wn=UnityEditor.EditorPrefs.GetString("T323.auditWin","LatheWindow");
var w=ZWin(wn);
foreach (var e in ZAll(w.rootVisualElement)) {
  var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  if (ZCls(b).Contains("zui-radio__")||ZCls(b).Contains("zui-segmented__")) continue;
  sb.Append("[").Append(b.enabledInHierarchy?"LIVE":"grey").Append("] '").Append(b.text).Append("' y=").Append(b.worldBound.y.ToString("F0")).Append(" cls=").Append(ZCls(b)).Append("\n");
}
return sb.ToString();

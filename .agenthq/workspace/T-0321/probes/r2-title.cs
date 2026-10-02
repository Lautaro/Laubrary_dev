var sb=new System.Text.StringBuilder();
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
var w = ZWin("LauminationBuilderWindow");
if (w!=null) foreach (var e in ZAll(w.rootVisualElement)) { var l=e as UnityEngine.UIElements.Label; if (l!=null && l.text!=null && l.text.StartsWith("3 · Canvas")) sb.Append("TITLE '").Append(l.text).Append("' tip='").Append(l.tooltip).Append("'\n"); }
return sb.ToString();

var sb=new System.Text.StringBuilder();
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
var pw = ZWin("PyreWindow");
sb.Append("pyre=").Append(pw!=null?pw.position.ToString():"null").Append("\n");
if (pw!=null) {
  var BF2=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
  var pv = pw.GetType().GetField("preview", BF2).GetValue(pw) as UnityEngine.UIElements.VisualElement;
  sb.Append("previewOverflowClipped=").Append(pv!=null? (pv.GetType().GetProperty("worldClip",BF2|System.Reflection.BindingFlags.FlattenHierarchy).GetValue(pv)).ToString() : "null")
    .Append(" wb=").Append(pv!=null?pv.worldBound.ToString():"").Append("\n");
}
var sw = ZWin("ShaperWindow");
foreach (var e in ZAll(sw.rootVisualElement)) { if (ZPath(e).IndexOf("ViewBar")<0) continue;
  var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b)) sb.Append("btn '").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append(" tip='").Append(b.tooltip).Append("'\n"); }
return sb.ToString();

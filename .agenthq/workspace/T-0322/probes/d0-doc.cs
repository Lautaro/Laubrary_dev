var sb=new System.Text.StringBuilder();
const string path="Assets/Shaper/AuditT322Doc.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)==null) {
  var docT = ZType("ShaperDocument");
  var doc = UnityEngine.ScriptableObject.CreateInstance(docT);
  UnityEditor.AssetDatabase.CreateAsset(doc, path);
  UnityEditor.AssetDatabase.SaveAssets();
  sb.Append("created\n");
}
sb.Append(ZBind("ShaperWindow", path)).Append("\n");
var win = ZWin("ShaperWindow");
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  sb.Append("[").Append(b.text).Append("] "); }
return sb.ToString();

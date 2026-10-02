var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
var st = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/ShaperViews.asset");
sb.Append("storeAsset=").Append(st!=null?st.name:"NULL").Append("\n");
foreach (var e in ZAll(win.rootVisualElement)) {
  if (ZPath(e).IndexOf("ViewBar")<0) continue;
  if (e is UnityEngine.UIElements.DropdownField d) sb.Append("picker value='").Append(d.value).Append("' choices=[").Append(string.Join(",", d.choices)).Append("]\n");
  var b=e as UnityEngine.UIElements.Button; if (b!=null) sb.Append("btn '").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append(" tip='").Append(b.tooltip.Length>60?b.tooltip.Substring(0,60):b.tooltip).Append("'\n");
  if (e is UnityEngine.UIElements.TextField t2) sb.Append("name='").Append(t2.value).Append("'\n");
}
sb.Append("lastView='").Append(UnityEditor.EditorPrefs.GetString("Shaper.lastView","<none>")).Append("'\n");
return sb.ToString();

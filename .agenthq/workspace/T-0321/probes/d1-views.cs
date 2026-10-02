var w = ZWin("ShaperWindow");
var all = ZAll(w.rootVisualElement);
var sb = new System.Text.StringBuilder();
foreach (var e in all) {
  var p = ZPath(e);
  if (p.IndexOf("ViewBar") < 0 && e.GetType().Name.IndexOf("ViewBar") < 0) continue;
  if (!ZDrawn(e)) continue;
  if (!(e is UnityEngine.UIElements.Button || e is UnityEngine.UIElements.TextField || e is UnityEngine.UIElements.DropdownField || e is UnityEngine.UIElements.Label)) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("'")
    .Append(" en=").Append(e.enabledInHierarchy)
    .Append(" tip='").Append(ZTip(e)).Append("'")
    .Append(" wb=").Append(e.worldBound).Append("\n");
}
var vb = all.Find(x => x.GetType().Name == "ZuiViewBar");
sb.Append("viewbar=").Append(vb==null?"NULL":vb.worldBound.ToString()).Append("\n");
return sb.ToString();

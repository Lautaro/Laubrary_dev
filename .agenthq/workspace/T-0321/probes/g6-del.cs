var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
for (int k=0;k<3;k++){
  UnityEngine.UIElements.Button del=null; UnityEngine.UIElements.DropdownField pick=null;
  foreach (var e in ZAll(win.rootVisualElement)) { if (ZPath(e).IndexOf("ViewBar")<0) continue;
    if (e is UnityEngine.UIElements.DropdownField df) pick=df;
    var b=e as UnityEngine.UIElements.Button; if (b!=null && b.text=="Delete view") del=b; }
  if (del==null) { sb.Append("no del btn at k=").Append(k).Append("\n"); break; }
  sb.Append("k=").Append(k).Append(" choices=[").Append(string.Join(",",pick.choices)).Append("] value='").Append(pick.value).Append("' delEn=").Append(del.enabledInHierarchy).Append("\n");
  ZClick(del);
}
return sb.ToString();

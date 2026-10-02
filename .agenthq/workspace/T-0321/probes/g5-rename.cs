var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.TextField nameF=null; UnityEngine.UIElements.Button ren=null, saveAs=null, del=null;
UnityEngine.UIElements.DropdownField pick=null;
foreach (var e in ZAll(win.rootVisualElement)) { if (ZPath(e).IndexOf("ViewBar")<0) continue;
  if (e is UnityEngine.UIElements.TextField tf && nameF==null) nameF=tf;
  if (e is UnityEngine.UIElements.DropdownField df) pick=df;
  var b=e as UnityEngine.UIElements.Button; if (b==null) continue;
  if (b.text=="Rename") ren=b; if (b.text=="Save as") saveAs=b; if (b.text=="Delete view") del=b; }
nameF.value = "AuditT321";  sb.Append("same-name reason='").Append(ren.tooltip).Append("' en=").Append(ren.enabledInHierarchy).Append("\n");
nameF.value = "AuditT321b"; sb.Append("new-name    en=").Append(ren.enabledInHierarchy).Append(" tip='").Append(ren.tooltip).Append("'\n");
ZClick(saveAs); // creates a 2nd view named AuditT321b
sb.Append("after 2nd save-as: choices=[").Append(string.Join(",", pick.choices)).Append("] value='").Append(pick.value).Append("'\n");
nameF.value = "AuditT321";  sb.Append("collide reason='").Append(ren.tooltip).Append("' en=").Append(ren.enabledInHierarchy).Append("\n");
nameF.value = "AuditT321c"; ZClick(ren);
return sb.ToString();

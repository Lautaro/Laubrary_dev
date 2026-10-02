var sw = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.TextField nf=null; UnityEngine.UIElements.Button sa=null;
foreach (var e in ZAll(sw.rootVisualElement)) { if (ZPath(e).IndexOf("ViewBar")<0) continue;
  if (e is UnityEngine.UIElements.TextField t && nf==null) nf=t;
  var b=e as UnityEngine.UIElements.Button; if (b!=null && b.text=="Save as") sa=b; }
nf.value="T321V";
sb.Append("saveAsEn after typing=").Append(sa.enabledInHierarchy).Append(" tip='").Append(sa.tooltip).Append("'\n");
ZClick(sa);
return sb.ToString();

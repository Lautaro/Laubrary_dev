var win = ZWin("ShaperWindow");
UnityEngine.UIElements.TextField nameF=null; UnityEngine.UIElements.Button saveAs=null;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (ZPath(e).IndexOf("ViewBar")>=0) { if (e is UnityEngine.UIElements.TextField tf && nameF==null) nameF=tf; var b=e as UnityEngine.UIElements.Button; if (b!=null && b.text=="Save as") saveAs=b; } }
if (nameF==null||saveAs==null) return "nameF="+(nameF!=null)+" saveAs="+(saveAs!=null);
nameF.value = "AuditT321";
ZClick(saveAs);
return "typed+saved";

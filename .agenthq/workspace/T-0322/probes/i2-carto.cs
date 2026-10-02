var sb=new System.Text.StringBuilder();
var w = ZWin("CartographerWindow");
UnityEngine.UIElements.TextField nameF=null; UnityEngine.UIElements.Button create=null;
foreach (var e in ZAll(w.rootVisualElement)) {
  if (e is UnityEngine.UIElements.TextField tf && ZDrawn(tf) && nameF==null) nameF=tf;
  var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Create") create=b; }
sb.Append("nameF=").Append(nameF!=null).Append(" create=").Append(create!=null).Append("\n");
if (nameF!=null) sb.Append("nameTip='").Append(ZTip(nameF)).Append("'\n");
if (nameF!=null && create!=null) { nameF.value="AuditT322Level"; ZClick(create); sb.Append("created\n"); }
return sb.ToString();

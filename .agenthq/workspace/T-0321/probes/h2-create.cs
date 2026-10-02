var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.TextField nf=null; UnityEngine.UIElements.Button cr=null;
foreach (var e in ZAll(win.rootVisualElement)) { if (!ZDrawn(e)) continue;
  if (e is UnityEngine.UIElements.TextField tf && tf.worldBound.y < 60 && nf==null) nf=tf;
  var b=e as UnityEngine.UIElements.Button; if (b!=null && b.text=="Create") cr=b; }
sb.Append("nf=").Append(nf!=null).Append(" cr=").Append(cr!=null).Append("\n");
if (nf!=null&&cr!=null) { nf.value="AuditT321Doc"; ZClick(cr); sb.Append("created\n"); }
return sb.ToString();

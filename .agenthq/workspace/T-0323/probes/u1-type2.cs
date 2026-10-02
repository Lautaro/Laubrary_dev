var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow");
UnityEngine.UIElements.TextField tf=null; UnityEngine.UIElements.Button create=null;
foreach (var e in ZAll(w.rootVisualElement)) { var t=e as UnityEngine.UIElements.TextField; if (t!=null && ZDrawn(t)) tf=t;
  var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Create") create=b; }
sb.Append("field=").Append(tf!=null).Append(" create=").Append(create!=null).Append("\n");
if (tf!=null) tf.value="AuditT323Walk3";
if (create!=null) { ZClick(create); sb.Append("created\n"); }
return sb.ToString();

var sb=new System.Text.StringBuilder();
var w=ZWin("MirageWindow");
UnityEngine.UIElements.TextField tf=null; UnityEngine.UIElements.Button cr=null;
foreach (var e in ZAll(w.rootVisualElement)) { var t=e as UnityEngine.UIElements.TextField; if (t!=null && ZDrawn(t)) tf=t; var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Create") cr=b; }
if (tf!=null) tf.value="AuditT323Walk4";
if (cr!=null) { ZClick(cr); sb.Append("created\n"); }
return sb.ToString();

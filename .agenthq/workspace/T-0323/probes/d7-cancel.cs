var sb=new System.Text.StringBuilder();
var w = ZWin("LatheWindow");
UnityEngine.UIElements.Button c=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Cancel") c=b; }
if (c!=null) { ZClick(c); sb.Append("cancelled\n"); } else sb.Append("no cancel\n");
return sb.ToString();

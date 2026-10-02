var sb=new System.Text.StringBuilder();
var w = ZWin("CartographerWindow");
w.position = new Rect(20,20,900,880);
// cold walk: press New, type a name, press Create — through the window's own affordances
UnityEngine.UIElements.Button newBtn=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="New") newBtn=b; }
if (newBtn==null) return "no New button";
sb.Append("New enabled=").Append(newBtn.enabledInHierarchy).Append("\n");
ZClick(newBtn);
w.Repaint();
return sb.ToString();

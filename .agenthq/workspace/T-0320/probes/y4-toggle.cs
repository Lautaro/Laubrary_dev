var win = ZWin("ShaperWindow");
UnityEngine.UIElements.Button t = null;
foreach (var e in ZAll(win.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Lighting") { t = b; break; } }
if (t == null) return "no Lighting toggle";
var sb = new System.Text.StringBuilder();
sb.AppendLine("type=" + t.GetType().Name + " focusable=" + t.focusable + " on=" + t.ClassListContains("zui-togglebutton--on"));
t.Focus();
var fc = win.rootVisualElement.panel.focusController;
sb.AppendLine("focused=" + object.ReferenceEquals(fc.focusedElement, t));
var ns = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled(); ns.target = t; using (ns) t.SendEvent(ns);
sb.AppendLine("after submit on=" + t.ClassListContains("zui-togglebutton--on"));
var kd = UnityEngine.UIElements.KeyDownEvent.GetPooled('\n', UnityEngine.KeyCode.Return, UnityEngine.EventModifiers.None); kd.target = t; using (kd) t.SendEvent(kd);
sb.AppendLine("after Return on=" + t.ClassListContains("zui-togglebutton--on"));
var kd2 = UnityEngine.UIElements.KeyDownEvent.GetPooled(' ', UnityEngine.KeyCode.Space, UnityEngine.EventModifiers.None); kd2.target = t; using (kd2) t.SendEvent(kd2);
sb.AppendLine("after Space on=" + t.ClassListContains("zui-togglebutton--on"));
return sb.ToString();

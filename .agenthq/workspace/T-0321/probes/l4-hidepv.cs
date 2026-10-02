var win = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) { var cl=ZCls(e)??"";
  if (cl.Contains("zui-field") && e.worldBound.width>400) e.style.visibility = UnityEngine.UIElements.Visibility.Visible; }
foreach (var e in ZAll(win.rootVisualElement)) if (e is UnityEngine.UIElements.IMGUIContainer) { sb.Append("IMGUI ").Append(e.worldBound).Append("\n"); }
var pv = win.GetType().GetField("preview", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(win) as UnityEngine.UIElements.VisualElement;
sb.Append("preview=").Append(pv!=null?pv.worldBound.ToString():"null").Append("\n");
if (pv!=null) pv.style.visibility = UnityEngine.UIElements.Visibility.Hidden;
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre6.png");
win.Repaint(); return sb.ToString();

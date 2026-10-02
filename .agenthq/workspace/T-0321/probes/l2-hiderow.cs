var win = ZWin("PyreWindow");
foreach (var e in ZAll(win.rootVisualElement)) { var cl=ZCls(e)??"";
  if (cl.Contains("zui-field") && e.worldBound.width>400 && System.Math.Abs(e.worldBound.y-531f)<3f) { e.style.visibility = UnityEngine.UIElements.Visibility.Hidden; } }
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre5.png");
win.Repaint(); return "hidden row";

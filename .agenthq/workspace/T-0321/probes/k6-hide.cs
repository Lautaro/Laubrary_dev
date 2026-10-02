var win = ZWin("PyreWindow");
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name=="SliderInt") { e.style.display = UnityEngine.UIElements.DisplayStyle.None; }
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre3.png");
win.Repaint(); return "hidden";

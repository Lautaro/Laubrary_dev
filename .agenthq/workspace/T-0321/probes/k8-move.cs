var win = ZWin("PyreWindow");
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name=="SliderInt") e.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
win.position = new Rect(120, 120, 820, 800);
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre4.png");
win.Repaint(); return "moved";

var w = ZWin("LatheWindow"); if (w==null) return "no window LatheWindow";
w.position = new Rect(20,20,820,880);
w.titleContent = new GUIContent("LatheWindow");
UnityEditor.EditorPrefs.SetString("T320.capWin","LatheWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0323/shots/lathe-after.png");
w.Focus(); w.Repaint();
return "ready " + w.position;

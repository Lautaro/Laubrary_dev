var w = ZWin("ShaperWindow"); if (w==null) return "no shaper";
w.position = new Rect(20, 40, 820, 900);
w.titleContent = new GUIContent("ShaperCapTag");
w.Repaint();
UnityEditor.EditorPrefs.SetString("T320.capWin","ShaperWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/s1.png");
return "pos=" + w.position;

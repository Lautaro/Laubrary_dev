int n = 0;
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w.GetType().Name == "PopupWindow") { w.Close(); n++; }
return "closed " + n + " popups";

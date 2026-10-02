foreach (var n in new string[]{"ChoreographerWindow","TapestryWindow","PyreWindow","ZoeWindow"})
{ var w = ZWin(n); if (w != null) w.position = new UnityEngine.Rect(40, 40, 1000, 820); }
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Brain Graph");
return "ok";

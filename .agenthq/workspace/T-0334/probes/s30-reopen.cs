foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w0 != null && w0.GetType().Name == "ShaperWindow") w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
w.position = new UnityEngine.Rect(30, 20, UnityEditor.EditorPrefs.GetFloat("T334.w",1500f), UnityEditor.EditorPrefs.GetFloat("T334.h",900f));
return ZBind("ShaperWindow", "Assets/Shaper/AuditT334W1.asset") + " pos=" + w.position;

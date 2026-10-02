// Open Shaper on the real demo document (never saved), place it, and report its transport state.
var w = ZOpen("ShaperWindow"); if (w == null) return "no ShaperWindow";
string r = ZBind("ShaperWindow", "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
w.position = new Rect(40, 20, 900, 880);
UnityEditor.EditorPrefs.SetString("T320.capWin", "ShaperWindow");
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
return r + " | dirty=" + UnityEditor.EditorUtility.IsDirty(doc) + " | pos=" + w.position;

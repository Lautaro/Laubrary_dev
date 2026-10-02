var lauT = ZType("Lauminary"); var lb = ZType("LauminationBuilderWindow");
var lau = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/ProtoGuy.asset", lauT);
var ofe = lb.GetMethod("OpenForEdit", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
ofe.Invoke(null, new object[]{ lau, "LegsWalk_N" });
var w = ZWin("LauminationBuilderWindow");
if (w!=null) { w.position = new Rect(20, 20, 1200, 900); w.titleContent = new GUIContent("LBCapTag"); w.Focus(); w.Repaint(); }
UnityEditor.EditorPrefs.SetString("T320.capWin","LauminationBuilderWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0322/shots/c1-lb.png");
return "opened for edit " + (w!=null?w.position.ToString():"null");

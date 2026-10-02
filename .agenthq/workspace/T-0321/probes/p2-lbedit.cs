var lauT = ZType("Lauminary"); var lb = ZType("LauminationBuilderWindow");
var lau = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd/ProtoGuy.asset", lauT);
var ofe = lb.GetMethod("OpenForEdit", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
ofe.Invoke(null, new object[]{ lau, "LegsWalk_N" });
var w = ZWin("LauminationBuilderWindow");
if (w!=null) { w.position = new Rect(20, 20, 1200, 900); w.Repaint(); }
return "opened for edit " + (w!=null?w.position.ToString():"null");

var t = ZType("MirageActiveView");
var view = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Mirage/AuditT326Mirage.asset");
if (t == null || view == null) return "missing " + (t==null?"MirageActiveView":"view");
var m = t.GetMethod("Set", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
if (m == null) return "no Set on MirageActiveView";
m.Invoke(null, new object[]{ view });
var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT);
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var vf = rigT.GetField("view", BFi);
var cam = rigT.GetField("previewCamera", BFi).GetValue(rig) as Camera;
UnityEditor.SceneView.RepaintAll();
return "active view set; rig.view=" + (vf.GetValue(rig) as UnityEngine.Object)?.name + " cam=" + (cam != null ? cam.name : "none")
  + " liveCount=?" ;

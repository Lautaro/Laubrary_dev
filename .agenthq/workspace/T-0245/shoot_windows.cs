// Open the Pyre window on the blast and the Zoe window on ProtoGuy, run ZuiAudit on both, and PrintWindow them.
var blast = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Pyre.Pyre>("Assets/Pyre/Imported/Driectional Blast Plus.asset");
var zoe = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Zoetrope.Zoe>("Assets/Demos/ProtoGuyDemo/ProtoGuy.asset");
System.Type zoeWinType = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { try { foreach (var t in a.GetTypes()) if (t.Name == "ZoeWindow" && typeof(UnityEditor.EditorWindow).IsAssignableFrom(t)) zoeWinType = t; } catch { } }
Laubrary.Pyre.Editor.PyreWindow.OpenFor(blast);
var pyreWin = UnityEditor.EditorWindow.GetWindow<Laubrary.Pyre.Editor.PyreWindow>();
pyreWin.titleContent = new UnityEngine.GUIContent("PyreProbeWin");
pyreWin.position = new UnityEngine.Rect(100, 100, 1500, 950);
UnityEditor.EditorWindow zoeWin = null;
if (zoeWinType != null) { zoeWinType.GetMethod("OpenFor", new[] { typeof(Laubrary.Zoetrope.Zoe) }).Invoke(null, new object[] { zoe }); zoeWin = UnityEditor.EditorWindow.GetWindow(zoeWinType); zoeWin.titleContent = new UnityEngine.GUIContent("ZoeProbeWin"); zoeWin.position = new UnityEngine.Rect(1700, 100, 1200, 1100); }
return "opened pyre=" + (pyreWin != null) + " zoe=" + (zoeWin != null) + " zoeType=" + (zoeWinType != null ? zoeWinType.FullName : "null");

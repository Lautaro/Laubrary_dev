var sb=new System.Text.StringBuilder();
sb.Append("states.cs=").Append(UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/AuditT322Zoe.states.cs")).Append(" ");
if (UnityEditor.AssetDatabase.IsValidFolder("Assets/Cartographer/Levels")) {
  var g = UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Cartographer/Levels"});
  if (g.Length==0) sb.Append("levelsFolder=").Append(UnityEditor.AssetDatabase.DeleteAsset("Assets/Cartographer/Levels")).Append(" "); }
UnityEditor.AssetDatabase.Refresh();
sb.Append("\nAssets/Shaper: ");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"})) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
// prefs
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
foreach (var k in new string[]{"Shaper.lastView","T322.auditWin","T322.driveWin","T322.t0","T320.capWin","T320.capOut","T321.src","T321.dst","T321.rect","T321.scale","T0312.out"}) UnityEditor.EditorPrefs.DeleteKey(k);
UnityEditor.Undo.ClearAll();
var demo = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.Append("\ndemoDirty=").Append(UnityEditor.EditorUtility.IsDirty(demo));
sb.Append(" compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed);
sb.Append(" isPlaying=").Append(UnityEditor.EditorApplication.isPlaying);
return sb.ToString();

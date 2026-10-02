var sb=new System.Text.StringBuilder();
// 1. unbind windows from real assets, then delete every scratch asset this task made
var chunkT = ZType("ChunkSpec"); var pyreT = ZType("Pyre");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Action<string> unbind = n => { var w = ZWin(n); if (w==null) return;
  System.Reflection.MethodInfo sa=null; for (var t=w.GetType(); t!=null && sa==null; t=t.BaseType) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
  if (sa!=null) sa.Invoke(w, new object[]{ null }); };
foreach (var n in new string[]{"ChunkWindow","PyreWindow","ZoeWindow","MirageWindow"}) unbind(n);
foreach (var p in new string[]{"Assets/Shaper/AuditT321Doc.asset","Assets/Shaper/AuditT321Pyre.asset","Assets/Shaper/AuditT321Chunk.asset","Assets/Shaper/ShaperViews.asset","Assets/Shaper/Audit0277/rfield0277.asset"})
  sb.Append(p).Append("=").Append(UnityEditor.AssetDatabase.DeleteAsset(p)).Append("\n");
sb.Append("Audit0277 folder=").Append(UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/Audit0277")).Append("\n");
UnityEditor.AssetDatabase.Refresh();
// 2. prefs
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
foreach (var k in new string[]{"Shaper.lastView","T321.src","T321.dst","T321.rect","T321.scale","T321.t0","T321.driveWin","T320.capWin","T320.capOut","T0312.out"}) UnityEditor.EditorPrefs.DeleteKey(k);
// 3. remaining contents of Assets/Shaper
sb.Append("Assets/Shaper now: ");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"})) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
sb.Append("\n");
return sb.ToString();

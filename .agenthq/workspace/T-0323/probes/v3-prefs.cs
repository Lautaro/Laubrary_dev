var sb=new System.Text.StringBuilder();
sb.Append("before userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<none>")).Append("\n");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
foreach (var k in new string[]{"T323.auditWin","T323.pressWin","T323.pressText","T323.pressNth","T323.pressAsset","T323.capOut","T323.capRect","T323.delay","T323.t0","T320.capWin","T320.capOut","T321.src","T321.dst","T321.rect","T321.scale","T322.auditWin","T322.t0","Shaper.lastView"}) UnityEditor.EditorPrefs.DeleteKey(k);
UnityEditor.EditorPrefs.SetString("T0312.out","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/out");
sb.Append("after  userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<none>")).Append("\n");
return sb.ToString();

var sb = new System.Text.StringBuilder();
// 1. scratch assets created by this task
foreach (var p in new string[]{
  "Assets/Shaper/AuditT0320W1.asset", "Assets/Shaper/AuditT0320W2.asset",
  "Assets/Shaper/AuditT0320W2.png", "Assets/Shaper/AuditT0320W2_1.png",
  "Assets/Shaper/AuditT0320W2.anim", "Assets/Shaper/AuditT0320W2_1.anim",
  "Assets/Shaper/AuditT0320W2.gif", "Assets/Shaper/AuditT0320W2_1.gif",
  "Assets/Shaper/AuditT0320W2 Clip.asset", "Assets/Shaper/AuditT0320W2 Clip_1.asset",
  "Assets/Pyre/AuditT0320.asset", "Assets/Pyre/AuditT0313.asset", "Assets/Pyre/AuditT0312.asset" })
{
  if (System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", p)))
    sb.AppendLine((UnityEditor.AssetDatabase.DeleteAsset(p) ? "deleted " : "FAILED  ") + p);
}
// 2. prefs this task set
foreach (var k in new string[]{ "T320.capTag","T320.capMode","T320.capOut","T320.capWin","T320.scroll","T320.winw","T320.winh","T320.pane","T320.doc","T320.layer","T320.sel","T320.pick","T320.col","T320.newname","T320.btn","T320.btnPending","T320.frame","T320.preSave","T320.play","T320.pyrew","T320.args","T320.auditWin","T320.auditW","T320.mode","T320.out","T320.focusPadId","T320.jump.ZuiPad","T320.jump.ZuiMicroMinMax","T320.jump.ZuiValue2DControl","T313.form","T313.shape","T313.pane","T313.winw","T0312.unit","T0312.tag","T0312.layer","T0312.pane","T0312.winw","T0312.doc","ZUI.Split.shaper.window.split.v1","ZUI.Split.pyre.window.split.v1" })
  if (UnityEditor.EditorPrefs.HasKey(k)) { UnityEditor.EditorPrefs.DeleteKey(k); sb.AppendLine("pref cleared " + k); }
// 3. the Shaper section selection back to what T-0318 restored
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel", "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
UnityEditor.EditorPrefs.DeleteKey("Shaper.lastView");
sb.AppendLine("section selection restored");
return sb.ToString();

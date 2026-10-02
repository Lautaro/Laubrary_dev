var win = ZWin("ShaperWindow"); var vt = win.rootVisualElement.panel.visualTree;
foreach (var e in ZAll(vt)) { var cl=ZCls(e)??""; if (cl.Contains("zui-popover__scrim")) ZClick(e); }
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=0;Tags=0");
win.GetType().GetMethod("Rebuild", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Invoke(win,null);
win.Repaint();
// press Apply / Update / Delete view with nothing selected — confirm they are no-ops that raise nothing
var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  if (b.text=="Apply"||b.text=="Update"||b.text=="Delete view"||b.text=="Save as") { sb.Append("press '").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append(" -> "); ZClick(b); sb.Append("no exception\n"); } }
sb.Append("viewsAssetAfter=").Append(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/ShaperViews.asset")!=null).Append("\n");
return sb.ToString();

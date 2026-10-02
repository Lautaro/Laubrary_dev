UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=0;Canvas=0;Layers=0;Shape=1;Fill=0;Swarm=0;SpriteFX=0;Lights=0;Tags=0");
var w = ZWin("ShaperWindow");
var m = w.GetType().GetMethod("Rebuild", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
var sb=new System.Text.StringBuilder();
sb.Append("rebuild=").Append(m!=null).Append("\n");
if (m!=null) m.Invoke(w,null);
else { foreach(var mm in w.GetType().GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public)) if (mm.Name.Contains("Build")||mm.Name.Contains("Refresh")) sb.Append("m:").Append(mm.Name).Append(" "); }
w.Repaint();
return sb.ToString();

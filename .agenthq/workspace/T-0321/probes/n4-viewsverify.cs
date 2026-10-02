UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=0;Tags=0");
var sw = ZWin("ShaperWindow");
sw.GetType().GetMethod("Rebuild", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Invoke(sw,null);
sw.Repaint(); return "ok";

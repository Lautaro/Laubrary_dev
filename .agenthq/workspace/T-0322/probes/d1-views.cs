var sb=new System.Text.StringBuilder();
var win = ZWin("ShaperWindow"); if (win==null) { ZType("ShaperWindow").GetMethod("Open", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).Invoke(null,null); win = ZWin("ShaperWindow"); }
sb.Append("viewsStoreExists=").Append(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/ShaperViews.asset")!=null).Append("\n");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=0;Tags=0");
win.position = new Rect(20,20,820,600);
win.titleContent = new GUIContent("ShaperCapTag");
win.GetType().GetMethod("Rebuild", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Invoke(win,null);
win.Focus(); win.Repaint();
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  if (ZPath(e).IndexOf("ViewBar")<0) continue;
  sb.Append("btn '").Append(b.text).Append("' enabled=").Append(b.enabledInHierarchy).Append(" tip='").Append(ZTip(b)).Append("'\n"); }
UnityEditor.EditorPrefs.SetString("T320.capWin","ShaperWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0322/shots/d1-views-empty.png");
return sb.ToString();

var w = ZWin("PyreWindow"); var sb=new System.Text.StringBuilder();
w.position = new Rect(20,20,820,900);
var boxT = ZType("ZuiBox"); var titleP = boxT.GetProperty("TitleText");
UnityEngine.UIElements.VisualElement frac=null;
foreach (var e in ZAll(w.rootVisualElement)) if (boxT.IsInstanceOfType(e) && (titleP.GetValue(e) as string)=="Fracture") { frac=e; break; }
sb.Append("frac=").Append(frac!=null?frac.worldBound.ToString():"NULL").Append("\n");
UnityEngine.UIElements.ScrollView sv=null;
foreach (var e in ZAll(w.rootVisualElement)) if (e is UnityEngine.UIElements.ScrollView s2 && ZDrawn(s2) && s2.worldBound.x<360) sv=s2;
if (sv!=null && frac!=null) { sv.scrollOffset = new Vector2(0, frac.worldBound.y - sv.worldBound.y + sv.scrollOffset.y - 10f); sb.Append("scrolled to ").Append(sv.scrollOffset).Append("\n"); }
UnityEditor.EditorPrefs.SetString("T320.capWin","PyreWindow");
UnityEditor.EditorPrefs.SetString("T320.capOut","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/jet-cards2.png");
w.Focus(); w.Repaint();
return sb.ToString();

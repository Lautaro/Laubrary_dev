var sb=new System.Text.StringBuilder();
var w = ZWin("AnimationAsepriteWindow");
sb.Append("all=").Append(ZAll(w.rootVisualElement).Count).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) sb.Append(e.GetType().Name).Append("['").Append(ZOwnText(e)).Append("' drawn=").Append(ZDrawn(e)).Append("] ");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
sb.Append("\nlau=").Append(w.GetType().GetField("_lauminary",BFi).GetValue(w));
sb.Append(" status=").Append(w.GetType().GetField("_status",BFi).GetValue(w));
return sb.ToString();

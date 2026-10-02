var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
sb.Append("win=").Append(win==null?"NULL":win.position.ToString()).Append("\n");
if (win==null) return sb.ToString();
int n=0; UnityEngine.UIElements.VisualElement stage=null;
foreach (var e in ZAll(win.rootVisualElement)) { n++; if (e.GetType().Name=="ShaperPreviewStage") stage=e; }
sb.Append("elems=").Append(n).Append(" stage=").Append(stage==null?"NULL":stage.worldBound.ToString()).Append("\n");
return sb.ToString();

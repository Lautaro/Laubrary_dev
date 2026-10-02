var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.VisualElement stage=null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name=="ShaperPreviewStage") stage=e;
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
foreach (var f in stage.GetType().GetFields(BF2)) sb.Append("f:").Append(f.Name).Append("=").Append(f.FieldType.Name).Append(" ");
sb.Append("\n");
foreach (var p in stage.GetType().GetProperties(BF2)) sb.Append("P:").Append(p.Name).Append(" ");
return sb.ToString();

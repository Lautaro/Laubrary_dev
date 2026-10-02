var sb=new System.Text.StringBuilder();
var w=ZWin("LatheWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && (b.text=="Show Move Gizmo")) sb.Append("BTN cls=").Append(ZCls(b)).Append(" tip=").Append(ZTip(b)).Append("\n"); }
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
foreach (var f in w.GetType().GetFields(BFi)) if (f.FieldType==typeof(bool)) sb.Append("fld ").Append(f.Name).Append("=").Append(f.GetValue(w)).Append("\n");
return sb.ToString();

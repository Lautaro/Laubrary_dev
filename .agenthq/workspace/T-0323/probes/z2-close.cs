var sb=new System.Text.StringBuilder();
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null && x.GetType().Name=="PopupWindow") { x.Close(); sb.Append("closed popup\n"); }
var w=ZWin("TextSplashWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
foreach (var f in w.GetType().GetFields(BFi)) if (f.FieldType==typeof(bool)||f.FieldType==typeof(float)||f.FieldType==typeof(double)) sb.Append(f.Name).Append("=").Append(f.GetValue(w)).Append(" ");
sb.Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && (b.text.Contains("Play")||b.text.Contains("Pause")||b.text=="Reset"||b.text=="▲")) sb.Append("BTN '").Append(b.text).Append("' cls=").Append(ZCls(b)).Append(" tip=").Append((ZTip(b)??"").Substring(0,Mathf.Min(90,(ZTip(b)??"").Length))).Append("\n"); }
return sb.ToString();

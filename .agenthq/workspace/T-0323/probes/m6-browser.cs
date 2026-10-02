var sb=new System.Text.StringBuilder();
var w=ZWin("MirageWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Add Previewable") { ZClick(b); sb.Append("pressed\n"); break; } }
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null && x.GetType().Name=="PopupWindow") sb.Append("POPUP ").Append(x.position).Append("\n");
var pickT = ZType("MirageAssetPicker");
if (pickT!=null) { var m=pickT.GetMethod("FindAll", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
  var list = m.Invoke(null,null) as System.Collections.IList; sb.Append("browser offers ").Append(list.Count).Append(" assets\n"); }
return sb.ToString();

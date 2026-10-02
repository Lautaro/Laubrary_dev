var sb=new System.Text.StringBuilder();
var w=ZWin("LatheWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Cancel") { ZClick(b); sb.Append("cancelled rename\n"); break; } }
// Bake Sprite Strip: call the method the SaveFilePanel would feed, without the dialog
var bakerT = ZType("LatheBaker");
var specT = ZType("LatheSpec");
var spec = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Shaper/AuditT323Lathe.asset", specT);
sb.Append("baker=").Append(bakerT!=null).Append(" spec=").Append(spec!=null).Append("\n");
if (bakerT!=null && spec!=null) {
  var m = bakerT.GetMethod("BakeStrip", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
  sb.Append("BakeStrip sig=").Append(m!=null? m.ToString():"NULL").Append("\n");
  if (m!=null) {
    var ps=m.GetParameters(); var args=new object[ps.Length];
    args[0]=spec; for(int i=1;i<ps.Length;i++) args[i]= ps[i].ParameterType==typeof(float)? (object)0f : System.Activator.CreateInstance(ps[i].ParameterType);
    var res = m.Invoke(null,args) as UnityEngine.Texture2D;
    sb.Append("strip=").Append(res==null?"NULL":(res.width+"x"+res.height)).Append("\n");
  }
}
return sb.ToString();

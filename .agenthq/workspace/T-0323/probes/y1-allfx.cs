var sb=new System.Text.StringBuilder();
var specT = ZType("SpriteFxSpec");
var spec = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/SpriteFx/AuditT323Walk1.asset", specT);
if (spec==null) { var gs=UnityEditor.AssetDatabase.FindAssets("AuditT323Walk1"); foreach(var g in gs) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n"); return sb.ToString(); }
var modT = ZType("SpriteFxModifier");
sb.Append("modT=").Append(modT).Append("\n");
System.Reflection.FieldInfo listF=null;
foreach (var f in specT.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
  sb.Append("f:").Append(f.Name).Append(":").Append(f.FieldType.Name).Append(" ");
sb.Append("\n");
return sb.ToString();

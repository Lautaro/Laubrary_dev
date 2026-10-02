var sb=new System.Text.StringBuilder();
var specT = ZType("SpriteFxSpec");
var gs=UnityEditor.AssetDatabase.FindAssets("AuditT323Walk1");
string path = gs.Length>0? UnityEditor.AssetDatabase.GUIDToAssetPath(gs[0]) : null;
var spec = path==null? null : UnityEditor.AssetDatabase.LoadAssetAtPath(path, specT);
if (spec==null) return "no spec";
sb.Append("path=").Append(path).Append("\n");
var listF = specT.GetField("modifiers", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
var list = listF.GetValue(spec) as System.Collections.IList;
var elemT = listF.FieldType.GetGenericArguments()[0];
sb.Append("elemT=").Append(elemT.FullName).Append(" count=").Append(list.Count).Append("\n");
int added=0; var names=new System.Text.StringBuilder();
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
  System.Type[] ts; try { ts=a.GetTypes(); } catch { continue; }
  foreach (var t in ts) {
    if (t.IsAbstract || !elemT.IsAssignableFrom(t)) continue;
    if (t.GetConstructor(System.Type.EmptyTypes)==null) continue;
    try { list.Add(System.Activator.CreateInstance(t)); added++; names.Append(t.Name).Append(" "); } catch {}
  }
}
UnityEditor.EditorUtility.SetDirty(spec);
sb.Append("added=").Append(added).Append(" total=").Append(list.Count).Append("\n").Append(names).Append("\n");
return sb.ToString();

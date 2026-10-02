var sb=new System.Text.StringBuilder();
string[] names = {"ChunkWindow","ChunksWindow","ZoetropeWindow","MirageWindow","LauminationBuilderWindow","LauminationBuilder","ZoeWindow"};
foreach (var n in names) { var t = ZType(n); sb.Append(n).Append("=").Append(t!=null?t.FullName:"-").Append("\n"); }
// list every EditorWindow subclass in Laubrary
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { if (!a.FullName.Contains("Laubrary")) continue;
  System.Type[] ts; try { ts=a.GetTypes(); } catch { continue; }
  foreach (var t in ts) if (typeof(UnityEditor.EditorWindow).IsAssignableFrom(t) && !t.IsAbstract) sb.Append("WIN ").Append(t.FullName).Append("\n"); }
return sb.ToString();

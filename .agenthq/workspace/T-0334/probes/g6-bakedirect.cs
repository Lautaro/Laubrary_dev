var sb = new System.Text.StringBuilder();
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>("Assets/Demos/ShaperDemo/AuditT334ShaperA.asset");
System.Type bakerT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{ System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
  foreach (var t in ts) if (t.Name == "ShaperBaker") { bakerT = t; break; } if (bakerT != null) break; }
var m = bakerT.GetMethod("Bake", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var ps = m.GetParameters();
var args = new object[ps.Length];
args[0] = doc;
for (int i = 1; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
var res = m.Invoke(null, args);
foreach (var f in res.GetType().GetFields())
  sb.Append(f.Name).Append("=").Append(f.GetValue(res)).Append("\n");
return sb.ToString();

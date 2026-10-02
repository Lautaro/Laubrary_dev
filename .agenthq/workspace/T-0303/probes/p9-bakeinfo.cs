var sb = new System.Text.StringBuilder();
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
for (var t = pyreT; t != null && t != typeof(UnityEditor.EditorWindow); t = t.BaseType)
  foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly))
  { var n = f.Name.ToLower(); if (n.Contains("bake") || n.Contains("dest") || n.Contains("ppu") || n.Contains("folder") || n.Contains("gif")) sb.Append(t.Name).Append('.').Append(f.Name).Append(" (").Append(f.FieldType.Name).Append(") = ").Append(f.GetValue(win)).Append("\n"); }
foreach (var m in pyreT.GetMethods(BFi)) if (m.Name.ToLower().Contains("bake")) sb.Append("method ").Append(m.Name).Append("(").Append(m.GetParameters().Length).Append(")\n");
return sb.ToString();

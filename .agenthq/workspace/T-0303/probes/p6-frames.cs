var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var spec = curP.GetValue(win);
var st = spec.GetType();
foreach (var f in st.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public))
  if (f.Name.ToLower().Contains("frame") || f.Name.ToLower().Contains("fps") || f.Name.ToLower().Contains("width") || f.Name.ToLower().Contains("height"))
    sb.Append("spec.").Append(f.Name).Append(" = ").Append(f.GetValue(spec)).Append("\n");
var rendT = FT("PyreRenderer");
var rm = rendT.GetMethod("RenderFrame", new System.Type[]{ st, typeof(int) });
var fcF = st.GetField("frameCount", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public);
int fc = fcF == null ? 16 : (int)fcF.GetValue(spec);
int blank = 0; var lits = new System.Text.StringBuilder(); var hashes = new System.Collections.Generic.HashSet<int>();
for (int i = 0; i < fc; i++)
{
    var px = rm.Invoke(null, new object[]{ spec, i }) as UnityEngine.Color32[];
    int lit = 0, h = 17;
    foreach (var c in px) { if (c.a > 0) lit++; h = h * 31 + c.r + c.g * 7 + c.b * 13 + c.a * 3; }
    hashes.Add(h); if (lit == 0) blank++;
    lits.Append(lit).Append(' ');
}
sb.Append("frames=").Append(fc).Append(" blank=").Append(blank).Append(" distinct=").Append(hashes.Count).Append("\n  lit: ").Append(lits.ToString()).Append("\n");
return sb.ToString();

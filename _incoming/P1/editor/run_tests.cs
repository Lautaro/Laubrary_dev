// Runs Laubrary.GoreLab.Tests.GoreGoldenTests by reflection (no Test Runner), plus compile state.
var sb = new System.Text.StringBuilder();
sb.AppendLine("compiling=" + UnityEditor.EditorApplication.isCompiling);
string[] classes = { "Laubrary.GoreLab.Tests.GoreGoldenTests" };
int pass = 0, fail = 0;
foreach (var cn in classes) {
    System.Type t = null;
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
    { try { var x = a.GetType(cn); if (x != null) { t = x; break; } } catch { } }
    if (t == null) { sb.AppendLine(cn + ": TYPE NOT FOUND"); continue; }
    var all = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    System.Func<System.Reflection.MethodInfo, string, bool> has = (m, attr) =>
    { foreach (var at in m.GetCustomAttributes(false)) if (at.GetType().Name == attr) return true; return false; };
    foreach (var m in all) {
        if (!has(m, "TestAttribute")) continue;
        var inst = System.Activator.CreateInstance(t);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { m.Invoke(inst, null); pass++; sb.AppendLine("  PASS " + m.Name + " (" + sw.ElapsedMilliseconds + " ms)"); }
        catch (System.Exception ex) { fail++; var inner = ex.InnerException ?? ex; sb.AppendLine("  FAIL " + m.Name + ": " + inner.Message.Replace("\n", " | ")); }
    }
}
// timing of the same benchmark frame inside the editor's runtime (Mono)
sb.AppendLine("TOTAL passed=" + pass + " failed=" + fail);
return sb.ToString();

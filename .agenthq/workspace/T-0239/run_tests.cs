var sb = new System.Text.StringBuilder();
int pass = 0, fail = 0;
string[] wanted = { "EventContextResolverTests", "PyreAnchorTests" };
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
{
    System.Type[] types;
    try { types = asm.GetTypes(); } catch { continue; }
    foreach (var t in types)
    {
        bool match = false;
        foreach (var w in wanted) if (t.Name == w) match = true;
        if (!match) continue;
        foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        {
            bool isTest = false;
            foreach (var a in m.GetCustomAttributes(false)) if (a.GetType().Name == "TestAttribute") isTest = true;
            if (!isTest || m.GetParameters().Length != 0) continue;
            object inst = null;
            try { inst = System.Activator.CreateInstance(t); m.Invoke(inst, null); pass++; }
            catch (System.Exception e)
            {
                fail++;
                var inner = e.InnerException ?? e;
                sb.AppendLine("FAIL " + t.Name + "." + m.Name + " :: " + inner.GetType().Name + ": " + inner.Message.Replace("\n", " | "));
            }
            finally { if (inst is System.IDisposable d) d.Dispose(); }
        }
    }
}
return "PASS=" + pass + " FAIL=" + fail + "\n" + sb.ToString();

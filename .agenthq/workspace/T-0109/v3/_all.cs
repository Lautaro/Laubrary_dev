using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class V3All
{
    public static string Execute()
    {
        string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v3\";
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        sb.AppendLine("dataPath=" + Application.dataPath);
        sb.AppendLine("compileFailed=" + EditorUtility.scriptCompilationFailed);
        // proof the new assembly is loaded, not a stale one
        Type ht = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { ht = a.GetType("Laubrary.Shaper.ShaperHeight"); if (ht != null) break; }
        var mi = ht == null ? null : ht.GetMethod("InverseAtEUpperBound", BindingFlags.Public | BindingFlags.Static);
        sb.AppendLine("InverseAtEUpperBound present = " + (mi != null));

        string[] names = { "ShaperFieldAudit", "ShaperFillAudit", "ShaperBorderAudit", "ShaperLightAudit", "ShaperHeightAudit" };
        foreach (string n in names)
        {
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { t = asm.GetType("Laubrary.Shaper.Editor." + n); if (t != null) break; }
            if (t == null) { sb.AppendLine(n + ": TYPE NOT FOUND"); continue; }
            var m = t.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static);
            var ps = m.GetParameters();
            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string res = (string)m.Invoke(null, args);
            sw.Stop();
            File.WriteAllText(outDir + n + ".txt", res);
            int pass = 0, fail = 0, vOk = 0, vTot = 0;
            foreach (var line in res.Split('\n'))
            {
                if (line.Contains("PASS")) pass++;
                if (line.Contains("FAIL")) fail++;
                if (line.Contains("VERDICT")) { vTot++; if (line.Contains("ok")) vOk++; }
            }
            sb.AppendLine(n + ": " + sw.ElapsedMilliseconds + " ms  PASS=" + pass + "  FAIL=" + fail + "  VERDICT ok " + vOk + "/" + vTot);
        }
        File.WriteAllText(outDir + "SUMMARY.txt", sb.ToString());
        return sb.ToString();
    }
}

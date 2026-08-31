using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class V2Audits
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        sb.AppendLine("dataPath=" + Application.dataPath);
        string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v2\";
        Directory.CreateDirectory(outDir);
        string[] names = { "ShaperFieldAudit", "ShaperFillAudit", "ShaperBorderAudit", "ShaperLightAudit" };
        foreach (string n in names)
        {
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("Laubrary.Shaper.Editor." + n);
                if (t != null) break;
            }
            if (t == null) { sb.AppendLine(n + ": TYPE NOT FOUND"); continue; }
            var m = t.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static);
            if (m == null) { sb.AppendLine(n + ": RunAll NOT FOUND"); continue; }
            try
            {
                var ps = m.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string res = (string)m.Invoke(null, args);
                sw.Stop();
                File.WriteAllText(outDir + n + ".txt", res);
                int fails = 0, oks = 0;
                foreach (var line in res.Split('\n'))
                {
                    if (line.Contains("FAIL")) fails++;
                    if (line.Contains("VERDICT")) { if (line.Contains("ok")) oks++; }
                }
                sb.AppendLine(n + ": " + res.Length + " chars, " + sw.ElapsedMilliseconds + " ms, lines-containing-FAIL=" + fails + ", VERDICT-ok=" + oks);
            }
            catch (Exception e) { sb.AppendLine(n + ": THREW " + e.GetBaseException().Message); }
        }
        return sb.ToString();
    }
}

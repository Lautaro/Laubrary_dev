using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Kick off the four earlier-wave audits ONE PER EDITOR UPDATE and write each result to a file,
// returning immediately so the 60 s bridge timeout is never hit.
public static class V2Audit1
{
    static Queue<string> q;
    static string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v2\";

    public static string Execute()
    {
        Directory.CreateDirectory(outDir);
        q = new Queue<string>(new[] { "ShaperFieldAudit", "ShaperFillAudit", "ShaperBorderAudit", "ShaperLightAudit" });
        File.WriteAllText(outDir + "STATE.txt", "starting");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return "queued 4 audits; poll " + outDir + "STATE.txt";
    }

    static void Tick()
    {
        if (q == null || q.Count == 0)
        {
            EditorApplication.update -= Tick;
            File.AppendAllText(outDir + "STATE.txt", "\nALL DONE");
            return;
        }
        string n = q.Dequeue();
        try
        {
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            { t = asm.GetType("Laubrary.Shaper.Editor." + n); if (t != null) break; }
            if (t == null) { File.AppendAllText(outDir + "STATE.txt", "\n" + n + ": TYPE NOT FOUND"); return; }
            var m = t.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static);
            var ps = m.GetParameters();
            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string res = (string)m.Invoke(null, args);
            sw.Stop();
            File.WriteAllText(outDir + n + ".txt", res);
            int fails = 0, verdictOk = 0, verdictTotal = 0;
            foreach (var line in res.Split('\n'))
            {
                if (line.Contains("FAIL")) fails++;
                if (line.Contains("VERDICT")) { verdictTotal++; if (line.Contains("ok")) verdictOk++; }
            }
            File.AppendAllText(outDir + "STATE.txt", "\n" + n + ": " + sw.ElapsedMilliseconds + " ms, chars=" + res.Length
                + ", lines-with-FAIL=" + fails + ", VERDICT ok " + verdictOk + "/" + verdictTotal);
        }
        catch (Exception e) { File.AppendAllText(outDir + "STATE.txt", "\n" + n + ": THREW " + e.GetBaseException().Message); }
    }
}

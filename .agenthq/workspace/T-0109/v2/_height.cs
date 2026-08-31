using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class V2Height
{
    static string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v2\";
    static bool started;
    public static string Execute()
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(outDir + "HSTATE.txt", "starting " + DateTime.Now);
        started = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return "queued height audit";
    }
    static void Tick()
    {
        if (started) { EditorApplication.update -= Tick; return; }
        started = true;
        EditorApplication.update -= Tick;
        try
        {
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            { t = asm.GetType("Laubrary.Shaper.Editor.ShaperHeightAudit"); if (t != null) break; }
            if (t == null) { File.AppendAllText(outDir + "HSTATE.txt", "\nTYPE NOT FOUND"); return; }
            var m = t.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string res = (string)m.Invoke(null, new object[] { null, null });
            sw.Stop();
            File.WriteAllText(outDir + "HEIGHT-AUDIT-V2.txt", res);
            File.AppendAllText(outDir + "HSTATE.txt", "\nDONE " + sw.ElapsedMilliseconds + " ms, chars=" + res.Length);
        }
        catch (Exception e) { File.AppendAllText(outDir + "HSTATE.txt", "\nTHREW " + e.GetBaseException()); }
    }
}

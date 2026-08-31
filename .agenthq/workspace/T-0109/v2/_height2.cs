using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class V2Height2
{
    public static string Execute()
    {
        string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v2\";
        Directory.CreateDirectory(outDir);
        File.WriteAllText(outDir + "HSTATE.txt", "starting " + DateTime.Now.ToString("HH:mm:ss"));
        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType("Laubrary.Shaper.Editor.ShaperHeightAudit"); if (t != null) break; }
        if (t == null) { File.AppendAllText(outDir + "HSTATE.txt", "\nTYPE NOT FOUND"); return "no type"; }
        var m = t.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string res = (string)m.Invoke(null, new object[] { null, null });
        sw.Stop();
        File.WriteAllText(outDir + "HEIGHT-AUDIT-V2.txt", res);
        File.AppendAllText(outDir + "HSTATE.txt", "\nDONE " + sw.ElapsedMilliseconds + " ms, chars=" + res.Length);
        return "done " + sw.ElapsedMilliseconds;
    }
}

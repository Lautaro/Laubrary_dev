if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
string outPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\FIELD-AUDIT.txt";
if (System.IO.File.Exists(outPath)) System.IO.File.Delete(outPath);
UnityEditor.EditorApplication.delayCall += () => {
    try {
        var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFieldAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
        string s = (string)t.GetMethod("RunAll").Invoke(null, null);
        System.IO.File.WriteAllText(outPath, s);
    } catch (System.Exception e) { System.IO.File.WriteAllText(outPath, "EXCEPTION: " + e); }
};
return "queued";

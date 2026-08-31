System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
string outPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\FIELD-AUDIT.txt";
string donePath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\BG-DONE.txt";
string[] ms = System.IO.File.ReadAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\chunk.txt").Trim().Split(',');
if (System.IO.File.Exists(donePath)) System.IO.File.Delete(donePath);
UnityEditor.EditorApplication.delayCall += () => {
    var sb = new System.Text.StringBuilder();
    try {
        var t = Find("Laubrary.Shaper.Editor.ShaperFieldAudit");
        foreach (var m in ms) sb.AppendLine((string)t.GetMethod(m.Trim()).Invoke(null, null));
        System.IO.File.AppendAllText(outPath, sb.ToString());
        System.IO.File.WriteAllText(donePath, "OK " + sb.Length);
    } catch (System.Exception e) { System.IO.File.WriteAllText(donePath, "EXCEPTION " + e); }
};
return "queued";

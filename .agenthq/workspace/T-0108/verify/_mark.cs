var logPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
long len = 0;
try { var fi = new System.IO.FileInfo(logPath); len = fi.Length; } catch {}
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\mark.txt", len.ToString());
UnityEditor.AssetDatabase.Refresh();
return "marked at " + len + " path=" + logPath;

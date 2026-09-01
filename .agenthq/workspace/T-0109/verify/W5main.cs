using System;
class MainW5 { public static void Main(string[] a) { string s = a.Length > 0 ? a[0] : "all"; if (s.StartsWith("W5")) W5.Run(s.Length > 2 ? s.Substring(2) : "all"); else if (s.StartsWith("W4")) W4.Run(s.Length > 2 ? s.Substring(2) : "all"); else W3.Run(s); } }

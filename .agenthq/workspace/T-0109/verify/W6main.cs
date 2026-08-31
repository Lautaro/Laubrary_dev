using System;
class MainW6 { public static void Main(string[] a) { string s = a.Length > 0 ? a[0] : "W6"; if (s == "W6") W6.Run(); else if (s.StartsWith("W5")) W5.Run(s.Length>2?s.Substring(2):"all"); else if (s.StartsWith("W4")) W4.Run(s.Length>2?s.Substring(2):"all"); else W3.Run(s); } }

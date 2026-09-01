using System;
class MainW4 { public static void Main(string[] a) { if (a.Length > 0 && a[0].StartsWith("W4")) W4.Run(a[0].Length > 2 ? a[0].Substring(2) : "all"); else W3.Run(a.Length > 0 ? a[0] : "all"); } }

var sw=System.Diagnostics.Stopwatch.StartNew();
while(sw.ElapsedMilliseconds < 40000) { }
return "slept " + sw.ElapsedMilliseconds + " ms";
